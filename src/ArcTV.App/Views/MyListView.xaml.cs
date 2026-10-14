using System.ComponentModel;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using ArcTV.Core.MyList;

namespace ArcTV.App.Views;

/// <summary>My List: the titles saved on the account, All or just the Watched ones, in the order you choose. Watched titles carry a green tick.</summary>
public partial class MyListView : UserControl
{
    private readonly MyListModel _model;
    private bool _watchedOnly;
    private ListSort _sort = ListSort.Recent;
    private long _popupClosedAt;
    private CancellationTokenSource? _run;

    public MyListView(MyListModel model)
    {
        InitializeComponent();
        Root.Children.Remove(HeaderPanel);
        Posters.Header = HeaderPanel; // the heading scrolls with the posters
        _model = model;
        Loaded += async (_, _) =>
        {
            _model.PropertyChanged -= OnModelChanged;
            _model.PropertyChanged += OnModelChanged;
            Refresh();
            await LoadAsync();
        };
        Unloaded += (_, _) =>
        {
            _model.PropertyChanged -= OnModelChanged;
            _run?.Cancel();
        };
    }

    private async Task LoadAsync()
    {
        _run?.Cancel();
        var run = _run = new CancellationTokenSource();
        await _model.LoadAsync(run.Token); // the list is refreshed each time the page opens; what was there stays up meanwhile
    }

    private void OnModelChanged(object? sender, PropertyChangedEventArgs e) => Refresh();

    private void Refresh()
    {
        var all = _model.Items;
        var failed = all is null && _model.Error is not null;
        LoadingPanel.Visibility = all is null && !failed ? Visibility.Visible : Visibility.Collapsed;
        ErrorPanel.Visibility = failed ? Visibility.Visible : Visibility.Collapsed;
        ErrorText.Text = _model.Error ?? "";
        ListControls.Visibility = all is not null ? Visibility.Visible : Visibility.Collapsed;

        AllPill.Tag = _watchedOnly ? "" : "selected";
        WatchedPill.Tag = _watchedOnly ? "selected" : "";
        SortButton.Content = $"Sort by: {_sort.Label()}  ⇅";

        var shown = all is null ? new List<WatchlistItem>() : _sort.Apply(_watchedOnly ? all.Where(i => i.Watched == true) : all);
        var sections = new List<PosterSection>();
        if (shown.Count > 0) sections.Add(new PosterSection(null, null, shown.Select(i => i.AsPreview()).ToList()));
        Posters.Show(sections);

        string? message = null;
        if (all is not null && shown.Count == 0)
            message = _watchedOnly
                ? "Nothing watched yet. Titles you finish will show up here."
                : "Your list is empty. Add titles from a title's page or Home's featured title to see them here.";
        Message.Text = message ?? "";
        Message.Visibility = message is null ? Visibility.Collapsed : Visibility.Visible;
    }

    private void OnAll(object sender, RoutedEventArgs e)
    {
        _watchedOnly = false;
        Refresh();
    }

    private void OnWatched(object sender, RoutedEventArgs e)
    {
        _watchedOnly = true;
        Refresh();
    }

    private async void OnRetry(object sender, RoutedEventArgs e) => await LoadAsync();

    // ---- the sort list ----

    private void OnSortButton(object sender, RoutedEventArgs e)
    {
        // clicking the button while the list is open closes it (the click outside); don't let that same click open it again
        if (Environment.TickCount64 - _popupClosedAt < 250) return;

        var rows = new StackPanel();
        foreach (var option in ListSorts.All)
        {
            var row = new Grid();
            row.Children.Add(new TextBlock { Text = option.Label(), FontSize = 15, FontWeight = option == _sort ? FontWeights.Bold : FontWeights.Normal, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 28, 0) });
            if (option == _sort) row.Children.Add(new TextBlock { Text = "✓", FontWeight = FontWeights.Bold, Foreground = (Brush)FindResource("AccentBrush"), HorizontalAlignment = HorizontalAlignment.Right, VerticalAlignment = VerticalAlignment.Center });
            var button = new Button { Style = (Style)FindResource("SuggestionRow"), Content = row, MinWidth = 200 };
            button.Click += (_, _) =>
            {
                SortPopup.IsOpen = false;
                _sort = option;
                Refresh();
            };
            rows.Children.Add(button);
        }

        SortPopup.Child = new Border
        {
            Padding = new Thickness(8), CornerRadius = new CornerRadius(14), Child = rows, Margin = new Thickness(0, 0, 24, 24),
            Background = (Brush)FindResource("BgElevatedBrush"), BorderBrush = new SolidColorBrush(Color.FromArgb(40, 255, 255, 255)), BorderThickness = new Thickness(1),
            Effect = new System.Windows.Media.Effects.DropShadowEffect { BlurRadius = 24, ShadowDepth = 8, Opacity = 0.6 },
        };
        SortPopup.IsOpen = true;
    }

    private void OnSortClosed(object? sender, EventArgs e) => _popupClosedAt = Environment.TickCount64;
}
