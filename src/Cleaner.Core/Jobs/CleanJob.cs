using Cleaner.Core.Settings;

namespace Cleaner.Core.Jobs;

public enum CleanPreset
{
    /// <summary>Categories on by default, minus the Recycle Bin. Suited to unattended runs.</summary>
    Safe,

    /// <summary>The categories ticked in the app.</summary>
    Selected,
}

public enum CleanJobOutcome
{
    Cleaned,
    DryRun,

    /// <summary>The settings file was damaged and hasn't been checked: nothing was deleted.</summary>
    SettingsNeedReview,
}

public sealed record CleanJobResult(CleanJobOutcome Outcome, ScanReport Scan, CleanReport? Clean, IReadOnlyList<CleanRule> Rules);

/// <summary>
/// One unattended scan-and-clean, as the command line runs it. Always applies the user's saved
/// settings and refuses to delete anything while a damaged settings file is waiting for review.
/// </summary>
public sealed class CleanJob
{
    private readonly Scanner _scanner;
    private readonly CleanRunner _runner;
    private readonly SettingsStore _settings;

    public CleanJob(Scanner scanner, CleanRunner runner, SettingsStore settings)
    {
        _scanner = scanner;
        _runner = runner;
        _settings = settings;
    }

    public static IReadOnlyList<CleanRule> SelectRules(IEnumerable<CleanRule> all, CleanPreset preset, AppSettings settings) =>
        preset switch
        {
            // The Recycle Bin is never emptied unattended: people use it as an undo.
            CleanPreset.Safe => all.Where(r => r.EnabledByDefault && r.Kind != RuleKind.RecycleBin).ToList(),
            _ => all.Where(settings.IsSelected).ToList(),
        };

    public CleanJobResult Run(IEnumerable<CleanRule> allRules, CleanPreset preset, bool dryRun, CancellationToken ct = default)
    {
        var settings = _settings.Load();
        var needsReview = _settings.LoadedDefaultsAfterError || _settings.NeedsReview;
        var rules = SelectRules(allRules, preset, settings);

        _scanner.Options = settings.ToScanOptions();
        var scan = _scanner.Scan(rules, ct: ct);

        if (needsReview)
            return new CleanJobResult(CleanJobOutcome.SettingsNeedReview, scan, null, rules);
        if (dryRun)
            return new CleanJobResult(CleanJobOutcome.DryRun, scan, null, rules);

        var ids = rules.Select(r => r.Id).ToHashSet(StringComparer.OrdinalIgnoreCase);
        var clean = _runner.Clean(scan, ids, settings.ToScanOptions(), ct);
        return new CleanJobResult(CleanJobOutcome.Cleaned, scan, clean, rules);
    }
}
