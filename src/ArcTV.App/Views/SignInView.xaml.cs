using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using ArcTV.Core.Account;
using QRCoder;

namespace ArcTV.App.Views;

public partial class SignInView : UserControl
{
    private readonly AccountModel _account = App.Account;
    private readonly DispatcherTimer _wait = new() { Interval = TimeSpan.FromSeconds(1) };
    private CancellationTokenSource? _qr;
    private bool _signUp;
    private bool _syncing;

    public SignInView()
    {
        InitializeComponent();
        ServerBox.Text = _account.ServerUrl;
        _wait.Tick += (_, _) => ShowError();
        Loaded += (_, _) => _account.PropertyChanged += OnAccountChanged;
        Unloaded += (_, _) =>
        {
            _account.PropertyChanged -= OnAccountChanged;
            _wait.Stop();
            StopQr();
        };
    }

    private void OnAccountChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs e) => Dispatcher.Invoke(ShowError);

    private string Password => PasswordHidden.Visibility == Visibility.Visible ? PasswordHidden.Password : PasswordShown.Text;

    private void Step(UIElement visible)
    {
        foreach (var panel in new UIElement[] { StartStep, FormStep, QrStep }) panel.Visibility = panel == visible ? Visibility.Visible : Visibility.Collapsed;
        Heading.Text = visible == QrStep ? "Sign in with your phone" : "Your Entertainment, Your Way";
        Sub.Visibility = visible == StartStep ? Visibility.Visible : Visibility.Collapsed;
        StopQr();
    }

    private void ShowForm(bool signUp)
    {
        _signUp = signUp;
        Step(FormStep);
        NamePanel.Visibility = signUp ? Visibility.Visible : Visibility.Collapsed;
        SubmitButton.Content = signUp ? "Create account" : "Log In";
        ToggleLabel.Text = signUp ? "Already have an account?" : "New here?";
        ToggleButton.Content = signUp ? "Log in" : "Create an account";
        ShowError();
        EmailBox.Focus();
    }

    private void OnStartLogIn(object sender, RoutedEventArgs e) => ShowForm(false);

    private void OnStartSignUp(object sender, RoutedEventArgs e) => ShowForm(true);

    private void OnToggleMode(object sender, RoutedEventArgs e) => ShowForm(!_signUp);

    private void OnBack(object sender, RoutedEventArgs e) => Step(StartStep);

    private void OnPasswordChanged(object sender, RoutedEventArgs e)
    {
        if (_syncing) return;
        _syncing = true;
        PasswordShown.Text = PasswordHidden.Password;
        _syncing = false;
    }

    private void OnPasswordShownChanged(object sender, TextChangedEventArgs e)
    {
        if (_syncing) return;
        _syncing = true;
        PasswordHidden.Password = PasswordShown.Text;
        _syncing = false;
    }

    private void OnShowPassword(object sender, RoutedEventArgs e)
    {
        var show = ShowPassword.IsChecked == true;
        PasswordHidden.Visibility = show ? Visibility.Collapsed : Visibility.Visible;
        PasswordShown.Visibility = show ? Visibility.Visible : Visibility.Collapsed;
    }

    private async void OnSubmit(object sender, RoutedEventArgs e)
    {
        SubmitButton.IsEnabled = false;
        try
        {
            // success flips MainWindow to the signed-in view
            if (_signUp) await _account.RegisterAsync(EmailBox.Text, Password, NameBox.Text);
            else await _account.SignInAsync(EmailBox.Text, Password);
        }
        finally
        {
            SubmitButton.IsEnabled = true;
            ShowError();
        }
    }

    private void ShowError()
    {
        var message = _account.Error;
        if (_account.Waiting && _account.WaitUntil is { } until)
        {
            var seconds = Math.Max(0, (int)Math.Ceiling((until - DateTime.UtcNow).TotalSeconds));
            message = $"{message} ({seconds}s)";
            SubmitButton.IsEnabled = false;
            _wait.Start();
        }
        else
        {
            if (_wait.IsEnabled) _wait.Stop();
            SubmitButton.IsEnabled = !_account.Busy;
        }

        ErrorText.Text = message;
        ErrorText.Visibility = string.IsNullOrEmpty(message) ? Visibility.Collapsed : Visibility.Visible;
    }

    private async void OnStartQr(object sender, RoutedEventArgs e)
    {
        Step(QrStep);
        var cancel = _qr = new CancellationTokenSource();
        QrImage.Source = null;
        QrMessage.Text = "Getting a code…";
        NewCodeButton.Visibility = Visibility.Collapsed;
        var outcome = await _account.SignInWithQrAsync(ShowCode, TimeSpan.FromSeconds(2), cancel.Token);
        if (cancel.IsCancellationRequested) return;
        switch (outcome)
        {
            case QrOutcome.Expired:
                QrImage.Source = null;
                QrMessage.Text = "This code ran out. Make a new one.";
                NewCodeButton.Visibility = Visibility.Visible;
                break;
            case QrOutcome.Failed:
                QrImage.Source = null;
                QrMessage.Text = _account.Error ?? "Couldn't get a code.";
                NewCodeButton.Visibility = Visibility.Visible;
                break;
        }
    }

    private void ShowCode(string url)
    {
        using var generator = new QRCodeGenerator();
        using var data = generator.CreateQrCode(url, QRCodeGenerator.ECCLevel.M);
        var png = new PngByteQRCode(data).GetGraphic(12, [0, 0, 0], [255, 255, 255]);
        var image = new BitmapImage();
        image.BeginInit();
        image.CacheOption = BitmapCacheOption.OnLoad;
        image.StreamSource = new MemoryStream(png);
        image.EndInit();
        image.Freeze();
        QrImage.Source = image;
        QrMessage.Text = "";
    }

    private void StopQr()
    {
        _qr?.Cancel();
        _qr = null;
    }

    private void OnHeadingClick(object sender, MouseButtonEventArgs e) => RevealServer();

    private void OnLogoClick(object sender, MouseButtonEventArgs e) => RevealServer();

    /// <summary>Ctrl-click the logo or heading to point the app at a different server.</summary>
    private void RevealServer()
    {
        if (Keyboard.Modifiers.HasFlag(ModifierKeys.Control)) ServerStep.Visibility = Visibility.Visible;
    }

    private void OnServerChanged(object sender, RoutedEventArgs e)
    {
        var text = ServerBox.Text.Trim();
        _account.ServerUrl = text == AppInfoDefault() ? "" : text;
    }

    private static string AppInfoDefault() => ArcTV.Core.AppInfo.DefaultApiBaseUrl;
}
