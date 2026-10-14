using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Animation;

namespace ArcTV.App.Controls;

/// <summary>
/// A grey stand-in shape shown where something is still loading (a poster, a line of words, a button). All of them share one brush that slowly brightens and dims, so they pulse
/// together; the pulse only runs while at least one is on screen.
/// </summary>
public class Skeleton : RoundedBorder
{
    private static readonly SolidColorBrush Pulse = new(Color.FromRgb(0x1C, 0x1C, 0x20));
    private static readonly ColorAnimation Breathe = new(Color.FromRgb(0x1C, 0x1C, 0x20), Color.FromRgb(0x2A, 0x2A, 0x31), TimeSpan.FromMilliseconds(900))
    {
        AutoReverse = true,
        RepeatBehavior = RepeatBehavior.Forever,
        EasingFunction = new SineEase { EasingMode = EasingMode.EaseInOut },
    };

    private static int _onScreen;

    public Skeleton()
    {
        Background = Pulse;
        CornerRadius = new CornerRadius(8);
        IsHitTestVisible = false;
        Loaded += (_, _) =>
        {
            if (_onScreen++ == 0) Pulse.BeginAnimation(SolidColorBrush.ColorProperty, Breathe);
        };
        Unloaded += (_, _) =>
        {
            if (--_onScreen == 0) Pulse.BeginAnimation(SolidColorBrush.ColorProperty, null);
        };
    }

    /// <summary>A shape of a set size and corner radius.</summary>
    public static Skeleton Box(double width, double height, double radius = 8, Thickness? margin = null, HorizontalAlignment align = HorizontalAlignment.Left)
    {
        var box = new Skeleton { Height = height, CornerRadius = new CornerRadius(radius), HorizontalAlignment = align };
        if (!double.IsNaN(width)) box.Width = width;
        if (margin is { } m) box.Margin = m;
        return box;
    }

    /// <summary>A row of poster-shaped shapes under a row title's shape, the size of a real poster row (they follow the window's poster scale like the real ones).</summary>
    public static StackPanel PosterRow(int count = 10)
    {
        var row = new StackPanel { Margin = new Thickness(0, 0, 0, 44), IsHitTestVisible = false };
        row.Children.Add(Box(260, 30, 8, new Thickness(24, 4, 0, 6)));
        var posters = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(24, 0, 0, 0) };
        for (var i = 0; i < count; i++)
        {
            var card = new StackPanel { Width = 288, Margin = new Thickness(0, 10, 16, 10) };
            card.Children.Add(Box(288, 432, 16));
            card.Children.Add(Box(190, 20, 6, new Thickness(2, 10, 0, 0)));
            PosterMetrics.Apply(card);
            posters.Children.Add(card);
        }

        // the real rows slide on out under the window's edge; this one is simply cut off by it
        var clip = new ScrollViewer { HorizontalScrollBarVisibility = ScrollBarVisibility.Hidden, VerticalScrollBarVisibility = ScrollBarVisibility.Disabled, Content = posters, IsHitTestVisible = false };
        row.Children.Add(clip);
        return row;
    }

    /// <summary>The whole of Home before its rows arrive: a big banner shape with a title, a line and buttons on it, then a few rows of posters.</summary>
    public static Grid HomePage()
    {
        var page = new Grid { ClipToBounds = true, IsHitTestVisible = false };
        var body = new StackPanel();
        var hero = new Grid { Height = 620 };
        var text = new StackPanel { VerticalAlignment = VerticalAlignment.Bottom, HorizontalAlignment = HorizontalAlignment.Left, Margin = new Thickness(48, 0, 0, 90) };
        text.Children.Add(Box(520, 56, 10));
        text.Children.Add(Box(360, 20, 6, new Thickness(0, 22, 0, 0)));
        text.Children.Add(Box(700, 18, 6, new Thickness(0, 18, 0, 0)));
        text.Children.Add(Box(620, 18, 6, new Thickness(0, 10, 0, 0)));
        var buttons = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 26, 0, 0) };
        buttons.Children.Add(Box(140, 44, 22, new Thickness(0, 0, 12, 0)));
        buttons.Children.Add(Box(120, 44, 22, new Thickness(0, 0, 12, 0)));
        buttons.Children.Add(Box(150, 44, 22));
        text.Children.Add(buttons);
        hero.Children.Add(text);
        body.Children.Add(hero);
        body.Children.Add(PosterRow());
        body.Children.Add(PosterRow());
        body.Children.Add(PosterRow());
        page.Children.Add(body);
        return page;
    }

    /// <summary>A grid of poster shapes for Movies and TV Shows before their first page arrives.</summary>
    public static WrapPanel PosterGridPage(int count = 24)
    {
        var grid = new WrapPanel { Margin = new Thickness(24, 0, 24, 0), IsHitTestVisible = false };
        for (var i = 0; i < count; i++)
        {
            var card = new StackPanel { Width = 288, Margin = new Thickness(0, 10, 16, 10) };
            card.Children.Add(Box(288, 432, 16));
            card.Children.Add(Box(190, 20, 6, new Thickness(2, 10, 0, 0)));
            PosterMetrics.Apply(card);
            grid.Children.Add(card);
        }

        return grid;
    }

    /// <summary>What sits under a title's buttons while its details load: a heading and a row of wide cards.</summary>
    public static StackPanel DetailRows()
    {
        var rows = new StackPanel { IsHitTestVisible = false };
        rows.Children.Add(Box(220, 30, 8, new Thickness(24, 0, 0, 14)));
        var cards = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(24, 0, 0, 0) };
        for (var i = 0; i < 8; i++) cards.Children.Add(Box(300, 169, 16, new Thickness(0, 0, 16, 0)));
        rows.Children.Add(new ScrollViewer { HorizontalScrollBarVisibility = ScrollBarVisibility.Hidden, VerticalScrollBarVisibility = ScrollBarVisibility.Disabled, Content = cards, IsHitTestVisible = false });
        return rows;
    }
}
