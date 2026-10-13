using ArcTV.Core.Addons;
using ArcTV.Core.Mvvm;

namespace ArcTV.Core.Details;

/// <summary>
/// What a title's page shows: the card's basics at once, then (when they arrive) the full details from Cinemeta: the cast and, for a show, its episodes.
/// Call it from the UI thread: it announces changes where it was called from.
/// </summary>
public sealed class DetailModel(MetaPreview preview, AddonClient? client = null) : ObservableObject
{
    private readonly AddonClient _client = client ?? new AddonClient();
    private MetaDetail _detail = preview.AsDetail();
    private bool _loaded;
    private int? _season;

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
            Raise(nameof(CurrentSeason));
            Raise(nameof(Episodes));
        }
    }

    public bool IsShow => Detail.Type == "series";

    /// <summary>False until the full details have come back (or failed to), so placeholders can hold their places.</summary>
    public bool Loaded { get => _loaded; private set => Set(ref _loaded, value); }

    public IReadOnlyList<CastMember> Cast => Detail.CastMembers;

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

        Loaded = true;
    }
}
