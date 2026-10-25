namespace Cleaner.Core;

/// <summary>Deletes what a scan found, re-checking every file at delete time.</summary>
public sealed class CleanRunner
{
    private readonly SafetyGuard _guard;
    private readonly IRecycleBin _recycleBin;
    private readonly TimeProvider _clock;

    public CleanRunner(SafetyGuard guard, IRecycleBin recycleBin, TimeProvider clock)
    {
        _guard = guard;
        _recycleBin = recycleBin;
        _clock = clock;
    }

    /// <param name="selectedRuleIds">Only rules the user explicitly ticked are cleaned.</param>
    public CleanReport Clean(ScanReport scan, IReadOnlySet<string> selectedRuleIds, CancellationToken ct = default)
    {
        var results = new List<RuleCleanResult>();

        foreach (var result in scan.Results)
        {
            if (result.Status != RuleStatus.Ok || !selectedRuleIds.Contains(result.Rule.Id))
                continue;

            results.Add(result.Rule.Kind == RuleKind.RecycleBin
                ? CleanRecycleBin(result)
                : CleanFiles(result, ct));
        }

        return new CleanReport(results);
    }

    private RuleCleanResult CleanRecycleBin(RuleScanResult result)
    {
        // Anything binned after the scan was never shown to the user: don't empty it unseen.
        var (bytesNow, _) = _recycleBin.Query();
        if (bytesNow > result.Bytes)
            return new RuleCleanResult(result.Rule.Id, 0, 0, 1);

        var ok = _recycleBin.Empty();
        return new RuleCleanResult(result.Rule.Id, 0, ok ? bytesNow : 0, ok ? 0 : 1);
    }

    private RuleCleanResult CleanFiles(RuleScanResult result, CancellationToken ct)
    {
        int deleted = 0, skipped = 0;
        long freed = 0;
        var now = _clock.GetUtcNow().UtcDateTime;
        var realRoots = new Dictionary<string, string?>(StringComparer.OrdinalIgnoreCase);

        foreach (var file in result.Files)
        {
            ct.ThrowIfCancellationRequested();
            if (!realRoots.TryGetValue(file.Root, out var realRoot))
                realRoots[file.Root] = realRoot = RealRoot(file.Root);

            if (realRoot is not null && TryDelete(file, realRoot, now))
            {
                deleted++;
                freed += file.Size;
            }
            else
            {
                skipped++;
            }
        }

        foreach (var root in result.Files.Where(f => f.RemoveEmptyDirs).Select(f => f.Root).Distinct(StringComparer.OrdinalIgnoreCase))
        {
            if (realRoots.GetValueOrDefault(root) is { } realRoot)
                RemoveEmptyDirs(root, realRoot);
        }

        return new RuleCleanResult(result.Rule.Id, deleted, freed, skipped);
    }

    /// <summary>The rule root's real path, or null if it is no longer a safe root.</summary>
    private string? RealRoot(string root)
    {
        if (!_guard.IsAllowedRoot(root) || Scanner.IsReparsePoint(root))
            return null;

        var real = SafeDelete.FinalPath(root);
        return real is not null && _guard.IsAllowedRoot(real) ? SafetyGuard.Normalize(real) : null;
    }

    private bool TryDelete(FileEntry file, string realRoot, DateTime now)
    {
        if (!_guard.IsAllowedFile(file.Path, file.Root))
            return false;

        // The checks run against the object actually opened, so swapping a folder for a
        // junction at any point cannot redirect the delete outside the rule's root.
        return SafeDelete.TryDelete(file.Path, (realPath, attributes, lastWriteUtc) =>
            !attributes.HasFlag(FileAttributes.Directory)
            && _guard.IsAllowedFile(realPath, realRoot)
            && lastWriteUtc == file.LastWriteUtc                      // unchanged since the scan
            && lastWriteUtc <= now.AddHours(-file.MinAgeHours));     // still old enough
    }

    private void RemoveEmptyDirs(string root, string realRoot)
    {
        var options = new EnumerationOptions
        {
            RecurseSubdirectories = true,
            IgnoreInaccessible = true,
            AttributesToSkip = FileAttributes.ReparsePoint,
        };

        List<string> dirs;
        try { dirs = Directory.EnumerateDirectories(root, "*", options).ToList(); }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException) { return; }

        // Deepest first so parents empty out after their children go. Deleting a folder
        // that still has anything in it fails, so only empty folders are removed.
        foreach (var dir in dirs.OrderByDescending(d => d.Length))
        {
            if (_guard.IsAllowedFile(dir, root))
            {
                SafeDelete.TryDelete(dir, (realPath, attributes, _) =>
                    attributes.HasFlag(FileAttributes.Directory) && _guard.IsAllowedFile(realPath, realRoot));
            }
        }
    }
}
