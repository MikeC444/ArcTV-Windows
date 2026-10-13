using System.Windows;
using System.Windows.Controls;
using ArcTV.App.Views;

namespace ArcTV.App;

public partial class MainWindow : Window
{
    public MainWindow()
    {
        InitializeComponent();
        App.Account.PropertyChanged += (_, args) =>
        {
            if (args.PropertyName == nameof(Core.Account.AccountModel.IsSignedIn)) Dispatcher.Invoke(ShowCurrent);
        };
        ShowCurrent();
    }

    private void ShowCurrent() => Host.Content = App.Account.IsSignedIn ? new ShellView(App.Account) : new SignInView();
}
