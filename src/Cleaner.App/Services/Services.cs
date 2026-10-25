using System.Diagnostics;
using System.Security.Principal;
using System.Windows;

namespace Cleaner.App.Services;

public interface IDialogService
{
    bool Confirm(string title, string message);
    void Error(string title, string message);
}

public interface IElevation
{
    bool IsElevated { get; }

    /// <summary>Relaunches the app elevated. Returns false if the user declined the UAC prompt.</summary>
    bool RestartElevated();
}

public interface IShell
{
    /// <summary>Opens File Explorer with the item selected.</summary>
    void ShowInExplorer(string path);
}

public sealed class WindowsShell : IShell
{
    public void ShowInExplorer(string path)
    {
        // Windows paths can't contain quotes, so quoting is enough to keep this one argument.
        if (path.Contains('"'))
            return;
        Process.Start(new ProcessStartInfo("explorer.exe", $"/select,\"{path}\"") { UseShellExecute = false })?.Dispose();
    }
}

public sealed class DialogService : IDialogService
{
    public bool Confirm(string title, string message) =>
        MessageBox.Show(Application.Current.MainWindow, message, title, MessageBoxButton.YesNo, MessageBoxImage.Warning, MessageBoxResult.No)
        == MessageBoxResult.Yes;

    public void Error(string title, string message) =>
        MessageBox.Show(Application.Current.MainWindow, message, title, MessageBoxButton.OK, MessageBoxImage.Error);
}

public sealed class WindowsElevation : IElevation
{
    public bool IsElevated { get; } =
        new WindowsPrincipal(WindowsIdentity.GetCurrent()).IsInRole(WindowsBuiltInRole.Administrator);

    public bool RestartElevated()
    {
        try
        {
            Process.Start(new ProcessStartInfo(Environment.ProcessPath!) { UseShellExecute = true, Verb = "runas" });
            Application.Current.Shutdown();
            return true;
        }
        catch (System.ComponentModel.Win32Exception)
        {
            return false; // UAC prompt declined
        }
    }
}
