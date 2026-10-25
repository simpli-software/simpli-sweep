using System.Text.Json;

namespace Cleaner.Core.Settings;

public sealed record AppSettings
{
    public int Version { get; init; } = 1;

    /// <summary>Categories the user ticked or unticked; categories not listed use their default.</summary>
    public Dictionary<string, bool> SelectedRules { get; init; } = new(StringComparer.OrdinalIgnoreCase);

    public double MinimumAgeHours { get; init; }
    public List<string> ExcludedFolders { get; init; } = [];
    public List<string> ExcludedPatterns { get; init; } = [];

    public bool IsSelected(CleanRule rule) =>
        SelectedRules.TryGetValue(rule.Id, out var selected) ? selected : rule.EnabledByDefault;

    /// <summary>Drops invalid entries so a hand-edited file can never loosen anything.</summary>
    public ScanOptions ToScanOptions() => new()
    {
        MinimumAgeHours = double.IsFinite(MinimumAgeHours) ? Math.Clamp(MinimumAgeHours, 0, 24 * 3650) : 0,
        ExcludedFolders = ExcludedFolders.Where(f => ScanOptions.ValidateFolder(f) is null).ToList(),
        ExcludedPatterns = ExcludedPatterns.Where(p => ScanOptions.ValidatePattern(p) is null).ToList(),
    };
}

/// <summary>Reads and writes settings.json in the app's own folder under %LOCALAPPDATA%.</summary>
public sealed class SettingsStore
{
    private static readonly JsonSerializerOptions Json = new() { WriteIndented = true, PropertyNameCaseInsensitive = true };

    private readonly string _path;

    public SettingsStore(string folder)
    {
        _path = Path.Combine(folder, "settings.json");
    }

    /// <summary>True if the last <see cref="Load"/> found a damaged file and fell back to defaults.</summary>
    public bool LoadedDefaultsAfterError { get; private set; }

    public AppSettings Load()
    {
        LoadedDefaultsAfterError = false;
        if (!File.Exists(_path))
            return new AppSettings();

        try
        {
            var loaded = JsonSerializer.Deserialize<AppSettings>(File.ReadAllText(_path), Json) ?? new AppSettings();
            // Keep id lookups case-insensitive whatever the file contained.
            return loaded with { SelectedRules = new Dictionary<string, bool>(loaded.SelectedRules, StringComparer.OrdinalIgnoreCase) };
        }
        catch (Exception e) when (e is JsonException or IOException or UnauthorizedAccessException or NotSupportedException)
        {
            LoadedDefaultsAfterError = true;
            return new AppSettings();
        }
    }

    /// <summary>Writes to a temporary file first, so a crash mid-save never leaves a half-written file.</summary>
    public void Save(AppSettings settings)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(_path)!);
        var temp = _path + ".tmp";
        File.WriteAllText(temp, JsonSerializer.Serialize(settings, Json));
        File.Move(temp, _path, overwrite: true);
    }
}
