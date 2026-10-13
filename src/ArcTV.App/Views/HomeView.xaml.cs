using System.ComponentModel;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using ArcTV.App.Controls;
using ArcTV.Core.Details;
using ArcTV.Core.Home;
using ArcTV.Core.MyList;

namespace ArcTV.App.Views;

public partial class HomeView : UserControl
{
    private readonly HomeModel _model;
    private readonly MyListModel? _list;
    private SmoothScroller? _smooth;
    private readonly IDetailExtras? _extras;
    private HeroBanner? _hero;

    public HomeView(HomeModel model, MyListModel? list = null, IDetailExtras? extras = null)
    {
        InitializeComponent();
        _model = model;
        _list = list;
        _extras = extras;
        Loaded += (_, _) =>
        {
            _model.PropertyChanged += OnModelChanged;
            Refresh();
            _hero?.Start(); // coming back from a title page, the banner goes on rotating
            if (_list is { Items: null }) _ = _list.LoadAsync(); // so the banner's My List button knows what is on the list
        };
        PageScroll.PreviewMouseWheel += (_, e) =>
        {
            if (!Keyboard.Modifiers.HasFlag(ModifierKeys.Shift)) (_smooth ??= new SmoothScroller(PageScroll, 1.1)).Wheel(e); // Shift-wheel is left for a row to move sideways
        };
        Unloaded += (_, _) =>
        {
            _smooth?.Stop();
            _model.PropertyChanged -= OnModelChanged;
            _hero?.Stop();
        };
        SizeChanged += (_, e) => { if (_hero is not null) _hero.Height = HeroHeight(e.NewSize.Height); };
    }

    private static double HeroHeight(double windowHeight) => Math.Max(windowHeight * 0.62, 500);

    private void OnModelChanged(object? sender, PropertyChangedEventArgs e) => Refresh();

    private void Refresh()
    {
        var showRows = _model.Sections.Count > 0;
        PageScroll.Visibility = showRows ? Visibility.Visible : Visibility.Collapsed;
        LoadingPanel.Visibility = !showRows && _model.State is HomeState.Idle or HomeState.Loading ? Visibility.Visible : Visibility.Collapsed;
        EmptyPanel.Visibility = !showRows && _model.State == HomeState.Empty ? Visibility.Visible : Visibility.Collapsed;
        if (!showRows) return;

        // update in place rather than rebuilding, so rows already on screen don't flicker as more arrive
        if (!ReferenceEquals(Rows.ItemsSource, _model.Sections)) Rows.ItemsSource = _model.Sections;

        var hero = _model.Hero;
        if (hero.Count == 0)
        {
            HeroHost.Children.Clear();
            _hero?.Stop();
            _hero = null;
            Rows.Margin = new Thickness(0, 90, 0, 40);
        }
        else if (_hero is null || !_hero.Shows(hero))
        {
            _hero?.Stop();
            _hero = new HeroBanner(hero, _list, _extras) { Height = HeroHeight(ActualHeight) };
            HeroHost.Children.Clear();
            HeroHost.Children.Add(_hero);
            Rows.Margin = new Thickness(0, -60, 0, 40);
        }
    }

    private void OnRetry(object sender, RoutedEventArgs e) => _ = _model.LoadAsync();
}
