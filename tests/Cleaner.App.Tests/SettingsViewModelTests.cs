using Cleaner.App.Services;
using Cleaner.App.ViewModels;
using Cleaner.Core;
using Cleaner.Core.Settings;
using Cleaner.Core.Tests;

namespace Cleaner.App.Tests;

public sealed class SettingsViewModelTests : IDisposable
{
    private readonly FakeMachine _m = new();
    private readonly FakePicker _picker = new();
    private readonly FakeDialogs _dialogs = new() { Answer = true };

    public void Dispose() => _m.Dispose();

    private string SettingsFolder => Path.Combine(_m.Root, "settings");
    private SettingsViewModel Settings() => new(new SettingsStore(SettingsFolder), _picker);

    private static readonly CleanRule TempRule = new()
    {
        Id = "temp",
        Name = "Temp",
        Category = "Windows",
        Targets = [new RuleTarget { Path = "%TEMP%", Recursive = true }],
    };

    private MainViewModel Main(SettingsViewModel settings) =>
        new([TempRule], _m.Scanner(), _m.Runner(), _dialogs, new Elevated()) { Settings = settings };

    [Fact]
    public void Changes_are_saved_and_reloaded()
    {
        var vm = Settings();
        _picker.Next = Path.Combine(_m.Temp, "Keep");
        vm.AddFolderCommand.Execute(null);
        vm.NewPattern = "*.log";
        vm.AddPatternCommand.Execute(null);
        vm.SelectedAge = vm.AgeChoices.Single(a => a.Hours == 24 * 7);

        var reloaded = Settings();

        Assert.Equal([Path.Combine(_m.Temp, "Keep")], reloaded.ExcludedFolders);
        Assert.Equal(["*.log"], reloaded.ExcludedPatterns);
        Assert.Equal(24 * 7, reloaded.SelectedAge.Hours);
    }

    [Fact]
    public void Invalid_patterns_are_refused_with_a_reason()
    {
        var vm = Settings();
        vm.NewPattern = @"..\*";

        vm.AddPatternCommand.Execute(null);

        Assert.Empty(vm.ExcludedPatterns);
        Assert.Contains("file-name pattern", vm.Status);
    }

    [Fact]
    public void Cancelled_folder_picker_changes_nothing()
    {
        var vm = Settings();
        _picker.Next = null;

        vm.AddFolderCommand.Execute(null);

        Assert.Empty(vm.ExcludedFolders);
    }

    [Fact]
    public void Unusual_saved_age_is_kept_not_rounded_down()
    {
        new SettingsStore(SettingsFolder).Save(new AppSettings { MinimumAgeHours = 48 });

        var vm = Settings();

        Assert.Equal(48, vm.SelectedAge.Hours);
    }

    [Fact]
    public async Task Ticks_on_the_Clean_tab_are_remembered()
    {
        _m.File(Path.Combine(_m.Temp, "a.tmp"));
        var main = Main(Settings());
        await main.ScanAsync();

        main.Items.Single().IsSelected = false;

        var again = Main(Settings());
        await again.ScanAsync();
        Assert.False(again.Items.Single().IsSelected);
    }

    [Fact]
    public async Task Changing_exclusions_clears_the_list_until_the_next_scan()
    {
        var keep = _m.File(Path.Combine(_m.Temp, "Keep", "a.tmp"));
        var settings = Settings();
        var main = Main(settings);
        await main.ScanAsync();
        Assert.True(main.CleanCommand.CanExecute(null));

        _picker.Next = Path.Combine(_m.Temp, "Keep");
        settings.AddFolderCommand.Execute(null);

        Assert.Empty(main.Items);
        Assert.False(main.CleanCommand.CanExecute(null));

        await main.ScanAsync();
        Assert.Equal(RuleStatus.NothingFound, main.Items.Single().Result.Status);
        Assert.True(File.Exists(keep));
    }

    [Fact]
    public async Task Damaged_settings_keep_Clean_off_until_the_user_has_checked()
    {
        Directory.CreateDirectory(SettingsFolder);
        File.WriteAllText(Path.Combine(SettingsFolder, "settings.json"), "{ not json");
        _m.File(Path.Combine(_m.Temp, "a.tmp"));
        var main = Main(Settings());
        await main.ScanAsync();

        Assert.True(main.SettingsNeedReview);
        Assert.False(main.CleanCommand.CanExecute(null));
        Assert.Contains("settings.json.bad", main.Settings!.Status);

        main.AcknowledgeSettingsCommand.Execute(null);

        Assert.False(main.SettingsNeedReview);
        Assert.True(main.CleanCommand.CanExecute(null));
    }

    [Fact]
    public async Task List_cleared_while_confirming_is_not_cleaned()
    {
        var file = _m.File(Path.Combine(_m.Temp, "Keep", "a.tmp"));
        var settings = Settings();
        var main = Main(settings);
        await main.ScanAsync();

        _picker.Next = Path.Combine(_m.Temp, "Keep");
        _dialogs.OnConfirm = () => settings.AddFolderCommand.Execute(null);
        await main.CleanAsync();

        Assert.True(File.Exists(file));
    }

    private sealed class FakePicker : IFolderPicker
    {
        public string? Next { get; set; }
        public string? PickFolder() => Next;
    }

    private sealed class FakeDialogs : IDialogService
    {
        public bool Answer { get; set; }
        public Action? OnConfirm { get; set; }

        public bool Confirm(string title, string message)
        {
            OnConfirm?.Invoke();
            return Answer;
        }
        public void Error(string title, string message) { }
    }

    private sealed class Elevated : IElevation
    {
        public bool IsElevated => true;
        public bool RestartElevated() => true;
    }
}
