using Cleaner.App.Services;
using Cleaner.App.ViewModels;
using Cleaner.Core;
using Cleaner.Core.Tests;

namespace Cleaner.App.Tests;

public sealed class MainViewModelTests : IDisposable
{
    private readonly FakeMachine _m = new();
    private readonly FakeDialogs _dialogs = new();
    private readonly FakeElevation _elevation = new();

    public void Dispose() => _m.Dispose();

    private static readonly CleanRule TempRule = new()
    {
        Id = "temp",
        Name = "Temp",
        Category = "Windows",
        Targets = [new RuleTarget { Path = "%TEMP%", Recursive = true }],
    };

    private static readonly CleanRule OffByDefault = new()
    {
        Id = "crash",
        Name = "Crash dumps",
        Category = "Windows",
        EnabledByDefault = false,
        Targets = [new RuleTarget { Path = "%LOCALAPPDATA%\\CrashDumps" }],
    };

    private static readonly CleanRule AdminRule = new()
    {
        Id = "wintemp",
        Name = "Windows temp",
        Category = "Windows",
        RequiresAdmin = true,
        Targets = [new RuleTarget { Path = "%WINDIR%\\Temp" }],
    };

    private MainViewModel Vm(params CleanRule[] rules) =>
        new(rules, _m.Scanner(_elevation.IsElevated), _m.Runner(), _dialogs, _elevation);

    [Fact]
    public async Task Scan_ticks_default_categories_only()
    {
        _m.File(Path.Combine(_m.Temp, "a.tmp"), bytes: 1000);
        _m.File(Path.Combine(_m.LocalAppData, "CrashDumps", "x.dmp"), bytes: 500);
        var vm = Vm(TempRule, OffByDefault);

        await vm.ScanAsync();

        Assert.True(vm.Items.Single(i => i.Rule.Id == "temp").IsSelected);
        Assert.False(vm.Items.Single(i => i.Rule.Id == "crash").IsSelected);
        Assert.Equal(1000, vm.SelectedBytes);
    }

    [Fact]
    public async Task Nothing_is_deleted_when_the_user_says_no()
    {
        var file = _m.File(Path.Combine(_m.Temp, "a.tmp"));
        var vm = Vm(TempRule);
        await vm.ScanAsync();

        _dialogs.Answer = false;
        await vm.CleanAsync();

        Assert.Equal(1, _dialogs.ConfirmCount);
        Assert.True(File.Exists(file));
    }

    [Fact]
    public async Task Only_ticked_categories_are_cleaned_after_confirmation()
    {
        var temp = _m.File(Path.Combine(_m.Temp, "a.tmp"));
        var dump = _m.File(Path.Combine(_m.LocalAppData, "CrashDumps", "x.dmp"));
        var vm = Vm(TempRule, OffByDefault);
        await vm.ScanAsync();

        _dialogs.Answer = true;
        await vm.CleanAsync();

        Assert.False(File.Exists(temp));
        Assert.True(File.Exists(dump));
        Assert.StartsWith("Done. Freed", vm.Status);
    }

    [Fact]
    public async Task Unticking_everything_disables_clean()
    {
        _m.File(Path.Combine(_m.Temp, "a.tmp"));
        var vm = Vm(TempRule);
        await vm.ScanAsync();
        Assert.True(vm.CleanCommand.CanExecute(null));

        vm.Items[0].IsSelected = false;

        Assert.False(vm.CleanCommand.CanExecute(null));
        Assert.Equal(0, vm.SelectedBytes);
    }

    [Fact]
    public async Task Admin_categories_cannot_be_ticked_and_offer_a_restart()
    {
        _m.File(Path.Combine(_m.Windir, "Temp", "a.log"));
        _elevation.Elevated = false;
        var vm = Vm(AdminRule);

        await vm.ScanAsync();

        var item = vm.Items.Single();
        Assert.False(item.CanSelect);
        item.IsSelected = true;
        Assert.False(item.IsSelected);
        Assert.True(vm.NeedsAdmin);
        Assert.True(vm.RestartAsAdminCommand.CanExecute(null));
    }

    [Fact]
    public async Task Declined_UAC_prompt_is_reported()
    {
        _elevation.Elevated = false;
        _elevation.AcceptPrompt = false;
        _m.File(Path.Combine(_m.Windir, "Temp", "a.log"));
        var vm = Vm(AdminRule);
        await vm.ScanAsync();

        await vm.RestartAsAdminCommand.ExecuteAsync();

        Assert.Contains("declined", vm.Status);
    }

    [Fact]
    public async Task Rescan_after_clean_shows_what_is_left()
    {
        _m.File(Path.Combine(_m.Temp, "a.tmp"));
        var vm = Vm(TempRule);
        await vm.ScanAsync();

        _dialogs.Answer = true;
        await vm.CleanAsync();

        Assert.Equal(RuleStatus.NothingFound, vm.Items.Single().Result.Status);
        Assert.False(vm.CleanCommand.CanExecute(null));
    }

    private sealed class FakeDialogs : IDialogService
    {
        public bool Answer { get; set; }
        public int ConfirmCount { get; private set; }

        public bool Confirm(string title, string message)
        {
            ConfirmCount++;
            return Answer;
        }

        public void Error(string title, string message) { }
    }

    private sealed class FakeElevation : IElevation
    {
        public bool Elevated { get; set; } = true;
        public bool AcceptPrompt { get; set; } = true;
        public bool IsElevated => Elevated;
        public bool RestartElevated() => AcceptPrompt;
    }
}
