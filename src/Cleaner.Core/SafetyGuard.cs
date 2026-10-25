namespace Cleaner.Core;

/// <summary>
/// Last line of defence: decides whether a path may ever be deleted, independent of any rule.
/// Every scan root and every file is checked here, at scan time and again at delete time.
/// </summary>
public sealed class SafetyGuard
{
    private readonly string[] _denyRoots;
    private readonly string[] _allowExceptions;
    private readonly string[] _neverRoots;

    /// <param name="denyRoots">Folders that must never be touched (and everything under them).</param>
    /// <param name="allowExceptions">Specific folders inside a deny root that rules may clean.</param>
    /// <param name="neverRoots">Folders whose children may be cleaned but which can never be a rule root themselves.</param>
    public SafetyGuard(IEnumerable<string> denyRoots, IEnumerable<string> allowExceptions, IEnumerable<string>? neverRoots = null)
    {
        _denyRoots = Clean(denyRoots);
        _allowExceptions = Clean(allowExceptions);
        _neverRoots = Clean(neverRoots ?? []);
    }

    private static string[] Clean(IEnumerable<string> paths) =>
        paths.Where(p => !string.IsNullOrWhiteSpace(p)).Select(Normalize).Distinct(StringComparer.OrdinalIgnoreCase).ToArray();

    public static SafetyGuard ForCurrentUser(PathTokens tokens)
    {
        static string F(Environment.SpecialFolder f) => Environment.GetFolderPath(f);
        var windir = tokens["WINDIR"];

        var deny = new List<string>
        {
            windir,
            F(Environment.SpecialFolder.ProgramFiles),
            F(Environment.SpecialFolder.ProgramFilesX86),
            F(Environment.SpecialFolder.MyDocuments),
            F(Environment.SpecialFolder.Desktop),
            F(Environment.SpecialFolder.MyPictures),
            F(Environment.SpecialFolder.MyMusic),
            F(Environment.SpecialFolder.MyVideos),
            F(Environment.SpecialFolder.Favorites),
            Path.Combine(tokens["USERPROFILE"], "Downloads"),
            Path.Combine(tokens["USERPROFILE"], "OneDrive"),
            Environment.GetEnvironmentVariable("OneDrive") ?? "",
            Environment.GetEnvironmentVariable("OneDriveCommercial") ?? "",
            Environment.GetEnvironmentVariable("OneDriveConsumer") ?? "",
        };

        var allow = new[]
        {
            Path.Combine(windir, "Temp"),
            Path.Combine(windir, "Minidump"),
            Path.Combine(windir, "SoftwareDistribution", "Download"),
            Path.Combine(windir, @"ServiceProfiles\NetworkService\AppData\Local\Microsoft\Windows\DeliveryOptimization\Cache"),
        };

        var never = new[]
        {
            tokens["USERPROFILE"],
            Path.Combine(tokens["USERPROFILE"], "AppData"),
            tokens["LOCALAPPDATA"],
            tokens["APPDATA"],
            tokens["PROGRAMDATA"],
        };

        return new SafetyGuard(deny, allow, never);
    }

    /// <summary>
    /// A rule root must be absolute, at least two levels deep, not a protected parent folder,
    /// must not contain a protected folder, and must itself be allowed.
    /// </summary>
    public bool IsAllowedRoot(string root)
    {
        if (!Path.IsPathFullyQualified(root))
            return false;

        var normalized = Normalize(root);
        var depth = normalized.Split('\\', StringSplitOptions.RemoveEmptyEntries).Length;
        if (depth < 3) // e.g. "C:\Users" or "C:\Windows" are never rule roots
            return false;

        if (_neverRoots.Any(n => n.Equals(normalized, StringComparison.OrdinalIgnoreCase)))
            return false;

        if (_denyRoots.Any(d => IsUnder(d, normalized)) && !_allowExceptions.Any(a => IsUnder(normalized, a)))
            return false;

        return IsAllowedPath(normalized);
    }

    /// <summary>True if <paramref name="path"/> is inside <paramref name="root"/> and not protected.</summary>
    public bool IsAllowedFile(string path, string root)
    {
        if (!Path.IsPathFullyQualified(path))
            return false;

        var p = Normalize(path);
        var r = Normalize(root);
        return IsUnder(p, r) && !string.Equals(p, r, StringComparison.OrdinalIgnoreCase) && IsAllowedPath(p);
    }

    private bool IsAllowedPath(string normalized)
    {
        if (_allowExceptions.Any(a => IsUnder(normalized, a)))
            return true;

        return !_denyRoots.Any(d => IsUnder(normalized, d));
    }

    public static string Normalize(string path) =>
        Path.TrimEndingDirectorySeparator(Path.GetFullPath(path));

    internal static bool IsUnder(string path, string root) =>
        path.Equals(root, StringComparison.OrdinalIgnoreCase)
        || path.StartsWith(root + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase);
}
