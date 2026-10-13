using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using ArcTV.App.Controls;
using ArcTV.Core.Continue;
using ArcTV.Core.Sources;

namespace ArcTV.App.Views;

/// <summary>The "Continue Watching" row at the top of Home: each card is a poster with how far in you got, the episode if it is a show, and a small cross to take it off. A click plays it from where you left it.</summary>
public sealed class ContinueRow : StackPanel
{
    // a wide picture (16:9), the title's landscape artwork
    private const double CardWidth = 440;
    private const double PictureHeight = 248;
    private readonly ContinueModel _model;
    private readonly Action<PlayTarget> _play;

    public ContinueRow(ContinueModel model, Action<PlayTarget> play)
    {
        _model = model;
        _play = play;
        Refresh();
    }

    public void Refresh()
    {
        Children.Clear();
        var items = _model.Items;
        Visibility = items.Count == 0 ? Visibility.Collapsed : Visibility.Visible;
        if (items.Count == 0) return;
        Margin = new Thickness(0, 0, 0, 44);
        Children.Add(new TextBlock { Text = "Continue Watching", FontSize = 26, FontWeight = FontWeights.Bold, Margin = new Thickness(24, 0, 0, 0) });
        var strip = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(24, 0, 0, 0) };
        foreach (var item in items) strip.Children.Add(Card(item));
        Children.Add(new ArrowScrollViewer { HorizontalScrollBarVisibility = ScrollBarVisibility.Hidden, VerticalScrollBarVisibility = ScrollBarVisibility.Disabled, ArrowCenter = 134, Content = strip });
    }

    private static Brush Res(string key) => (Brush)Application.Current.FindResource(key);

    private UIElement Card(ContinueItem item)
    {
        var picture = new Grid();
        picture.Children.Add(new TextBlock { Text = item.Title, TextWrapping = TextWrapping.Wrap, TextAlignment = TextAlignment.Center, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(12), Foreground = Res("Text3Brush"), FontSize = 13 });
        picture.Children.Add(new RemoteImage { Url = item.BackdropUrl ?? item.PosterUrl, FallbackUrl = item.PosterUrl, DecodeWidth = 700, Stretch = Stretch.UniformToFill });
        // the title's logo, top left, on a soft dark corner so it reads on any picture
        if (_model.Logos.TryGetValue(item.ContentId, out var logo))
        {
            picture.Children.Add(new System.Windows.Shapes.Rectangle
            {
                Height = 90, VerticalAlignment = VerticalAlignment.Top, IsHitTestVisible = false,
                Fill = new LinearGradientBrush(Color.FromArgb(0xB0, 0, 0, 0), Color.FromArgb(0, 0, 0, 0), 90),
            });
            picture.Children.Add(new RemoteImage
            {
                Url = logo, DecodeWidth = 360, Stretch = Stretch.Uniform, StretchDirection = StretchDirection.DownOnly, HorizontalAlignment = HorizontalAlignment.Left, VerticalAlignment = VerticalAlignment.Top,
                MaxWidth = 180, MaxHeight = 64, Margin = new Thickness(18, 18, 0, 0), IsHitTestVisible = false,
            });
        }

        if (item.EpisodeLabel is { } label)
            picture.Children.Add(new Border
            {
                Background = new SolidColorBrush(Color.FromArgb(0xC8, 0, 0, 0)), CornerRadius = new CornerRadius(8), Padding = new Thickness(8, 3, 8, 3), Margin = new Thickness(8, 0, 0, 14),
                HorizontalAlignment = HorizontalAlignment.Left, VerticalAlignment = VerticalAlignment.Bottom,
                Child = new TextBlock { Text = label, FontSize = 12, FontWeight = FontWeights.Bold, Foreground = Brushes.White },
            });
        // how far in: a thin line along the bottom, filled in the brand colours
        var bar = new Grid { Height = 5, VerticalAlignment = VerticalAlignment.Bottom, Background = new SolidColorBrush(Color.FromArgb(0x99, 0, 0, 0)) };
        bar.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(Math.Max(item.Progress, 0.0001), GridUnitType.Star) });
        bar.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(Math.Max(1 - item.Progress, 0.0001), GridUnitType.Star) });
        bar.Children.Add(new Border { Background = Res("PillGradient") });
        picture.Children.Add(bar);

        var card = new StackPanel { Width = CardWidth, Margin = new Thickness(0, 10, 16, 10), Background = Brushes.Transparent, Cursor = System.Windows.Input.Cursors.Hand };
        card.Children.Add(new RoundedBorder { Width = CardWidth, Height = PictureHeight, CornerRadius = new CornerRadius(16), Background = Res("SurfaceBrush"), BorderBrush = new SolidColorBrush(Color.FromArgb(0x24, 255, 255, 255)), BorderThickness = new Thickness(1), Child = picture });
        card.Children.Add(new TextBlock { Text = item.Title, Margin = new Thickness(2, 8, 2, 0), FontSize = 17, FontWeight = FontWeights.SemiBold, TextTrimming = TextTrimming.CharacterEllipsis });

        HoverLift.Attach(card);

        var play = new Button { Style = (Style)Application.Current.FindResource("PosterButton"), Content = card, ToolTip = item.IsMovie ? "Continue watching" : $"Continue {item.EpisodeLabel}" };
        play.Click += (_, _) => _play(item.ToTarget() with { ChooseSource = true }); // from here the source list always shows (starting a source by itself did not work from here)

        var remove = new Border
        {
            Width = 30, Height = 30, CornerRadius = new CornerRadius(15), Background = new SolidColorBrush(Color.FromArgb(0xC8, 0, 0, 0)), Cursor = System.Windows.Input.Cursors.Hand,
            HorizontalAlignment = HorizontalAlignment.Right, VerticalAlignment = VerticalAlignment.Top, Margin = new Thickness(0, 20, 26, 0), ToolTip = "Take this off Continue Watching",
            Child = new TextBlock { Text = "", FontFamily = new FontFamily("Segoe MDL2 Assets"), FontSize = 12, Foreground = Brushes.White, HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center },
        };
        remove.MouseLeftButtonUp += (_, e) =>
        {
            e.Handled = true;
            _ = _model.RemoveAsync(item);
        };

        var cell = new Grid { Width = CardWidth + 16 };
        cell.Children.Add(play);
        cell.Children.Add(remove);
        return cell;
    }
}
