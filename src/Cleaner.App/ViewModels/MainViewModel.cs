using System.Collections.ObjectModel;
using System.Globalization;
using Cleaner.App.Services;
using Cleaner.Core;

namespace Cleaner.App.ViewModels;

public sealed class MainViewModel : ObservableObject
{
    private readonly IReadOnlyList<CleanRule> _rules;
    private readonly Scanner _scanner;
    private readonly CleanRunner _runner;
    private readonly IDialogService _dialogs;
    private readonly IElevation _elevation;

    private ScanReport? _scan;
    private bool _isBusy;
    private string _status = "Press Scan to see what can be cleaned. Nothing is deleted until you press Clean.";

    public MainViewModel(IReadOnlyList<CleanRule> rules, Scanner scanner, CleanRunner runner, IDialogService dialogs, IElevation elevation)
    {
        _rules = rules;
        _scanner = scanner;
        _runner = runner;
        _dialogs = dialogs;
        _elevation = elevation;

        ScanCommand = new AsyncCommand(ScanAsync, () => !IsBusy);
        CleanCommand = new AsyncCommand(CleanAsync, () => !IsBusy && SelectedBytes > 0);
        RestartAsAdminCommand = new AsyncCommand(RestartAsAdminAsync, () => !IsBusy && NeedsAdmin);
    }

    public ObservableCollection<RuleItemViewModel> Items { get; } = [];

    public StartupViewModel? Startup
    {
        get => _startup;
        init
        {
            _startup = value;
            if (value is not null)
            {
                value.Items.CollectionChanged += (_, _) =>
                {
                    OnPropertyChanged(nameof(NeedsAdmin));
                    RefreshCommands();
                };
            }
        }
    }

    private readonly StartupViewModel? _startup;

    public DiskUsageViewModel? Disk { get; init; }

    public SettingsViewModel? Settings
    {
        get => _settings;
        init
        {
            _settings = value;
            if (value is not null)
                value.OptionsChanged += (_, _) => InvalidateScan();
        }
    }

    private readonly SettingsViewModel? _settings;

    public AsyncCommand ScanCommand { get; }
    public AsyncCommand CleanCommand { get; }
    public AsyncCommand RestartAsAdminCommand { get; }

    public bool IsElevated => _elevation.IsElevated;

    public bool IsBusy
    {
        get => _isBusy;
        private set
        {
            if (Set(ref _isBusy, value))
                RefreshCommands();
        }
    }

    public string Status
    {
        get => _status;
        private set => Set(ref _status, value);
    }

    public long SelectedBytes => Items.Where(i => i.IsSelected).Sum(i => i.Bytes);

    public string SelectedText => Items.Count == 0
        ? ""
        : string.Format(CultureInfo.CurrentCulture, "Selected: {0}", ByteSize.Format(SelectedBytes));

    /// <summary>Some categories or startup programs need the app to run as administrator.</summary>
    public bool NeedsAdmin => !IsElevated
        && (Items.Any(i => i.Result.Status == RuleStatus.NeedsAdmin) || (Startup?.Items.Any(i => !i.CanToggle) ?? false));

    public async Task ScanAsync()
    {
        IsBusy = true;
        Status = "Scanning…";
        try
        {
            var progress = new Progress<CleanRule>(r => Status = $"Scanning: {r.Name}…");
            _scanner.Options = Settings?.Current.ToScanOptions() ?? Cleaner.Core.Settings.ScanOptions.Default;
            _scan = await Task.Run(() => _scanner.Scan(_rules, progress));
            SetItems(_scan);
            Status = _scan.TotalBytes > 0
                ? "Review the list, untick anything you want to keep, then press Clean."
                : "Nothing to clean right now.";
        }
        catch (Exception e)
        {
            _scan = null;
            Items.Clear();
            OnSelectionChanged(this, EventArgs.Empty);
            Status = "Scan failed. Press Scan again to retry.";
            _dialogs.Error("Scan failed", e.Message);
        }
        finally
        {
            IsBusy = false;
        }
    }

    public async Task CleanAsync()
    {
        if (_scan is null)
            return;

        var selected = Items.Where(i => i.IsSelected).ToList();
        if (selected.Count == 0)
            return;

        var message = string.Format(
            CultureInfo.CurrentCulture,
            "Permanently delete {0} from {1} {2}?\n\nThis cannot be undone. Files that are in use will be skipped.",
            ByteSize.Format(SelectedBytes),
            selected.Count,
            selected.Count == 1 ? "category" : "categories");

        if (!_dialogs.Confirm("Clean selected items", message))
            return;

        IsBusy = true;
        Status = "Cleaning…";
        CleanReport report;
        try
        {
            var ids = selected.Select(i => i.Rule.Id).ToHashSet(StringComparer.OrdinalIgnoreCase);
            var scan = _scan;
            report = await Task.Run(() => _runner.Clean(scan, ids));
        }
        catch (Exception e)
        {
            IsBusy = false;
            _dialogs.Error("Clean failed", e.Message);
            await ScanAsync();
            Status = "Clean stopped because of an error. The list shows what is left.";
            return;
        }
        finally
        {
            IsBusy = false;
        }

        await ScanAsync();
        Status = report.Skipped == 0
            ? string.Format(CultureInfo.CurrentCulture, "Done. Freed {0}.", ByteSize.Format(report.BytesFreed))
            : string.Format(CultureInfo.CurrentCulture, "Done. Freed {0}. {1:N0} items were in use or had changed since the scan and were left alone.",
                ByteSize.Format(report.BytesFreed), report.Skipped);
    }

    private Task RestartAsAdminAsync()
    {
        if (!_elevation.RestartElevated())
            Status = "Administrator access was declined. Admin-only categories were skipped.";
        return Task.CompletedTask;
    }

    private void SetItems(ScanReport scan)
    {
        foreach (var item in Items)
        {
            item.SelectionChanged -= OnSelectionChanged;
            item.SelectionChanged -= RememberSelection;
        }

        Items.Clear();
        foreach (var result in scan.Results.OrderBy(r => r.Rule.Category).ThenByDescending(r => r.Status == RuleStatus.Ok ? r.Bytes : -1))
        {
            var item = new RuleItemViewModel(result, Settings?.Current.IsSelected(result.Rule));
            item.SelectionChanged += OnSelectionChanged;
            item.SelectionChanged += RememberSelection;
            Items.Add(item);
        }

        OnSelectionChanged(this, EventArgs.Empty);
        OnPropertyChanged(nameof(NeedsAdmin));
    }

    private void RememberSelection(object? sender, EventArgs e)
    {
        if (sender is RuleItemViewModel item)
            Settings?.SetRuleSelected(item.Rule.Id, item.IsSelected);
    }

    /// <summary>
    /// Exclusions or minimum age changed: the current list may include files that are now
    /// protected, so it is cleared and Clean stays off until the next scan.
    /// </summary>
    private void InvalidateScan()
    {
        if (_scan is null || IsBusy)
            return;

        _scan = null;
        foreach (var item in Items)
        {
            item.SelectionChanged -= OnSelectionChanged;
            item.SelectionChanged -= RememberSelection;
        }
        Items.Clear();
        OnSelectionChanged(this, EventArgs.Empty);
        Status = "Settings changed. Press Scan again to see the updated list.";
    }

    private void OnSelectionChanged(object? sender, EventArgs e)
    {
        OnPropertyChanged(nameof(SelectedBytes));
        OnPropertyChanged(nameof(SelectedText));
        RefreshCommands();
    }

    private void RefreshCommands()
    {
        ScanCommand.RaiseCanExecuteChanged();
        CleanCommand.RaiseCanExecuteChanged();
        RestartAsAdminCommand.RaiseCanExecuteChanged();
    }
}
