using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Media;
using ArcTV.Core.Addons;
using ArcTV.Core.Browse;

namespace ArcTV.App.Views;

/// <summary>
/// Movies or TV Shows, laid out like the web app's: the title with a genre picker beside it, Featured / Highest Rated / Newest, then a poster grid that
/// keeps loading as you scroll.
/// </summary>
public partial class BrowseView : UserControl
{
    private const double LoadMoreWithin = 900;

    private readonly string _title;
    private readonly BrowseModel _model;
    private readonly ObservableCollection<MetaPreview> _shown = [];
    private readonly Dictionary<BrowseSort, Button> _sortButtons = [];
    private BrowseSort _sort = BrowseSort.Featured;
    private string? _genre;
    private long _popupClosedAt;
    private CancellationTokenSource? _run;

    public BrowseView(string title, BrowseModel model)
    {
        InitializeComponent();
        _title = title;
        _model = model;
        TitleText.Text = title;
        Posters.ItemsSource = _shown;

        foreach (var sort in BrowseSorts.All)
        {
            var pill = new Button { Content = sort.Label(), Style = (Style)FindResource("ChoicePill"), Margin = new Thickness(0, 0, 10, 0), Tag = sort == _sort ? "selected" : "" };
            pill.Click += (_, _) => ChooseSort(sort);
            _sortButtons[sort] = pill;
            SortPills.Children.Add(pill);
        }

        BuildGenrePicker();
        Loaded += OnLoaded;
        Unloaded += (_, _) =>
        {
            _model.PropertyChanged -= OnModelChanged;
            _run?.Cancel();
        };
    }

    private async void OnLoaded(object sender, RoutedEventArgs e)
    {
        _model.PropertyChanged -= OnModelChanged;
        _model.PropertyChanged += OnModelChanged;
        Refresh();
        await ReloadAsync();
    }

    private async Task ReloadAsync()
    {
        _run?.Cancel();
        var run = _run = new CancellationTokenSource();
        try
        {
            await _model.ReloadAsync(_genre, run.Token);
        }
        catch (OperationCanceledException)
        {
            // left the page, or chose another genre
        }
    }

    private void OnModelChanged(object? sender, PropertyChangedEventArgs e) => Refresh();

    private void Refresh()
    {
        Sync();
        var items = _model.Items.Count;
        var firstPage = _model.Loading && items == 0;
        LoadingPanel.Visibility = firstPage ? Visibility.Visible : Visibility.Collapsed;
        SortPills.Visibility = items > 0 ? Visibility.Visible : Visibility.Collapsed;

        string? message = null;
        if (!firstPage && items == 0)
            message = _model.NoAddons
                ? "No addons installed. Add an addon in Settings to bring movies and TV shows into Arc TV."
                : _genre is null ? "Nothing to show here right now." : $"No {_genre} {_title.ToLowerInvariant()} found right now.";
        Message.Text = message ?? "";
        Message.Visibility = message is null ? Visibility.Collapsed : Visibility.Visible;
    }

    /// <summary>Brings the grid in line with the model: new pages are added to the end without rebuilding what is already on screen.</summary>
    private void Sync()
    {
        var target = _sort.Apply(_model.Items);
        var appended = target.Count >= _shown.Count;
        for (var i = 0; appended && i < _shown.Count; i++) appended = ReferenceEquals(_shown[i], target[i]);
        if (!appended)
        {
            _shown.Clear();
            foreach (var item in target) _shown.Add(item);
            return;
        }

        for (var i = _shown.Count; i < target.Count; i++) _shown.Add(target[i]);
    }

    private void ChooseSort(BrowseSort sort)
    {
        _sort = sort;
        foreach (var (name, button) in _sortButtons) button.Tag = name == sort ? "selected" : "";
        Sync();
    }

    private void OnScrollChanged(object sender, ScrollChangedEventArgs e)
    {
        if (e.VerticalChange == 0 && e.ExtentHeightChange == 0 && e.ViewportHeightChange == 0) return;
        if (PageScroll.ScrollableHeight - PageScroll.VerticalOffset < LoadMoreWithin) _ = LoadMoreAsync();
    }

    private async Task LoadMoreAsync()
    {
        try
        {
            await _model.LoadMoreAsync();
        }
        catch (OperationCanceledException)
        {
            // nothing to do
        }
    }

    // ---- the genre picker: a pill beside the title that opens a two-column list with a tick on the chosen genre ----

    private IReadOnlyList<string> Options => _model.Type == "movie" ? Genres.Movies : Genres.Shows;

    private void BuildGenrePicker()
    {
        GenreButton.Content = (_genre ?? "All genres") + "  ▾";

        var panel = new StackPanel();
        panel.Children.Add(GenreRow("All genres", _genre is null, bold: true, () => ChooseGenre(null)));
        panel.Children.Add(new Border { Height = 1, Background = new SolidColorBrush(Color.FromArgb(40, 255, 255, 255)), Margin = new Thickness(0, 8, 0, 8) });
        var grid = new UniformGrid { Columns = 2 };
        foreach (var genre in Options) grid.Children.Add(GenreRow(genre, string.Equals(_genre, genre, StringComparison.Ordinal), bold: false, () => ChooseGenre(genre)));
        panel.Children.Add(grid);

        GenrePopup.Child = new Border
        {
            Width = 410, Padding = new Thickness(16), CornerRadius = new CornerRadius(12), Child = panel,
            Background = (Brush)FindResource("SurfaceBrush"),
            BorderBrush = new SolidColorBrush(Color.FromArgb(40, 255, 255, 255)), BorderThickness = new Thickness(1),
        };
    }

    private Button GenreRow(string text, bool selected, bool bold, Action choose)
    {
        var row = new Grid();
        row.Children.Add(new TextBlock { Text = text, FontSize = bold ? 16 : 14, FontWeight = bold || selected ? FontWeights.Bold : FontWeights.Normal, VerticalAlignment = VerticalAlignment.Center });
        if (selected)
            row.Children.Add(new TextBlock { Text = "✓", FontWeight = FontWeights.Bold, Foreground = (Brush)FindResource("AccentBrush"), HorizontalAlignment = HorizontalAlignment.Right, VerticalAlignment = VerticalAlignment.Center });

        var button = new Button
        {
            Content = row, Height = 34, Margin = new Thickness(0, 1, 8, 1), Cursor = System.Windows.Input.Cursors.Hand, HorizontalContentAlignment = HorizontalAlignment.Stretch,
            Template = RowTemplate(),
        };
        button.Click += (_, _) => choose();
        return button;
    }

    private static ControlTemplate RowTemplate()
    {
        var box = new FrameworkElementFactory(typeof(Border), "Box");
        box.SetValue(Border.CornerRadiusProperty, new CornerRadius(8));
        box.SetValue(Border.BackgroundProperty, Brushes.Transparent);
        box.SetValue(Border.PaddingProperty, new Thickness(10, 0, 10, 0));
        var content = new FrameworkElementFactory(typeof(ContentPresenter));
        content.SetValue(VerticalAlignmentProperty, VerticalAlignment.Center);
        box.AppendChild(content);
        var template = new ControlTemplate(typeof(Button)) { VisualTree = box };
        var hover = new Trigger { Property = IsMouseOverProperty, Value = true };
        hover.Setters.Add(new Setter(Border.BackgroundProperty, new SolidColorBrush(Color.FromArgb(30, 255, 255, 255)), "Box"));
        template.Triggers.Add(hover);
        return template;
    }

    private void OnGenreButton(object sender, RoutedEventArgs e)
    {
        // clicking the pill while the list is open closes it (the click outside); don't let that same click open it again
        if (Environment.TickCount64 - _popupClosedAt < 250) return;
        GenrePopup.IsOpen = true;
    }

    private void OnGenreClosed(object? sender, EventArgs e) => _popupClosedAt = Environment.TickCount64;

    private async void ChooseGenre(string? genre)
    {
        GenrePopup.IsOpen = false;
        if (string.Equals(genre, _genre, StringComparison.Ordinal)) return;
        _genre = genre;
        BuildGenrePicker();
        PageScroll.ScrollToTop();
        await ReloadAsync();
    }
}
