using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using ArcTV.App.Controls;
using ArcTV.Core.Downloads;
using ArcTV.Core.Sources;
using ArcTV.Core.Storage;

namespace ArcTV.App.Views;

/// <summary>
/// "Downloads", over a title's page (as on the Mac app): the files the account's debrid addons offer for the movie or episode, best quality at the smallest size first, a season and
/// episode choice for a show, and what is being saved. Downloads are part of Arc TV Plus: without it the panel says so instead of listing files.
/// </summary>
public sealed class DownloadPanelView : Grid
{
    private const string Mdl2 = "Segoe MDL2 Assets";
    private const double PanelWidth = 720;
    private const double PanelHeight = 700;
    private const double MaxZoom = 1.3;
    private const double BannerHeight = 230;

    private readonly Func<PlayTarget, CancellationToken, Task<SourceResult>> _find;
    private readonly DownloadManager _manager;
    private readonly LocalSettings _local;
    private readonly TextBlock _folderText = new() { FontSize = 13, TextTrimming = TextTrimming.CharacterEllipsis, VerticalAlignment = VerticalAlignment.Center };
    private readonly bool _plus;
    private readonly Action _seePlus;
    private readonly Action _close;
    private readonly Action<PlayTarget> _saved;
    private readonly Action? _seeAll;
    private readonly RoundedBorder _panel;
    private readonly ScrollViewer _scroll;
    private readonly ContentControl _bannerHost = new();
    private readonly ContentControl _pickers = new();
    private readonly ContentControl _sources = new();
    private readonly StackPanel _jobList = new();
    private readonly Border _jobsBar;
    private readonly HashSet<string> _started = [];
    private readonly Dictionary<string, (Border Button, TextBlock Glyph, Border Row)> _rows = [];
    private readonly Dictionary<DownloadJob, ProgressBar> _bars = [];
    private PlayTarget _target;
    private readonly Action<DownloadEvent>? _report;
    private CancellationTokenSource? _run;

    public DownloadPanelView(PlayTarget target, Func<PlayTarget, CancellationToken, Task<SourceResult>> find, DownloadManager manager, LocalSettings local, bool plus, Action seePlus, Action close, Action<PlayTarget> saved, Action? seeAll = null, Action<DownloadEvent>? report = null)
    {
        (_target, _find, _manager, _local, _plus, _seePlus, _close, _saved, _seeAll, _report) = (target, find, manager, local, plus, seePlus, close, saved, seeAll, report);

        var dim = new Border { Background = new SolidColorBrush(Color.FromArgb(0xB3, 0, 0, 0)) };
        dim.MouseLeftButtonDown += (_, e) =>
        {
            e.Handled = true;
            _close();
        };
        Children.Add(dim);

        var body = new StackPanel { Margin = new Thickness(24) };
        if (_plus) body.Children.Add(FolderRow());
        body.Children.Add(_pickers);
        body.Children.Add(_sources);
        _scroll = new ScrollViewer { VerticalScrollBarVisibility = ScrollBarVisibility.Auto, Content = body };
        _scroll.ScrollChanged += (_, e) => e.Handled = true; // scrolling here must not make the page's top bar come and go

        _jobsBar = new Border
        {
            Background = Ui.Res("SurfaceBrush"), BorderBrush = Ui.Line, BorderThickness = new Thickness(0, 1, 0, 0), Padding = new Thickness(24, 12, 24, 12), Visibility = Visibility.Collapsed, Child = _jobList,
        };

        var column = new DockPanel();
        DockPanel.SetDock(_bannerHost, Dock.Top);
        DockPanel.SetDock(_jobsBar, Dock.Bottom);
        column.Children.Add(_bannerHost);
        column.Children.Add(_jobsBar);
        column.Children.Add(_scroll);

        _panel = new RoundedBorder
        {
            Width = PanelWidth, CornerRadius = new CornerRadius(22), Background = Ui.Res("BgElevatedBrush"), BorderBrush = Ui.Line, BorderThickness = new Thickness(1), Child = column,
            HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center,
            Effect = new System.Windows.Media.Effects.DropShadowEffect { BlurRadius = 40, ShadowDepth = 12, Opacity = 0.6 },
        };
        _panel.MouseLeftButtonDown += (_, e) => e.Handled = true; // a click inside is not a click on the dim
        Children.Add(_panel);

        // always the same size, whatever the files below are showing (never changing with the season or episode picked). It is drawn up to 1.3 times its 720 x 700 units, less in a window
        // too small for that, and never wider or taller than the window
        var zoom = new ScaleTransform(1, 1);
        _panel.LayoutTransform = zoom;
        SizeChanged += (_, e) =>
        {
            var scale = Math.Clamp(Math.Min((e.NewSize.Width - 48) / PanelWidth, (e.NewSize.Height - 48) / PanelHeight), 0.5, MaxZoom);
            zoom.ScaleX = zoom.ScaleY = scale;
            _panel.Width = Math.Min(PanelWidth, (e.NewSize.Width - 48) / scale);
            _panel.Height = Math.Min(PanelHeight, (e.NewSize.Height - 48) / scale);
        };

        ShowBanner();
        Loaded += (_, _) =>
        {
            _manager.Jobs.CollectionChanged += OnJobsChanged;
            foreach (var job in _manager.Jobs) job.PropertyChanged += OnJobChanged;
            RebuildJobs();
            Report(_plus ? DownloadEvent.Opened : DownloadEvent.Popup); // every press of Download is counted: the panel opened, or (without Plus) the Plus popup shown
            if (_plus) Reload();
            else ShowNotPlus();
        };
        Unloaded += (_, _) =>
        {
            _run?.Cancel();
            _manager.Jobs.CollectionChanged -= OnJobsChanged;
            foreach (var job in _manager.Jobs) job.PropertyChanged -= OnJobChanged;
        };
    }

    /// <summary>"Save to  C:\Users\...\Downloads  Change": where files go, and a folder picker to change it.</summary>
    private UIElement FolderRow()
    {
        _manager.Folder = _local.DownloadFolder ?? DownloadManager.DefaultFolder();
        _folderText.Text = _manager.Folder;
        _folderText.Foreground = Ui.Res("TextBrush");
        var change = new Button { Content = "Change", Style = Ui.StyleOf("ChoicePill"), Height = 34, Padding = new Thickness(16, 0, 16, 0), Margin = new Thickness(12, 0, 0, 0), ToolTip = "Choose another folder to save downloads in" };
        change.Click += (_, _) =>
        {
            var dialog = new Microsoft.Win32.OpenFolderDialog { Title = "Choose where downloads are saved", InitialDirectory = _manager.Folder };
            if (dialog.ShowDialog(Window.GetWindow(this)) != true || string.IsNullOrEmpty(dialog.FolderName)) return;
            _local.DownloadFolder = dialog.FolderName;
            _manager.Folder = dialog.FolderName;
            _folderText.Text = dialog.FolderName;
        };
        var row = new DockPanel { Margin = new Thickness(0, 0, 0, 16) };
        DockPanel.SetDock(change, Dock.Right);
        row.Children.Add(change);
        row.Children.Add(new TextBlock { Text = "Save to", FontSize = 13, FontWeight = FontWeights.SemiBold, Foreground = Ui.Res("Text2Brush"), VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 10, 0) });
        row.Children.Add(_folderText);
        return row;
    }

    /// <summary>What the panel is showing (for the tests): "plus" (the invitation), "loading", "list" or "none".</summary>
    public string State { get; private set; } = "";

    private static TextBlock Words(string text, double size, FontWeight? weight = null, Brush? brush = null) =>
        new() { Text = text, FontSize = size, FontWeight = weight ?? FontWeights.Normal, Foreground = brush ?? Ui.Res("TextBrush"), TextWrapping = TextWrapping.Wrap };

    // ---- the picture across the top ----

    private void ShowBanner()
    {
        var detail = _target.Title;
        var grid = new Grid { Height = BannerHeight, ClipToBounds = true, Background = Ui.Res("SurfaceHighBrush") };
        grid.Children.Add(new RemoteImage { Url = detail.Background ?? detail.Poster, Stretch = Stretch.UniformToFill, VerticalAlignment = VerticalAlignment.Top, DecodeWidth = 1440 });
        var page = (Color)Application.Current.FindResource("BgElevatedColor");
        grid.Children.Add(new System.Windows.Shapes.Rectangle
        {
            Fill = new LinearGradientBrush
            {
                StartPoint = new Point(0, 0), EndPoint = new Point(0, 1),
                GradientStops =
                {
                    new GradientStop(Color.FromArgb(0x99, 0, 0, 0), 0), new GradientStop(Color.FromArgb(0, 0, 0, 0), 0.35), new GradientStop(Color.FromArgb(0x99, page.R, page.G, page.B), 0.7), new GradientStop(page, 1),
                },
            },
        });

        var top = new DockPanel { VerticalAlignment = VerticalAlignment.Top, Margin = new Thickness(16) };
        var back = RoundButton("", "Back", _close);
        var close = RoundButton("", "Close", _close);
        DockPanel.SetDock(back, Dock.Left);
        DockPanel.SetDock(close, Dock.Right);
        top.Children.Add(back);
        top.Children.Add(close);
        top.Children.Add(new TextBlock { Text = "Downloads", FontSize = 24, FontWeight = FontWeights.Bold, Foreground = Brushes.White, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(12, 0, 0, 0) });
        grid.Children.Add(top);

        var bottom = new StackPanel { VerticalAlignment = VerticalAlignment.Bottom, HorizontalAlignment = HorizontalAlignment.Left, Margin = new Thickness(24, 0, 24, 14) };
        if (!string.IsNullOrEmpty(detail.Logo)) bottom.Children.Add(new RemoteImage { Url = detail.Logo, Stretch = Stretch.Uniform, StretchDirection = StretchDirection.DownOnly, HorizontalAlignment = HorizontalAlignment.Left, MaxWidth = 320, MaxHeight = 84, DecodeWidth = 640 });
        else bottom.Children.Add(Words(detail.Name, 32, FontWeights.ExtraBold));
        var episode = detail.Type == "series" && _target is { Season: { } season, Episode: { } number } ? $"S{season}E{number}" : null;
        bottom.Children.Add(new ContentControl { Margin = new Thickness(0, 6, 0, 0), Content = Words(string.Join(" • ", new[] { detail.Year?.ToString(), detail.Type == "series" ? "Show" : "Movie", episode }.Where(part => part is not null)), 14, brush: Ui.Res("Text2Brush")) });
        grid.Children.Add(bottom);
        _bannerHost.Content = grid;
    }

    private static Button RoundButton(string glyph, string tip, Action action)
    {
        var button = new Button
        {
            Style = Ui.StyleOf("PosterButton"), ToolTip = tip, Cursor = System.Windows.Input.Cursors.Hand,
            Content = new Border
            {
                Width = 36, Height = 36, CornerRadius = new CornerRadius(18), Background = new SolidColorBrush(Color.FromArgb(0x80, 0, 0, 0)), BorderBrush = new SolidColorBrush(Color.FromArgb(0x2E, 255, 255, 255)), BorderThickness = new Thickness(1),
                Child = new TextBlock { Text = glyph, FontFamily = new FontFamily(Mdl2), FontSize = 14, Foreground = Brushes.White, HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center },
            },
        };
        button.Click += (_, _) => action();
        return button;
    }

    // ---- without Arc TV Plus ----

    private void ShowNotPlus()
    {
        State = "plus";
        _pickers.Content = null;
        var stack = new StackPanel { Margin = new Thickness(0, 6, 0, 6) };
        stack.Children.Add(Words("Downloads are part of Arc TV Plus", 20, FontWeights.Bold));
        stack.Children.Add(new ContentControl { Margin = new Thickness(0, 8, 0, 18), Content = Words("Save movies and episodes from your debrid service to this PC, best quality first.", 14, brush: Ui.Res("Text2Brush")) });
        var row = new StackPanel { Orientation = Orientation.Horizontal };
        var see = new Button { Content = "See Arc TV Plus", Style = Ui.StyleOf("PrimaryButton"), Height = 44, Padding = new Thickness(24, 0, 24, 0), Margin = new Thickness(0, 0, 12, 0) };
        see.Click += (_, _) =>
        {
            _close();
            _seePlus();
        };
        var later = new Button { Content = "Not now", Style = Ui.StyleOf("GhostButton"), Height = 44, Padding = new Thickness(24, 0, 24, 0) };
        later.Click += (_, _) => _close();
        row.Children.Add(see);
        row.Children.Add(later);
        stack.Children.Add(row);
        _sources.Content = stack;
    }

    // ---- season and episode, for a show ----

    private void ShowPickers()
    {
        var detail = _target.Title;
        var seasons = detail.SeasonNumbers;
        if (detail.Type != "series" || seasons.Count == 0)
        {
            _pickers.Content = null;
            return;
        }

        var current = _target.Season ?? seasons[0];
        var stack = new StackPanel { Margin = new Thickness(0, 0, 0, 16) };
        stack.Children.Add(Words("Season", 13, FontWeights.SemiBold, Ui.Res("Text2Brush")));
        var seasonRow = new WrapPanel { Margin = new Thickness(0, 6, 0, 10) };
        foreach (var number in seasons)
        {
            var count = detail.Seasons[number].Count;
            var pill = new Button { Content = $"{number}", Style = Ui.StyleOf("ChoicePill"), Margin = new Thickness(0, 0, 8, 8), MinWidth = 44, Tag = number == current ? "selected" : "", ToolTip = DownloadChoices.SeasonLabel(number, count) };
            var chosen = number;
            pill.Click += (_, _) =>
            {
                if (detail.Seasons[chosen].FirstOrDefault() is { } first) Pick(first);
            };
            seasonRow.Children.Add(pill);
        }

        stack.Children.Add(seasonRow);
        stack.Children.Add(Words("Episode", 13, FontWeights.SemiBold, Ui.Res("Text2Brush")));
        var episodeRow = new WrapPanel { Margin = new Thickness(0, 6, 0, 0) };
        foreach (var episode in detail.Seasons.GetValueOrDefault(current) ?? [])
        {
            var pill = new Button { Content = $"{episode.Episode ?? 0}", Style = Ui.StyleOf("ChoicePill"), Margin = new Thickness(0, 0, 8, 8), MinWidth = 44, Tag = episode.Episode == _target.Episode ? "selected" : "", ToolTip = episode.DisplayTitle };
            var chosen = episode;
            pill.Click += (_, _) => Pick(chosen);
            episodeRow.Children.Add(pill);
        }

        stack.Children.Add(episodeRow);
        _pickers.Content = stack;
    }

    private void Pick(Core.Details.EpisodeInfo episode)
    {
        _target = new PlayTarget(_target.Title, episode.Season, episode.Episode, DownloadChoices.EpisodeHeading(episode));
        ShowBanner();
        Reload();
    }

    // ---- the files ----

    private void Reload()
    {
        _run?.Cancel();
        var run = _run = new CancellationTokenSource();
        ShowPickers();
        State = "loading";
        _rows.Clear();
        _sources.Content = new StackPanel
        {
            Children =
            {
                Words("Asking your addons…", 14, brush: Ui.Res("Text2Brush")),
                new ProgressBar { IsIndeterminate = true, Height = 3, Width = 160, HorizontalAlignment = HorizontalAlignment.Left, Margin = new Thickness(0, 10, 0, 0), Foreground = Ui.Res("AccentBrush"), Background = Ui.Res("SurfaceBrush"), BorderThickness = new Thickness(0) },
            },
        };
        _ = LoadAsync(run);
    }

    private async Task LoadAsync(CancellationTokenSource run)
    {
        IReadOnlyList<SourceItem> choices;
        try
        {
            choices = DownloadChoices.Sorted((await _find(_target, run.Token)).Items);
        }
        catch (Exception) when (run.IsCancellationRequested)
        {
            return;
        }
        catch (Exception)
        {
            choices = [];
        }

        if (run.IsCancellationRequested) return;
        ShowChoices(choices);
    }

    private void ShowChoices(IReadOnlyList<SourceItem> choices)
    {
        if (choices.Count == 0)
        {
            State = "none";
            _sources.Content = Words("No files to download for this title. Downloads need a debrid addon that gives direct links; torrents can't be saved this way.", 14, brush: Ui.Res("Text2Brush"));
            return;
        }

        State = "list";
        var stack = new StackPanel();
        var header = new DockPanel { Margin = new Thickness(0, 0, 0, 10) };
        var order = Words("Best quality first, then smallest", 13, brush: Ui.Res("Text3Brush"));
        DockPanel.SetDock(order, Dock.Right);
        header.Children.Add(order);
        header.Children.Add(Words(choices.Count == 1 ? "1 source" : $"{choices.Count} sources", 14, brush: Ui.Res("Text2Brush")));
        stack.Children.Add(header);
        foreach (var item in choices) stack.Children.Add(Row(item));
        stack.Children.Add(new ContentControl { Margin = new Thickness(0, 4, 0, 0), Content = Words("Choose a source to download the file.", 12, brush: Ui.Res("Text3Brush")) });
        _sources.Content = stack;
    }

    private Border Row(SourceItem item)
    {
        var badge = new Border
        {
            Width = 70, Margin = new Thickness(0, 0, 14, 0), Padding = new Thickness(0, 12, 0, 12), CornerRadius = new CornerRadius(10), Background = Ui.Res("BgBrush"), BorderBrush = Ui.Line, BorderThickness = new Thickness(1),
            VerticalAlignment = VerticalAlignment.Center,
            Child = new TextBlock { Text = item.Quality.Label(), FontSize = 18, FontWeight = FontWeights.ExtraBold, Foreground = Ui.Res("AccentBrush"), HorizontalAlignment = HorizontalAlignment.Center },
        };

        var text = new StackPanel { VerticalAlignment = VerticalAlignment.Center };
        text.Children.Add(new TextBlock { Text = DownloadChoices.Kind(item), FontSize = 16, FontWeight = FontWeights.Bold, Foreground = Ui.Res("TextBrush"), TextTrimming = TextTrimming.CharacterEllipsis });
        text.Children.Add(new TextBlock { Text = item.Headline, FontSize = 12, Foreground = Ui.Res("Text3Brush"), TextTrimming = TextTrimming.CharacterEllipsis, Margin = new Thickness(0, 3, 0, 0) });
        var chips = new WrapPanel { Margin = new Thickness(0, 6, 0, 0) };
        if (item.SizeText is { } size) chips.Children.Add(Chip(size, true));
        if (item.CodecText is { } codec) chips.Children.Add(Chip(codec.Replace("  ·  ", " · "), false));
        if (DownloadChoices.Hdr(item) is { } hdr) chips.Children.Add(Chip(hdr, false, Ui.Res("AccentBrush"), true));
        if (DownloadChoices.IsUnusuallySmall(item)) chips.Children.Add(Chip("Unusually small", false, Amber, true));
        if (DownloadChoices.Debrid(item) is { } debrid) chips.Children.Add(Chip((debrid.Cached ? "\u2713  " : "\u23F3  ") + debrid.Label, false, debrid.Cached ? Green : Amber, false));
        text.Children.Add(chips);

        var started = _started.Contains(item.Id);
        var glyph = new TextBlock { FontFamily = new FontFamily(Mdl2), FontSize = 16, FontWeight = FontWeights.SemiBold, HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center };
        var button = new Border { Width = 44, Height = 44, CornerRadius = new CornerRadius(12), BorderThickness = new Thickness(1), VerticalAlignment = VerticalAlignment.Center, Child = glyph };
        var line = new DockPanel();
        DockPanel.SetDock(badge, Dock.Left);
        DockPanel.SetDock(button, Dock.Right);
        line.Children.Add(badge);
        line.Children.Add(button);
        line.Children.Add(text);

        var row = new Border { Child = line, Padding = new Thickness(14, 12, 14, 12), Margin = new Thickness(0, 0, 0, 10), CornerRadius = new CornerRadius(14), Background = Ui.Res("SurfaceBrush"), BorderThickness = new Thickness(1), Cursor = System.Windows.Input.Cursors.Hand };
        _rows[item.Id] = (button, glyph, row);
        Mark(item.Id, started);
        row.MouseLeftButtonUp += (_, _) => Save(item);
        return row;
    }

    private void Mark(string id, bool started)
    {
        var (button, glyph, row) = _rows[id];
        var accent = Ui.Res("AccentBrush");
        glyph.Text = started ? "" : "";
        glyph.Foreground = started ? Ui.Res("BgBrush") : accent;
        button.Background = started ? accent : Brushes.Transparent;
        button.BorderBrush = accent;
        button.Opacity = started ? 1 : 0.8;
        row.BorderBrush = started ? accent : Ui.Line;
    }

    private static readonly SolidColorBrush Amber = new(Color.FromRgb(0xFF, 0xB0, 0x20));
    private static readonly SolidColorBrush Green = new(Color.FromRgb(0x4A, 0xDE, 0x80));

    /// <summary>A small label under a file's name: grey and filled, or (<paramref name="outlined"/>) an outline in its own colour. <paramref name="colour"/> colours the words.</summary>
    private static Border Chip(string text, bool bold, Brush? colour = null, bool outlined = false) => new()
    {
        Margin = new Thickness(0, 0, 8, 4), Padding = new Thickness(7, 3, 7, 3), CornerRadius = new CornerRadius(6),
        Background = outlined ? Brushes.Transparent : new SolidColorBrush(Color.FromArgb(0x1A, 255, 255, 255)),
        BorderBrush = outlined && colour is not null ? colour : Brushes.Transparent, BorderThickness = new Thickness(1),
        Child = new TextBlock { Text = text, FontSize = 11, FontWeight = bold ? FontWeights.Bold : FontWeights.Medium, Foreground = colour ?? (bold ? Ui.Res("TextBrush") : Ui.Res("Text2Brush")) },
    };

    private void Report(string kind, string? resolution = null, bool? cached = null) =>
        _report?.Invoke(new DownloadEvent(kind, _target.Title.Type == "movie", _target.Season is not null, resolution, cached));

    /// <summary>Starts saving a file; the button turns into a tick at once, so a click always visibly does something.</summary>
    private void Save(SourceItem item)
    {
        if (item.PlayUrl is not { } url) return;
        Report(DownloadEvent.File, item.Quality.Label(), DownloadChoices.Debrid(item)?.Cached);
        _manager.Start(url, DownloadChoices.FileName(item.Headline, url));
        _started.Add(item.Id);
        Mark(item.Id, true);
        _saved(_target);
    }

    // ---- what is being saved ----

    private void OnJobsChanged(object? sender, System.Collections.Specialized.NotifyCollectionChangedEventArgs e)
    {
        if (e.OldItems is not null)
            foreach (DownloadJob job in e.OldItems) job.PropertyChanged -= OnJobChanged;
        if (e.NewItems is not null)
            foreach (DownloadJob job in e.NewItems) job.PropertyChanged += OnJobChanged;
        RebuildJobs();
    }

    private void OnJobChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (sender is not DownloadJob job) return;
        if (e.PropertyName == nameof(DownloadJob.Fraction))
        {
            if (_bars.TryGetValue(job, out var bar))
            {
                bar.IsIndeterminate = job.Fraction is null;
                bar.Value = job.Fraction ?? 0;
            }
        }
        else
        {
            RebuildJobs();
        }
    }

    private void RebuildJobs()
    {
        _jobList.Children.Clear();
        _bars.Clear();
        _jobsBar.Visibility = _manager.Jobs.Count > 0 ? Visibility.Visible : Visibility.Collapsed;
        if (_manager.Jobs.Count == 0) return;

        var header = new DockPanel { Margin = new Thickness(0, 0, 0, 8) };
        var clear = new Button { Content = "Clear finished", Style = Ui.StyleOf("LinkButton") };
        clear.Click += (_, _) => _manager.ClearFinished();
        DockPanel.SetDock(clear, Dock.Right);
        header.Children.Add(clear);
        if (_seeAll is not null)
        {
            var all = new Button { Content = "See all downloads", Style = Ui.StyleOf("LinkButton"), Margin = new Thickness(0, 0, 16, 0) };
            all.Click += (_, _) =>
            {
                _close();
                _seeAll();
            };
            DockPanel.SetDock(all, Dock.Right);
            header.Children.Add(all);
        }

        header.Children.Add(Words("Downloads", 15, FontWeights.Bold));
        _jobList.Children.Add(header);
        foreach (var job in _manager.Jobs.Take(3)) _jobList.Children.Add(JobRow(job));
    }

    private UIElement JobRow(DownloadJob job)
    {
        var line = new DockPanel();
        var name = new TextBlock { Text = job.Name, FontSize = 12, FontWeight = FontWeights.Medium, Foreground = Ui.Res("TextBrush"), TextTrimming = TextTrimming.CharacterEllipsis, VerticalAlignment = VerticalAlignment.Center };
        if (job.State == DownloadState.Failed && !job.CanResume)
        {
            // nothing to do for it but read why
        }
        else
        {
            var action = new Button { Content = job.State switch { DownloadState.Running => "Cancel", DownloadState.Done => "Show in folder", _ => "Resume" }, Style = Ui.StyleOf("LinkButton") };
            action.Click += (_, _) =>
            {
                if (job.State == DownloadState.Running) _manager.Cancel(job);
                else if (job.CanResume) _manager.Resume(job);
                else if (job.FilePath is { } path) Process.Start(new ProcessStartInfo("explorer.exe", $"/select,\"{path}\"") { UseShellExecute = true });
            };
            DockPanel.SetDock(action, Dock.Right);
            line.Children.Add(action);
        }

        line.Children.Add(name);

        var stack = new StackPanel { Margin = new Thickness(0, 0, 0, 8) };
        stack.Children.Add(line);
        switch (job.State)
        {
            case DownloadState.Running:
                var bar = new ProgressBar { Height = 6, Minimum = 0, Maximum = 1, Value = job.Fraction ?? 0, IsIndeterminate = job.Fraction is null, Foreground = Ui.Res("AccentBrush"), Background = Ui.Res("BgBrush"), BorderThickness = new Thickness(0), Margin = new Thickness(0, 4, 0, 0) };
                _bars[job] = bar;
                stack.Children.Add(bar);
                break;
            case DownloadState.Done:
                stack.Children.Add(Words($"Saved to {(job.FilePath is { } saved ? Path.GetDirectoryName(saved) : "your Downloads folder")}", 11, brush: new SolidColorBrush(Color.FromRgb(0x4A, 0xDE, 0x80))));
                break;
            default: // stopped or failed
                stack.Children.Add(Words(job.Message ?? "The download failed.", 11, brush: Amber));
                break;
        }

        return stack;
    }
}
