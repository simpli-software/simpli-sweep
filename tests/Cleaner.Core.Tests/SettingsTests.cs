using Cleaner.Core.Settings;

namespace Cleaner.Core.Tests;

public sealed class SettingsTests : IDisposable
{
    private readonly FakeMachine _m = new();

    public void Dispose() => _m.Dispose();

    private static CleanRule Rule(double minAge = 0) => new()
    {
        Id = "test",
        Name = "test",
        Targets = [new RuleTarget { Path = "%TEMP%", Recursive = true, MinAgeHours = minAge }],
    };

    private (ScanReport, CleanReport) Run(ScanOptions options, CleanRule rule)
    {
        var scanner = _m.Scanner();
        scanner.Options = options;
        var scan = scanner.Scan([rule]);
        return (scan, _m.Runner().Clean(scan, new HashSet<string> { rule.Id }));
    }

    [Fact]
    public void Excluded_folders_are_left_alone()
    {
        var keep = _m.File(Path.Combine(_m.Temp, "MyProject", "build.tmp"));
        var go = _m.File(Path.Combine(_m.Temp, "other.tmp"));

        Run(new ScanOptions { ExcludedFolders = [Path.Combine(_m.Temp, "MyProject")] }, Rule());

        Assert.True(File.Exists(keep));
        Assert.False(File.Exists(go));
    }

    [Fact]
    public void Excluded_folder_match_is_by_whole_folder_name()
    {
        var similar = _m.File(Path.Combine(_m.Temp, "MyProject2", "x.tmp"));

        Run(new ScanOptions { ExcludedFolders = [Path.Combine(_m.Temp, "MyProject")] }, Rule());

        Assert.False(File.Exists(similar)); // "MyProject" doesn't protect "MyProject2"
    }

    [Fact]
    public void Excluded_patterns_are_left_alone()
    {
        var log = _m.File(Path.Combine(_m.Temp, "a", "install.LOG"));
        var tmp = _m.File(Path.Combine(_m.Temp, "a", "x.tmp"));

        Run(new ScanOptions { ExcludedPatterns = ["*.log"] }, Rule());

        Assert.True(File.Exists(log));
        Assert.False(File.Exists(tmp));
    }

    [Fact]
    public void Minimum_age_only_ever_tightens()
    {
        var threeDays = _m.File(Path.Combine(_m.Temp, "3d.tmp"), ageHours: 72);
        var tenDays = _m.File(Path.Combine(_m.Temp, "10d.tmp"), ageHours: 240);

        // Setting 7 days on a rule that allows 1 day: the stricter 7 days wins.
        Run(new ScanOptions { MinimumAgeHours = 24 * 7 }, Rule(minAge: 24));

        Assert.True(File.Exists(threeDays));
        Assert.False(File.Exists(tenDays));
    }

    [Fact]
    public void Lower_minimum_age_cannot_loosen_a_rule()
    {
        var oneHour = _m.File(Path.Combine(_m.Temp, "1h.tmp"), ageHours: 1);

        Run(new ScanOptions { MinimumAgeHours = 0 }, Rule(minAge: 24));

        Assert.True(File.Exists(oneHour));
    }

    [Fact]
    public void Exclusion_added_after_the_scan_is_honoured_at_clean_time()
    {
        var file = _m.File(Path.Combine(_m.Temp, "keep", "x.tmp"));
        var scan = _m.Scanner().Scan([Rule()]);
        var stricter = scan with { Options = new ScanOptions { ExcludedFolders = [Path.Combine(_m.Temp, "keep")] } };

        _m.Runner().Clean(stricter, new HashSet<string> { "test" });

        Assert.True(File.Exists(file));
    }

    [Theory]
    [InlineData("*.log", true)]
    [InlineData("report-*.pdf", true)]
    [InlineData("..\\*", false)]
    [InlineData("C:\\*.log", false)]
    [InlineData("sub/*.log", false)]
    [InlineData(" ", false)]
    public void Patterns_must_be_file_names_only(string pattern, bool valid)
    {
        Assert.Equal(valid, ScanOptions.ValidatePattern(pattern) is null);
    }

    [Fact]
    public void Settings_round_trip_and_unknown_rules_use_defaults()
    {
        var store = new SettingsStore(Path.Combine(_m.Root, "settings"));
        store.Save(new AppSettings
        {
            SelectedRules = new(StringComparer.OrdinalIgnoreCase) { ["user-temp"] = false },
            MinimumAgeHours = 48,
            ExcludedFolders = [@"C:\Keep"],
            ExcludedPatterns = ["*.log"],
        });

        var loaded = store.Load();

        Assert.False(loaded.IsSelected(new CleanRule { Id = "USER-TEMP", Name = "x" }));
        Assert.True(loaded.IsSelected(new CleanRule { Id = "new-rule", Name = "x" }));
        Assert.False(loaded.IsSelected(new CleanRule { Id = "off-rule", Name = "x", EnabledByDefault = false }));
        Assert.Equal(48, loaded.ToScanOptions().MinimumAgeHours);
        Assert.Equal([@"C:\Keep"], loaded.ToScanOptions().ExcludedFolders);
    }

    [Fact]
    public void Damaged_settings_file_falls_back_to_defaults()
    {
        var folder = Path.Combine(_m.Root, "settings");
        Directory.CreateDirectory(folder);
        File.WriteAllText(Path.Combine(folder, "settings.json"), "{ not json");
        var store = new SettingsStore(folder);

        var loaded = store.Load();

        Assert.True(store.LoadedDefaultsAfterError);
        Assert.Empty(loaded.SelectedRules);
    }

    [Fact]
    public void Hand_edited_bad_values_cannot_loosen_anything()
    {
        var options = new AppSettings
        {
            MinimumAgeHours = -100,
            ExcludedFolders = ["relative\\path", @"C:\Fine"],
            ExcludedPatterns = ["..\\..\\*", "*.log"],
        }.ToScanOptions();

        Assert.Equal(0, options.MinimumAgeHours);
        Assert.Equal([@"C:\Fine"], options.ExcludedFolders);
        Assert.Equal(["*.log"], options.ExcludedPatterns);
    }
}
