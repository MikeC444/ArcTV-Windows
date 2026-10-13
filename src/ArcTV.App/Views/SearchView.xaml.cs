using System.ComponentModel;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;
using ArcTV.App.Controls;
using ArcTV.Core.Addons;
using ArcTV.Core.Search;

namespace ArcTV.App.Views;

/// <summary>
/// Search, laid out like the web app's: a round search box that searches as you type, suggestions under it, recent searches, then Movies and TV Shows as
/// their own groups of posters.
/// </summary>
public partial class SearchView : UserControl
{
    private const int MinLetters = 2;

    private readonly SearchModel _model;
    private readonly DispatcherTimer _pause = new() { Interval = TimeSpan.FromMilliseconds(250) };
    private bool _settingText;
    private bool _suggestionsOpen;
    private int _highlighted = -1;
    private List<MetaPreview> _suggestions = [];

    public SearchView(SearchModel model)
    {
        InitializeComponent();
        _model = model;
        _pause.Tick += (_, _) =>
        {
            _pause.Stop();
            var q = Box.Text.Trim();
            if (q.Length >= MinLetters) _ = SearchAsync(q);
        };

        Loaded += (_, _) =>
        {
            _model.PropertyChanged += OnModelChanged;
            Refresh();
            Box.Focus();
        };
        Unloaded += (_, _) =>
        {
            _model.PropertyChanged -= OnModelChanged;
            _pause.Stop();
        };
    }

    private void OnModelChanged(object? sender, PropertyChangedEventArgs e) => Refresh();

    private async Task SearchAsync(string query)
    {
        try
        {
            await _model.SearchAsync(query);
        }
        catch (OperationCanceledException)
        {
            // a newer search took over
        }
    }

    // ---- what is on the page ----

    private void Refresh()
    {
        var state = _model.State;
        IdlePanel.Visibility = state == SearchState.Idle ? Visibility.Visible : Visibility.Collapsed;
        SearchingPanel.Visibility = state == SearchState.Searching ? Visibility.Visible : Visibility.Collapsed;
        NonePanel.Visibility = state == SearchState.None ? Visibility.Visible : Visibility.Collapsed;
        ErrorText.Visibility = state == SearchState.Error ? Visibility.Visible : Visibility.Collapsed;
        CheckingPanel.Visibility = state == SearchState.Results && _model.StillChecking ? Visibility.Visible : Visibility.Collapsed;
        Posters.Visibility = state == SearchState.Results ? Visibility.Visible : Visibility.Hidden; // hidden, not collapsed, so it keeps its width and does not re-lay-out each time

        NoneText.Text = $"Nothing found for “{_model.Query}”. Try a different search.";
        ErrorText.Text = SearchModel.ErrorText;

        var sections = new List<PosterSection>();
        if (state == SearchState.Results)
        {
            if (_model.Movies.Count > 0) sections.Add(new PosterSection("Movies", Count(_model.Movies.Count), _model.Movies));
            if (_model.TvShows.Count > 0) sections.Add(new PosterSection("TV Shows", Count(_model.TvShows.Count), _model.TvShows));
        }

        Posters.Show(sections);
        BuildRecents();
        BuildSuggestions();
    }

    private static string Count(int n) => $"{n} {(n == 1 ? "result" : "results")}";

    private void BuildRecents()
    {
        RecentPanel.Visibility = _model.Recents.Count > 0 ? Visibility.Visible : Visibility.Collapsed;
        Recents.Children.Clear();
        foreach (var term in _model.Recents)
        {
            var open = new Button { Style = (Style)FindResource("SuggestionRow"), Content = new StackPanel { Orientation = Orientation.Horizontal, Children =
            {
                new TextBlock { Text = "", FontFamily = new FontFamily("Segoe MDL2 Assets"), Foreground = (Brush)FindResource("Text3Brush"), Margin = new Thickness(0, 0, 8, 0), VerticalAlignment = VerticalAlignment.Center },
                new TextBlock { Text = term, FontSize = 15, FontWeight = FontWeights.Medium, VerticalAlignment = VerticalAlignment.Center },
            } } };
            open.Click += async (_, _) => await RunNowAsync(term);

            var forget = new Button { Style = (Style)FindResource("SuggestionRow"), ToolTip = "Remove from recent searches", Content = new TextBlock { Text = "", FontFamily = new FontFamily("Segoe MDL2 Assets"), FontSize = 11, Foreground = (Brush)FindResource("Text3Brush") } };
            forget.Click += (_, _) => _model.Forget(term);

            Recents.Children.Add(new Border
            {
                CornerRadius = new CornerRadius(12), Margin = new Thickness(0, 0, 10, 10), Background = (Brush)FindResource("SurfaceBrush"),
                Child = new StackPanel { Orientation = Orientation.Horizontal, Children = { open, forget } },
            });
        }
    }

    // ---- the suggestions under the box ----

    private void BuildSuggestions()
    {
        _suggestions = _model.Movies.Concat(_model.TvShows).Take(5).ToList();
        var show = _suggestionsOpen && Box.Text.Trim().Length >= MinLetters && _suggestions.Count > 0 && Box.IsKeyboardFocused;
        if (!show)
        {
            SuggestionPopup.IsOpen = false;
            return;
        }

        if (_highlighted >= _suggestions.Count) _highlighted = -1;
        Suggestions.Children.Clear();
        for (var i = 0; i < _suggestions.Count; i++)
        {
            var item = _suggestions[i];
            var row = new Button { Style = (Style)FindResource("SuggestionRow"), Tag = i == _highlighted ? "selected" : "", Content = SuggestionContent(item), DataContext = item };
            row.Click += (_, _) => PickedSuggestion(); // the click goes on up to the page, which opens the title
            Suggestions.Children.Add(row);
        }

        SuggestionBox.Width = Math.Max(300, BoxBorder.ActualWidth + 24);
        SuggestionPopup.IsOpen = true;
    }

    private UIElement SuggestionContent(MetaPreview item)
    {
        var grid = new Grid();
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

        var thumb = new Border { Width = 34, Height = 50, CornerRadius = new CornerRadius(5), ClipToBounds = true, Background = (Brush)FindResource("SurfaceHighBrush"), Margin = new Thickness(0, 0, 14, 0), Child = new RemoteImage { Url = item.Poster, DecodeWidth = 80, Stretch = Stretch.UniformToFill } };
        var name = new TextBlock { Text = item.Name, FontSize = 16, FontWeight = FontWeights.Bold, TextTrimming = TextTrimming.CharacterEllipsis, VerticalAlignment = VerticalAlignment.Center };
        var kind = new TextBlock { Text = (item.Type == "movie" ? "Movie" : "TV Show") + (item.ReleaseInfo is { Length: > 0 } year ? $" · {year}" : ""), FontSize = 14, Foreground = (Brush)FindResource("Text3Brush"), VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(12, 0, 0, 0) };
        Grid.SetColumn(name, 1);
        Grid.SetColumn(kind, 2);
        grid.Children.Add(thumb);
        grid.Children.Add(name);
        grid.Children.Add(kind);
        return grid;
    }

    private void PickedSuggestion()
    {
        _model.Remember(Box.Text);
        _suggestionsOpen = false;
        SuggestionPopup.IsOpen = false;
    }

    // ---- typing ----

    private void OnTextChanged(object sender, TextChangedEventArgs e)
    {
        Placeholder.Visibility = Box.Text.Length == 0 ? Visibility.Visible : Visibility.Collapsed;
        ClearButton.Visibility = Box.Text.Length == 0 ? Visibility.Collapsed : Visibility.Visible;
        if (_settingText) return;

        _pause.Stop();
        _highlighted = -1;
        _suggestionsOpen = true;
        var q = Box.Text.Trim();
        if (q.Length == 0)
        {
            _model.Reset();
            return;
        }

        if (q.Length >= MinLetters) _pause.Start(); // search as you type, after a short pause and never for a single letter
    }

    private void OnBoxFocus(object sender, KeyboardFocusChangedEventArgs e)
    {
        BoxBorder.BorderBrush = (Brush)FindResource("AccentBrush");
        BoxBorder.BorderThickness = new Thickness(2);
        _suggestionsOpen = true;
        BuildSuggestions();
    }

    private void OnBoxBlur(object sender, KeyboardFocusChangedEventArgs e)
    {
        BoxBorder.BorderBrush = new SolidColorBrush(Color.FromArgb(0x26, 255, 255, 255));
        BoxBorder.BorderThickness = new Thickness(1);
    }

    /// <summary>Up and Down move through the suggestions, Return opens the one marked (or searches for what is typed), Escape closes them.</summary>
    private async void OnBoxKey(object sender, KeyEventArgs e)
    {
        var listing = SuggestionPopup.IsOpen && _suggestions.Count > 0;
        switch (e.Key)
        {
            case Key.Down when listing:
                _highlighted = Math.Min(_suggestions.Count - 1, _highlighted + 1);
                BuildSuggestions();
                e.Handled = true;
                break;
            case Key.Up when listing:
                _highlighted = _highlighted > 0 ? _highlighted - 1 : -1;
                BuildSuggestions();
                e.Handled = true;
                break;
            case Key.Escape when listing:
                _suggestionsOpen = false;
                _highlighted = -1;
                BuildSuggestions();
                e.Handled = true;
                break;
            case Key.Enter:
                e.Handled = true;
                if (listing && _highlighted >= 0 && _highlighted < Suggestions.Children.Count) ((Button)Suggestions.Children[_highlighted]).RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                else await RunNowAsync(Box.Text);
                break;
        }
    }

    /// <summary>Searches now (no pause), keeps the words in the box and remembers them.</summary>
    private async Task RunNowAsync(string text)
    {
        var q = text.Trim();
        if (q.Length == 0) return;
        _pause.Stop();
        _suggestionsOpen = false;
        _settingText = true;
        Box.Text = q;
        Box.CaretIndex = q.Length;
        _settingText = false;
        _model.Remember(q);
        await SearchAsync(q);
    }

    private void OnClear(object sender, RoutedEventArgs e)
    {
        Box.Clear(); // the box announces the change, which resets the page
        Box.Focus();
    }

    private void OnClearRecents(object sender, RoutedEventArgs e) => _model.ClearRecents();
}
