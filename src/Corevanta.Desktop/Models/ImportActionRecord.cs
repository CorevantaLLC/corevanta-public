namespace Corevanta.Desktop.Models;

public sealed class ImportActionRecord
{
    public long Id { get; init; }
    public string ActionType { get; init; } = string.Empty;
    public string Title { get; init; } = string.Empty;
    public string Detail { get; init; } = string.Empty;
    public string TargetKey { get; init; } = string.Empty;
    public DateTime CreatedAtUtc { get; init; }

    public string DisplayLabel
    {
        get
        {
            var when = CreatedAtUtc.ToLocalTime().ToString("yyyy-MM-dd HH:mm:ss");
            if (string.IsNullOrWhiteSpace(Detail))
            {
                return $"[{when}] {Title}";
            }

            return $"[{when}] {Title} - {Detail}";
        }
    }
}
