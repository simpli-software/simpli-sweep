using System.Reflection;
using System.Windows;
using Cleaner.App.Services;
using Cleaner.App.ViewModels;
using Cleaner.Core;

namespace Cleaner.App;

public partial class App : Application
{
    /// <summary>Display name from the build's &lt;Product&gt; setting; never hard-coded.</summary>
    public static string ProductName { get; } =
        typeof(App).Assembly.GetCustomAttribute<AssemblyProductAttribute>()?.Product ?? "Cleaner";

    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);
        ProcessHardening.Apply();

        DispatcherUnhandledException += (_, args) =>
        {
            // Last resort: report instead of crashing. Never includes paths in logs (there are none).
            MessageBox.Show(args.Exception.Message, ProductName, MessageBoxButton.OK, MessageBoxImage.Error);
            args.Handled = true;
        };

        var tokens = PathTokens.FromEnvironment();
        var guard = SafetyGuard.ForCurrentUser(tokens);
        var recycleBin = new ShellRecycleBin();
        var elevation = new WindowsElevation();

        var viewModel = new MainViewModel(
            RuleLoader.LoadBuiltIn(),
            new Scanner(tokens, guard, recycleBin, TimeProvider.System, elevation.IsElevated),
            new CleanRunner(guard, recycleBin, TimeProvider.System),
            new DialogService(),
            elevation);

        var window = new MainWindow { DataContext = viewModel };
        MainWindow = window;
        window.Show();
        _ = viewModel.ScanAsync();
    }
}
