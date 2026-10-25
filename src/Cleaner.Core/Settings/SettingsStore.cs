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
        SelectedRules is not null && SelectedRules.TryGetValue(rule.Id, out var selected) ? selected : rule.EnabledByDefault;

    /// <summary>Drops invalid entries so a hand-edited file can never loosen anything.</summary>
    public ScanOptions ToScanOptions() => new()
    {
        MinimumAgeHours = double.IsFinite(MinimumAgeHours) ? Math.Clamp(MinimumAgeHours, 0, 24 * 3650) : 0,
        ExcludedFolders = (ExcludedFolders ?? []).Select(NormalizeFolder).OfType<string>().ToList(),
        ExcludedPatterns = (ExcludedPatterns ?? []).Where(p => ScanOptions.ValidatePattern(p) is null).ToList(),
    };

    /// <summary>Full, canonical form so "C:/a/../Projects" matches scanned paths; null if unusable.</summary>
    private static string? NormalizeFolder(string? folder)
    {
        if (folder is null || ScanOptions.ValidateFolder(folder) is not null)
            return null;
        try { return Path.TrimEndingDirectorySeparator(Path.GetFullPath(folder)); }
        catch (Exception e) when (e is ArgumentException or NotSupportedException or PathTooLongException) { return null; }
    }
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

    /// <summary>
    /// True if the last <see cref="Load"/> found a damaged file. It is moved aside to settings.json.bad
    /// (never overwritten) and defaults are used.
    /// </summary>
    public bool LoadedDefaultsAfterError { get; private set; }

    public string DamagedCopyPath => _path + ".bad";

    /// <summary>False until the user has run the app (or saved settings) at least once.</summary>
    public bool Exists => File.Exists(_path);

    /// <summary>
    /// A damaged file is waiting for the user to check their settings. Stays true across restarts
    /// until <see cref="MarkReviewed"/> is called.
    /// </summary>
    public bool NeedsReview => File.Exists(DamagedCopyPath);

    /// <summary>The user has checked their settings: keep the damaged copy under a dated name.</summary>
    public void MarkReviewed()
    {
        if (File.Exists(DamagedCopyPath))
            File.Move(DamagedCopyPath, ArchiveName(), overwrite: false);
    }

    private string ArchiveName() =>
        $"{DamagedCopyPath}-{DateTime.UtcNow:yyyyMMdd'T'HHmmss'Z'}-{Guid.NewGuid().ToString("N")[..8]}";

    private bool _damagedFileInPlace;

    public AppSettings Load()
    {
        LoadedDefaultsAfterError = false;
        if (!File.Exists(_path))
            return new AppSettings();

        try
        {
            var loaded = JsonSerializer.Deserialize<AppSettings>(File.ReadAllText(_path), Json) ?? new AppSettings();

            // Keep id lookups case-insensitive whatever the file contained; on a clash, "off" wins.
            var rules = new Dictionary<string, bool>(StringComparer.OrdinalIgnoreCase);
            foreach (var (id, selected) in loaded.SelectedRules ?? [])
            {
                if (id is not null && !rules.TryAdd(id, selected))
                    rules[id] &= selected;
            }

            return loaded with
            {
                SelectedRules = rules,
                ExcludedFolders = loaded.ExcludedFolders ?? [],
                ExcludedPatterns = loaded.ExcludedPatterns ?? [],
            };
        }
        catch (Exception)
        {
            // Any problem with the file: set it aside so the user's exclusions can be recovered,
            // and start from defaults. Saving later can never overwrite the damaged copy.
            LoadedDefaultsAfterError = true;
            // An earlier damaged copy the user hasn't reviewed yet is kept under a dated name, not replaced.
            try
            {
                if (File.Exists(DamagedCopyPath))
                    File.Move(DamagedCopyPath, ArchiveName(), overwrite: false);
                File.Move(_path, DamagedCopyPath, overwrite: false);
            }
            catch (Exception e) when (e is IOException or UnauthorizedAccessException) { _damagedFileInPlace = true; }
            return new AppSettings();
        }
    }

    /// <summary>Writes to a temporary file first, so a crash mid-save never leaves a half-written file.</summary>
    public void Save(AppSettings settings)
    {
        if (_damagedFileInPlace)
            throw new IOException("The damaged settings file couldn't be set aside, so it won't be overwritten.");

        Directory.CreateDirectory(Path.GetDirectoryName(_path)!);
        var temp = _path + ".tmp";
        File.WriteAllText(temp, JsonSerializer.Serialize(settings, Json));
        File.Move(temp, _path, overwrite: true);
    }
}
