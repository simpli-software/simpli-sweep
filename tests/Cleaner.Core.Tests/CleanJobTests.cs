using Cleaner.Core.Jobs;
using Cleaner.Core.Settings;

namespace Cleaner.Core.Tests;

public sealed class CleanJobTests : IDisposable
{
    private readonly FakeMachine _m = new();

    public void Dispose() => _m.Dispose();

    private string SettingsFolder => Path.Combine(_m.Root, "settings");

    private static readonly CleanRule Temp = new()
    {
        Id = "temp",
        Name = "Temp",
        Targets = [new RuleTarget { Path = "%TEMP%", Recursive = true }],
    };

    private static readonly CleanRule OffByDefault = new()
    {
        Id = "dumps",
        Name = "Dumps",
        EnabledByDefault = false,
        Targets = [new RuleTarget { Path = "%LOCALAPPDATA%\\CrashDumps" }],
    };

    private static readonly CleanRule Bin = new() { Id = "recycle-bin", Name = "Bin", Kind = RuleKind.RecycleBin };

    private CleanJob Job() => new(_m.Scanner(), _m.Runner(), new SettingsStore(SettingsFolder));

    [Fact]
    public void Safe_preset_cleans_default_categories_but_never_the_Recycle_Bin()
    {
        var temp = _m.File(Path.Combine(_m.Temp, "a.tmp"));
        var dump = _m.File(Path.Combine(_m.LocalAppData, "CrashDumps", "x.dmp"));
        _m.RecycleBin.Bytes = 1000;

        var result = Job().Run([Temp, OffByDefault, Bin], CleanPreset.Safe, dryRun: false);

        Assert.Equal(CleanJobOutcome.Cleaned, result.Outcome);
        Assert.False(File.Exists(temp));
        Assert.True(File.Exists(dump));
        Assert.False(_m.RecycleBin.Emptied);
    }

    [Fact]
    public void Selected_preset_follows_the_ticks_saved_by_the_app()
    {
        var temp = _m.File(Path.Combine(_m.Temp, "a.tmp"));
        var dump = _m.File(Path.Combine(_m.LocalAppData, "CrashDumps", "x.dmp"));
        new SettingsStore(SettingsFolder).Save(new AppSettings
        {
            SelectedRules = new(StringComparer.OrdinalIgnoreCase) { ["temp"] = false, ["dumps"] = true },
        });

        Job().Run([Temp, OffByDefault], CleanPreset.Selected, dryRun: false);

        Assert.True(File.Exists(temp));
        Assert.False(File.Exists(dump));
    }

    [Fact]
    public void Dry_run_deletes_nothing()
    {
        var temp = _m.File(Path.Combine(_m.Temp, "a.tmp"));

        var result = Job().Run([Temp], CleanPreset.Safe, dryRun: true);

        Assert.Equal(CleanJobOutcome.DryRun, result.Outcome);
        Assert.Equal(1, result.Scan.TotalFiles);
        Assert.True(File.Exists(temp));
    }

    [Fact]
    public void Saved_exclusions_and_age_apply()
    {
        var keep = _m.File(Path.Combine(_m.Temp, "Keep", "a.tmp"));
        var young = _m.File(Path.Combine(_m.Temp, "young.tmp"), ageHours: 30);
        var old = _m.File(Path.Combine(_m.Temp, "old.tmp"), ageHours: 24 * 10);
        new SettingsStore(SettingsFolder).Save(new AppSettings
        {
            MinimumAgeHours = 24 * 7,
            ExcludedFolders = [Path.Combine(_m.Temp, "Keep")],
        });

        Job().Run([Temp], CleanPreset.Safe, dryRun: false);

        Assert.True(File.Exists(keep));
        Assert.True(File.Exists(young));
        Assert.False(File.Exists(old));
    }

    [Theory]
    [InlineData(true)]  // damaged file found on this run
    [InlineData(false)] // damaged copy from an earlier run not yet reviewed
    public void Nothing_is_deleted_while_settings_need_review(bool damagedNow)
    {
        var temp = _m.File(Path.Combine(_m.Temp, "a.tmp"));
        Directory.CreateDirectory(SettingsFolder);
        File.WriteAllText(Path.Combine(SettingsFolder, damagedNow ? "settings.json" : "settings.json.bad"), "{ not json");

        var result = Job().Run([Temp], CleanPreset.Safe, dryRun: false);

        Assert.Equal(CleanJobOutcome.SettingsNeedReview, result.Outcome);
        Assert.True(File.Exists(temp));
    }
}
