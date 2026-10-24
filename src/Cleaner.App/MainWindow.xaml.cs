using System.Windows;

namespace Cleaner.App;

public partial class MainWindow : Window
{
    public MainWindow()
    {
        InitializeComponent();
        Title = App.ProductName;
        TitleText.Text = App.ProductName;
    }
}
