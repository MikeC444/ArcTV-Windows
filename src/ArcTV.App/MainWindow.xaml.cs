using System.Windows;
using ArcTV.Core;

namespace ArcTV.App;

public partial class MainWindow : Window
{
    public MainWindow()
    {
        InitializeComponent();
        VersionLabel.Text = $"Arc TV for Windows · {AppInfo.Version}";
        Status.Text = "The foundations are in place. Sign-in and Home come next.";
    }
}
