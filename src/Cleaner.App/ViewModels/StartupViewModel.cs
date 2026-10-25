using System.Collections.ObjectModel;
using System.Globalization;
using Cleaner.Core.Startup;

namespace Cleaner.App.ViewModels;

public sealed class StartupViewModel : ObservableObject
{
    private readonly StartupManager _manager;
    private readonly IFileTrust _trust;
    private readonly bool _isElevated;
    private bool _isBusy;
    private string _status = "";

    public StartupViewModel(StartupManager manager, IFileTrust trust, bool isElevated)
    {
        _manager = manager;
        _trust = trust;
        _isElevated = isElevated;
        RefreshCommand = new AsyncCommand(LoadAsync, () => !IsBusy);
    }

    public ObservableCollection<StartupItemViewModel> Items { get; } = [];
    public AsyncCommand RefreshCommand { get; }

    public bool IsBusy
    {
        get => _isBusy;
        private set
        {
            if (Set(ref _isBusy, value))
                RefreshCommand.RaiseCanExecuteChanged();
        }
    }

    public string Status
    {
        get => _status;
        private set => Set(ref _status, value);
    }

    public async Task LoadAsync()
    {
        IsBusy = true;
        Status = "Reading startup programs…";
        try
        {
            var rows = await Task.Run(() => _manager.List()
                .Select(item => (item, details: _trust.Inspect(item.ExecutablePath)))
                .OrderBy(r => r.item.Name, StringComparer.CurrentCultureIgnoreCase)
                .ToList());

            Items.Clear();
            foreach (var (item, details) in rows)
                Items.Add(new StartupItemViewModel(item, details, !item.RequiresAdmin || _isElevated, Toggle, UpdateSummary));

            UpdateSummary();
        }
        catch (Exception e)
        {
            Status = "Could not read startup programs: " + e.Message;
        }
        finally
        {
            IsBusy = false;
        }
    }

    /// <returns>The item after the change, or null if it could not be changed.</returns>
    private StartupItem? Toggle(StartupItem item, bool enabled)
    {
        try
        {
            return _manager.SetEnabled(item, enabled);
        }
        catch (UnauthorizedAccessException)
        {
            Status = "That item applies to all users. Restart as administrator to change it.";
            return null;
        }
        catch (IOException e)
        {
            Status = "Could not change it: " + e.Message;
            return null;
        }
    }

    private void UpdateSummary()
    {
        var on = Items.Count(i => i.IsEnabled);
        Status = string.Format(CultureInfo.CurrentCulture,
            "{0} of {1} programs start with Windows. Switching one off doesn't uninstall or delete anything; you can switch it back on here or in Task Manager.",
            on, Items.Count);
    }
}

public sealed class StartupItemViewModel : ObservableObject
{
    private readonly Func<StartupItem, bool, StartupItem?> _toggle;
    private readonly Action _changed;
    private StartupItem _item;

    public StartupItemViewModel(StartupItem item, FileDetails details, bool canToggle, Func<StartupItem, bool, StartupItem?> toggle, Action changed)
    {
        _item = item;
        Details = details;
        CanToggle = canToggle;
        _toggle = toggle;
        _changed = changed;
    }

    public FileDetails Details { get; }
    public bool CanToggle { get; }
    public string Name => _item.Name.EndsWith(".lnk", StringComparison.OrdinalIgnoreCase) ? _item.Name[..^4] : _item.Name;
    public string Command => _item.Command;
    public string Location => _item.Location;
    public string Publisher => Details.Publisher ?? "Unknown publisher";

    public string SignatureText => Details.Signature switch
    {
        SignatureStatus.Valid => "Signed",
        SignatureStatus.Unsigned => "Not signed",
        SignatureStatus.Invalid => "Signature problem",
        SignatureStatus.Missing => "Program file not found",
        _ => "",
    };

    /// <summary>Unsigned, broken or missing programs are worth a second look.</summary>
    public bool NeedsAttention => Details.Signature is SignatureStatus.Unsigned or SignatureStatus.Invalid or SignatureStatus.Missing;

    public string AdminNote => CanToggle ? "" : "Needs administrator";

    public bool IsEnabled
    {
        get => _item.Enabled;
        set
        {
            if (value == _item.Enabled || !CanToggle)
            {
                OnPropertyChanged();
                return;
            }

            if (_toggle(_item, value) is { } updated)
            {
                _item = updated;
                _changed();
            }

            OnPropertyChanged(); // also snaps the switch back if the change failed
        }
    }
}
