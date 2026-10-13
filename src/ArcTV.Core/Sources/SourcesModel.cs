using ArcTV.Core.Addons;
using ArcTV.Core.Mvvm;

namespace ArcTV.Core.Sources;

/// <summary>What the source page shows: every stream the account's addons offer for a title or episode, filtered and sorted. Call it from the UI thread.</summary>
public sealed class SourcesModel(PlayTarget target, Func<CancellationToken, Task<IReadOnlyList<SyncedAddon>>> addons, AddonClient? client = null) : ObservableObject
{
    private SourceResult _result = new();
    private bool _loading = true;
    private SourceFilter _filter = SourceFilter.All;
    private SourceSort _sort = SourceSort.Quality;
    private int _addonCount;
    private int _generation;

    public PlayTarget Target { get; } = target;

    public SourceResult Result { get => _result; private set { _result = value; Raise(); Raise(nameof(Arranged)); Raise(nameof(RecommendedId)); } }

    public bool Loading { get => _loading; private set => Set(ref _loading, value); }

    public SourceFilter Filter { get => _filter; set { if (Set(ref _filter, value)) Raise(nameof(Arranged)); } }

    public SourceSort Sort { get => _sort; set { if (Set(ref _sort, value)) Raise(nameof(Arranged)); } }

    public IReadOnlyList<SourceItem> Arranged => SourceListing.Arrange(Result.Items, Filter, Sort);

    public string? RecommendedId => SourceListing.RecommendedId(Result.Items);

    /// <summary>What to tell the person when nothing came back: why, and what to do next.</summary>
    public string EmptyMessage
    {
        get
        {
            if (_addonCount == 0) return "Your account has no addons yet. Add one (such as Torrentio) in Settings > Addons on another device, then come back.";
            if (Result.Failures.Count + Result.Items.Count == 0 && Result.Skipped.Count > 0) return "None of your addons give streams for this title. Add one that does (such as Torrentio) in Settings > Addons on another device.";
            if (Result.Failures.Count > 0 && Result.Failures.Count >= _addonCount - Result.Skipped.Count) return "No addon answered. The reasons are below.";
            return "Your addons had no streams for this title. A very new or very obscure title may not have any yet; try another.";
        }
    }

    public async Task LoadAsync(CancellationToken cancel = default)
    {
        var generation = ++_generation;
        Loading = true;
        try
        {
            IReadOnlyList<SyncedAddon> found;
            try
            {
                found = await addons(cancel);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                found = [];
            }

            _addonCount = found.Count;
            var result = await SourceFinder.FindAsync(found, Target.Title.Type, Target.StreamId, client, cancel);
            if (generation != _generation) return;
            Result = result;
        }
        catch (OperationCanceledException)
        {
            return;
        }

        Loading = false;
        Raise(nameof(EmptyMessage));
    }
}
