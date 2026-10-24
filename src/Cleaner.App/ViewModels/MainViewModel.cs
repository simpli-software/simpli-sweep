using System.IO;
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

    /// <summary>Some categories were skipped because the app isn't running as administrator.</summary>
    public bool NeedsAdmin => !IsElevated && Items.Any(i => i.Result.Status == RuleStatus.NeedsAdmin);

    public async Task ScanAsync()
    {
        IsBusy = true;
        Status = "Scanning…";
        try
        {
            var progress = new Progress<CleanRule>(r => Status = $"Scanning: {r.Name}…");
            _scan = await Task.Run(() => _scanner.Scan(_rules, progress));
            SetItems(_scan);
            Status = _scan.TotalBytes > 0
                ? "Review the list, untick anything you want to keep, then press Clean."
                : "Nothing to clean right now.";
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException or InvalidDataException)
        {
            Status = "Scan failed.";
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
            report = await Task.Run(() => _runner.Clean(_scan, ids));
        }
        finally
        {
            IsBusy = false;
        }

        await ScanAsync();
        Status = report.Skipped == 0
            ? string.Format(CultureInfo.CurrentCulture, "Done. Freed {0}.", ByteSize.Format(report.BytesFreed))
            : string.Format(CultureInfo.CurrentCulture, "Done. Freed {0}. {1:N0} files were in use or had changed and were left alone.",
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
            item.SelectionChanged -= OnSelectionChanged;

        Items.Clear();
        foreach (var result in scan.Results.OrderBy(r => r.Rule.Category).ThenByDescending(r => r.Status == RuleStatus.Ok ? r.Bytes : -1))
        {
            var item = new RuleItemViewModel(result);
            item.SelectionChanged += OnSelectionChanged;
            Items.Add(item);
        }

        OnSelectionChanged(this, EventArgs.Empty);
        OnPropertyChanged(nameof(NeedsAdmin));
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
