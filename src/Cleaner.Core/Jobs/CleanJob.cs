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

    /// <summary>No settings file for this account, so the user's exclusions can't be applied: nothing was deleted.</summary>
    NoSettings,
}

public sealed record CleanJobOptions
{
    public CleanPreset Preset { get; init; } = CleanPreset.Safe;
    public bool DryRun { get; init; } = true;

    /// <summary>Unattended runs never empty the Recycle Bin unless asked to explicitly.</summary>
    public bool IncludeRecycleBin { get; init; }

    /// <summary>Clean even with no settings file (no exclusions, rule defaults only).</summary>
    public bool AllowMissingSettings { get; init; }
}

/// <param name="SettingsNeedReview">True when the settings file was damaged; a real run would refuse until it is checked.</param>
public sealed record CleanJobResult(
    CleanJobOutcome Outcome, ScanReport Scan, CleanReport? Clean, IReadOnlyList<CleanRule> Rules, bool SettingsNeedReview = false);

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

    public static IReadOnlyList<CleanRule> SelectRules(IEnumerable<CleanRule> all, CleanJobOptions options, AppSettings settings) =>
        all.Where(r => options.Preset == CleanPreset.Safe ? r.EnabledByDefault : settings.IsSelected(r))
            // People use the Recycle Bin as an undo: an unattended run only empties it when told to.
            .Where(r => r.Kind != RuleKind.RecycleBin || options.IncludeRecycleBin)
            .ToList();

    public CleanJobResult Run(IEnumerable<CleanRule> allRules, CleanJobOptions options, CancellationToken ct = default)
    {
        var hasSettings = _settings.Exists;
        var settings = _settings.Load();
        var needsReview = _settings.BlocksCleaning;
        var rules = SelectRules(allRules, options, settings);

        _scanner.Options = settings.ToScanOptions();
        var scan = _scanner.Scan(rules, ct: ct);

        if (options.DryRun)
            return new CleanJobResult(CleanJobOutcome.DryRun, scan, null, rules, needsReview);
        if (needsReview)
            return new CleanJobResult(CleanJobOutcome.SettingsNeedReview, scan, null, rules, true);
        if (!hasSettings && !options.AllowMissingSettings)
            return new CleanJobResult(CleanJobOutcome.NoSettings, scan, null, rules);

        var ids = rules.Select(r => r.Id).ToHashSet(StringComparer.OrdinalIgnoreCase);
        var clean = _runner.Clean(scan, ids, settings.ToScanOptions(), ct);
        return new CleanJobResult(CleanJobOutcome.Cleaned, scan, clean, rules);
    }
}
