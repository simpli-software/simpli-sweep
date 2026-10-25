using System.Text.Json.Serialization;

namespace Cleaner.Core;

public enum RuleKind
{
    Files,
    RecycleBin,
}

/// <summary>A cleaning rule loaded from JSON. Rules are data; the engine never trusts them blindly.</summary>
public sealed record CleanRule
{
    public required string Id { get; init; }
    public required string Name { get; init; }
    public string Description { get; init; } = "";
    public string Category { get; init; } = "Other";
    public bool RequiresAdmin { get; init; }
    public bool EnabledByDefault { get; init; } = true;

    [JsonConverter(typeof(JsonStringEnumConverter<RuleKind>))]
    public RuleKind Kind { get; init; } = RuleKind.Files;

    public IReadOnlyList<RuleTarget> Targets { get; init; } = [];
}

/// <summary>
/// One folder a rule cleans. <see cref="Path"/> starts with a known token (e.g. %LOCALAPPDATA%)
/// and may use '*' in directory segments to match profiles (e.g. "User Data\*\Cache").
/// </summary>
public sealed record RuleTarget
{
    public required string Path { get; init; }
    public IReadOnlyList<string> Patterns { get; init; } = ["*"];
    public bool Recursive { get; init; }
    public double MinAgeHours { get; init; }
    public bool RemoveEmptyDirs { get; init; }

    /// <summary>Folder names directly under the target path that are never scanned (plain names only).</summary>
    public IReadOnlyList<string> SkipFolders { get; init; } = [];
}
