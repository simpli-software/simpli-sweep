using Cleaner.App.Services;
using Cleaner.App.ViewModels;
using Cleaner.Core.DiskUsage;
using Cleaner.Core.Tests;

namespace Cleaner.App.Tests;

public sealed class DiskUsageViewModelTests : IDisposable
{
    private readonly FakeMachine _m = new();
    private readonly FakeShell _shell = new();

    public void Dispose() => _m.Dispose();

    private string Drive => Path.Combine(_m.Root, "Drive");

    private DiskUsageViewModel Vm() =>
        new(new DiskUsageScanner(largestFileCount: 5), _shell, [new DriveChoice(Drive, "Test", 0, 0)], Path.Combine(Drive, "Windows"));

    [Fact]
    public async Task Scan_lists_folders_largest_first_with_a_row_for_loose_files()
    {
        _m.File(Path.Combine(Drive, "Games", "big.pak"), bytes: 9000);
        _m.File(Path.Combine(Drive, "Docs", "a.txt"), bytes: 100);
        _m.File(Path.Combine(Drive, "loose.bin"), bytes: 50);
        var vm = Vm();

        await vm.ScanAsync();

        Assert.Equal(["Games", "Docs", "(files in this folder)"], vm.Folders.Select(f => f.Name));
        Assert.Equal(9000, vm.Folders.First(f => f.Name == "Games").Size);
        Assert.EndsWith("big.pak", vm.LargestFiles[0].Path);
        Assert.Contains("8.9 KB", vm.Status);
    }

    [Fact]
    public async Task Can_drill_into_a_folder_and_back_up()
    {
        _m.File(Path.Combine(Drive, "Games", "Steam", "x.pak"), bytes: 5000);
        _m.File(Path.Combine(Drive, "Games", "Epic", "y.pak"), bytes: 3000);
        var vm = Vm();
        await vm.ScanAsync();
        Assert.False(vm.UpCommand.CanExecute(null));

        vm.OpenFolderCommand.Execute(vm.Folders.Single(f => f.Name == "Games"));

        Assert.Equal(Path.Combine(Drive, "Games"), vm.CurrentPath);
        Assert.Equal(["Steam", "Epic"], vm.Folders.Select(f => f.Name));
        Assert.Equal(62.5, vm.Folders[0].Percent);
        Assert.True(vm.UpCommand.CanExecute(null));

        vm.UpCommand.Execute(null);

        Assert.Equal(Drive, vm.CurrentPath);
    }

    [Fact]
    public async Task Show_opens_Explorer_and_never_deletes()
    {
        var file = _m.File(Path.Combine(Drive, "Games", "big.pak"), bytes: 9000);
        var vm = Vm();
        await vm.ScanAsync();

        vm.ShowInExplorerCommand.Execute(vm.LargestFiles[0].Path);

        Assert.Equal([file], _shell.Shown);
        Assert.True(File.Exists(file));
    }

    [Fact]
    public async Task Folders_without_subfolders_cannot_be_opened()
    {
        _m.File(Path.Combine(Drive, "Flat", "a.bin"), bytes: 10);
        var vm = Vm();

        await vm.ScanAsync();

        Assert.False(vm.Folders.Single(f => f.Name == "Flat").CanOpen);
    }

    [Fact]
    public async Task Hard_link_note_shows_only_where_the_Windows_folder_is_in_view()
    {
        _m.File(Path.Combine(Drive, "Windows", "WinSxS", "x", "a.dll"), bytes: 100);
        _m.File(Path.Combine(Drive, "Games", "Steam", "b.pak"), bytes: 50);
        var vm = Vm();
        await vm.ScanAsync();
        Assert.True(vm.ShowWindowsNote); // drive root contains Windows

        vm.OpenFolderCommand.Execute(vm.Folders.Single(f => f.Name == "Games"));
        Assert.False(vm.ShowWindowsNote);

        vm.UpCommand.Execute(null);
        vm.OpenFolderCommand.Execute(vm.Folders.Single(f => f.Name == "Windows"));
        Assert.True(vm.ShowWindowsNote);
    }

    private sealed class FakeShell : IShell
    {
        public List<string> Shown { get; } = [];
        public void ShowInExplorer(string path) => Shown.Add(path);
    }
}
