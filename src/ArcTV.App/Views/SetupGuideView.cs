using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using ArcTV.App.Controls;

namespace ArcTV.App.Views;

/// <summary>
/// "How to set up your sources": a beginner's guide (a debrid service, then Torrentio, then Arc TV), the same as the Mac and web apps', opened from the page that says no sources were found.
/// </summary>
public sealed class SetupGuideView : ScrollViewer
{
    private static readonly SolidColorBrush Amber = new(Color.FromRgb(0xFF, 0xB0, 0x20));

    public SetupGuideView(Action back, Action openAddons)
    {
        VerticalScrollBarVisibility = ScrollBarVisibility.Auto;
        var page = new StackPanel { MaxWidth = 760, HorizontalAlignment = HorizontalAlignment.Center, Margin = new Thickness(44, 132, 44, 60) };

        var backButton = new Button { Content = "‹  Back", Style = Ui.StyleOf("ChoicePill"), HorizontalAlignment = HorizontalAlignment.Left };
        backButton.Click += (_, _) => back();
        page.Children.Add(backButton);
        page.Children.Add(new TextBlock { Text = "How to set up your sources", FontSize = 34, FontWeight = FontWeights.ExtraBold, Foreground = Ui.Res("PillGradient"), Margin = new Thickness(0, 22, 0, 12), TextWrapping = TextWrapping.Wrap });
        page.Children.Add(Paragraph("Arc TV is a player. It shows you movies and shows, but it needs somewhere to get the video from. This guide shows you how to connect that, one small step at a time. You don't need to be good with computers.", 16));

        page.Children.Add(Box("The short version",
            Numbered("Debrid service:", "a small subscription that gives you fast, ready-to-play links.", 1),
            Numbered("Torrentio:", "a free addon that finds those links. You connect it to your debrid service.", 2),
            Numbered("Arc TV:", "you paste one link into the app, and you're done.", 3),
            Paragraph("A plan is very cheap. Plans usually cost only a few dollars a month, and you can stop any time. You only need one service. The price is on the service's own website, so check there for today's plans.")));

        page.Children.Add(Part("Part 1 of 3", "Get a debrid service",
            "A debrid service is a website you pay a small amount to each month. It does the heavy lifting and gives Arc TV a link that plays straight away. The two most popular are Real-Debrid and TorBox. Pick one. You don't need both.",
            Step(1, "Make an account", Paragraph("Go to the website of the one you chose and sign up with your email."), Row(Site("real-debrid.com", "https://real-debrid.com"), Paragraph("or"), Site("torbox.app", "https://torbox.app"))),
            Step(2, "Buy a plan", Paragraph("Pick the cheapest plan to start. It's very cheap, and the lowest one is all you need. The website will walk you through paying.")),
            Step(3, "Find your API key",
                Paragraph("An API key is just a long password made of letters and numbers. It lets Torrentio use the plan you paid for. You will copy it in a moment."),
                Bullet("Real-Debrid: while signed in, open real-debrid.com/apitoken and copy the long code you see."),
                Bullet("TorBox: sign in, open Settings, and copy your API key.")),
            Step(4, "Keep it safe", Paragraph("Leave the page open, or paste the key into a note for the next part.")),
            Warning("Never share your API key. Anyone with it can use the plan you paid for. If it ever leaks, make a new one on the service's website.")));

        page.Children.Add(Part("Part 2 of 3", "Connect it to Torrentio",
            "Torrentio is a free addon that searches for your movies and shows. Here you tell it about your debrid service. At the end it gives you one special link, which you will use in Part 3.",
            Step(1, "Open the Torrentio setup page", Site("torrentio.strem.fun/configure", "https://torrentio.strem.fun/configure")),
            Step(2, "Choose your debrid service", Paragraph("Scroll down to the box that says Debrid provider. Click it and choose the service you signed up for (RealDebrid or TorBox).")),
            Step(3, "Paste your API key", Paragraph("A new box appears. Paste the API key from Part 1 into it. With Real-Debrid there may also be a Continue to Real-Debrid button. You can use it to sign in and it will fill the key in for you.")),
            Step(4, "Leave everything else alone", Paragraph("The other settings are optional and the defaults work well. If you're not sure, skip it.")),
            Step(5, "Copy your link",
                Paragraph("Scroll to the very bottom of the page. You'll see a button called Install. Don't click Install. It's built for a different app. Copy the link instead: right-click the Install button and choose Copy Link."),
                Paragraph("The link looks something like https://torrentio.strem.fun/realdebrid=…/manifest.json. It may start with stremio://, and Arc TV accepts that too.")),
            Warning("This link has your API key inside it. Only paste it into Arc TV. Don't post it or send it to anyone.")));

        var addons = new Button { Content = "Go to Settings > Addons", Style = Ui.StyleOf("PrimaryButton"), Height = 42, Padding = new Thickness(22, 0, 22, 0), HorizontalAlignment = HorizontalAlignment.Left };
        addons.Click += (_, _) => openAddons();
        page.Children.Add(Part("Part 3 of 3", "Add Torrentio to Arc TV", "Last part. You paste the link you copied and Arc TV does the rest.",
            Step(1, "Open the Addons page", addons),
            Step(2, "Paste your link", Paragraph("Click the box under Add an addon, paste the link from Part 2, and press Add.")),
            Step(3, "Wait for the confirmation", Paragraph("After a moment you'll see that Torrentio was added. It now shows in your addon list.")),
            Step(4, "Play something", Paragraph("Open any movie or show and press Play. The list of sources shows; pick one. (The right-click menu on a poster has Choose source too.)")),
            new TextBlock { Text = "That's it, you're done. Your addons are saved to your account, so they also show up on your other devices once you sign in to Arc TV.", FontSize = 15, FontWeight = FontWeights.SemiBold, TextWrapping = TextWrapping.Wrap }));

        var faq = new StackPanel { Margin = new Thickness(0, 34, 0, 0) };
        faq.Children.Add(new TextBlock { Text = "Something went wrong?", FontSize = 24, FontWeight = FontWeights.Bold, Margin = new Thickness(0, 0, 0, 14) });
        faq.Children.Add(Faq("It says \"Couldn't install that addon\"", "The link was probably cut short. It must end in /manifest.json. Go back to the Torrentio page and copy it again."));
        faq.Children.Add(Faq("There are no sources, or a source won't play", "Check that your debrid plan is still active and you used the right API key. If you made a new key, repeat Part 2 to get a new link and replace the old addon."));
        faq.Children.Add(Faq("I want to change my debrid service or key", "In Settings > Addons, remove Torrentio. Then do Part 2 and Part 3 again with the new details."));
        faq.Children.Add(Faq("One title has no sources", "Not every title is available everywhere. Try a different source in the list, or a different title."));
        page.Children.Add(faq);

        Content = page;
    }

    // ---- pieces ----

    private static TextBlock Paragraph(string text, double size = 15) =>
        new() { Text = text, FontSize = size, Foreground = Ui.Res("Text2Brush"), TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 0, 0, 8) };

    private static UIElement Numbered(string lead, string text, int number)
    {
        var line = new TextBlock { FontSize = 15, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 0, 0, 8) };
        line.Inlines.Add(new System.Windows.Documents.Run($"{number}. ") { Foreground = Ui.Res("AccentBrush") });
        line.Inlines.Add(new System.Windows.Documents.Run(lead + " ") { FontWeight = FontWeights.Bold });
        line.Inlines.Add(new System.Windows.Documents.Run(text) { Foreground = Ui.Res("Text2Brush") });
        return line;
    }

    private static UIElement Bullet(string text)
    {
        var row = new Grid();
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(18) });
        row.ColumnDefinitions.Add(new ColumnDefinition());
        row.Children.Add(new TextBlock { Text = "•", Foreground = Ui.Res("Text3Brush"), FontSize = 15 });
        var words = Paragraph(text);
        Grid.SetColumn(words, 1);
        row.Children.Add(words);
        return row;
    }

    private static UIElement Row(params UIElement[] items)
    {
        var row = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 0, 0, 8) };
        foreach (var item in items)
        {
            if (item is FrameworkElement element) element.Margin = new Thickness(0, 0, 8, 0);
            row.Children.Add(item);
        }

        return row;
    }

    private static UIElement Site(string label, string address)
    {
        var link = new Button { Content = label, Style = Ui.StyleOf("LinkButton"), FontSize = 15, FontWeight = FontWeights.SemiBold, ToolTip = address };
        link.Click += (_, _) => WebLinks.Open(address);
        return link;
    }

    private static UIElement Warning(string text) => new Border
    {
        Margin = new Thickness(0, 6, 0, 0), Padding = new Thickness(14), CornerRadius = new CornerRadius(12), Background = new SolidColorBrush(Color.FromArgb(0x1F, 0xFF, 0xB0, 0x20)),
        BorderBrush = new SolidColorBrush(Color.FromArgb(0x80, 0xFF, 0xB0, 0x20)), BorderThickness = new Thickness(1),
        Child = new TextBlock { Text = text, FontSize = 14, TextWrapping = TextWrapping.Wrap },
    };

    private static UIElement Faq(string question, string answer)
    {
        var stack = new StackPanel { Margin = new Thickness(0, 0, 0, 14) };
        stack.Children.Add(new TextBlock { Text = question, FontSize = 16, FontWeight = FontWeights.SemiBold, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 0, 0, 4) });
        stack.Children.Add(Paragraph(answer));
        return stack;
    }

    private static UIElement Box(string title, params UIElement[] content)
    {
        var stack = new StackPanel();
        stack.Children.Add(new TextBlock { Text = title, FontSize = 20, FontWeight = FontWeights.Bold, Margin = new Thickness(0, 0, 0, 10) });
        foreach (var item in content) stack.Children.Add(item);
        return new Border { Margin = new Thickness(0, 22, 0, 0), Padding = new Thickness(20), CornerRadius = new CornerRadius(14), Background = Ui.Res("BgElevatedBrush"), BorderBrush = Ui.Line, BorderThickness = new Thickness(1), Child = stack };
    }

    private static UIElement Part(string label, string title, string intro, params UIElement[] content)
    {
        var stack = new StackPanel { Margin = new Thickness(0, 34, 0, 0) };
        stack.Children.Add(new TextBlock { Text = label.ToUpperInvariant(), FontSize = 12, FontWeight = FontWeights.Bold, Foreground = Ui.Res("AccentBrush") });
        stack.Children.Add(new TextBlock { Text = title, FontSize = 26, FontWeight = FontWeights.ExtraBold, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 4, 0, 10) });
        stack.Children.Add(Paragraph(intro));
        foreach (var item in content) stack.Children.Add(item);
        return stack;
    }

    private static UIElement Step(int number, string title, params UIElement[] content)
    {
        var row = new Grid { Margin = new Thickness(0, 12, 0, 0) };
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(50) });
        row.ColumnDefinitions.Add(new ColumnDefinition());
        row.Children.Add(new Border
        {
            Width = 34, Height = 34, CornerRadius = new CornerRadius(17), HorizontalAlignment = HorizontalAlignment.Left, VerticalAlignment = VerticalAlignment.Top, Background = Ui.Res("PillGradient"),
            Child = new TextBlock { Text = number.ToString(), FontSize = 16, FontWeight = FontWeights.ExtraBold, Foreground = new SolidColorBrush(Color.FromRgb(0x04, 0x16, 0x1A)), HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center },
        });
        var stack = new StackPanel();
        stack.Children.Add(new TextBlock { Text = title, FontSize = 17, FontWeight = FontWeights.Bold, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 5, 0, 8) });
        foreach (var item in content) stack.Children.Add(item);
        Grid.SetColumn(stack, 1);
        row.Children.Add(stack);
        return row;
    }
}
