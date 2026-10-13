using System.ComponentModel;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;
using ArcTV.Core.Addons;

namespace ArcTV.App.Views;

/// <summary>Settings > Addons: what is installed on the account, a switch for each, remove, and add one from a link.</summary>
public sealed class AddonsPane : UserControl
{
    private readonly AddonsModel _addons;
    private readonly StackPanel _list = new();
    private readonly TextBlock _message = new() { FontSize = 13, Margin = new Thickness(0, 10, 0, 0), TextWrapping = TextWrapping.Wrap };
    private readonly TextBox _link = new() { Style = (Style)Application.Current.FindResource("Field") };
    private readonly Button _add = new() { Content = "Add", Style = (Style)Application.Current.FindResource("PrimaryButton"), Height = 44, Width = 96, Margin = new Thickness(10, 0, 0, 0) };
    private string? _confirming;
    private CancellationTokenSource? _run;

    private static Brush Res(string key) => (Brush)Application.Current.FindResource(key);

    public AddonsPane(AddonsModel addons)
    {
        _addons = addons;
        var column = new StackPanel();
        column.Children.Add(new TextBlock
        {
            Text = "Addons give Arc TV its streams (and catalogues). They are kept on your account, so they follow you to every device.",
            FontSize = 13, Foreground = Res("Text2Brush"), TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 0, 0, 16),
        });

        var box = new Border { Background = Res("BgElevatedBrush"), BorderBrush = new SolidColorBrush(Color.FromArgb(0x22, 255, 255, 255)), BorderThickness = new Thickness(1), CornerRadius = new CornerRadius(14), Padding = new Thickness(16) };
        var boxContent = new StackPanel();
        boxContent.Children.Add(new TextBlock { Text = "Add an addon", FontSize = 15, FontWeight = FontWeights.Bold, Margin = new Thickness(0, 0, 0, 10) });
        var row = new Grid();
        row.ColumnDefinitions.Add(new ColumnDefinition());
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        row.Children.Add(_link);
        Grid.SetColumn(_add, 1);
        row.Children.Add(_add);
        boxContent.Children.Add(row);
        boxContent.Children.Add(new TextBlock { Text = "Paste the addon's link (https:// or stremio://)", FontSize = 12, Foreground = Res("Text3Brush"), Margin = new Thickness(0, 8, 0, 0) });
        boxContent.Children.Add(_message);
        box.Child = boxContent;
        column.Children.Add(box);

        _list.Margin = new Thickness(0, 16, 0, 0);
        column.Children.Add(_list);

        Content = column;

        _add.Click += async (_, _) => await AddAsync();
        _link.KeyDown += async (_, e) =>
        {
            if (e.Key == Key.Enter) await AddAsync();
        };
        Loaded += async (_, _) =>
        {
            _addons.PropertyChanged -= OnModelChanged;
            _addons.PropertyChanged += OnModelChanged;
            Refresh();
            _run?.Cancel();
            _run = new CancellationTokenSource();
            await _addons.LoadAsync(_run.Token);
        };
        Unloaded += (_, _) =>
        {
            _addons.PropertyChanged -= OnModelChanged;
            _run?.Cancel();
        };
    }

    private async Task AddAsync()
    {
        var text = _link.Text;
        await _addons.InstallAsync(text);
        if (!_addons.MessageIsError) _link.Clear();
    }

    private void OnModelChanged(object? sender, PropertyChangedEventArgs e) => Dispatcher.BeginInvoke(Refresh);

    private void Refresh()
    {
        _message.Text = _addons.Message ?? "";
        _message.Visibility = string.IsNullOrEmpty(_addons.Message) ? Visibility.Collapsed : Visibility.Visible;
        _message.Foreground = _addons.MessageIsError ? new SolidColorBrush(Color.FromRgb(0xFF, 0x6B, 0x8A)) : Res("AccentBrush");
        _add.Content = _addons.Busy ? "Adding…" : "Add";
        _add.IsEnabled = !_addons.Busy;

        _list.Children.Clear();
        if (!_addons.Loaded)
        {
            _list.Children.Add(new TextBlock { Text = "Loading your addons…", Foreground = Res("Text2Brush") });
            return;
        }

        if (_addons.Items.Count == 0)
        {
            _list.Children.Add(new TextBlock { Text = "No addons yet. Paste a link above to add one (Torrentio, for example, gives streams).", Foreground = Res("Text2Brush"), TextWrapping = TextWrapping.Wrap });
            return;
        }

        foreach (var addon in _addons.Items) _list.Children.Add(Card(addon));
    }

    private UIElement Card(SyncedAddon addon)
    {
        var card = new Border
        {
            Background = Res("BgElevatedBrush"), BorderBrush = new SolidColorBrush(Color.FromArgb(0x22, 255, 255, 255)), BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(14), Padding = new Thickness(16, 14, 16, 14), Margin = new Thickness(0, 0, 0, 10),
        };
        var grid = new Grid();
        grid.ColumnDefinitions.Add(new ColumnDefinition());
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

        var text = new StackPanel();
        var title = new StackPanel { Orientation = Orientation.Horizontal };
        title.Children.Add(new TextBlock { Text = addon.Name, FontSize = 15, FontWeight = FontWeights.Bold, Foreground = addon.Enabled ? Res("TextBrush") : Res("Text3Brush") });
        if (addon.ManifestJson?.Version is { Length: > 0 } version)
            title.Children.Add(new TextBlock { Text = "v" + version, FontSize = 12, Foreground = Res("Text3Brush"), Margin = new Thickness(8, 3, 0, 0) });
        text.Children.Add(title);
        if (addon.ManifestJson?.Description is { Length: > 0 } description)
            text.Children.Add(new TextBlock { Text = description, FontSize = 12, Foreground = Res("Text2Brush"), TextWrapping = TextWrapping.Wrap, TextTrimming = TextTrimming.CharacterEllipsis, MaxHeight = 34, Margin = new Thickness(0, 4, 0, 0) });
        text.Children.Add(new TextBlock { Text = AddonsModel.Provides(addon), FontSize = 12, Foreground = Res("Text3Brush"), Margin = new Thickness(0, 4, 0, 0) });
        grid.Children.Add(text);

        var right = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(14, 0, 0, 0) };
        if (_confirming == addon.ManifestUrl)
        {
            right.Children.Add(new TextBlock { Text = "Remove from every device?", FontSize = 12, Foreground = Res("Text2Brush"), VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 12, 0) });
            var yes = new Button { Content = "Remove", Style = (Style)Application.Current.FindResource("LinkButton"), Foreground = new SolidColorBrush(Color.FromRgb(0xFF, 0x6B, 0x8A)), Margin = new Thickness(0, 0, 12, 0) };
            yes.Click += async (_, _) =>
            {
                _confirming = null;
                await _addons.RemoveAsync(addon);
            };
            var no = new Button { Content = "Cancel", Style = (Style)Application.Current.FindResource("LinkButton"), Foreground = Res("Text2Brush") };
            no.Click += (_, _) =>
            {
                _confirming = null;
                Refresh();
            };
            right.Children.Add(yes);
            right.Children.Add(no);
        }
        else
        {
            var toggle = new ToggleButton { Style = (Style)Application.Current.FindResource("SwitchToggle"), IsChecked = addon.Enabled, ToolTip = addon.Enabled ? "On: turn it off" : "Off: turn it on" };
            toggle.Click += async (_, _) => await _addons.SetEnabledAsync(addon, toggle.IsChecked == true);
            var remove = new Button { Content = "Remove", Style = (Style)Application.Current.FindResource("LinkButton"), Foreground = Res("Text2Brush"), Margin = new Thickness(16, 0, 0, 0), ToolTip = "Take this addon off your account" };
            remove.Click += (_, _) =>
            {
                _confirming = addon.ManifestUrl;
                Refresh();
            };
            right.Children.Add(toggle);
            right.Children.Add(remove);
        }

        Grid.SetColumn(right, 1);
        grid.Children.Add(right);
        card.Child = grid;
        return card;
    }
}
