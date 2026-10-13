using System.Windows;
using System.Windows.Controls;
using ArcTV.App.Views;
using ArcTV.Core;

namespace ArcTV.App;

public partial class MainWindow : Window
{
    public MainWindow()
    {
        InitializeComponent();
        VersionLabel.Text = $"Arc TV for Windows · {AppInfo.Version}";
        App.Account.PropertyChanged += (_, args) =>
        {
            if (args.PropertyName == nameof(Core.Account.AccountModel.IsSignedIn)) Dispatcher.Invoke(ShowCurrent);
        };
        ShowCurrent();
    }

    private void ShowCurrent() => Host.Content = App.Account.IsSignedIn ? new SignedInPlaceholder(App.Account) : new SignInView();
}
