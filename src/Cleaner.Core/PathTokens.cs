namespace Cleaner.Core;

/// <summary>
/// Expands the small, fixed set of tokens rules may use. Unknown tokens are an error,
/// so a rule can never point at an arbitrary environment variable.
/// </summary>
public sealed class PathTokens
{
    private readonly IReadOnlyDictionary<string, string> _values;

    public PathTokens(IReadOnlyDictionary<string, string> values)
    {
        _values = new Dictionary<string, string>(values, StringComparer.OrdinalIgnoreCase);
    }

    public static PathTokens FromEnvironment()
    {
        static string Folder(Environment.SpecialFolder f) => Environment.GetFolderPath(f);

        return new PathTokens(new Dictionary<string, string>
        {
            ["TEMP"] = Path.GetTempPath(),
            ["LOCALAPPDATA"] = Folder(Environment.SpecialFolder.LocalApplicationData),
            ["APPDATA"] = Folder(Environment.SpecialFolder.ApplicationData),
            ["USERPROFILE"] = Folder(Environment.SpecialFolder.UserProfile),
            ["PROGRAMDATA"] = Folder(Environment.SpecialFolder.CommonApplicationData),
            ["WINDIR"] = Folder(Environment.SpecialFolder.Windows),
        });
    }

    public string this[string token] => _values[token];

    /// <summary>Returns the expanded path, or null if the template is malformed or uses an unknown token.</summary>
    public string? Expand(string template)
    {
        if (!template.StartsWith('%'))
            return null;

        var end = template.IndexOf('%', 1);
        if (end < 0)
            return null;

        var token = template[1..end];
        if (!_values.TryGetValue(token, out var value) || string.IsNullOrWhiteSpace(value))
            return null;

        var rest = template[(end + 1)..].TrimStart('\\', '/');
        if (rest.Contains('%'))
            return null;

        return rest.Length == 0 ? value : System.IO.Path.Combine(value, rest);
    }
}
