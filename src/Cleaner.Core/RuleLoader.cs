using System.Text.Json;

namespace Cleaner.Core;

public static class RuleLoader
{
    private static readonly JsonSerializerOptions Options = new()
    {
        PropertyNameCaseInsensitive = true,
        ReadCommentHandling = JsonCommentHandling.Skip,
        AllowTrailingCommas = true,
    };

    /// <summary>Loads the rules compiled into this assembly (Rules/*.json).</summary>
    public static IReadOnlyList<CleanRule> LoadBuiltIn()
    {
        var asm = typeof(RuleLoader).Assembly;
        var rules = new List<CleanRule>();

        foreach (var name in asm.GetManifestResourceNames().Where(n => n.EndsWith(".json", StringComparison.OrdinalIgnoreCase)).Order())
        {
            using var stream = asm.GetManifestResourceStream(name)!;
            using var reader = new StreamReader(stream);
            rules.AddRange(Parse(reader.ReadToEnd()));
        }

        Validate(rules);
        return rules;
    }

    public static IReadOnlyList<CleanRule> Parse(string json) =>
        JsonSerializer.Deserialize<List<CleanRule>>(json, Options) ?? [];

    /// <summary>Throws if rules are structurally unsafe or ambiguous.</summary>
    public static void Validate(IEnumerable<CleanRule> rules)
    {
        var ids = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var rule in rules)
        {
            if (!ids.Add(rule.Id))
                throw new InvalidDataException($"Duplicate rule id '{rule.Id}'.");

            if (rule.Kind == RuleKind.Files && rule.Targets.Count == 0)
                throw new InvalidDataException($"Rule '{rule.Id}' has no targets.");

            foreach (var t in rule.Targets)
            {
                if (!t.Path.StartsWith('%'))
                    throw new InvalidDataException($"Rule '{rule.Id}': target paths must start with a token like %LOCALAPPDATA%.");
                if (t.Path.Contains(".."))
                    throw new InvalidDataException($"Rule '{rule.Id}': '..' is not allowed in target paths.");
                if (t.Patterns.Count == 0 || t.Patterns.Any(p => p.Contains('\\') || p.Contains('/') || p.Contains("..")))
                    throw new InvalidDataException($"Rule '{rule.Id}': patterns must be file-name patterns only.");
                if (t.MinAgeHours < 0)
                    throw new InvalidDataException($"Rule '{rule.Id}': minAgeHours cannot be negative.");
            }
        }
    }
}
