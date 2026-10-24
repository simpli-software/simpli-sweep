namespace Cleaner.Core;

public enum RuleStatus
{
    Ok,
    NeedsAdmin,
    Invalid,
    NothingFound,
}

public sealed record FileEntry(string Path, string Root, long Size, DateTime LastWriteUtc, double MinAgeHours, bool RemoveEmptyDirs);

public sealed record RuleScanResult(CleanRule Rule, RuleStatus Status, IReadOnlyList<FileEntry> Files, long Bytes)
{
    public int FileCount => Files.Count;
}

public sealed record ScanReport(IReadOnlyList<RuleScanResult> Results)
{
    public long TotalBytes => Results.Sum(r => r.Bytes);
    public int TotalFiles => Results.Sum(r => r.FileCount);
}

public sealed record RuleCleanResult(string RuleId, int Deleted, long BytesFreed, int Skipped);

/// <summary>Counts and sizes only — never paths or file names, which can contain personal data.</summary>
public sealed record CleanReport(IReadOnlyList<RuleCleanResult> Results)
{
    public int Deleted => Results.Sum(r => r.Deleted);
    public long BytesFreed => Results.Sum(r => r.BytesFreed);
    public int Skipped => Results.Sum(r => r.Skipped);
}
