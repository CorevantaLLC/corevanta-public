using System.Text.RegularExpressions;
using System.IO;

namespace Corevanta.Desktop.Services;

public sealed class EbParser
{
    private static readonly Regex AssignmentRegex =
        new(@"([$\w]+(?:\(\d+\))?)\s*=\s*(""[^""]*""|\S+)", RegexOptions.Compiled);

    public IReadOnlyList<ParsedEntity> ParseFile(string filePath)
    {
        var entities = new List<ParsedEntity>();
        ParsedEntityBuilder? current = null;
        var sortOrder = 0;

        foreach (var rawLine in File.ReadLines(filePath))
        {
            var line = rawLine.TrimEnd();

            if (line.StartsWith("{SYSTEM ENTITY", StringComparison.OrdinalIgnoreCase))
            {
                if (current is not null)
                {
                    entities.Add(current.Build());
                }

                current = new ParsedEntityBuilder { SystemEntityHeader = line };
                sortOrder = 0;
                continue;
            }

            if (current is null)
            {
                continue;
            }

            if (line.StartsWith("&T", StringComparison.OrdinalIgnoreCase))
            {
                current.EntityType = line.Length > 2 ? line[2..].Trim() : string.Empty;
                continue;
            }

            if (line.StartsWith("&N", StringComparison.OrdinalIgnoreCase))
            {
                current.EntityName = line.Length > 2 ? line[2..].Trim() : string.Empty;
                continue;
            }

            var matches = AssignmentRegex.Matches(line);
            if (matches.Count == 0)
            {
                continue;
            }

            foreach (Match match in matches)
            {
                current.Properties.Add(new ParsedProperty
                {
                    PropertyName = match.Groups[1].Value.Trim(),
                    PropertyValue = NormalizeValue(match.Groups[2].Value),
                    SortOrder = sortOrder++
                });
            }
        }

        if (current is not null)
        {
            entities.Add(current.Build());
        }

        return entities;
    }

    private static string NormalizeValue(string value)
    {
        var trimmed = value.Trim();
        if (trimmed.Length >= 2 && trimmed.StartsWith('"') && trimmed.EndsWith('"'))
        {
            return trimmed[1..^1];
        }

        return trimmed;
    }

    private sealed class ParsedEntityBuilder
    {
        public string SystemEntityHeader { get; init; } = string.Empty;
        public string? EntityType { get; set; }
        public string? EntityName { get; set; }
        public List<ParsedProperty> Properties { get; } = [];

        public ParsedEntity Build() =>
            new()
            {
                SystemEntityHeader = SystemEntityHeader,
                EntityType = EntityType,
                EntityName = EntityName,
                Properties = Properties
            };
    }
}

public sealed class ParsedEntity
{
    public string SystemEntityHeader { get; init; } = string.Empty;
    public string? EntityType { get; init; }
    public string? EntityName { get; init; }
    public List<ParsedProperty> Properties { get; init; } = [];
}

public sealed class ParsedProperty
{
    public string PropertyName { get; init; } = string.Empty;
    public string PropertyValue { get; init; } = string.Empty;
    public int SortOrder { get; init; }
}
