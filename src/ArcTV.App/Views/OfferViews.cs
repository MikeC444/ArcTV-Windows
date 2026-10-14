using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using ArcTV.Core;

namespace ArcTV.App.Views;

/// <summary>The dimmed page and the card on it, shared by the two popups below (a click on the dim closes the card).</summary>
internal static class OfferCard
{
    public static Grid Frame(UIElement content, Action close)
    {
        var grid = new Grid();
        var dim = new Border { Background = new SolidColorBrush(Color.FromArgb(0xA6, 0, 0, 0)) };
        dim.MouseLeftButtonDown += (_, e) =>
        {
            e.Handled = true;
            close();
        };
        grid.Children.Add(dim);
        var card = new Border
        {
            Width = 520, MaxWidth = 700, Padding = new Thickness(28), CornerRadius = new CornerRadius(22), Background = Ui.Res("BgElevatedBrush"), BorderBrush = Ui.Line, BorderThickness = new Thickness(1),
            HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center, Child = content,
            Effect = new System.Windows.Media.Effects.DropShadowEffect { BlurRadius = 40, ShadowDepth = 12, Opacity = 0.6 },
        };
        card.MouseLeftButtonDown += (_, e) => e.Handled = true; // a click inside is not a click on the dim
        grid.Children.Add(card);
        return grid;
    }

    public static Image Logo() => new() { Source = new BitmapImage(new Uri("pack://application:,,,/ArcTV;component/Assets/logo.png")), Height = 30, HorizontalAlignment = HorizontalAlignment.Left };

    public static Button CloseCross(Action close)
    {
        var button = new Button
        {
            Style = Ui.StyleOf("PosterButton"), Cursor = System.Windows.Input.Cursors.Hand, ToolTip = "Close", HorizontalAlignment = HorizontalAlignment.Right, VerticalAlignment = VerticalAlignment.Top,
            Content = new Border
            {
                Width = 32, Height = 32, CornerRadius = new CornerRadius(16), Background = new SolidColorBrush(Color.FromArgb(0x1F, 255, 255, 255)),
                Child = new TextBlock { Text = "", FontFamily = new FontFamily("Segoe MDL2 Assets"), FontSize = 12, FontWeight = FontWeights.Bold, HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center },
            },
        };
        button.Click += (_, _) => close();
        return button;
    }
}

/// <summary>"Arc TV X is out": what is new, with Download, Later and "Skip this version" (the Mac app's card). Shown once per launch when a newer release exists.</summary>
public sealed class UpdateOfferView : Grid
{
    public ReleaseInfo Release { get; }

    /// <param name="dismiss">Told "skip" when the person chose "Skip this version", else not (Later, the cross, or a click outside).</param>
    public UpdateOfferView(ReleaseInfo release, string currentVersion, Action<bool> dismiss, Action<ReleaseInfo> download)
    {
        Release = release;
        var stack = new StackPanel();
        var top = new DockPanel { Margin = new Thickness(0, 0, 0, 14) };
        var cross = OfferCard.CloseCross(() => dismiss(false));
        DockPanel.SetDock(cross, Dock.Right);
        top.Children.Add(cross);
        top.Children.Add(OfferCard.Logo());
        stack.Children.Add(top);
        stack.Children.Add(new TextBlock { Text = $"Arc TV {release.Version} is out", FontSize = 26, FontWeight = FontWeights.ExtraBold, TextWrapping = TextWrapping.Wrap });
        stack.Children.Add(new TextBlock { Text = $"You have {currentVersion}. Here is what is new:", FontSize = 14, Foreground = Ui.Res("Text2Brush"), Margin = new Thickness(0, 6, 0, 12) });
        stack.Children.Add(new Border
        {
            Background = Ui.Res("SurfaceBrush"), CornerRadius = new CornerRadius(12), Padding = new Thickness(14), MaxHeight = 240,
            Child = new ScrollViewer { VerticalScrollBarVisibility = ScrollBarVisibility.Auto, Content = new TextBlock { Text = release.Notes.Length == 0 ? "Improvements and fixes." : release.Notes, FontSize = 14, TextWrapping = TextWrapping.Wrap } },
        });

        var buttons = new DockPanel { Margin = new Thickness(0, 18, 0, 0) };
        var skip = new Button { Content = "Skip this version", Style = Ui.StyleOf("LinkButton"), VerticalAlignment = VerticalAlignment.Center };
        skip.Click += (_, _) => dismiss(true);
        DockPanel.SetDock(skip, Dock.Right);
        buttons.Children.Add(skip);
        var get = new Button { Content = "⬇  Download", Style = Ui.StyleOf("PrimaryButton"), Height = 42, Padding = new Thickness(22, 0, 22, 0), Margin = new Thickness(0, 0, 12, 0) };
        get.Click += (_, _) =>
        {
            download(release);
            dismiss(false);
        };
        var later = new Button { Content = "Later", Style = Ui.StyleOf("GhostButton"), Height = 42, Padding = new Thickness(22, 0, 22, 0), Margin = new Thickness(0, 0, 12, 0) };
        later.Click += (_, _) => dismiss(false);
        buttons.Children.Add(new StackPanel { Orientation = Orientation.Horizontal, Children = { get, later } });
        stack.Children.Add(buttons);
        Children.Add(OfferCard.Frame(stack, () => dismiss(false)));
    }
}

/// <summary>"Everything in Arc TV Plus": what a Plus membership gives, shown once to members (the Mac app's welcome card).</summary>
public sealed class PlusWelcomeView : Grid
{
    public PlusWelcomeView(Action close, Action seeSettings)
    {
        var stack = new StackPanel();
        var top = new DockPanel { Margin = new Thickness(0, 0, 0, 14) };
        var cross = OfferCard.CloseCross(close);
        DockPanel.SetDock(cross, Dock.Right);
        top.Children.Add(cross);
        var brand = new StackPanel { Orientation = Orientation.Horizontal };
        brand.Children.Add(OfferCard.Logo());
        brand.Children.Add(new TextBlock { Text = "PLUS", FontSize = 12, FontWeight = FontWeights.ExtraBold, Foreground = Ui.Res("AccentBrush"), VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(10, 0, 0, 0) });
        top.Children.Add(brand);
        stack.Children.Add(top);
        stack.Children.Add(new TextBlock { Text = "Everything in Arc TV Plus.", FontSize = 28, FontWeight = FontWeights.ExtraBold, TextWrapping = TextWrapping.Wrap });
        stack.Children.Add(new TextBlock { Text = "Here is what your Plus membership gives you.", FontSize = 15, Foreground = Ui.Res("Text2Brush"), Margin = new Thickness(0, 6, 0, 18) });
        foreach (var (glyph, title, detail) in Benefits) stack.Children.Add(Benefit(glyph, title, detail));

        var go = new Button { Content = "See my Plus settings  →", Style = Ui.StyleOf("PrimaryButton"), Height = 44, Margin = new Thickness(0, 8, 12, 0), HorizontalAlignment = HorizontalAlignment.Stretch };
        go.Click += (_, _) => seeSettings();
        var closeButton = new Button { Content = "Close", Style = Ui.StyleOf("GhostButton"), Height = 44, Padding = new Thickness(26, 0, 26, 0), Margin = new Thickness(0, 8, 0, 0) };
        closeButton.Click += (_, _) => close();
        var row = new DockPanel();
        DockPanel.SetDock(closeButton, Dock.Right);
        row.Children.Add(closeButton);
        row.Children.Add(go);
        stack.Children.Add(row);
        stack.Children.Add(new TextBlock { Text = "Parental controls are on the way", FontSize = 12, Foreground = Ui.Res("Text3Brush"), HorizontalAlignment = HorizontalAlignment.Center, Margin = new Thickness(0, 12, 0, 0) });
        Children.Add(OfferCard.Frame(stack, close));
    }

    /// <summary>The same list as the Mac's, with Windows' words for where downloads go.</summary>
    public static readonly (string Glyph, string Title, string Detail)[] Benefits =
    [
        ("", "Picked for you", "A Home row chosen from the movies and shows you like, with the reason under each poster. Tune it in Settings > Recommendations."),
        ("", "Up to 5 profiles", "Their own My List, Continue Watching and recommendations. Add kids profiles and PIN locks."),
        ("", "Smart source picking", "Skips the source list and starts the best source your PC can play."),
        ("", "Your stats", "How much you watch, your streak and a map of your last 13 weeks."),
        ("", "Downloads", "Save movies and episodes to this PC from the Download button on a title's page."),
    ];

    private static UIElement Benefit(string glyph, string title, string detail)
    {
        var row = new Grid { Margin = new Thickness(0, 0, 0, 14) };
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(52) });
        row.ColumnDefinitions.Add(new ColumnDefinition());
        row.Children.Add(new Border
        {
            Width = 38, Height = 38, CornerRadius = new CornerRadius(19), HorizontalAlignment = HorizontalAlignment.Left, VerticalAlignment = VerticalAlignment.Top, Background = new SolidColorBrush(Color.FromArgb(0x1F, 0x19, 0xE6, 0xFF)),
            Child = new TextBlock { Text = glyph, FontFamily = new FontFamily("Segoe MDL2 Assets"), FontSize = 16, Foreground = Ui.Res("AccentBrush"), HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center },
        });
        var words = new StackPanel();
        words.Children.Add(new TextBlock { Text = title, FontSize = 15, FontWeight = FontWeights.Bold });
        words.Children.Add(new TextBlock { Text = detail, FontSize = 13, Foreground = Ui.Res("Text2Brush"), TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 2, 0, 0) });
        Grid.SetColumn(words, 1);
        row.Children.Add(words);
        return row;
    }
}
