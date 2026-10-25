using Cleaner.Core.Startup;

namespace Cleaner.Core.Tests;

public sealed class FakeRegistry : IRegistryStore
{
    private readonly Dictionary<(Hive, string), Dictionary<string, object>> _keys = [];

    public bool ReadOnlyMachine { get; set; }

    public void Set(Hive hive, string key, string name, object value)
    {
        if (!_keys.TryGetValue((hive, key.ToLowerInvariant()), out var values))
            _keys[(hive, key.ToLowerInvariant())] = values = new Dictionary<string, object>(StringComparer.OrdinalIgnoreCase);
        values[name] = value;
    }

    public object? Get(Hive hive, string key, string name) => GetValue(hive, key, name);

    public IReadOnlyList<string> GetValueNames(Hive hive, string key) =>
        _keys.TryGetValue((hive, key.ToLowerInvariant()), out var v) ? v.Keys.ToList() : [];

    public object? GetValue(Hive hive, string key, string name) =>
        _keys.TryGetValue((hive, key.ToLowerInvariant()), out var v) && v.TryGetValue(name, out var value) ? value : null;

    public void SetBinary(Hive hive, string key, string name, byte[] value)
    {
        if (hive == Hive.LocalMachine && ReadOnlyMachine)
            throw new UnauthorizedAccessException("Requested registry access is not allowed.");
        Set(hive, key, name, value);
    }
}
