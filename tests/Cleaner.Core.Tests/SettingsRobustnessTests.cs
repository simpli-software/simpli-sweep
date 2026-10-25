using Cleaner.Core.Settings;

namespace Cleaner.Core.Tests;

/// <summary>Hand-edited or damaged settings files must never crash the app or loosen cleaning.</summary>
public sealed class SettingsRobustnessTests : IDisposable
{
    private readonly FakeMachine _m = new();

    public void Dispose() => _m.Dispose();

    private string Folder => Path.Combine(_m.Root, "settings");
    private string FilePath => Path.Combine(Folder, "settings.json");

    private SettingsStore Write(string json)
    {
        Directory.CreateDirectory(Folder);
        File.WriteAllText(FilePath, json);
        return new SettingsStore(Folder);
    }

    [Theory]
    [InlineData("""{"SelectedRules": null}""")]
    [InlineData("""{"ExcludedFolders": null, "ExcludedPatterns": null}""")]
    [InlineData("""{"SelectedRules": {"a": true, "A": false}}""")]
    [InlineData("""null""")]
    public void Odd_but_valid_files_load_without_crashing(string json)
    {
        var store = Write(json);

        var settings = store.Load();
        var options = settings.ToScanOptions();

        Assert.False(store.LoadedDefaultsAfterError);
        Assert.NotNull(options);
    }

    [Fact]
    public void Conflicting_ticks_resolve_to_off()
    {
        var settings = Write("""{"SelectedRules": {"a": true, "A": false}}""").Load();

        Assert.False(settings.IsSelected(new CleanRule { Id = "a", Name = "a" }));
    }

    [Theory]
    [InlineData("{ not json")]
    [InlineData("""{"MinimumAgeHours": "soon"}""")]
    [InlineData("""{"SelectedRules": [1, 2]}""")]
    public void Damaged_file_is_set_aside_and_never_overwritten(string json)
    {
        var store = Write(json);

        store.Load();
        store.Save(new AppSettings());

        Assert.True(store.LoadedDefaultsAfterError);
        Assert.Equal(json, File.ReadAllText(store.DamagedCopyPath));
        Assert.NotEqual(json, File.ReadAllText(FilePath));
    }

    [Fact]
    public void Damaged_file_that_cannot_be_moved_is_not_overwritten()
    {
        var store = Write("{ not json");
        AppSettings loaded;
        using (new FileStream(FilePath, FileMode.Open, FileAccess.Read, FileShare.Read)) // blocks the move
            loaded = store.Load();

        Assert.Throws<IOException>(() => store.Save(loaded));
        Assert.Equal("{ not json", File.ReadAllText(FilePath));
    }

    [Theory]
    [InlineData("C:/Projects")]
    [InlineData("C:\\a\\..\\Projects")]
    [InlineData("C:\\Projects\\")]
    public void Hand_typed_folder_forms_are_normalised(string folder)
    {
        var options = new AppSettings { ExcludedFolders = [folder] }.ToScanOptions();

        Assert.Equal(["C:\\Projects"], options.ExcludedFolders);
        Assert.True(options.IsExcluded("C:\\Projects\\x.tmp"));
    }

    [Fact]
    public void Strictest_combines_exclusions_and_takes_the_longer_age()
    {
        var a = new ScanOptions { MinimumAgeHours = 24, ExcludedFolders = ["C:\\A"] };
        var b = new ScanOptions { MinimumAgeHours = 168, ExcludedPatterns = ["*.log"] };

        var s = ScanOptions.Strictest(a, b);

        Assert.Equal(168, s.MinimumAgeHours);
        Assert.True(s.IsExcluded("C:\\A\\f.tmp"));
        Assert.True(s.IsExcluded("C:\\B\\f.log"));
    }

    [Fact]
    public void Clean_honours_settings_changed_after_the_scan()
    {
        var keep = _m.File(Path.Combine(_m.Temp, "Keep", "a.tmp"));
        var young = _m.File(Path.Combine(_m.Temp, "young.tmp"), ageHours: 30);
        var rule = new CleanRule { Id = "t", Name = "t", Targets = [new RuleTarget { Path = "%TEMP%", Recursive = true }] };
        var scan = _m.Scanner().Scan([rule]); // scanned with no settings

        var now = new ScanOptions { MinimumAgeHours = 24 * 7, ExcludedFolders = [Path.Combine(_m.Temp, "Keep")] };
        _m.Runner().Clean(scan, new HashSet<string> { "t" }, now);

        Assert.True(File.Exists(keep));
        Assert.True(File.Exists(young));
    }
}
