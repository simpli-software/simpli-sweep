namespace Cleaner.Core.Startup;

public enum StartupSource
{
    UserRun,
    MachineRun,
    MachineRun32,
    UserStartupFolder,
    CommonStartupFolder,
}

public sealed record StartupItem(
    StartupSource Source,
    string Name,
    string Command,
    string? ExecutablePath,
    bool Enabled,
    bool RequiresAdmin)
{
    public string Id => $"{Source}:{Name}";

    public string Location => Source switch
    {
        StartupSource.UserRun => "Registry (your account)",
        StartupSource.MachineRun or StartupSource.MachineRun32 => "Registry (all users)",
        StartupSource.UserStartupFolder => "Startup folder (your account)",
        _ => "Startup folder (all users)",
    };
}

/// <summary>
/// Lists startup programs and turns them on or off the same way Task Manager does: by writing the
/// Explorer "StartupApproved" flag. Entries themselves are never changed or deleted, so anything
/// switched off here can be switched back on here, in Task Manager, or in Settings.
/// </summary>
public sealed class StartupManager
{
    private const string RunKey = @"Software\Microsoft\Windows\CurrentVersion\Run";
    private const string Run32Key = @"Software\WOW6432Node\Microsoft\Windows\CurrentVersion\Run";
    private const string ApprovedKey = @"Software\Microsoft\Windows\CurrentVersion\Explorer\StartupApproved\";

    private readonly IRegistryStore _registry;
    private readonly PathTokens _tokens;
    private readonly IShortcutResolver _shortcuts;
    private readonly TimeProvider _clock;

    public StartupManager(IRegistryStore registry, PathTokens tokens, IShortcutResolver shortcuts, TimeProvider clock)
    {
        _registry = registry;
        _tokens = tokens;
        _shortcuts = shortcuts;
        _clock = clock;
    }

    public string UserStartupFolder => Path.Combine(_tokens["APPDATA"], @"Microsoft\Windows\Start Menu\Programs\Startup");
    public string CommonStartupFolder => Path.Combine(_tokens["PROGRAMDATA"], @"Microsoft\Windows\Start Menu\Programs\StartUp");

    public IReadOnlyList<StartupItem> List()
    {
        var items = new List<StartupItem>();
        AddRegistry(items, StartupSource.UserRun, Hive.CurrentUser, RunKey);
        AddRegistry(items, StartupSource.MachineRun, Hive.LocalMachine, RunKey);
        AddRegistry(items, StartupSource.MachineRun32, Hive.LocalMachine, Run32Key);
        AddFolder(items, StartupSource.UserStartupFolder, UserStartupFolder);
        AddFolder(items, StartupSource.CommonStartupFolder, CommonStartupFolder);
        return items;
    }

    /// <summary>Turns an item on or off. Throws <see cref="UnauthorizedAccessException"/> without admin for all-users items.</summary>
    public StartupItem SetEnabled(StartupItem item, bool enabled)
    {
        var (hive, key) = Approved(item.Source);
        _registry.SetBinary(hive, key, item.Name, enabled ? EnabledFlag() : DisabledFlag(_clock.GetUtcNow()));
        return item with { Enabled = enabled };
    }

    private void AddRegistry(List<StartupItem> items, StartupSource source, Hive hive, string key)
    {
        foreach (var name in _registry.GetValueNames(hive, key))
        {
            if (string.IsNullOrEmpty(name) || _registry.GetValue(hive, key, name) is not string command || command.Length == 0)
                continue;

            items.Add(new StartupItem(source, name, command, CommandLine.ExecutablePath(command), IsEnabled(source, name), hive == Hive.LocalMachine));
        }
    }

    private void AddFolder(List<StartupItem> items, StartupSource source, string folder)
    {
        if (!Directory.Exists(folder))
            return;

        foreach (var file in Directory.EnumerateFiles(folder))
        {
            var name = Path.GetFileName(file);
            if (name.Equals("desktop.ini", StringComparison.OrdinalIgnoreCase))
                continue;

            var target = name.EndsWith(".lnk", StringComparison.OrdinalIgnoreCase) ? _shortcuts.Resolve(file) : file;
            items.Add(new StartupItem(source, name, target ?? file, target, IsEnabled(source, name), source == StartupSource.CommonStartupFolder));
        }
    }

    private bool IsEnabled(StartupSource source, string name)
    {
        var (hive, key) = Approved(source);
        // No flag means enabled. Windows uses an even first byte for on (02, 06) and odd for off (03, 07).
        return _registry.GetValue(hive, key, name) is not byte[] { Length: > 0 } flag || (flag[0] & 1) == 0;
    }

    private static (Hive, string) Approved(StartupSource source) => source switch
    {
        StartupSource.UserRun => (Hive.CurrentUser, ApprovedKey + "Run"),
        StartupSource.MachineRun => (Hive.LocalMachine, ApprovedKey + "Run"),
        StartupSource.MachineRun32 => (Hive.LocalMachine, ApprovedKey + "Run32"),
        StartupSource.UserStartupFolder => (Hive.CurrentUser, ApprovedKey + "StartupFolder"),
        _ => (Hive.LocalMachine, ApprovedKey + "StartupFolder"),
    };

    internal static byte[] EnabledFlag() => [0x02, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0];

    /// <summary>03 followed by three zero bytes and the time it was switched off (FILETIME), as Task Manager writes it.</summary>
    internal static byte[] DisabledFlag(DateTimeOffset when)
    {
        var flag = new byte[12];
        flag[0] = 0x03;
        BitConverter.GetBytes(when.UtcDateTime.ToFileTimeUtc()).CopyTo(flag, 4);
        return flag;
    }
}
