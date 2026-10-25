using Cleaner.Core.Startup;

namespace Cleaner.Core.Tests;

public sealed class StartupManagerTests : IDisposable
{
    private const string Run = @"Software\Microsoft\Windows\CurrentVersion\Run";
    private const string Run32 = @"Software\WOW6432Node\Microsoft\Windows\CurrentVersion\Run";
    private const string Approved = @"Software\Microsoft\Windows\CurrentVersion\Explorer\StartupApproved\";

    private readonly FakeMachine _m = new();
    private readonly FakeRegistry _reg = new();
    private readonly StartupManager _mgr;

    public StartupManagerTests()
    {
        _mgr = new StartupManager(_reg, _m.Tokens, new FakeShortcuts(), _m.Clock);
    }

    public void Dispose() => _m.Dispose();

    [Fact]
    public void Lists_registry_and_folder_items_from_every_source()
    {
        _reg.Set(Hive.CurrentUser, Run, "Chat", "\"C:\\Program Files\\Chat\\chat.exe\" --minimized");
        _reg.Set(Hive.LocalMachine, Run, "Audio", @"C:\Windows\audio.exe");
        _reg.Set(Hive.LocalMachine, Run32, "Updater", @"C:\Program Files (x86)\Up\up.exe /silent");
        _m.File(Path.Combine(_mgr.UserStartupFolder, "Notes.lnk"));
        _m.File(Path.Combine(_mgr.UserStartupFolder, "desktop.ini"));
        _m.File(Path.Combine(_mgr.CommonStartupFolder, "Backup.lnk"));

        var items = _mgr.List();

        Assert.Equal(
            ["UserRun:Chat", "MachineRun:Audio", "MachineRun32:Updater", "UserStartupFolder:Notes.lnk", "CommonStartupFolder:Backup.lnk"],
            items.Select(i => i.Id));
        Assert.All(items, i => Assert.True(i.Enabled));
        Assert.False(items[0].RequiresAdmin);
        Assert.True(items[1].RequiresAdmin);
        Assert.True(items[4].RequiresAdmin);
        Assert.Equal(@"C:\Program Files\Chat\chat.exe", items[0].ExecutablePath);
        Assert.Equal(@"C:\Target\Notes.exe", items[3].ExecutablePath);
    }

    [Theory]
    [InlineData(new byte[] { 0x02, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0 }, true)]
    [InlineData(new byte[] { 0x06, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0 }, true)]
    [InlineData(new byte[] { 0x03, 0, 0, 0, 1, 2, 3, 4, 5, 6, 7, 8 }, false)]
    [InlineData(new byte[] { 0x07, 0, 0, 0, 1, 2, 3, 4, 5, 6, 7, 8 }, false)]
    public void Reads_the_same_on_off_flag_as_Task_Manager(byte[] flag, bool enabled)
    {
        _reg.Set(Hive.CurrentUser, Run, "Chat", @"C:\chat.exe");
        _reg.Set(Hive.CurrentUser, Approved + "Run", "Chat", flag);

        Assert.Equal(enabled, _mgr.List().Single().Enabled);
    }

    [Fact]
    public void Disabling_writes_only_the_approval_flag_and_never_touches_the_entry()
    {
        _reg.Set(Hive.CurrentUser, Run, "Chat", @"C:\chat.exe");
        var item = _mgr.List().Single();

        var updated = _mgr.SetEnabled(item, false);

        Assert.False(updated.Enabled);
        Assert.Equal(@"C:\chat.exe", _reg.Get(Hive.CurrentUser, Run, "Chat"));
        var flag = Assert.IsType<byte[]>(_reg.Get(Hive.CurrentUser, Approved + "Run", "Chat"));
        Assert.Equal(12, flag.Length);
        Assert.Equal(0x03, flag[0]);
        Assert.Equal(_m.Clock.GetUtcNow().UtcDateTime.ToFileTimeUtc(), BitConverter.ToInt64(flag, 4));
        Assert.False(_mgr.List().Single().Enabled);
    }

    [Fact]
    public void Re_enabling_restores_the_item()
    {
        _reg.Set(Hive.CurrentUser, Run, "Chat", @"C:\chat.exe");
        var item = _mgr.SetEnabled(_mgr.List().Single(), false);

        _mgr.SetEnabled(item, true);

        Assert.True(_mgr.List().Single().Enabled);
    }

    [Fact]
    public void Startup_folder_files_are_never_moved_or_deleted()
    {
        var lnk = _m.File(Path.Combine(_mgr.UserStartupFolder, "Notes.lnk"));

        _mgr.SetEnabled(_mgr.List().Single(), false);

        Assert.True(File.Exists(lnk));
        Assert.NotNull(_reg.Get(Hive.CurrentUser, Approved + "StartupFolder", "Notes.lnk"));
    }

    [Theory]
    [InlineData(StartupSource.MachineRun, Approved + "Run")]
    [InlineData(StartupSource.MachineRun32, Approved + "Run32")]
    [InlineData(StartupSource.CommonStartupFolder, Approved + "StartupFolder")]
    public void All_users_items_write_to_the_machine_hive(StartupSource source, string key)
    {
        var item = new StartupItem(source, "X", @"C:\x.exe", @"C:\x.exe", true, true);

        _mgr.SetEnabled(item, false);

        Assert.NotNull(_reg.Get(Hive.LocalMachine, key, "X"));
        Assert.Null(_reg.Get(Hive.CurrentUser, key, "X"));
    }

    [Fact]
    public void Without_admin_machine_items_report_access_denied()
    {
        _reg.ReadOnlyMachine = true;
        var item = new StartupItem(StartupSource.MachineRun, "X", @"C:\x.exe", @"C:\x.exe", true, true);

        Assert.Throws<UnauthorizedAccessException>(() => _mgr.SetEnabled(item, false));
    }

    [Theory]
    [InlineData("\"C:\\Program Files\\App\\app.exe\" --tray", @"C:\Program Files\App\app.exe")]
    [InlineData(@"C:\Program Files\App\app.exe --tray", @"C:\Program Files\App\app.exe")]
    [InlineData(@"C:\Tools\run.cmd", @"C:\Tools\run.cmd")]
    [InlineData("rundll32.exe shell32.dll,Control_RunDLL", null)]
    [InlineData("", null)]
    public void Finds_the_executable_in_a_command_line(string command, string? expected)
    {
        Assert.Equal(expected, CommandLine.ExecutablePath(command));
    }

    private sealed class FakeShortcuts : IShortcutResolver
    {
        public string? Resolve(string lnkPath) => $@"C:\Target\{Path.GetFileNameWithoutExtension(lnkPath)}.exe";
    }
}
