using System.ComponentModel;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using ArcTV.App.Controls;
using ArcTV.Core.Continue;
using ArcTV.Core.Details;
using ArcTV.Core.Home;
using ArcTV.Core.MyList;
using ArcTV.Core.Recommend;
using ArcTV.Core.Sources;

namespace ArcTV.App.Views;

public partial class HomeView : UserControl
{
    private readonly HomeModel _model;
    private readonly MyListModel? _list;
    private SmoothScroller? _smooth;
    private readonly IDetailExtras? _extras;
    private readonly Action<PlayTarget>? _play;
    private HeroBanner? _hero;
    private readonly ContinueModel? _continue;
    private ContinueRow? _continueRow;
    private readonly PickedModel? _picked;
    private PickedRowView? _pickedRow;

    public HomeView(HomeModel model, MyListModel? list = null, IDetailExtras? extras = null, Action<PlayTarget>? play = null, ContinueModel? cont = null, PickedModel? picked = null)
    {
        _continue = cont;
        _picked = picked;
        InitializeComponent();
        _model = model;
        _list = list;
        _extras = extras;
        _play = play;
        Loaded += (_, _) =>
        {
            _model.PropertyChanged += OnModelChanged;
            if (_continue is not null)
            {
                _continue.PropertyChanged -= OnContinueChanged;
                _continue.PropertyChanged += OnContinueChanged;
                _ = _continue.LoadAsync(); // coming back from a title, how far you got is read again
            }

            if (_picked is not null)
            {
                _picked.PropertyChanged -= OnPickedChanged;
                _picked.PropertyChanged += OnPickedChanged;
            }

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
            if (_continue is not null) _continue.PropertyChanged -= OnContinueChanged;
            if (_picked is not null) _picked.PropertyChanged -= OnPickedChanged;
            _hero?.Stop();
        };
        SizeChanged += (_, e) => { if (_hero is not null) _hero.Height = HeroHeight(e.NewSize.Height); };
    }

    private static double HeroHeight(double windowHeight) => Math.Max(windowHeight * 0.62, 500) + HeroBanner.FadeExtra;

    private void OnModelChanged(object? sender, PropertyChangedEventArgs e) => Refresh();

    private void OnContinueChanged(object? sender, PropertyChangedEventArgs e) => Dispatcher.BeginInvoke(RefreshContinue);

    private void OnPickedChanged(object? sender, PropertyChangedEventArgs e) => Dispatcher.BeginInvoke(RefreshPicked);

    private void RefreshPicked()
    {
        if (_picked is null) return;
        if (_pickedRow is null)
        {
            _pickedRow = new PickedRowView(_picked);
            PickedHost.Children.Clear();
            PickedHost.Children.Add(_pickedRow);
        }
        else
        {
            _pickedRow.Refresh();
        }
    }

    private void RefreshContinue()
    {
        if (_continue is null || _play is null) return;
        if (_continueRow is null)
        {
            _continueRow = new ContinueRow(_continue, _play);
            ContinueHost.Children.Clear();
            ContinueHost.Children.Add(_continueRow);
        }
        else
        {
            _continueRow.Refresh();
        }
    }

    private void Refresh()
    {
        var showRows = _model.Sections.Count > 0;
        PageScroll.Visibility = showRows ? Visibility.Visible : Visibility.Collapsed;
        var loading = !showRows && _model.State is HomeState.Idle or HomeState.Loading;
        if (!loading) LoadingPanel.Content = null;
        else LoadingPanel.Content ??= Skeleton.HomePage(); // made only while it shows, so its pulse runs only then
        EmptyPanel.Visibility = !showRows && _model.State == HomeState.Empty ? Visibility.Visible : Visibility.Collapsed;
        RefreshContinue();
        RefreshPicked();
        if (!showRows) return;

        // update in place rather than rebuilding, so rows already on screen don't flicker as more arrive
        if (!ReferenceEquals(Rows.ItemsSource, _model.Sections)) Rows.ItemsSource = _model.Sections;

        var hero = _model.Hero;
        if (hero.Count == 0)
        {
            HeroHost.Children.Clear();
            _hero?.Stop();
            _hero = null;
            RowsHost.Margin = new Thickness(0, 118, 0, 8);
        }
        else if (_hero is null || !_hero.Shows(hero))
        {
            _hero?.Stop();
            _hero = new HeroBanner(hero, _list, _extras, _play) { Height = HeroHeight(ActualHeight) };
            HeroHost.Children.Clear();
            HeroHost.Children.Add(_hero);
            RowsHost.Margin = new Thickness(0, -60 - HeroBanner.FadeExtra, 0, 8); // the rows start where they did: the banner's extra height is only fade
        }
    }

    private void OnRetry(object sender, RoutedEventArgs e) => _ = _model.LoadAsync();
}
