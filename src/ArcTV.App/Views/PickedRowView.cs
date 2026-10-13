using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using ArcTV.App.Controls;
using ArcTV.Core.Recommend;

namespace ArcTV.App.Views;

/// <summary>
/// The "Picked for you" row on Home (Arc TV Plus): posters chosen from what you like, finish and save, with the reason under each, and a small cross to take a title out for five days. A
/// click opens the title (the shell sees the click on a button that stands for a title).
/// </summary>
public sealed class PickedRowView : StackPanel
{
    private const double CardWidth = 288;
    private readonly PickedModel _model;

    public PickedRowView(PickedModel model)
    {
        _model = model;
        Refresh();
    }

    public void Refresh()
    {
        Children.Clear();
        var row = _model.Row;
        Visibility = row is null ? Visibility.Collapsed : Visibility.Visible;
        if (row is null) return;
        Margin = new Thickness(0, 0, 0, 44);
        Children.Add(new TextBlock { Text = row.Title, FontSize = 32, FontWeight = FontWeights.Bold, Margin = new Thickness(24, 0, 0, 0) });
        var strip = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(24, 0, 0, 0) };
        foreach (var entry in row.Entries) strip.Children.Add(Card(entry));
        Children.Add(new ArrowScrollViewer { HorizontalScrollBarVisibility = ScrollBarVisibility.Hidden, VerticalScrollBarVisibility = ScrollBarVisibility.Disabled, ArrowCenter = 226, ScalesWithPosters = true, Content = strip });
    }

    private static Brush Res(string key) => (Brush)Application.Current.FindResource(key);

    private UIElement Card(PickedEntry entry)
    {
        var picture = new Grid();
        picture.Children.Add(new TextBlock { Text = entry.Item.Name, TextWrapping = TextWrapping.Wrap, TextAlignment = TextAlignment.Center, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(12), Foreground = Res("Text3Brush"), FontSize = 13 });
        picture.Children.Add(new RemoteImage { Url = entry.Item.Poster, DecodeWidth = 600, Stretch = Stretch.UniformToFill });

        var card = new StackPanel { Width = CardWidth, Margin = new Thickness(0, 10, 16, 10), Background = Brushes.Transparent, Cursor = System.Windows.Input.Cursors.Hand };
        card.Children.Add(new RoundedBorder { Width = CardWidth, Height = 432, CornerRadius = new CornerRadius(16), Background = Res("SurfaceBrush"), BorderBrush = new SolidColorBrush(Color.FromArgb(0x24, 255, 255, 255)), BorderThickness = new Thickness(1), Child = picture });
        card.Children.Add(new TextBlock { Text = entry.Item.Name, Margin = new Thickness(2, 8, 2, 0), FontSize = 20, FontWeight = FontWeights.SemiBold, TextTrimming = TextTrimming.CharacterEllipsis });
        if (entry.Reason is { Length: > 0 } reason)
            card.Children.Add(new TextBlock { Text = reason, Margin = new Thickness(2, 2, 2, 0), FontSize = 16, Foreground = Res("Text2Brush"), TextWrapping = TextWrapping.Wrap, MaxHeight = 44, TextTrimming = TextTrimming.CharacterEllipsis });

        HoverLift.Attach(card);

        // the button stands for the title: DataContext is what the shell looks for to open it
        var open = new Button { Style = (Style)Application.Current.FindResource("PosterButton"), Content = card, DataContext = entry.Item };

        var remove = new Border
        {
            Width = 30, Height = 30, CornerRadius = new CornerRadius(15), Background = new SolidColorBrush(Color.FromArgb(0xC8, 0, 0, 0)), Cursor = System.Windows.Input.Cursors.Hand,
            HorizontalAlignment = HorizontalAlignment.Right, VerticalAlignment = VerticalAlignment.Top, Margin = new Thickness(0, 20, 26, 0), ToolTip = "Not now: take this out of Picked for you for 5 days",
            Child = new TextBlock { Text = "", FontFamily = new FontFamily("Segoe MDL2 Assets"), FontSize = 12, Foreground = Brushes.White, HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center },
        };
        var id = entry.Id;
        remove.MouseLeftButtonUp += (_, e) =>
        {
            e.Handled = true;
            _ = _model.RemoveAsync(id);
        };

        var cell = new Grid { Width = CardWidth + 16 };
        PosterMetrics.Apply(cell); // drawn bigger or smaller with the window, like the posters
        CardMenu.SetItem(cell, entry.Item); // right-click opens the card menu
        CardMenu.SetFromPicked(cell, true);
        cell.Children.Add(open);
        cell.Children.Add(remove);
        return cell;
    }
}
