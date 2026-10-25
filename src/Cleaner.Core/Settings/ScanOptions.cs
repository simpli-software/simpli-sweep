using System.IO.Enumeration;

namespace Cleaner.Core.Settings;

/// <summary>
/// User choices applied to every scan and clean. They can only make cleaning stricter:
/// a longer minimum age and things to leave alone. Nothing here can widen what a rule removes.
/// </summary>
public sealed record ScanOptions
{
    public static ScanOptions Default { get; } = new();

    /// <summary>Files must be at least this old, on top of each rule's own minimum. 0 = rule defaults.</summary>
    public double MinimumAgeHours { get; init; }

    /// <summary>Folders whose contents are never removed (absolute paths).</summary>
    public IReadOnlyList<string> ExcludedFolders { get; init; } = [];

    /// <summary>File-name patterns that are never removed, e.g. "*.log".</summary>
    public IReadOnlyList<string> ExcludedPatterns { get; init; } = [];

    public double EffectiveMinAge(double ruleMinAgeHours) => Math.Max(ruleMinAgeHours, Math.Max(0, MinimumAgeHours));

    /// <summary>True if the file at <paramref name="path"/> must be left alone.</summary>
    public bool IsExcluded(string path)
    {
        var name = Path.GetFileName(path);
        foreach (var pattern in ExcludedPatterns)
        {
            if (FileSystemName.MatchesSimpleExpression(pattern, name, ignoreCase: true))
                return true;
        }

        foreach (var folder in ExcludedFolders)
        {
            var f = Path.TrimEndingDirectorySeparator(folder);
            if (path.StartsWith(f + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase)
                || (f.EndsWith(Path.DirectorySeparatorChar) && path.StartsWith(f, StringComparison.OrdinalIgnoreCase)))
            {
                return true;
            }
        }
        return false;
    }

    /// <summary>Returns a problem description, or null if the folder can be excluded.</summary>
    public static string? ValidateFolder(string folder) =>
        string.IsNullOrWhiteSpace(folder) || !Path.IsPathFullyQualified(folder) ? "Choose a full folder path, like C:\\Projects." : null;

    /// <summary>Returns a problem description, or null if the pattern can be excluded.</summary>
    public static string? ValidatePattern(string pattern) =>
        string.IsNullOrWhiteSpace(pattern) || pattern.IndexOfAny(['\\', '/', ':']) >= 0 || pattern.Contains("..")
            ? "Use a file-name pattern like *.log or report-*.pdf (no folders)."
            : null;
}
