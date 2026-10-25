using System.Collections.ObjectModel;
using Cleaner.App.Services;
using Cleaner.Core.Settings;

namespace Cleaner.App.ViewModels;

public sealed record AgeChoice(string Label, double Hours);

/// <summary>Settings tab. Every change is saved straight away.</summary>
public sealed class SettingsViewModel : ObservableObject
{
    private readonly SettingsStore _store;
    private readonly IFolderPicker _folders;
    private AppSettings _current;
    private AgeChoice _selectedAge;
    private string _newPattern = "";
    private string _status = "";

    public SettingsViewModel(SettingsStore store, IFolderPicker folders)
    {
        _store = store;
        _folders = folders;
        _current = store.Load();
        if (store.LoadedDefaultsAfterError)
            _status = "The settings file was damaged, so defaults are in use. Changing anything saves a fresh copy.";

        var choices = new List<AgeChoice>
        {
            new("Each category's own default", 0),
            new("Older than 1 day", 24),
            new("Older than 1 week", 24 * 7),
            new("Older than 1 month", 24 * 30),
        };
        var saved = _current.ToScanOptions().MinimumAgeHours;
        if (choices.All(a => a.Hours != saved))
        {
            // Keep a value set some other way exactly; never round it down to something looser.
            choices.Add(new(string.Format(System.Globalization.CultureInfo.CurrentCulture, "Older than {0:0.#} days", saved / 24), saved));
        }
        AgeChoices = choices;
        _selectedAge = choices.First(a => a.Hours == saved);

        foreach (var f in _current.ExcludedFolders)
            ExcludedFolders.Add(f);
        foreach (var p in _current.ExcludedPatterns)
            ExcludedPatterns.Add(p);

        AddFolderCommand = new RelayCommand(AddFolder);
        RemoveFolderCommand = new RelayCommand<string>(f => Update(() => ExcludedFolders.Remove(f!)));
        AddPatternCommand = new RelayCommand(AddPattern);
        RemovePatternCommand = new RelayCommand<string>(p => Update(() => ExcludedPatterns.Remove(p!)));
    }

    /// <summary>Raised when something that changes scan results (age, exclusions) changes.</summary>
    public event EventHandler? OptionsChanged;

    public AppSettings Current => _current;
    public IReadOnlyList<AgeChoice> AgeChoices { get; }
    public ObservableCollection<string> ExcludedFolders { get; } = [];
    public ObservableCollection<string> ExcludedPatterns { get; } = [];
    public RelayCommand AddFolderCommand { get; }
    public RelayCommand<string> RemoveFolderCommand { get; }
    public RelayCommand AddPatternCommand { get; }
    public RelayCommand<string> RemovePatternCommand { get; }

    public AgeChoice SelectedAge
    {
        get => _selectedAge;
        set
        {
            if (value is not null && Set(ref _selectedAge, value))
                Update(() => { });
        }
    }

    public string NewPattern
    {
        get => _newPattern;
        set => Set(ref _newPattern, value);
    }

    public string Status
    {
        get => _status;
        private set => Set(ref _status, value);
    }

    /// <summary>Remembers a category tick from the Clean tab. Doesn't change scan results.</summary>
    public void SetRuleSelected(string ruleId, bool selected)
    {
        var rules = new Dictionary<string, bool>(_current.SelectedRules, StringComparer.OrdinalIgnoreCase) { [ruleId] = selected };
        Save(_current with { SelectedRules = rules });
    }

    private void AddFolder()
    {
        if (_folders.PickFolder() is not { } folder)
            return;

        if (ScanOptions.ValidateFolder(folder) is { } problem)
        {
            Status = problem;
            return;
        }

        if (!ExcludedFolders.Contains(folder, StringComparer.OrdinalIgnoreCase))
            Update(() => ExcludedFolders.Add(folder));
    }

    private void AddPattern()
    {
        var pattern = NewPattern.Trim();
        if (ScanOptions.ValidatePattern(pattern) is { } problem)
        {
            Status = problem;
            return;
        }

        if (!ExcludedPatterns.Contains(pattern, StringComparer.OrdinalIgnoreCase))
            Update(() => ExcludedPatterns.Add(pattern));
        NewPattern = "";
    }

    private void Update(Action change)
    {
        change();
        var saved = Save(_current with
        {
            MinimumAgeHours = SelectedAge.Hours,
            ExcludedFolders = [.. ExcludedFolders],
            ExcludedPatterns = [.. ExcludedPatterns],
        });
        if (saved)
            Status = "Saved. Scan again to see the effect.";

        // Applies to the next scan even if saving failed (settings only ever make cleaning stricter).
        OptionsChanged?.Invoke(this, EventArgs.Empty);
    }

    private bool Save(AppSettings settings)
    {
        _current = settings;
        try
        {
            _store.Save(settings);
            return true;
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            Status = "Couldn't save settings: " + e.Message;
            return false;
        }
    }
}
