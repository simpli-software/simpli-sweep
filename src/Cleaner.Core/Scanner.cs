using System.IO.Enumeration;

using Cleaner.Core.Settings;

namespace Cleaner.Core;

/// <summary>Finds what each rule would delete. Read-only: never modifies the file system.</summary>
public sealed class Scanner
{
    private static readonly EnumerationOptions Shallow = new()
    {
        RecurseSubdirectories = false,
        IgnoreInaccessible = true,
        AttributesToSkip = FileAttributes.ReparsePoint,
        MatchCasing = MatchCasing.CaseInsensitive,
    };

    private readonly PathTokens _tokens;
    private readonly SafetyGuard _guard;
    private readonly IRecycleBin _recycleBin;
    private readonly TimeProvider _clock;
    private readonly bool _isElevated;

    public Scanner(PathTokens tokens, SafetyGuard guard, IRecycleBin recycleBin, TimeProvider clock, bool isElevated)
    {
        _tokens = tokens;
        _guard = guard;
        _recycleBin = recycleBin;
        _clock = clock;
        _isElevated = isElevated;
    }

    /// <param name="progress">Receives each rule just before it is scanned.</param>
    /// <summary>The user's settings for the next scan (minimum age, exclusions). Only ever stricter than the rules.</summary>
    public ScanOptions Options { get; set; } = ScanOptions.Default;

    public ScanReport Scan(IEnumerable<CleanRule> rules, IProgress<CleanRule>? progress = null, CancellationToken ct = default)
    {
        var options = Options;
        return new(rules.Select(r =>
        {
            progress?.Report(r);
            return ScanRule(r, options, ct);
        }).ToList(), options);
    }

    public RuleScanResult ScanRule(CleanRule rule, CancellationToken ct = default) => ScanRule(rule, Options, ct);

    private RuleScanResult ScanRule(CleanRule rule, ScanOptions options, CancellationToken ct)
    {
        if (rule.RequiresAdmin && !_isElevated)
            return new RuleScanResult(rule, RuleStatus.NeedsAdmin, [], 0);

        if (rule.Kind == RuleKind.RecycleBin)
        {
            var (bytes, items) = _recycleBin.Query();
            return new RuleScanResult(rule, bytes > 0 ? RuleStatus.Ok : RuleStatus.NothingFound, [], bytes, items);
        }

        var files = new List<FileEntry>();
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var target in rule.Targets)
        {
            var template = _tokens.Expand(target.Path);
            if (template is null)
                return new RuleScanResult(rule, RuleStatus.Invalid, [], 0);

            foreach (var root in ExpandWildcardDirs(template))
            {
                ct.ThrowIfCancellationRequested();
                if (!_guard.IsAllowedRoot(root))
                    continue;

                foreach (var file in EnumerateFiles(root, target, options, ct))
                {
                    if (seen.Add(file.Path))
                        files.Add(file);
                }
            }
        }

        var total = files.Sum(f => f.Size);
        return new RuleScanResult(rule, files.Count > 0 ? RuleStatus.Ok : RuleStatus.NothingFound, files, total);
    }

    /// <summary>Expands '*' / '?' in directory segments, skipping junctions and symlinks.</summary>
    internal static IEnumerable<string> ExpandWildcardDirs(string template)
    {
        var full = Path.GetFullPath(template);
        var rootPart = Path.GetPathRoot(full)!;
        var segments = full[rootPart.Length..].Split(Path.DirectorySeparatorChar, StringSplitOptions.RemoveEmptyEntries);

        IEnumerable<string> current = [rootPart];
        foreach (var segment in segments)
        {
            var seg = segment;
            current = seg.IndexOfAny(['*', '?']) >= 0
                ? current.SelectMany(dir => Directory.Exists(dir) ? Directory.EnumerateDirectories(dir, seg, Shallow) : [])
                : current.Select(dir => Path.Combine(dir, seg));
        }

        return current.Where(d => Directory.Exists(d) && !IsReparsePoint(d));
    }

    private IEnumerable<FileEntry> EnumerateFiles(string root, RuleTarget target, ScanOptions options, CancellationToken ct)
    {
        var minAge = options.EffectiveMinAge(target.MinAgeHours);
        var cutoff = _clock.GetUtcNow().UtcDateTime.AddHours(-minAge);
        var removeEmpty = target.RemoveEmptyDirs && target.Recursive;
        var pending = new Stack<string>();
        pending.Push(root);

        while (pending.Count > 0)
        {
            ct.ThrowIfCancellationRequested();
            var dir = pending.Pop();

            // One listing per folder gives files (with size and time) and subfolders together.
            foreach (var entry in SafeEnumerate(() => new DirectoryInfo(dir).EnumerateFileSystemInfos("*", Shallow)))
            {
                if (entry is DirectoryInfo sub)
                {
                    var skipped = string.Equals(dir, root, StringComparison.OrdinalIgnoreCase)
                        && target.SkipFolders.Contains(sub.Name, StringComparer.OrdinalIgnoreCase);
                    if (target.Recursive && !skipped)
                        pending.Push(sub.FullName);
                    continue;
                }

                var file = (FileInfo)entry;
                if (!MatchesAny(file.Name, target.Patterns)
                    || file.LastWriteTimeUtc > cutoff
                    || !_guard.IsAllowedFile(file.FullName, root)
                    || options.IsExcluded(file.FullName))
                {
                    continue;
                }

                yield return new FileEntry(file.FullName, root, file.Length, file.LastWriteTimeUtc, minAge, removeEmpty);
            }
        }
    }

    private static bool MatchesAny(string name, IReadOnlyList<string> patterns)
    {
        foreach (var pattern in patterns)
        {
            if (FileSystemName.MatchesSimpleExpression(pattern, name, ignoreCase: true))
                return true;
        }
        return false;
    }

    private static IEnumerable<T> SafeEnumerate<T>(Func<IEnumerable<T>> source)
    {
        try { return source().ToList(); }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException) { return []; }
    }

    internal static bool IsReparsePoint(string path)
    {
        try { return File.GetAttributes(path).HasFlag(FileAttributes.ReparsePoint); }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException) { return true; }
    }
}
