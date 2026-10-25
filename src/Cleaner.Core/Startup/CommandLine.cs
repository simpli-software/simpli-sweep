namespace Cleaner.Core.Startup;

public static class CommandLine
{
    private static readonly string[] Extensions = [".exe", ".com", ".bat", ".cmd"];

    /// <summary>Best-effort executable path from a Run command line, or null.</summary>
    public static string? ExecutablePath(string command)
    {
        var expanded = Environment.ExpandEnvironmentVariables(command.Trim());
        if (expanded.Length == 0)
            return null;

        string candidate;
        if (expanded[0] == '"')
        {
            var end = expanded.IndexOf('"', 1);
            candidate = end > 1 ? expanded[1..end] : expanded.Trim('"');
        }
        else
        {
            // Unquoted paths may contain spaces: cut after the first known extension.
            candidate = expanded;
            foreach (var ext in Extensions)
            {
                var i = expanded.IndexOf(ext, StringComparison.OrdinalIgnoreCase);
                if (i > 0)
                {
                    candidate = expanded[..(i + ext.Length)];
                    break;
                }
            }
        }

        return Path.IsPathFullyQualified(candidate) ? candidate : null;
    }
}
