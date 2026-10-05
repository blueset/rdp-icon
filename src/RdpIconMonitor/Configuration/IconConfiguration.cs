using System.Text.Json;
using System.Text.Json.Serialization;

namespace RdpIconMonitor.Configuration;

public sealed record IconStyle(double? Hue, string? SmallText, string? LargeText);

public sealed record IconRule(string Name, double? Hue = null, string? SmallText = null, string? LargeText = null)
{
    [JsonIgnore]
    public IconStyle Style => new(Hue, SmallText, LargeText);
}

public sealed record IconConfiguration(IconRule[] Instances)
{
    public static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow,
        WriteIndented = true
    };

    public static IconConfiguration Parse(string json)
    {
        var config = JsonSerializer.Deserialize<IconConfiguration>(json, JsonOptions)
            ?? throw new InvalidDataException("Configuration must be an object.");
        if (config.Instances is null)
            throw new InvalidDataException("'instances' must be an array.");
        var names = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var rules = new List<IconRule>();
        foreach (var rule in config.Instances)
        {
            if (rule is null || string.IsNullOrWhiteSpace(rule.Name))
                throw new InvalidDataException("Every instance needs a nonempty 'name'.");
            if (!names.Add(rule.Name))
                throw new InvalidDataException($"Duplicate instance name: {rule.Name}");
            if (rule.Hue is double hue && !double.IsFinite(hue))
                throw new InvalidDataException($"Hue must be finite: {rule.Name}");
            rules.Add(rule with
            {
                Hue = rule.Hue is double value ? (value % 360 + 360) % 360 : null,
                SmallText = NormalizeText(rule.SmallText, rule.Name),
                LargeText = NormalizeText(rule.LargeText, rule.Name)
            });
        }
        return new(rules.ToArray());
    }

    private static string? NormalizeText(string? text, string name)
    {
        if (text?.Any(char.IsControl) == true)
            throw new InvalidDataException($"Text must be a single line without control characters: {name}");
        return string.IsNullOrWhiteSpace(text) ? null : text;
    }
}
