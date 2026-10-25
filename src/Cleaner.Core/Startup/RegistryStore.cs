using Microsoft.Win32;

namespace Cleaner.Core.Startup;

public enum Hive
{
    CurrentUser,
    LocalMachine,
}

/// <summary>The few registry operations the startup manager needs, so tests can use a fake.</summary>
public interface IRegistryStore
{
    IReadOnlyList<string> GetValueNames(Hive hive, string key);
    object? GetValue(Hive hive, string key, string name);

    /// <exception cref="UnauthorizedAccessException">Without the rights to write that hive.</exception>
    void SetBinary(Hive hive, string key, string name, byte[] value);
}

public sealed class WindowsRegistryStore : IRegistryStore
{
    private static RegistryKey Root(Hive hive) => hive == Hive.CurrentUser ? Registry.CurrentUser : Registry.LocalMachine;

    public IReadOnlyList<string> GetValueNames(Hive hive, string key)
    {
        using var k = Root(hive).OpenSubKey(key);
        return k?.GetValueNames() ?? [];
    }

    public object? GetValue(Hive hive, string key, string name)
    {
        using var k = Root(hive).OpenSubKey(key);
        return k?.GetValue(name, null, RegistryValueOptions.DoNotExpandEnvironmentNames);
    }

    public void SetBinary(Hive hive, string key, string name, byte[] value)
    {
        try
        {
            using var k = Root(hive).CreateSubKey(key, writable: true);
            k.SetValue(name, value, RegistryValueKind.Binary);
        }
        catch (System.Security.SecurityException e)
        {
            throw new UnauthorizedAccessException(e.Message, e);
        }
    }
}
