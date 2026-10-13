namespace ArcTV.Core.Settings;

/// <summary>A language the person can pick for subtitles or audio. <see cref="Code"/> is what the account stores (and what the player is told); null means "decide for me".</summary>
public sealed record LanguageChoice(string? Code, string Name);

public static class LanguageChoices
{
    /// <summary>The same list the web app offers, in the same order: the system choice, then English, then every other language A to Z.</summary>
    public static IReadOnlyList<LanguageChoice> Subtitles(string systemName = "System Default") =>
        [new LanguageChoice(null, systemName), .. All.Where(l => l.Code == "en"), .. All.Where(l => l.Code != "en").OrderBy(l => l.Name, StringComparer.CurrentCultureIgnoreCase)];

    public static IReadOnlyList<LanguageChoice> Audio() => [new LanguageChoice(null, "Automatic"), .. All];

    public static readonly IReadOnlyList<LanguageChoice> All =
    [
        new("en", "English"), new("es", "Spanish"), new("fr", "French"), new("de", "German"), new("it", "Italian"), new("pt", "Portuguese"), new("nl", "Dutch"),
        new("sv", "Swedish"), new("pl", "Polish"), new("tr", "Turkish"), new("ar", "Arabic"), new("hi", "Hindi"), new("ja", "Japanese"), new("ko", "Korean"),
        new("zh", "Chinese"), new("sr", "Serbian"), new("hr", "Croatian"), new("bs", "Bosnian"), new("mk", "Macedonian"),
    ];
}

/// <summary>Moving a row in Settings > Home Rows. A port of the web app's <c>moveRow</c>.</summary>
public static class HomeRowOrder
{
    /// <summary>The full order after moving <paramref name="id"/> one place up (<paramref name="delta"/> -1) or down (+1); unchanged if it can't move.</summary>
    public static List<string> Move(IReadOnlyList<string> order, string id, int delta)
    {
        var next = order.ToList();
        var index = next.IndexOf(id);
        var target = index + delta;
        if (index < 0 || target < 0 || target >= next.Count) return next;
        (next[index], next[target]) = (next[target], next[index]);
        return next;
    }
}

/// <summary>How big subtitles are drawn. <see cref="MpvFontSize"/> is mpv's <c>sub-font-size</c> (pixels at a 720-high picture; its default is 55).</summary>
public enum SubtitleSize { Small, Medium, Large, ExtraLarge }

public enum SubtitleColor { White, Yellow, Cyan, Green }

public static class SubtitleStyle
{
    public static string Label(this SubtitleSize size) => size switch { SubtitleSize.Small => "Small", SubtitleSize.Medium => "Medium", SubtitleSize.Large => "Large", _ => "Extra large" };

    public static int MpvFontSize(this SubtitleSize size) => size switch { SubtitleSize.Small => 40, SubtitleSize.Medium => 55, SubtitleSize.Large => 75, _ => 100 };

    /// <summary>The share of the picture's height a line of text is drawn at, so a preview matches playback.</summary>
    public static double Fraction(this SubtitleSize size) => size.MpvFontSize() / 720.0;

    public static string Label(this SubtitleColor color) => color switch { SubtitleColor.White => "White", SubtitleColor.Yellow => "Yellow", SubtitleColor.Cyan => "Cyan", _ => "Green" };

    /// <summary>What mpv's <c>sub-color</c> takes.</summary>
    public static string Hex(this SubtitleColor color) => color switch { SubtitleColor.White => "#FFFFFF", SubtitleColor.Yellow => "#FFFF00", SubtitleColor.Cyan => "#00FFFF", _ => "#00FF00" };

    public static (byte R, byte G, byte B) Rgb(this SubtitleColor color) => color switch
    {
        SubtitleColor.White => (255, 255, 255), SubtitleColor.Yellow => (255, 255, 0), SubtitleColor.Cyan => (0, 255, 255), _ => (0, 255, 0),
    };

    public static SubtitleSize SizeFrom(string? stored) => Enum.TryParse<SubtitleSize>(stored, true, out var size) ? size : SubtitleSize.Medium;

    public static SubtitleColor ColorFrom(string? stored) => Enum.TryParse<SubtitleColor>(stored, true, out var color) ? color : SubtitleColor.White;
}
