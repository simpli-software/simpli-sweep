using Cleaner.App.ViewModels;
using Cleaner.Core.Startup;
using Cleaner.Core.Tests;

namespace Cleaner.App.Tests;

public sealed class StartupViewModelTests : IDisposable
{
    private const string Run = @"Software\Microsoft\Windows\CurrentVersion\Run";

    private readonly FakeMachine _m = new();
    private readonly FakeRegistry _reg = new();

    public void Dispose() => _m.Dispose();

    private StartupViewModel Vm(bool elevated) =>
        new(new StartupManager(_reg, _m.Tokens, new NoShortcuts(), _m.Clock), new FakeTrust(), elevated);

    [Fact]
    public async Task Switching_off_updates_the_item_and_summary()
    {
        _reg.Set(Hive.CurrentUser, Run, "Chat", @"C:\chat.exe");
        _reg.Set(Hive.CurrentUser, Run, "Music", @"C:\music.exe");
        var vm = Vm(elevated: false);
        await vm.LoadAsync();
        Assert.StartsWith("2 of 2", vm.Status);

        vm.Items.Single(i => i.Name == "Chat").IsEnabled = false;

        Assert.False(vm.Items.Single(i => i.Name == "Chat").IsEnabled);
        Assert.StartsWith("1 of 2", vm.Status);
    }

    [Fact]
    public async Task All_users_items_are_locked_without_admin()
    {
        _reg.Set(Hive.LocalMachine, Run, "Audio", @"C:\audio.exe");
        var vm = Vm(elevated: false);
        await vm.LoadAsync();

        var item = vm.Items.Single();
        item.IsEnabled = false;

        Assert.False(item.CanToggle);
        Assert.True(item.IsEnabled);
        Assert.Equal("Needs administrator", item.AdminNote);
    }

    [Fact]
    public async Task Failed_change_snaps_back_and_explains()
    {
        _reg.Set(Hive.LocalMachine, Run, "Audio", @"C:\audio.exe");
        _reg.ReadOnlyMachine = true; // e.g. policy blocks it even when elevated
        var vm = Vm(elevated: true);
        await vm.LoadAsync();

        var item = vm.Items.Single();
        item.IsEnabled = false;

        Assert.True(item.IsEnabled);
        Assert.Contains("administrator", vm.Status);
    }

    [Fact]
    public async Task Unsigned_programs_are_flagged()
    {
        _reg.Set(Hive.CurrentUser, Run, "Signed", @"C:\signed.exe");
        _reg.Set(Hive.CurrentUser, Run, "Sketchy", @"C:\sketchy.exe");
        var vm = Vm(elevated: false);
        await vm.LoadAsync();

        Assert.False(vm.Items.Single(i => i.Name == "Signed").NeedsAttention);
        Assert.True(vm.Items.Single(i => i.Name == "Sketchy").NeedsAttention);
        Assert.Equal("Not signed", vm.Items.Single(i => i.Name == "Sketchy").SignatureText);
    }

    private sealed class NoShortcuts : IShortcutResolver
    {
        public string? Resolve(string lnkPath) => null;
    }

    private sealed class FakeTrust : IFileTrust
    {
        public FileDetails Inspect(string? path) => path is not null && path.Contains("sketchy")
            ? new FileDetails(null, SignatureStatus.Unsigned)
            : new FileDetails("Contoso", SignatureStatus.Valid);
    }
}
