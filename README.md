# Corevanta

Corevanta is a source-available Windows desktop engineering tool for legacy Honeywell TDC DCS documentation, search, chart viewing, CL program browsing, logic block viewing, hardware references, PLC integration, and change-management review.

[Download the signed installer](https://github.com/CorevantaLLC/corevanta-public/releases/latest) | [Purchase a commercial license](https://buy.stripe.com/9B64gz5T7cKm86bb4Pfbq00) | [Product website](https://corevantallc.com) | [User manual](./Corevanta%20User%20Manual.pdf)

## Licensing

Corevanta is **source-available, not open source**.

| Use | License | Price |
| --- | --- | ---: |
| Personal, educational, and other noncommercial use | [PolyForm Noncommercial License 1.0.0](./LICENSE.md) | Free |
| Commercial, consulting, or business operational use | [Corevanta commercial license](./COMMERCIAL-LICENSING.md) | $5,000 USD per computer |

Commercial use is not granted by the PolyForm Noncommercial License. Purchase a commercial license before using Corevanta for consulting, production, revenue-generating, or other commercial work.

Required Notice: Copyright 2026 Corevanta LLC.

## Download

Most users should download the latest digitally signed Windows installer from [GitHub Releases](https://github.com/CorevantaLLC/corevanta-public/releases/latest). The official installer is signed by Corevanta LLC.

The no-activation build is licensed only for noncommercial use. A purchased commercial license uses the licensed Corevanta build and is activated with the purchaser email.

## Build from source

Requirements:

- Windows 10 or Windows 11, 64-bit
- .NET 10 SDK

```powershell
dotnet restore .\src\Corevanta.Desktop\Corevanta.Desktop.GitHubFree.csproj
dotnet build .\src\Corevanta.Desktop\Corevanta.Desktop.GitHubFree.csproj -c Release
dotnet run --project .\src\Corevanta.Desktop\Corevanta.Desktop.GitHubFree.csproj -c Release
```

The public project builds the noncommercial, no-activation edition. Building or modifying the source does not grant commercial-use rights.

## Documentation

- [Corevanta User Manual](./Corevanta%20User%20Manual.pdf)
- [Corevanta Quick Start Guide](./Corevanta%20Quick%20Start%20Guide.pdf)
- [Commercial licensing](./COMMERCIAL-LICENSING.md)
- [Third-party notices](./THIRD_PARTY_NOTICES.md)

## Repository scope

This repository contains the Corevanta desktop application source. Licensing infrastructure, signing infrastructure, customer data, test backups, and internal utilities are not included.

## Contributions and support

Corevanta LLC is not accepting external code contributions at this time. Please use GitHub Issues for reproducible software defects and email [customerservice@corevantallc.com](mailto:customerservice@corevantallc.com) for licensing or commercial support.

## Disclaimer

Corevanta is an independent product of Corevanta LLC. It is not affiliated with, endorsed by, or sponsored by Honeywell International Inc. Honeywell, TDC, TDC 2000, and TDC 3000 are trademarks of Honeywell International Inc.
