using System.Collections.ObjectModel;
using System.Windows;
using System.Windows.Controls;
using ArcTV.App.Controls;
using ArcTV.Core.Addons;

namespace ArcTV.App.Views;

/// <summary>One line of the poster grid.</summary>
internal sealed record PosterRow(IReadOnlyList<MetaPreview> Items);

/// <summary>A title over the lines that follow it.</summary>
internal sealed record SectionHeader(string Title, string? Detail);

/// <summary>A group of titles, with a heading when it has a <paramref name="Title"/> ("Movies", "12 results").</summary>
public sealed record PosterSection(string? Title, string? Detail, IReadOnlyList<MetaPreview> Items);

/// <summary>
/// Posters in lines that fill the width (as many as fit, the spare width shared out between them), in one scrolling list that only builds the lines on screen.
/// The wheel glides, resizing keeps your place, and a new page of titles only adds lines at the end. Used by Movies, TV Shows and Search.
/// </summary>
public partial class PosterGrid : UserControl
{
    private const double DesignTileWidth = 304; // a poster (288) and the space after it (16), before it is drawn bigger or smaller with the window
    private const double SideSpace = 48;
    private const double ScrollBarWidth = 0; // the theme hides scroll bars
    private const double LoadMoreWithin = 900;

    public static readonly DependencyProperty ExtraProperty = DependencyProperty.Register(nameof(Extra), typeof(double), typeof(PosterGrid), new PropertyMetadata(0.0));

    /// <summary>Space added between posters so a line fills the width: the window is rarely an exact number of posters wide.</summary>
    public double Extra { get => (double)GetValue(ExtraProperty); private set => SetValue(ExtraProperty, value); }

    /// <summary>Raised when the end of the list is near on screen, so the page can fetch more.</summary>
    public event EventHandler? NearEnd;

    private readonly ObservableCollection<object> _entries = [];
    private IReadOnlyList<PosterSection> _sections = [];
    private int _perRow;
    private ScrollViewer? _scroll;
    private SmoothScroller? _smooth;

    public PosterGrid()
    {
        InitializeComponent();
        Rows.ItemsSource = _entries;
        Rows.PreviewMouseWheel += (_, e) =>
        {
            if (Scroll() is { } scroll) (_smooth ??= new SmoothScroller(scroll)).Wheel(e);
        };
        SizeChanged += (_, _) =>
        {
            UpdateExtra();
            if (PerRow() != _perRow) Rebuild();
        };
        Loaded += (_, _) =>
        {
            PosterMetrics.Instance.PropertyChanged -= OnPosterScale;
            PosterMetrics.Instance.PropertyChanged += OnPosterScale;
            OnPosterScale(null, new System.ComponentModel.PropertyChangedEventArgs(null));
        };
        Unloaded += (_, _) =>
        {
            _smooth?.Stop();
            PosterMetrics.Instance.PropertyChanged -= OnPosterScale;
        };
    }

    /// <summary>The posters were drawn bigger or smaller (the window changed size): more or fewer now fit across a line.</summary>
    private void OnPosterScale(object? sender, System.ComponentModel.PropertyChangedEventArgs e)
    {
        if (!CheckAccess())
        {
            PosterMetrics.Instance.PropertyChanged -= OnPosterScale; // a screen from a window that is gone (its thread is not this one): it is not listening any more
            return;
        }

        UpdateExtra();
        if (PerRow() != _perRow) Rebuild();
    }

    /// <summary>Shows these groups of titles. Lines that are already right are left alone, so adding a page of titles does not rebuild what is on screen.</summary>
    public void Show(IReadOnlyList<PosterSection> sections)
    {
        _sections = sections;
        Rebuild();
    }

    public void ScrollToTop()
    {
        _smooth?.Stop();
        Scroll()?.ScrollToTop();
    }

    private ScrollViewer? Scroll()
    {
        if (_scroll is null)
        {
            Rows.ApplyTemplate();
            _scroll = Rows.Template.FindName("PageScroll", Rows) as ScrollViewer;
        }

        return _scroll;
    }

    // ---- laying out ----

    /// <summary>The width a line of posters has to fill (the control less its side space and the scroll bar).</summary>
    private double Available() => ActualWidth - ScrollBarWidth - SideSpace;

    private static double TileWidth => DesignTileWidth * PosterMetrics.Instance.Scale;

    private int PerRow() => Math.Max(1, (int)(Available() / TileWidth));

    private void UpdateExtra()
    {
        var perRow = PerRow();
        var extra = perRow > 1 ? Math.Max(0, (Available() - perRow * TileWidth) / (perRow - 1)) : 0;
        if (Math.Abs(extra - Extra) >= 0.5) Extra = extra;
    }

    private static List<object> Build(IReadOnlyList<PosterSection> sections, int perRow)
    {
        var entries = new List<object>();
        foreach (var section in sections)
        {
            if (section.Title is not null) entries.Add(new SectionHeader(section.Title, section.Detail));
            for (var start = 0; start < section.Items.Count; start += perRow)
            {
                var line = new List<MetaPreview>(perRow);
                for (var i = start; i < Math.Min(start + perRow, section.Items.Count); i++) line.Add(section.Items[i]);
                entries.Add(new PosterRow(line));
            }
        }

        return entries;
    }

    private static bool Same(object a, object b) => a switch
    {
        PosterRow first when b is PosterRow second => first.Items.Count == second.Items.Count && first.Items.Zip(second.Items).All(pair => ReferenceEquals(pair.First, pair.Second)),
        SectionHeader first when b is SectionHeader second => first == second,
        _ => false,
    };

    private void Rebuild()
    {
        var perRow = PerRow();
        var next = Build(_sections, perRow);

        // when only the number across changes (the window was resized), stay at the same place in the list instead of jumping back to the start
        double? place = null;
        if (perRow != _perRow && _entries.Count > 0 && Scroll() is { ExtentHeight: > 0 } before) place = before.VerticalOffset / before.ExtentHeight;

        var same = 0;
        while (same < _entries.Count && same < next.Count && Same(_entries[same], next[same])) same++;
        while (_entries.Count > same) _entries.RemoveAt(_entries.Count - 1);
        for (var i = same; i < next.Count; i++) _entries.Add(next[i]);
        _perRow = perRow;

        if (place is { } fraction && Scroll() is { } scroll && _entries.Count > 0)
        {
            Rows.UpdateLayout();
            scroll.ScrollToVerticalOffset(fraction * scroll.ExtentHeight);
        }
    }

    private void OnScrollChanged(object sender, ScrollChangedEventArgs e)
    {
        if (e.VerticalChange == 0 && e.ExtentHeightChange == 0 && e.ViewportHeightChange == 0) return;
        if (e.OriginalSource is ScrollViewer scroll && scroll.ScrollableHeight - scroll.VerticalOffset < LoadMoreWithin) NearEnd?.Invoke(this, EventArgs.Empty);
    }
}
