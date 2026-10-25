using System.Collections.ObjectModel;
using System.Globalization;
using System.Windows.Input;
using Cleaner.App.Services;
using Cleaner.Core;
using Cleaner.Core.DiskUsage;

namespace Cleaner.App.ViewModels;

public sealed record DriveChoice(string Root, string Label, long FreeBytes, long TotalBytes)
{
    public string Display => string.Format(CultureInfo.CurrentCulture, "{0} {1} — {2} free of {3}",
        Root, Label, ByteSize.Format(FreeBytes), ByteSize.Format(TotalBytes));
}

/// <summary>Disk tab: shows where space goes. Read-only — it never deletes anything.</summary>
public sealed class DiskUsageViewModel : ObservableObject
{
    private readonly DiskUsageScanner _scanner;
    private readonly IShell _shell;
    private CancellationTokenSource? _cts;
    private DriveChoice? _selectedDrive;
    private DiskUsageReport? _report;
    private FolderNode? _current;
    private bool _isBusy;
    private string _status = "Pick a drive and press Scan to see what is using space.";

    public DiskUsageViewModel(DiskUsageScanner scanner, IShell shell, IReadOnlyList<DriveChoice> drives)
    {
        _scanner = scanner;
        _shell = shell;
        Drives = drives;
        _selectedDrive = drives.FirstOrDefault();

        ScanCommand = new AsyncCommand(ScanAsync, () => !IsBusy && SelectedDrive is not null);
        CancelCommand = new RelayCommand(() => _cts?.Cancel(), () => IsBusy);
        UpCommand = new RelayCommand(() => Show(_current?.Parent), () => !IsBusy && _current?.Parent is not null);
        OpenFolderCommand = new RelayCommand<FolderRowViewModel>(row => Show(row?.Node), _ => !IsBusy);
        ShowInExplorerCommand = new RelayCommand<string>(path =>
        {
            if (path is not null)
                _shell.ShowInExplorer(path);
        });
    }

    public IReadOnlyList<DriveChoice> Drives { get; }
    public ObservableCollection<FolderRowViewModel> Folders { get; } = [];
    public ObservableCollection<LargeFile> LargestFiles { get; } = [];

    public AsyncCommand ScanCommand { get; }
    public RelayCommand CancelCommand { get; }
    public RelayCommand UpCommand { get; }
    public RelayCommand<FolderRowViewModel> OpenFolderCommand { get; }
    public RelayCommand<string> ShowInExplorerCommand { get; }

    public DriveChoice? SelectedDrive
    {
        get => _selectedDrive;
        set
        {
            if (Set(ref _selectedDrive, value))
                ScanCommand.RaiseCanExecuteChanged();
        }
    }

    public bool IsBusy
    {
        get => _isBusy;
        private set
        {
            if (!Set(ref _isBusy, value))
                return;
            ScanCommand.RaiseCanExecuteChanged();
            CancelCommand.RaiseCanExecuteChanged();
            UpCommand.RaiseCanExecuteChanged();
        }
    }

    public string Status
    {
        get => _status;
        private set => Set(ref _status, value);
    }

    public string CurrentPath => _current?.FullPath ?? "";

    public async Task ScanAsync()
    {
        if (SelectedDrive is not { } drive)
            return;

        _cts = new CancellationTokenSource();
        IsBusy = true;
        Status = "Scanning… this can take a minute on a large drive.";
        var progress = new Progress<DiskScanProgress>(p =>
            Status = string.Format(CultureInfo.CurrentCulture, "Scanning… {0:N0} files, {1}", p.Files, ByteSize.Format(p.Bytes)));

        try
        {
            var token = _cts.Token;
            _report = await Task.Run(() => _scanner.Scan(drive.Root, progress, token), token);

            LargestFiles.Clear();
            foreach (var file in _report.LargestFiles)
                LargestFiles.Add(file);

            Show(_report.Root);
            Status = string.Format(CultureInfo.CurrentCulture, "{0} in {1:N0} files.{2}",
                ByteSize.Format(_report.Root.Size), _report.Root.FileCount,
                _report.InaccessibleFolders > 0
                    ? string.Format(CultureInfo.CurrentCulture, " {0:N0} folders couldn't be read without administrator rights.", _report.InaccessibleFolders)
                    : "");
        }
        catch (OperationCanceledException)
        {
            Status = "Scan cancelled.";
        }
        catch (Exception e)
        {
            Status = "Scan failed: " + e.Message;
        }
        finally
        {
            IsBusy = false;
            _cts.Dispose();
            _cts = null;
        }
    }

    private void Show(FolderNode? folder)
    {
        if (folder is null)
            return;

        _current = folder;
        Folders.Clear();

        var parentSize = Math.Max(1, folder.Size);
        var rows = folder.Children
            .Where(c => c.Size > 0 || c.Children.Count > 0)
            .Select(c => new FolderRowViewModel(c, c.Name, c.Size, parentSize, c.FullPath))
            .ToList();
        if (folder.OwnFilesSize > 0)
            rows.Add(new FolderRowViewModel(null, "(files in this folder)", folder.OwnFilesSize, parentSize, folder.FullPath));

        foreach (var row in rows.OrderByDescending(r => r.Size))
            Folders.Add(row);

        OnPropertyChanged(nameof(CurrentPath));
        UpCommand.RaiseCanExecuteChanged();
    }
}

public sealed class FolderRowViewModel(FolderNode? node, string name, long size, long parentSize, string path)
{
    public FolderNode? Node { get; } = node;
    public string Name { get; } = name;
    public string Path { get; } = path;
    public long Size { get; } = size;
    public string SizeText => ByteSize.Format(Size);

    /// <summary>0–100, for the bar showing this row's share of the current folder.</summary>
    public double Percent { get; } = 100.0 * size / parentSize;

    public bool CanOpen => Node is not null && Node.Children.Count > 0;
}

public sealed class RelayCommand(Action execute, Func<bool>? canExecute = null) : ICommand
{
    public event EventHandler? CanExecuteChanged;
    public bool CanExecute(object? parameter) => canExecute?.Invoke() ?? true;
    public void Execute(object? parameter) => execute();
    public void RaiseCanExecuteChanged() => CanExecuteChanged?.Invoke(this, EventArgs.Empty);
}

public sealed class RelayCommand<T>(Action<T?> execute, Func<T?, bool>? canExecute = null) : ICommand
{
    public event EventHandler? CanExecuteChanged;
    public bool CanExecute(object? parameter) => canExecute?.Invoke(parameter is T t ? t : default) ?? true;
    public void Execute(object? parameter) => execute(parameter is T t ? t : default);
    public void RaiseCanExecuteChanged() => CanExecuteChanged?.Invoke(this, EventArgs.Empty);
}
