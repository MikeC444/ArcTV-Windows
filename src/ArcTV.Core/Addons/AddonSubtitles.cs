using System.Text.Json;

namespace ArcTV.Core.Addons;

/// <summary>A subtitle file an addon offers for the video being played (the Stremio protocol's <c>/subtitles/{type}/{id}.json</c>).</summary>
public sealed record AddonSubtitle(string AddonName, string LanguageCode, string LanguageName, string Url)
{
    /// <summary>How the Subtitles menu names it: "English · Addon name".</summary>
    public string MenuTitle => $"{LanguageName} · {AddonName}";
}

/// <summary>What an addon wrote as a subtitle's language ("eng", "en", "pt-BR", "English") turned into something a language preference ("en") can be compared with, and into words.</summary>
public static class SubtitleLanguage
{
    // ISO 639-2 ("eng", "fre") to the two-letter codes a language preference uses; and the two-letter codes to their English names
    private static readonly Dictionary<string, string> ThreeToTwo = Pairs(
        "eng en,spa es,fra fr,fre fr,deu de,ger de,ita it,por pt,nld nl,dut nl,swe sv,pol pl,tur tr,ara ar,hin hi,jpn ja,kor ko,zho zh,chi zh,rus ru," +
        "ukr uk,ces cs,cze cs,slk sk,slo sk,hun hu,ron ro,rum ro,bul bg,ell el,gre el,heb he,fas fa,per fa,tha th,vie vi,ind id,msa ms,may ms,fin fi," +
        "dan da,nor no,nob nb,isl is,ice is,hrv hr,srp sr,bos bs,scc sr,scr hr,slv sl,lit lt,lav lv,est et,cat ca,eus eu,baq eu,glg gl,ben bn,tam ta,tel te,urd ur,fil tl,sqi sq,alb sq,mkd mk,mac mk,hye hy,arm hy,kat ka,geo ka");

    private static readonly Dictionary<string, string> Names = Pairs(
        "en English,es Spanish,fr French,de German,it Italian,pt Portuguese,nl Dutch,sv Swedish,pl Polish,tr Turkish,ar Arabic,hi Hindi,ja Japanese,ko Korean,zh Chinese,ru Russian," +
        "uk Ukrainian,cs Czech,sk Slovak,hu Hungarian,ro Romanian,bg Bulgarian,el Greek,he Hebrew,fa Persian,th Thai,vi Vietnamese,id Indonesian,ms Malay,fi Finnish," +
        "da Danish,no Norwegian,nb Norwegian Bokmål,is Icelandic,hr Croatian,sr Serbian,bs Bosnian,sl Slovenian,lt Lithuanian,lv Latvian,et Estonian,ca Catalan,eu Basque,gl Galician," +
        "bn Bengali,ta Tamil,te Telugu,ur Urdu,tl Filipino,sq Albanian,mk Macedonian,hy Armenian,ka Georgian");

    private static Dictionary<string, string> Pairs(string text) =>
        text.Split(',', StringSplitOptions.RemoveEmptyEntries).Select(pair => pair.Trim()).Select(pair => (Code: pair[..pair.IndexOf(' ')], Value: pair[(pair.IndexOf(' ') + 1)..])).ToDictionary(p => p.Code, p => p.Value);

    /// <summary>The two-letter code for whatever an addon sent: "eng", "en", "en-US", "pt-BR" and "English" all give one a language preference can be compared with; null when it can't be told.</summary>
    public static string? Key(string? raw)
    {
        var text = (raw ?? "").Trim().ToLowerInvariant();
        if (text.Length == 0) return null;
        var parts = text.Split(['-', '_'], StringSplitOptions.RemoveEmptyEntries);
        var basePart = parts.Length > 0 ? parts[0] : text;
        if (basePart.Length == 2) return basePart;
        if (ThreeToTwo.TryGetValue(basePart, out var two)) return two;
        // a written-out name ("english")
        return Names.FirstOrDefault(pair => string.Equals(pair.Value, text, StringComparison.OrdinalIgnoreCase)).Key;
    }

    /// <summary>"English" for "eng"; the code in capitals when the language isn't known, so the list never shows a blank.</summary>
    public static string DisplayName(string? raw)
    {
        if (Key(raw) is { } key && Names.TryGetValue(key, out var name)) return name;
        var text = (raw ?? "").Trim().ToUpperInvariant();
        return text.Length == 0 ? "Unknown" : text;
    }
}

public static class AddonSubtitles
{
    /// <summary>Reads one addon's answer (<c>{"subtitles":[{"id":..,"url":..,"lang":..}]}</c>): only files with a web address, one per address, each with a readable language. Never throws: an odd entry is skipped.</summary>
    public static List<AddonSubtitle> Parse(string json, string addonName)
    {
        var found = new List<AddonSubtitle>();
        var seen = new HashSet<string>();
        try
        {
            using var doc = JsonDocument.Parse(json);
            if (doc.RootElement.ValueKind != JsonValueKind.Object || !doc.RootElement.TryGetProperty("subtitles", out var list) || list.ValueKind != JsonValueKind.Array) return found;
            foreach (var entry in list.EnumerateArray())
            {
                if (entry.ValueKind != JsonValueKind.Object) continue;
                var url = Text(entry, "url").Trim();
                if (!(url.StartsWith("https://", StringComparison.OrdinalIgnoreCase) || url.StartsWith("http://", StringComparison.OrdinalIgnoreCase)) || !seen.Add(url)) continue;
                var lang = Text(entry, "lang").Trim();
                found.Add(new AddonSubtitle(addonName, lang, SubtitleLanguage.DisplayName(lang), url));
            }
        }
        catch (JsonException)
        {
            // an unreadable answer is no subtitles
        }

        return found;
    }

    private static string Text(JsonElement entry, string name) => entry.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.String ? value.GetString() ?? "" : "";

    /// <summary>
    /// Puts the person's preferred language first, then the rest by language name (keeping each addon's own order within one language), and keeps at most <paramref name="perLanguage"/>
    /// files per language and <paramref name="total"/> overall so the menu stays short.
    /// </summary>
    public static List<AddonSubtitle> Rank(IEnumerable<AddonSubtitle> found, string? preferredLanguage, int perLanguage = 5, int total = 60)
    {
        var preferred = SubtitleLanguage.Key(preferredLanguage);
        var order = new List<string>();
        var groups = new Dictionary<string, List<AddonSubtitle>>();
        foreach (var file in found)
        {
            var key = SubtitleLanguage.Key(file.LanguageCode) ?? file.LanguageName.ToLowerInvariant();
            if (!groups.TryGetValue(key, out var group))
            {
                groups[key] = group = [];
                order.Add(key);
            }

            group.Add(file);
        }

        return order
            .OrderBy(key => key == preferred ? 0 : 1)
            .ThenBy(key => groups[key][0].LanguageName, StringComparer.CurrentCultureIgnoreCase)
            .SelectMany(key => groups[key].Take(perLanguage))
            .Take(total)
            .ToList();
    }
}

public static class SubtitleFinder
{
    private static readonly TimeSpan AddonTimeout = TimeSpan.FromSeconds(8);

    /// <summary>
    /// Asks the enabled addons that offer subtitles which files they have for the video being played. Best effort: an addon that is slow, down or answers badly just adds nothing, and
    /// this never throws (except when cancelled), since a missing subtitle list must never get in the way of the video.
    /// </summary>
    public static async Task<IReadOnlyList<AddonSubtitle>> FindAsync(IEnumerable<SyncedAddon> addons, string type, string id, string? filename, string? preferredLanguage, AddonClient client, CancellationToken cancel = default)
    {
        var asked = addons.Where(a => a.Enabled && a.ManifestJson?.OffersSubtitles(type, id) == true).ToList();
        if (asked.Count == 0) return [];
        var answers = await Task.WhenAll(asked.Select(async addon =>
        {
            using var limit = CancellationTokenSource.CreateLinkedTokenSource(cancel);
            limit.CancelAfter(AddonTimeout);
            try
            {
                return await client.FetchSubtitlesAsync(addon.ManifestUrl, addon.Name, type, id, filename, limit.Token).ConfigureAwait(false);
            }
            catch (Exception) when (!cancel.IsCancellationRequested)
            {
                return [];
            }
        })).ConfigureAwait(false);
        cancel.ThrowIfCancellationRequested();
        return AddonSubtitles.Rank(answers.SelectMany(files => files), preferredLanguage);
    }
}
