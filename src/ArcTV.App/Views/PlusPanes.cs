using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using ArcTV.App.Controls;
using ArcTV.Core.Addons;
using ArcTV.Core.MyList;
using ArcTV.Core.Plus;
using ArcTV.Core.Settings;

namespace ArcTV.App.Views;

/// <summary>Base of the four Plus pages: built from the Plus model, which it asks for what it shows once it is on screen.</summary>
internal abstract class PlusPane : ModelPane
{
    protected PlusPane(SettingsModel settings, PlusModel plus, params System.ComponentModel.INotifyPropertyChanged[] more) : base(settings, [plus, .. more]) => Plus = plus;

    protected PlusModel Plus { get; }

    protected override bool NeedsSettings => false;

    protected override async Task ShownAsync() => await Plus.LoadStatusAsync();

    /// <summary>The note a Plus-only page shows without Plus.</summary>
    protected UIElement Locked(string title, string detail, Action seePlus)
    {
        var panel = new StackPanel();
        panel.Children.Add(new TextBlock { Text = "", FontFamily = new FontFamily("Segoe MDL2 Assets"), FontSize = 22, Foreground = Ui.Res("Text3Brush") });
        panel.Children.Add(new TextBlock { Text = title, FontSize = 16, FontWeight = FontWeights.Bold, Margin = new Thickness(0, 10, 0, 0) });
        panel.Children.Add(new TextBlock { Text = detail, FontSize = 13, Foreground = Ui.Res("Text2Brush"), TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 6, 0, 12) });
        var row = new StackPanel { Orientation = Orientation.Horizontal };
        row.Children.Add(Ui.Act("See Arc TV Plus", seePlus));
        panel.Children.Add(row);
        return Ui.Card(panel, 18);
    }

    protected static Border Chip(string text, bool soon)
    {
        var color = soon ? Color.FromRgb(0xC9, 0xB5, 0xFF) : Color.FromRgb(0x19, 0xE6, 0xFF);
        return new Border
        {
            Child = new TextBlock { Text = text, FontSize = 11, FontWeight = FontWeights.Bold, Foreground = new SolidColorBrush(color) },
            BorderBrush = new SolidColorBrush(Color.FromArgb(0xAA, color.R, color.G, color.B)), BorderThickness = new Thickness(1), CornerRadius = new CornerRadius(11),
            Padding = new Thickness(10, 2, 10, 2), Margin = new Thickness(8, 0, 0, 0), VerticalAlignment = VerticalAlignment.Center,
        };
    }

    protected static Border Surface(UIElement child, double padding = 16) => new()
    {
        Background = Ui.Res("SurfaceBrush"), CornerRadius = new CornerRadius(14), Padding = new Thickness(padding), Child = child,
    };
}

/// <summary>Settings > Arc TV Plus: what Plus adds, whether you have it, and how to subscribe. Nothing here changes what the free app does.</summary>
internal sealed class ArcTvPlusPane(SettingsModel settings, PlusModel plus) : PlusPane(settings, plus)
{
    private string? _busy;
    private bool _waiting;
    private string? _error;
    private bool _confirmingCancel;
    private string? _cancelError;

    protected override void Build(StackPanel column)
    {
        var status = Plus.Status;
        var owned = status.Paywall && status.Active;

        var line = new WrapPanel { Margin = new Thickness(0, 0, 0, 14) };
        void Badge(string text) => line.Children.Add(new Border
        {
            Background = Ui.Res("SurfaceHighBrush"), CornerRadius = new CornerRadius(12), Padding = new Thickness(12, 3, 12, 3), Margin = new Thickness(0, 0, 12, 4),
            Child = new TextBlock { Text = text, FontSize = 13, FontWeight = FontWeights.Bold },
        });
        void Say(string text) => line.Children.Add(new TextBlock { Text = text, FontSize = 13, Foreground = Ui.Res("Text2Brush"), TextWrapping = TextWrapping.Wrap, MaxWidth = 520, VerticalAlignment = VerticalAlignment.Center });
        if (!Plus.StatusKnown)
        {
            Say("Checking your Arc TV Plus status…");
        }
        else if (!status.Paywall)
        {
            Badge("Early access");
            Say("Arc TV Plus is in early access: its features are free for now and will need a Plus subscription once it launches.");
        }
        else if (status.Active)
        {
            Badge("Arc TV Plus");
            var plan = PlusPlan.LabelFor(status.Plan);
            Say($"You have Arc TV Plus{(plan is null ? "" : $" ({plan})")}{ValidText(status)} Thank you for supporting Arc TV.");
        }
        else
        {
            Badge("Free plan");
            Say("You're on the free plan. Everything below is included with Plus.");
        }

        column.Children.Add(line);
        if (owned && status.Plan is "monthly" or "yearly") column.Children.Add(Subscription(status));
        column.Children.Add(new TextBlock { Text = PlusCopy.FreeNote, FontSize = 14, Foreground = Ui.Res("Text2Brush"), TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 4, 0, 12) });
        column.Children.Add(Ui.Card(new TextBlock { Text = "♥  " + PlusCopy.ProceedsNote, FontSize = 13, TextWrapping = TextWrapping.Wrap }));

        column.Children.Add(Ui.Heading("What Plus adds"));
        var perks = new WrapPanel();
        foreach (var perk in PlusCopy.Perks)
        {
            var card = new StackPanel();
            var title = new WrapPanel();
            title.Children.Add(new TextBlock { Text = perk.Title, FontSize = 15, FontWeight = FontWeights.Bold, VerticalAlignment = VerticalAlignment.Center });
            title.Children.Add(Chip(perk.Soon ? "Coming soon" : status.Paywall ? "Plus" : "Included in early access", perk.Soon));
            if (perk.Tag is { } extra && !perk.Soon) title.Children.Add(Chip(extra, true));
            card.Children.Add(title);
            card.Children.Add(new TextBlock { Text = perk.Detail, FontSize = 13, Foreground = Ui.Res("Text2Brush"), TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 8, 0, 0) });
            var cell = Surface(card);
            cell.Width = 330;
            cell.Margin = new Thickness(0, 0, 14, 14);
            perks.Children.Add(cell);
        }

        column.Children.Add(perks);
        if (status.Paywall && !status.Active) column.Children.Add(Subscribe(status));
    }

    private static string ValidText(PlusStatus status) =>
        IsoDateText(status.ValidUntil) is { } until ? $". Your current period runs to {until}." : ", for life.";

    private static string? IsoDateText(string? iso) =>
        iso is not null && ArcTV.Core.Account.IsoDate.Parse(iso) is { } date ? date.ToLocalTime().ToString("d MMM yyyy", System.Globalization.CultureInfo.CurrentCulture) : null;

    private UIElement Subscription(PlusStatus status)
    {
        var label = PlusPlan.LabelFor(status.Plan) ?? "";
        var until = IsoDateText(status.ValidUntil);
        var grid = new Grid();
        grid.ColumnDefinitions.Add(new ColumnDefinition());
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        var text = new StackPanel();
        text.Children.Add(new TextBlock { Text = $"Arc TV Plus · {label}", FontSize = 12, FontWeight = FontWeights.SemiBold, Foreground = Ui.Res("Text3Brush") });
        text.Children.Add(new TextBlock
        {
            FontSize = 14, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 3, 0, 0),
            Text = status.CancelAtPeriodEnd ? $"Your subscription won't renew.{(until is null ? "" : $" You keep Plus until {until}.")}" : until is null ? "Active." : $"Renews on {until}.",
        });
        if (_confirmingCancel)
            text.Children.Add(new TextBlock
            {
                FontSize = 12, Foreground = Ui.Res("Text2Brush"), TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 8, 0, 0),
                Text = until is null
                    ? "You keep Arc TV Plus until the end of the period you've paid for. After that it won't renew and you won't be charged again."
                    : $"You keep Arc TV Plus until {until}. After that it won't renew and you won't be charged again.",
            });
        if (_cancelError is { } problem) text.Children.Add(new TextBlock { Text = problem, FontSize = 13, Foreground = Ui.Pink, Margin = new Thickness(0, 8, 0, 0), TextWrapping = TextWrapping.Wrap });
        grid.Children.Add(text);

        var right = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(14, 0, 0, 0) };
        if (!status.CancelAtPeriodEnd)
        {
            if (_confirmingCancel)
            {
                var yes = new Button { Content = "Cancel subscription", Style = (Style)Application.Current.FindResource("LinkButton"), Foreground = Ui.Pink, Margin = new Thickness(0, 0, 12, 0) };
                yes.Click += async (_, _) => await CancelAsync();
                var keep = new Button { Content = "Keep Plus", Style = (Style)Application.Current.FindResource("LinkButton"), Foreground = Ui.Res("Text2Brush") };
                keep.Click += (_, _) =>
                {
                    _confirmingCancel = false;
                    Settings.Redraw();
                };
                right.Children.Add(yes);
                right.Children.Add(keep);
            }
            else
            {
                right.Children.Add(Ui.Act("Cancel subscription", () =>
                {
                    _cancelError = null;
                    _confirmingCancel = true;
                    Settings.Redraw();
                }));
            }
        }

        Grid.SetColumn(right, 1);
        grid.Children.Add(right);
        return Ui.Card(grid);
    }

    private async Task CancelAsync()
    {
        _confirmingCancel = false;
        try
        {
            await Plus.CancelSubscriptionAsync();
        }
        catch (Exception ex)
        {
            _cancelError = PlusModel.CancelProblem(ex);
        }

        Settings.Redraw();
    }

    private UIElement Subscribe(PlusStatus status)
    {
        var panel = new StackPanel();
        panel.Children.Add(Ui.Heading("How to subscribe"));
        var steps = new StackPanel { Margin = new Thickness(0, 0, 0, 12) };
        foreach (var step in new[]
        {
            $"1. Pick a plan below: monthly, yearly, or a one-time Lifetime payment.{(status.TrialDays > 0 ? $" Yearly starts with {status.TrialDays} days free." : "")}",
            "2. Pay on Stripe's secure page, which opens in your browser.",
            "3. Come back to this page: Plus switches on by itself within a minute.",
        })
            steps.Children.Add(new TextBlock { Text = step, FontSize = 13, Foreground = Ui.Res("Text2Brush"), TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 0, 0, 4) });
        panel.Children.Add(steps);

        var plans = new WrapPanel();
        foreach (var plan in PlusPlan.All)
        {
            var card = new StackPanel();
            card.Children.Add(new TextBlock { Text = plan.Label, FontSize = 17, FontWeight = FontWeights.Bold });
            card.Children.Add(new TextBlock { Text = "Price at checkout", FontSize = 20, FontWeight = FontWeights.ExtraBold, Margin = new Thickness(0, 8, 0, 0) });
            card.Children.Add(new TextBlock { Text = plan.Per, FontSize = 12, Foreground = Ui.Res("Text3Brush") });
            card.Children.Add(new TextBlock { Text = plan.Blurb, FontSize = 13, Foreground = Ui.Res("Text2Brush"), TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 8, 0, 12) });
            var trial = plan.Id == "yearly" && status.TrialDays > 0;
            var button = new Button
            {
                Content = _busy == plan.Id ? "Opening…" : plan.Id == "lifetime" ? "Get Lifetime" : trial ? $"Start {status.TrialDays}-day free trial" : $"Choose {plan.Label}",
                Style = (Style)Application.Current.FindResource("PrimaryButton"), Height = 42, IsEnabled = _busy is null,
            };
            var id = plan.Id;
            button.Click += async (_, _) => await ChooseAsync(id);
            card.Children.Add(button);
            if (trial || plan.Note is not null) card.Children.Insert(0, new TextBlock { Text = trial ? $"{status.TrialDays} days free" : plan.Note, FontSize = 12, FontWeight = FontWeights.Bold, Foreground = Ui.Res("AccentBrush"), Margin = new Thickness(0, 0, 0, 6) });
            var cell = Surface(card, 18);
            cell.Width = 210;
            cell.Margin = new Thickness(0, 0, 14, 14);
            plans.Children.Add(cell);
        }

        panel.Children.Add(plans);
        if (_waiting) panel.Children.Add(new TextBlock { Text = "Waiting for your payment… this switches on by itself when it goes through.", FontSize = 13, Foreground = Ui.Res("Text2Brush"), TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 0, 0, 8) });
        if (_error is { } problem) panel.Children.Add(new TextBlock { Text = problem, FontSize = 13, Foreground = Ui.Pink, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 0, 0, 8) });
        if (status.TrialDays > 0)
            panel.Children.Add(new TextBlock
            {
                Text = $"Your card is taken at checkout, but nothing is charged for the first {status.TrialDays} days. Cancel before then and you pay nothing. The free trial is for the yearly plan, once per account.",
                FontSize = 12, Foreground = Ui.Res("Text3Brush"), TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 0, 0, 6),
            });
        panel.Children.Add(new TextBlock { Text = "Payments are handled by Stripe's secure checkout page; we never see your card.", FontSize = 12, Foreground = Ui.Res("Text3Brush"), TextWrapping = TextWrapping.Wrap });
        return panel;
    }

    private async Task ChooseAsync(string plan)
    {
        _busy = plan;
        _error = null;
        Settings.Redraw();
        try
        {
            WebLinks.Open(await Plus.StartCheckoutAsync(plan));
            _waiting = true;
        }
        catch (Exception ex)
        {
            _error = PlusModel.CheckoutProblem(ex);
        }

        _busy = null;
        Settings.Redraw();
    }
}

/// <summary>Settings > Plus settings: switches for the Plus features.</summary>
internal sealed class PlusSettingsPane(SettingsModel settings, PlusModel plus, ArcTV.Core.Storage.LocalSettings local, Action seePlus) : PlusPane(settings, plus)
{
    protected override void Build(StackPanel column)
    {
        if (!Plus.StatusKnown)
        {
            column.Children.Add(Ui.Note("Checking your Arc TV Plus status…"));
            return;
        }

        if (!Plus.Active)
        {
            column.Children.Add(Locked("Plus settings are only for Arc TV Plus.", "Switch Smart source picking and your other Plus features on and off here.", seePlus));
            return;
        }

        column.Children.Add(Ui.ToggleRow("Smart source picking",
            "Skip the source list and start the best source this PC can play. If none surely plays, you still get the list. Saved on this PC; Arc TV for Windows follows it once it can start a source by itself.",
            local.SmartPicking, on => local.SmartPicking = on));
    }
}

/// <summary>Settings > Recommendations: what has been liked or marked Not for me, what shapes the Picked for you row, and a reset.</summary>
internal sealed class RecommendationsPane : PlusPane
{
    private readonly MyListModel _list;
    private readonly Func<FeedbackItem, CancellationToken, Task<string?>> _posterLookup;
    private readonly Action _seePlus;
    private Rating _tab = Rating.Like;
    private bool _confirmingReset;

    public RecommendationsPane(SettingsModel settings, PlusModel plus, MyListModel list, Func<FeedbackItem, CancellationToken, Task<string?>> posterLookup, Action seePlus) : base(settings, plus, list)
    {
        _list = list;
        _posterLookup = posterLookup;
        _seePlus = seePlus;
        // the same poster card Home uses (a click on it opens the title: the shell sees the click on a button that stands for a title)
        Resources.MergedDictionaries.Add(new ResourceDictionary { Source = new Uri("pack://application:,,,/ArcTV;component/Views/PosterTemplate.xaml") });
    }

    protected override async Task ShownAsync()
    {
        await Plus.LoadStatusAsync();
        if (!Plus.Active) return;
        await Task.WhenAll(Plus.LoadRatingsAsync(), _list.Items is null ? _list.LoadAsync() : Task.CompletedTask);
        await Plus.LoadPostersAsync(_posterLookup);
    }

    protected override void Build(StackPanel column)
    {
        if (!Plus.StatusKnown)
        {
            column.Children.Add(Ui.Note("Checking your Arc TV Plus status…"));
            return;
        }

        if (!Plus.Active)
        {
            column.Children.Add(Locked("Recommendations are only for Arc TV Plus.", "With Plus, Arc TV learns what you like and fills a Picked for you row on Home.", _seePlus));
            return;
        }

        var summary = RecommendationSummary.Of(Plus.Ratings.Where(p => p.Value.Rating is not null).ToDictionary(p => p.Key, p => p.Value.Rating!.Value), _list.Items ?? []);
        column.Children.Add(Ui.Note("See what shapes your Picked for you row, and fine-tune what you see."));
        column.Children.Add(Rated(summary));
        column.Children.Add(Shapes(summary));
        column.Children.Add(HowItWorks());
        column.Children.Add(Reset());
    }

    private UIElement Rated(RecommendationSummary summary)
    {
        var panel = new StackPanel { Margin = new Thickness(0, 0, 0, 12) };
        var tabs = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 0, 0, 12) };
        Button Tab(string text, Rating rating)
        {
            var button = new Button { Content = text, Style = (Style)Application.Current.FindResource("ChoicePill"), Height = 38, Margin = new Thickness(0, 0, 10, 0), Tag = _tab == rating ? "selected" : "" };
            button.Click += (_, _) =>
            {
                _tab = rating;
                Settings.Redraw();
            };
            return button;
        }

        tabs.Children.Add(Tab($"Liked ({summary.Likes})", Rating.Like));
        tabs.Children.Add(Tab($"Not for me ({summary.Dislikes})", Rating.Dislike));
        panel.Children.Add(tabs);

        var shown = Plus.Ratings.Values.Where(i => i.Rating == _tab).OrderByDescending(i => i.UpdatedAt, StringComparer.Ordinal).ToList();
        if (shown.Count == 0)
        {
            panel.Children.Add(new TextBlock
            {
                Text = _tab == Rating.Like ? "Nothing liked yet. Choose Like on any movie or show and it shows up here." : "Nothing marked Not for me yet. Those titles, and ones like them, are left out of your picks.",
                FontSize = 13, Foreground = Ui.Res("Text3Brush"), TextWrapping = TextWrapping.Wrap,
            });
            return panel;
        }

        // a row of posters you can scroll sideways (arrows at its ends), each with a small cross to take the rating off
        var strip = new StackPanel { Orientation = Orientation.Horizontal };
        foreach (var item in shown)
        {
            var cell = new Grid();
            cell.Children.Add(new ContentControl { Content = new MetaPreview(item.ContentId, item.IsMovie ? "movie" : "series", item.Title, Plus.Posters.GetValueOrDefault(item.ContentId)) });
            var remove = new Border
            {
                Width = 30, Height = 30, CornerRadius = new CornerRadius(15), Background = new SolidColorBrush(Color.FromArgb(0xC8, 0, 0, 0)), Cursor = System.Windows.Input.Cursors.Hand,
                HorizontalAlignment = HorizontalAlignment.Left, VerticalAlignment = VerticalAlignment.Top, Margin = new Thickness(10, 20, 0, 0), ToolTip = "Take this off your list",
                Child = new TextBlock { Text = "\uE711", FontFamily = new FontFamily("Segoe MDL2 Assets"), FontSize = 12, Foreground = Brushes.White, HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center },
            };
            var captured = item;
            remove.MouseLeftButtonUp += (_, e) =>
            {
                e.Handled = true;
                _ = Plus.RemoveRatingAsync(captured);
            };
            cell.Children.Add(remove);
            strip.Children.Add(cell);
        }

        panel.Children.Add(new ArrowScrollViewer { HorizontalScrollBarVisibility = ScrollBarVisibility.Auto, VerticalScrollBarVisibility = ScrollBarVisibility.Disabled, ArrowCenter = 190, Content = strip });
        return panel;
    }

    private static UIElement Shapes(RecommendationSummary s)
    {
        var panel = new StackPanel();
        panel.Children.Add(new TextBlock { Text = "What shapes your picks", FontSize = 16, FontWeight = FontWeights.Bold, Margin = new Thickness(0, 0, 0, 10) });
        void Row(string label, string points)
        {
            var grid = new Grid { Margin = new Thickness(0, 0, 0, 8) };
            grid.ColumnDefinitions.Add(new ColumnDefinition());
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            grid.Children.Add(new TextBlock { Text = label, FontSize = 14, Foreground = Ui.Res("Text2Brush"), TextWrapping = TextWrapping.Wrap });
            var value = new TextBlock { Text = points, FontSize = 14, FontWeight = FontWeights.Bold, Margin = new Thickness(12, 0, 0, 0) };
            Grid.SetColumn(value, 1);
            grid.Children.Add(value);
            panel.Children.Add(grid);
        }

        Row($"Titles you like ({s.Likes})", RecommendationSummary.PointsLabel(s.LikePoints));
        Row($"Titles marked Not for me ({s.Dislikes})", RecommendationSummary.PointsLabel(s.DislikePoints));
        Row($"Your My List and finished titles ({s.Finished} finished, {s.Saved} saved)", RecommendationSummary.PointsLabel(s.ListPoints));
        void Small(string text, bool accent = false) => panel.Children.Add(new TextBlock { Text = text, FontSize = 12, Foreground = accent ? Ui.Res("AccentBrush") : Ui.Res("Text3Brush"), TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 4, 0, 0) });
        Small("Movies count as finished when you watch them to the end; mark a show as watched yourself. Points are how much each title counts toward your taste. They are not a score.");
        if (s.OverLimit) Small($"Only your {RecommendConfig.InteractionDetailFetchLimit} strongest ratings and saves are used for your picks right now.");
        if (!s.Ready)
            Small($"Not enough yet: your picks start once you have at least {RecommendConfig.MinInteractionsForPersonalisation} titles you like, finished, saved or marked Not for me, and at least one of them liked, finished or saved. Until then Home shows popular titles.", true);
        return Ui.Card(panel, 18);
    }

    private static UIElement HowItWorks()
    {
        var panel = new StackPanel();
        panel.Children.Add(new TextBlock { Text = "How it works", FontSize = 16, FontWeight = FontWeights.Bold, Margin = new Thickness(0, 0, 0, 10) });
        var steps = new (string Title, string Detail)[]
        {
            ("It learns your taste.", "Every movie or show you like, finish or save adds points toward the genres, directors and cast you enjoy. A Like counts most, and Not for me counts against."),
            ("It scores what is on offer.", "Titles from your Home rows are compared with your taste, mostly on genre, then on director and cast. Titles you have already finished, rated or are watching are left out."),
            ("It shows a spread.", "Your strongest matches stay, and the rest of the row follows your mix of tastes, so a smaller taste still gets its share."),
            ("It changes when you refresh.", "Most of the row is different next time, and no single title of yours can explain too many picks."),
            ("It explains itself.", "The line under most picks names the title behind it. A few say \"More from directors you enjoy\" instead, and with no real match no reason is shown."),
        };
        for (var i = 0; i < steps.Length; i++)
        {
            var row = new Grid { Margin = new Thickness(0, 0, 0, 10) };
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(34) });
            row.ColumnDefinitions.Add(new ColumnDefinition());
            row.Children.Add(new Border
            {
                Width = 22, Height = 22, CornerRadius = new CornerRadius(11), Background = Ui.Res("BrandGradient"), VerticalAlignment = VerticalAlignment.Top, HorizontalAlignment = HorizontalAlignment.Left,
                Child = new TextBlock { Text = (i + 1).ToString(), FontSize = 12, FontWeight = FontWeights.ExtraBold, Foreground = Brushes.Black, HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center },
            });
            var text = new StackPanel();
            text.Children.Add(new TextBlock { Text = steps[i].Title, FontSize = 14, FontWeight = FontWeights.SemiBold });
            text.Children.Add(new TextBlock { Text = steps[i].Detail, FontSize = 13, Foreground = Ui.Res("Text2Brush"), TextWrapping = TextWrapping.Wrap });
            Grid.SetColumn(text, 1);
            row.Children.Add(text);
            panel.Children.Add(row);
        }

        return Ui.Card(panel, 18);
    }

    private UIElement Reset()
    {
        var panel = new StackPanel();
        panel.Children.Add(new TextBlock { Text = "Reset preferences", FontSize = 16, FontWeight = FontWeights.Bold, Margin = new Thickness(0, 0, 0, 8) });
        panel.Children.Add(new TextBlock
        {
            Text = $"Clears every Like and Not for me ({Plus.Ratings.Count} now). Your picks still use your My List and finished titles, and a title you removed from the row stays out for {RecommendConfig.PickedDismissDays} days.",
            FontSize = 13, Foreground = Ui.Res("Text2Brush"), TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 0, 0, 12),
        });
        var row = new StackPanel { Orientation = Orientation.Horizontal };
        if (_confirmingReset)
        {
            row.Children.Add(new TextBlock { Text = "Every Like and Not for me is cleared, on every device.", FontSize = 12, Foreground = Ui.Res("Text2Brush"), VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 12, 0) });
            var yes = new Button { Content = "Reset", Style = (Style)Application.Current.FindResource("LinkButton"), Foreground = Ui.Pink, Margin = new Thickness(0, 0, 12, 0) };
            yes.Click += async (_, _) =>
            {
                _confirmingReset = false;
                await Plus.ResetRatingsAsync();
                Settings.Redraw();
            };
            var no = new Button { Content = "Cancel", Style = (Style)Application.Current.FindResource("LinkButton"), Foreground = Ui.Res("Text2Brush") };
            no.Click += (_, _) =>
            {
                _confirmingReset = false;
                Settings.Redraw();
            };
            row.Children.Add(yes);
            row.Children.Add(no);
        }
        else
        {
            var reset = Ui.Act("Reset preferences", () =>
            {
                _confirmingReset = true;
                Settings.Redraw();
            });
            reset.IsEnabled = Plus.Ratings.Count > 0;
            row.Children.Add(reset);
        }

        panel.Children.Add(row);
        return Ui.Card(panel, 18);
    }
}

/// <summary>Settings > Your stats: how much you watch, how this week compares, your streak, a map of the last 13 weeks and your busiest day.</summary>
internal sealed class StatsPane(SettingsModel settings, PlusModel plus, Action seePlus) : PlusPane(settings, plus)
{
    private static readonly Color Cyan = Color.FromRgb(0x19, 0xE6, 0xFF);
    private static readonly Color Violet = Color.FromRgb(0x9B, 0x5C, 0xFF);

    protected override async Task ShownAsync()
    {
        await Plus.LoadStatusAsync();
        if (Plus.Active && Plus.Stats is null) await Plus.LoadStatsAsync();
    }

    protected override void Build(StackPanel column)
    {
        if (!Plus.StatusKnown)
        {
            column.Children.Add(Ui.Note("Checking your Arc TV Plus status…"));
            return;
        }

        if (!Plus.Active)
        {
            column.Children.Add(Locked("Your stats are only for Arc TV Plus.", "See how much you watch, how this week compares, your streak, a map of your last 13 weeks and your busiest day.", seePlus));
            return;
        }

        if (Plus.Stats is not { } stats)
        {
            column.Children.Add(Ui.Note(Plus.StatsFailed ? "Couldn't read your watch history right now. Try again in a moment." : "Reading your watch history…"));
            return;
        }

        if (stats.IsEmpty)
        {
            column.Children.Add(Ui.Note("Nothing here yet. Watch something and your stats will appear."));
            return;
        }

        column.Children.Add(Hero(stats));
        var tiles = new WrapPanel { Margin = new Thickness(0, 0, 0, 6) };
        string Plural(int n, string word) => $"{n} {word}{(n == 1 ? "" : "s")}";
        void Tile(string label, string value, string? note = null, bool hot = false)
        {
            var panel = new StackPanel();
            panel.Children.Add(new TextBlock { Text = label, FontSize = 12, Foreground = Ui.Res("Text3Brush") });
            panel.Children.Add(new TextBlock { Text = value, FontSize = 19, FontWeight = FontWeights.Bold, Margin = new Thickness(0, 4, 0, 0) });
            if (note is not null) panel.Children.Add(new TextBlock { Text = note, FontSize = 12, Foreground = Ui.Res("Text3Brush"), Margin = new Thickness(0, 4, 0, 0) });
            var cell = Surface(panel, 12);
            cell.Width = 200;
            cell.Margin = new Thickness(0, 0, 10, 10);
            if (hot) cell.BorderBrush = new SolidColorBrush(Color.FromArgb(0x8C, 0xFF, 0x9F, 0x43));
            if (hot) cell.BorderThickness = new Thickness(1);
            tiles.Children.Add(cell);
        }

        Tile("Last 7 days", WatchStats.Duration(stats.Last7DaysMs));
        Tile("Last 30 days", WatchStats.Duration(stats.Last30DaysMs));
        Tile("Day streak", Plural(stats.StreakDays, "day"),
            stats.LongestStreakDays > stats.StreakDays ? $"best {stats.LongestStreakDays}" : stats.StreakDays > 0 && stats.StreakDays == stats.LongestStreakDays ? "your best" : null, stats.StreakDays >= 3);
        Tile("Per day you watch", WatchStats.Duration(stats.AvgPerActiveDayMs), $"{Plural(stats.ActiveDays, "day")} in all");
        Tile("Movies finished", stats.MoviesFinished.ToString());
        Tile("Episodes watched", stats.EpisodesWatched.ToString(), stats.ShowsWatched > 0 ? $"across {Plural(stats.ShowsWatched, "show")}" : null);
        column.Children.Add(tiles);

        column.Children.Add(Split(stats));
        column.Children.Add(Heat(stats));
        column.Children.Add(Weekdays(stats));
        column.Children.Add(new TextBlock
        {
            Text = $"Counted from your watch history{(Plus.StatsTruncated ? " (the most recent part of it)" : "")}. A title you watch again counts once, on the day you last watched it.",
            FontSize = 12, Foreground = Ui.Res("Text3Brush"), TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 12, 0, 0),
        });
    }

    private static UIElement Hero(WatchStats stats)
    {
        var panel = new StackPanel();
        panel.Children.Add(new TextBlock { Text = "You've watched", FontSize = 13, FontWeight = FontWeights.Medium, Foreground = Ui.Res("Text3Brush") });
        panel.Children.Add(new TextBlock { Text = WatchStats.Duration(stats.TotalMs), FontSize = 44, FontWeight = FontWeights.ExtraBold, Foreground = Ui.Res("PillGradient"), Margin = new Thickness(0, 4, 0, 4) });
        var line = new WrapPanel();
        if (stats.FirstWatchedAt is { } first)
            line.Children.Add(new TextBlock { Text = $"since {first.ToLocalTime().ToString("MMMM yyyy", System.Globalization.CultureInfo.CurrentCulture)}", FontSize = 13, Foreground = Ui.Res("Text2Brush"), Margin = new Thickness(0, 0, 12, 0), VerticalAlignment = VerticalAlignment.Center });
        if (WatchStats.WeekChange(stats.Last7DaysMs, stats.Prev7DaysMs) is { } change)
        {
            var text = change.Direction == "same" ? "Same as last week" : $"{change.Percent}% {(change.Direction == "up" ? "more" : "less")} than last week";
            var color = change.Direction == "up" ? Cyan : change.Direction == "down" ? Color.FromRgb(0xFF, 0xB8, 0x6B) : Color.FromRgb(0xA0, 0xA0, 0xAA);
            line.Children.Add(new Border
            {
                Background = Ui.Res("SurfaceHighBrush"), CornerRadius = new CornerRadius(12), Padding = new Thickness(10, 3, 10, 3),
                Child = new TextBlock { Text = text, FontSize = 12, FontWeight = FontWeights.Bold, Foreground = new SolidColorBrush(color) },
            });
        }

        panel.Children.Add(line);
        return Ui.Card(panel, 20);
    }

    private static UIElement Split(WatchStats stats)
    {
        var total = stats.MovieMs + stats.ShowMs;
        var panel = new StackPanel { Margin = new Thickness(0, 10, 0, 0) };
        if (total <= 0) return panel;
        var moviePercent = (int)Math.Round((double)stats.MovieMs / total * 100, MidpointRounding.AwayFromZero);
        panel.Children.Add(new TextBlock { Text = "Movies or shows", FontSize = 14, FontWeight = FontWeights.SemiBold, Margin = new Thickness(0, 0, 0, 8) });
        var bar = new Grid { Height = 10 };
        bar.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(Math.Max(moviePercent, 0.0001), GridUnitType.Star) });
        bar.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(Math.Max(100 - moviePercent, 0.0001), GridUnitType.Star) });
        bar.Children.Add(new Border { Background = new SolidColorBrush(Cyan), CornerRadius = new CornerRadius(5, 0, 0, 5) });
        var shows = new Border { Background = new SolidColorBrush(Violet), CornerRadius = new CornerRadius(0, 5, 5, 0) };
        Grid.SetColumn(shows, 1);
        bar.Children.Add(shows);
        panel.Children.Add(bar);
        var legend = new Grid { Margin = new Thickness(0, 8, 0, 0) };
        legend.ColumnDefinitions.Add(new ColumnDefinition());
        legend.ColumnDefinitions.Add(new ColumnDefinition());
        legend.Children.Add(new TextBlock { Text = $"● Movies {WatchStats.Duration(stats.MovieMs)} · {moviePercent}%", FontSize = 13, Foreground = new SolidColorBrush(Cyan) });
        var right = new TextBlock { Text = $"● Shows {WatchStats.Duration(stats.ShowMs)} · {100 - moviePercent}%", FontSize = 13, Foreground = new SolidColorBrush(Violet), HorizontalAlignment = HorizontalAlignment.Right };
        Grid.SetColumn(right, 1);
        legend.Children.Add(right);
        panel.Children.Add(legend);
        return panel;
    }

    private static Brush LevelBrush(int level) => level switch
    {
        0 => Ui.Res("SurfaceHighBrush"),
        1 => new SolidColorBrush(Color.FromArgb(0x47, Cyan.R, Cyan.G, Cyan.B)),
        2 => new SolidColorBrush(Color.FromArgb(0x85, Cyan.R, Cyan.G, Cyan.B)),
        _ => new SolidColorBrush(Cyan),
    };

    private static UIElement Heat(WatchStats stats)
    {
        var cells = WatchStats.HeatCells(stats.DailyMs, DateTime.Now);
        var panel = new StackPanel { Margin = new Thickness(0, 18, 0, 0) };
        panel.Children.Add(new TextBlock { Text = "Last 13 weeks", FontSize = 14, FontWeight = FontWeights.SemiBold, Margin = new Thickness(0, 0, 0, 8) });
        var row = new StackPanel { Orientation = Orientation.Horizontal };
        var labels = new StackPanel { Margin = new Thickness(0, 0, 8, 0) };
        foreach (var name in new[] { "Mon", "", "Wed", "", "Fri", "", "Sun" })
            labels.Children.Add(new TextBlock { Text = name, FontSize = 10, Foreground = Ui.Res("Text3Brush"), Height = 18, Margin = new Thickness(0, 2, 0, 2), VerticalAlignment = VerticalAlignment.Center });
        row.Children.Add(labels);
        for (var week = 0; week < (cells.Count + 6) / 7; week++)
        {
            var col = new StackPanel { Margin = new Thickness(0, 0, 4, 0) };
            for (var day = 0; day < 7; day++)
            {
                var index = week * 7 + day;
                var box = new Border { Width = 18, Height = 18, CornerRadius = new CornerRadius(4), Margin = new Thickness(0, 2, 0, 2) };
                if (index < cells.Count && cells[index].Ms is { } ms)
                {
                    box.Background = LevelBrush(cells[index].Level);
                    box.ToolTip = $"{cells[index].Label}: {(ms > 0 ? WatchStats.Duration(ms) : "nothing watched")}";
                }

                col.Children.Add(box);
            }

            row.Children.Add(col);
        }

        panel.Children.Add(row);
        var legend = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right, Margin = new Thickness(0, 6, 0, 0) };
        legend.Children.Add(new TextBlock { Text = "Less", FontSize = 11, Foreground = Ui.Res("Text3Brush"), Margin = new Thickness(0, 0, 4, 0) });
        for (var level = 0; level < 5; level++) legend.Children.Add(new Border { Width = 12, Height = 12, CornerRadius = new CornerRadius(3), Background = LevelBrush(level), Margin = new Thickness(0, 0, 4, 0) });
        legend.Children.Add(new TextBlock { Text = "More", FontSize = 11, Foreground = Ui.Res("Text3Brush") });
        panel.Children.Add(legend);
        return panel;
    }

    private static UIElement Weekdays(WatchStats stats)
    {
        var peak = Math.Max(stats.ByWeekdayMs.Max(), 1);
        var panel = new StackPanel { Margin = new Thickness(0, 18, 0, 0) };
        panel.Children.Add(new TextBlock { Text = $"Busiest day{(stats.BusiestWeekday is { } d ? $": {WatchStats.Weekdays[d]}" : "")}", FontSize = 14, FontWeight = FontWeights.SemiBold, Margin = new Thickness(0, 0, 0, 10) });
        var bars = new Grid();
        for (var i = 0; i < 7; i++)
        {
            bars.ColumnDefinitions.Add(new ColumnDefinition());
            var col = new StackPanel { HorizontalAlignment = HorizontalAlignment.Center };
            col.Children.Add(new TextBlock { Text = WatchStats.Short(stats.ByWeekdayMs[i]), FontSize = 11, FontWeight = i == stats.BusiestWeekday ? FontWeights.Bold : FontWeights.Normal, Foreground = new SolidColorBrush(Cyan), Height = 16, HorizontalAlignment = HorizontalAlignment.Center });
            var track = new Grid { Height = 100, Width = 34 };
            track.Children.Add(new Border
            {
                Height = Math.Max(4, 100.0 * stats.ByWeekdayMs[i] / peak), VerticalAlignment = VerticalAlignment.Bottom, CornerRadius = new CornerRadius(6), Opacity = 0.85, Background = Ui.Res("PillGradient"),
            });
            col.Children.Add(track);
            col.Children.Add(new TextBlock { Text = WatchStats.Weekdays[i][..3], FontSize = 11, Foreground = Ui.Res("Text2Brush"), HorizontalAlignment = HorizontalAlignment.Center, Margin = new Thickness(0, 4, 0, 0) });
            Grid.SetColumn(col, i);
            bars.Children.Add(col);
        }

        panel.Children.Add(bars);
        return panel;
    }
}
