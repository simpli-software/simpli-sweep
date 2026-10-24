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
        var ok = _recycleBin.Empty();
        return new RuleCleanResult(result.Rule.Id, 0, ok ? result.Bytes : 0, ok ? 0 : 1);
    }

    private RuleCleanResult CleanFiles(RuleScanResult result, CancellationToken ct)
    {
        int deleted = 0, skipped = 0;
        long freed = 0;
        var now = _clock.GetUtcNow().UtcDateTime;

        foreach (var file in result.Files)
        {
            ct.ThrowIfCancellationRequested();
            if (TryDelete(file, now))
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
            RemoveEmptyDirs(root);

        return new RuleCleanResult(result.Rule.Id, deleted, freed, skipped);
    }

    private bool TryDelete(FileEntry file, DateTime now)
    {
        try
        {
            if (!_guard.IsAllowedFile(file.Path, file.Root))
                return false;

            var info = new FileInfo(file.Path);
            if (!info.Exists || info.Attributes.HasFlag(FileAttributes.ReparsePoint))
                return false;

            // The file changed since the scan (or became too new): leave it alone.
            if (info.LastWriteTimeUtc != file.LastWriteUtc || info.LastWriteTimeUtc > now.AddHours(-file.MinAgeHours))
                return false;

            // A parent folder may have been swapped for a junction since the scan.
            if (HasReparseAncestor(info.Directory, file.Root))
                return false;

            info.Delete();
            return true;
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            return false; // locked or in use
        }
    }

    private static bool HasReparseAncestor(DirectoryInfo? dir, string root)
    {
        var normalizedRoot = SafetyGuard.Normalize(root);
        for (var d = dir; d is not null; d = d.Parent)
        {
            if (d.Attributes.HasFlag(FileAttributes.ReparsePoint))
                return true;
            if (SafetyGuard.Normalize(d.FullName).Equals(normalizedRoot, StringComparison.OrdinalIgnoreCase))
                return false;
        }
        return true; // walked past the drive root without meeting the rule root
    }

    private void RemoveEmptyDirs(string root)
    {
        if (!_guard.IsAllowedRoot(root) || Scanner.IsReparsePoint(root))
            return;

        var options = new EnumerationOptions
        {
            RecurseSubdirectories = true,
            IgnoreInaccessible = true,
            AttributesToSkip = FileAttributes.ReparsePoint,
        };

        List<string> dirs;
        try { dirs = Directory.EnumerateDirectories(root, "*", options).ToList(); }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException) { return; }

        // Deepest first so parents empty out after their children go.
        foreach (var dir in dirs.OrderByDescending(d => d.Length))
        {
            try
            {
                if (_guard.IsAllowedFile(dir, root)
                    && !Scanner.IsReparsePoint(dir)
                    && !Directory.EnumerateFileSystemEntries(dir).Any())
                {
                    Directory.Delete(dir);
                }
            }
            catch (Exception e) when (e is IOException or UnauthorizedAccessException) { }
        }
    }
}
