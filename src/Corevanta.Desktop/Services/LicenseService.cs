using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.IO;
using System.Net.Http;
using System.Net.Http.Json;
using Microsoft.Win32;

namespace Corevanta.Desktop.Services;

public sealed class LicenseService
{
    private const string LicenseFileName = "license.json";
    private const string TestLicenseKey = "CV-TEST-5000-2026";
    private const string LicenseApiBaseUrlEnvironmentVariable = "COREVANTA_LICENSE_API_URL";
    private const string DefaultLicenseApiBaseUrl = "https://corevanta-licensing-api.onrender.com";
    private const string LicensePublicKeyPemEnvironmentVariable = "COREVANTA_LICENSE_PUBLIC_KEY_PEM";
    private const string DefaultLicensePublicKeyPem = """
        -----BEGIN PUBLIC KEY-----
        MIIBIjANBgkqhkiG9w0BAQEFAAOCAQ8AMIIBCgKCAQEAx86bSOcu3LpiNEJlPZ0B
        5Gef0OHJSFhxB7/0CAkBHuITqZH0KmgJ5GJUR9XMq11nAIVca7LZNYB1iDaP2W83
        ExZoM6TocTKNqsr1vLQkYeeuJVhIw3oD0Qk6UKmkojgjAZdT8Di9vPLmHeb4u5mB
        RC0sZ1vBEdTW6vGkUYHTvPeb6ug+2mcXvymgRXI2G3Lz8LsTQwbaFCTgAd5KFh1/
        SLEnLPfAW0qZXhPWmDt4jEFsG9XFu58EdI65uqs87p2s6pPajC7Q1K4/8zXhM85W
        GrIp/s5zxgD78vdqUHMBdGIXzw8vUrd692B3NtND8U5uUzAdz2DUwXENB5tWdVLA
        xQIDAQAB
        -----END PUBLIC KEY-----
        """;
    private static readonly HttpClient LicenseHttpClient = new() { Timeout = TimeSpan.FromSeconds(15) };
    private readonly string _licenseFilePath;

    public LicenseService(string dataDirectory)
    {
        Directory.CreateDirectory(dataDirectory);
        _licenseFilePath = Path.Combine(dataDirectory, LicenseFileName);
    }

    public string MachineHash => ComputeMachineHash();

    public string GetTestLicenseKey()
    {
#if DEBUG
        return TestLicenseKey;
#else
        return string.Empty;
#endif
    }

    public LicenseValidationResult ValidateStoredLicense()
    {
        var stored = ReadStoredLicense();
        if (stored is null || string.IsNullOrWhiteSpace(stored.LicenseKey))
        {
            return LicenseValidationResult.Invalid("No active license. Enter a license key to continue.");
        }

        return ValidateLicenseKey(stored.LicenseKey);
    }

    public LicenseValidationResult ActivateLicense(string licenseKey, string emailOrUser)
    {
        var normalizedLicenseKey = (licenseKey ?? string.Empty).Trim();
        if (string.IsNullOrWhiteSpace(normalizedLicenseKey))
        {
            if (!TryRequestOnlineLicense(emailOrUser, out var fetchedLicenseKey, out var activationError))
            {
                return LicenseValidationResult.Invalid(activationError);
            }

            normalizedLicenseKey = fetchedLicenseKey;
        }

        var result = ValidateLicenseKey(normalizedLicenseKey);
        if (!result.IsValid)
        {
            return result;
        }

        var payload = new StoredLicense
        {
            LicenseKey = normalizedLicenseKey,
            EmailOrUser = (emailOrUser ?? string.Empty).Trim(),
            ActivatedUtc = DateTime.UtcNow
        };

        var json = JsonSerializer.Serialize(payload, new JsonSerializerOptions { WriteIndented = true });
        File.WriteAllText(_licenseFilePath, json, Encoding.UTF8);
        return result;
    }

    private bool TryRequestOnlineLicense(string emailOrUser, out string licenseKey, out string errorMessage)
    {
        licenseKey = string.Empty;
        errorMessage = string.Empty;

        var normalizedEmail = (emailOrUser ?? string.Empty).Trim();
        if (string.IsNullOrWhiteSpace(normalizedEmail))
        {
            errorMessage = "Enter a license key, or provide purchaser email for online activation.";
            return false;
        }

        var baseUrl = Environment.GetEnvironmentVariable(LicenseApiBaseUrlEnvironmentVariable)?.Trim();
        if (string.IsNullOrWhiteSpace(baseUrl))
        {
            baseUrl = DefaultLicenseApiBaseUrl;
        }

        baseUrl = baseUrl.TrimEnd('/');
        var endpoint = $"{baseUrl}/api/licenses/activate";

        try
        {
            var response = LicenseHttpClient.PostAsJsonAsync(endpoint, new OnlineActivationRequest(normalizedEmail, MachineHash))
                .GetAwaiter().GetResult();
            var payload = response.Content.ReadFromJsonAsync<OnlineActivationResponse>()
                .GetAwaiter().GetResult();

            if (!response.IsSuccessStatusCode)
            {
                var serverError = payload?.Error;
                if (string.IsNullOrWhiteSpace(serverError))
                {
                    serverError = $"Online activation failed ({(int)response.StatusCode}).";
                }

                errorMessage = serverError.Trim();
                return false;
            }

            if (payload is null || string.IsNullOrWhiteSpace(payload.LicenseKey))
            {
                errorMessage = "Online activation response did not include a license key.";
                return false;
            }

            licenseKey = payload.LicenseKey.Trim();
            return true;
        }
        catch (Exception ex)
        {
            errorMessage = $"Online activation failed: {ex.Message}";
            return false;
        }
    }

    private LicenseValidationResult ValidateLicenseKey(string licenseKey)
    {
        var normalized = (licenseKey ?? string.Empty).Trim();
        if (string.IsNullOrWhiteSpace(normalized))
        {
            return LicenseValidationResult.Invalid("License key is required.");
        }

#if DEBUG
        if (string.Equals(normalized, TestLicenseKey, StringComparison.OrdinalIgnoreCase))
        {
            return LicenseValidationResult.Valid("Corevanta Test License", DateTime.UtcNow.AddYears(1));
        }
#endif

        // Signed format for production:
        // CV2.<base64url(payloadJson)>.<base64url(rsaSha256Signature(payloadJson))>
        var parts = normalized.Split('.', StringSplitOptions.RemoveEmptyEntries);
        if (parts.Length != 3 || !parts[0].Equals("CV2", StringComparison.OrdinalIgnoreCase))
        {
            return LicenseValidationResult.Invalid("License format is invalid.");
        }

        byte[] payloadBytes;
        byte[] signatureBytes;
        try
        {
            payloadBytes = Base64UrlDecode(parts[1]);
            signatureBytes = Base64UrlDecode(parts[2]);
        }
        catch
        {
            return LicenseValidationResult.Invalid("License format is invalid.");
        }

        if (!TryGetPublicKeyPem(out var publicKeyPem))
        {
            return LicenseValidationResult.Invalid(
                $"License verifier key is not configured. Set {LicensePublicKeyPemEnvironmentVariable}.");
        }

        using var rsa = RSA.Create();
        try
        {
            rsa.ImportFromPem(publicKeyPem);
        }
        catch
        {
            return LicenseValidationResult.Invalid("License verifier public key is invalid.");
        }

        var signatureValid = rsa.VerifyData(
            payloadBytes,
            signatureBytes,
            HashAlgorithmName.SHA256,
            RSASignaturePadding.Pkcs1);
        if (!signatureValid)
        {
            return LicenseValidationResult.Invalid("License signature is invalid.");
        }

        SignedLicensePayload? payload;
        try
        {
            payload = JsonSerializer.Deserialize<SignedLicensePayload>(payloadBytes);
        }
        catch
        {
            payload = null;
        }

        if (payload is null)
        {
            return LicenseValidationResult.Invalid("License payload is invalid.");
        }

        if (payload.ExpiresUtc.HasValue && payload.ExpiresUtc.Value < DateTime.UtcNow)
        {
            return LicenseValidationResult.Invalid("License has expired.");
        }

        var localMachineHash = ComputeMachineHash();
        if (string.IsNullOrWhiteSpace(payload.MachineHash))
        {
            return LicenseValidationResult.Invalid("License is missing a machine hash.");
        }

        if (!payload.MachineHash.Equals(localMachineHash, StringComparison.OrdinalIgnoreCase))
        {
            return LicenseValidationResult.Invalid("License is not valid for this machine.");
        }

        var licensedTo = string.IsNullOrWhiteSpace(payload.LicensedTo) ? "Corevanta Customer" : payload.LicensedTo.Trim();
        return LicenseValidationResult.Valid(licensedTo, payload.ExpiresUtc);
    }

    private StoredLicense? ReadStoredLicense()
    {
        if (!File.Exists(_licenseFilePath))
        {
            return null;
        }

        try
        {
            var json = File.ReadAllText(_licenseFilePath, Encoding.UTF8);
            return JsonSerializer.Deserialize<StoredLicense>(json);
        }
        catch
        {
            return null;
        }
    }

    private static string ComputeMachineHash()
    {
        var machineGuid = string.Empty;
        try
        {
            machineGuid = Registry.GetValue(
                @"HKEY_LOCAL_MACHINE\SOFTWARE\Microsoft\Cryptography",
                "MachineGuid",
                string.Empty) as string ?? string.Empty;
        }
        catch
        {
            // Ignore and fall back below.
        }

        var machineData = string.IsNullOrWhiteSpace(machineGuid)
            ? string.Join("|",
                Environment.MachineName ?? string.Empty,
                Environment.UserDomainName ?? string.Empty,
                Environment.OSVersion.VersionString ?? string.Empty,
                Environment.ProcessorCount.ToString())
            : string.Join("|",
                machineGuid.Trim(),
                Environment.OSVersion.VersionString ?? string.Empty);

        var bytes = Encoding.UTF8.GetBytes(machineData);
        var hash = SHA256.HashData(bytes);
        return Convert.ToHexString(hash);
    }

    private static bool TryGetPublicKeyPem(out string pem)
    {
        pem = Environment.GetEnvironmentVariable(LicensePublicKeyPemEnvironmentVariable)?.Trim() ?? string.Empty;
        if (!string.IsNullOrWhiteSpace(pem))
        {
            return true;
        }

        pem = DefaultLicensePublicKeyPem.Trim();
        return !string.IsNullOrWhiteSpace(pem);
    }

    private static byte[] Base64UrlDecode(string value)
    {
        var base64 = value.Replace('-', '+').Replace('_', '/');
        var padding = 4 - (base64.Length % 4);
        if (padding is > 0 and < 4)
        {
            base64 = base64 + new string('=', padding);
        }

        return Convert.FromBase64String(base64);
    }

    private sealed class StoredLicense
    {
        public string LicenseKey { get; init; } = string.Empty;
        public string EmailOrUser { get; init; } = string.Empty;
        public DateTime ActivatedUtc { get; init; }
    }

    private sealed class SignedLicensePayload
    {
        public string LicensedTo { get; init; } = string.Empty;
        public DateTime? ExpiresUtc { get; init; }
        public string MachineHash { get; init; } = string.Empty;
    }

    private sealed record OnlineActivationRequest(string Email, string MachineHash);

    private sealed class OnlineActivationResponse
    {
        public string LicenseKey { get; init; } = string.Empty;
        public string Message { get; init; } = string.Empty;
        public string Error { get; init; } = string.Empty;
    }
}

public sealed record LicenseValidationResult(bool IsValid, string Message, DateTime? ExpiresUtc)
{
    public static LicenseValidationResult Valid(string licensedTo, DateTime? expiresUtc) =>
        new(true, $"Licensed to {licensedTo}.", expiresUtc);

    public static LicenseValidationResult Invalid(string message) =>
        new(false, message, null);
}
