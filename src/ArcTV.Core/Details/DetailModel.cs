using System.Globalization;
using ArcTV.Core.Addons;
using ArcTV.Core.Mvvm;

namespace ArcTV.Core.Details;

/// <summary>
/// What a title's page shows: the card's basics at once, then (when they arrive) the full details from Cinemeta: the cast and, for a show, its episodes.
/// Call it from the UI thread: it announces changes where it was called from.
/// </summary>
public sealed partial class DetailModel(MetaPreview preview, AddonClient? client = null, IDetailExtras? extras = null) : ObservableObject
{
    private readonly AddonClient _client = client ?? new AddonClient();
    private MetaDetail _detail = preview.AsDetail();
    private bool _loaded;
    private int? _season;
    private IReadOnlyList<CastMember>? _cast;
    private string? _trailerId;
    private string? _releaseDate;

    public MetaPreview Preview { get; } = preview;

    public MetaDetail Detail
    {
        get => _detail;
        private set
        {
            _detail = value;
            Raise();
            Raise(nameof(IsShow));
            Raise(nameof(Cast));
            Raise(nameof(Facts));
            Raise(nameof(CurrentSeason));
            Raise(nameof(Episodes));
        }
    }

    public bool IsShow => Detail.Type == "series";

    /// <summary>False until the full details have come back (or failed to), so placeholders can hold their places.</summary>
    public bool Loaded { get => _loaded; private set => Set(ref _loaded, value); }

    /// <summary>The cast the addon sent, with photos and characters filled in from the account's lookup when it has them.</summary>
    public IReadOnlyList<CastMember> Cast => _cast ?? Detail.CastMembers;

    /// <summary>The YouTube id of the trailer, or null (still looking, or there is none).</summary>
    public string? TrailerId { get => _trailerId; private set => Set(ref _trailerId, value); }

    /// <summary>A movie's release date as the account's lookup gave it: null while it is being looked up, empty when there is none.</summary>
    public string? ReleaseDate { get => _releaseDate; private set => Set(ref _releaseDate, value); }

    /// <summary>"Mar 5, 2024   2h 17m": the release date (or just the year while it is unknown) and how long it is.</summary>
    public string Facts
    {
        get
        {
            var year = Detail.Year?.ToString(CultureInfo.InvariantCulture);
            var when = ReleaseDate switch
            {
                _ when IsShow => year,
                null => null,
                "" => year,
                var date => Format.ReleaseDate(date) ?? year,
            };
            var length = Format.RuntimeMinutes(Detail.Runtime) is { } minutes ? Format.Runtime(minutes) : null;
            return string.Join("   ", new[] { when, length }.Where(text => !string.IsNullOrEmpty(text)));
        }
    }

    /// <summary>The season chosen, else the first one.</summary>
    public int CurrentSeason => _season is { } chosen && Detail.SeasonNumbers.Contains(chosen) ? chosen : Detail.SeasonNumbers.Count > 0 ? Detail.SeasonNumbers[0] : 1;

    public IReadOnlyList<EpisodeInfo> Episodes => Detail.Seasons.TryGetValue(CurrentSeason, out var episodes) ? episodes : [];

    public void ChooseSeason(int season)
    {
        _season = season;
        Raise(nameof(CurrentSeason));
        Raise(nameof(Episodes));
    }

    public async Task LoadAsync(CancellationToken cancel = default)
    {
        try
        {
            var full = await _client.FetchMetaAsync(AppInfo.CinemetaManifestUrl, Preview.Type, Preview.Id, cancel);
            if (full is not null)
            {
                // the card's own words fill in whatever the full answer leaves out
                Detail = full with
                {
                    Poster = full.Poster ?? Preview.Poster,
                    Background = full.Background ?? Preview.Background,
                    Logo = full.Logo ?? Preview.Logo,
                    Description = full.Description ?? Preview.Description,
                    ImdbRating = full.ImdbRating ?? Preview.ImdbRating,
                    Genres = full.Genres ?? Preview.Genres,
                };
            }
        }
        catch (OperationCanceledException) when (cancel.IsCancellationRequested)
        {
            return;
        }
        catch (Exception)
        {
            // the addon has nothing more: the page keeps what the card had
        }

        _cast = null;
        Raise(nameof(Cast));
        Loaded = true;

        // the account's server adds the trailer, a movie's release date and the cast's photos; each stays quiet when it has nothing
        if (extras is not null) await Task.WhenAll(FindTrailerAsync(cancel), FindReleaseDateAsync(cancel), FindCastPhotosAsync(cancel));
    }

    private async Task FindTrailerAsync(CancellationToken cancel)
    {
        try
        {
            TrailerId = await extras!.TrailerIdAsync(Detail.Name, Detail.Type, Detail.Year, cancel);
        }
        catch (Exception)
        {
            // no trailer button
        }
    }

    private async Task FindReleaseDateAsync(CancellationToken cancel)
    {
        if (IsShow)
        {
            ReleaseDate = "";
            return;
        }

        try
        {
            ReleaseDate = await extras!.ReleaseDateAsync(Detail.Name, Detail.Year, cancel) ?? "";
        }
        catch (Exception)
        {
            ReleaseDate = "";
        }

        Raise(nameof(Facts));
    }

    [System.Text.RegularExpressions.GeneratedRegex(@"^tt\d+$")]
    private static partial System.Text.RegularExpressions.Regex ImdbId();

    private async Task FindCastPhotosAsync(CancellationToken cancel)
    {
        var current = Cast;
        if (!ImdbId().IsMatch(Detail.Id) || (current.Count > 0 && current.All(m => m.PhotoUrl is not null))) return;
        try
        {
            var photos = await extras!.CastPhotosAsync(Detail.Id, Detail.Type, cancel);
            if (photos.Count == 0) return;
            _cast = CastMerge.Merge(current, photos);
            Raise(nameof(Cast));
        }
        catch (Exception)
        {
            // the cast stays as the addon sent it
        }
    }
}
