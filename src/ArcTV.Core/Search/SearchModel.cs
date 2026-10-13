using ArcTV.Core.Addons;
using ArcTV.Core.Home;
using ArcTV.Core.Mvvm;
using ArcTV.Core.Storage;

namespace ArcTV.Core.Search;

public enum SearchState { Idle, Searching, Results, None, Error }

/// <summary>
/// What the Search page shows: nothing yet, a search under way, the movies and shows found (with a note while slower addons are still answering), nothing
/// found, or an error. Call it from the UI thread: it announces changes where it was called from.
/// </summary>
public sealed class SearchModel(string userId, Func<CancellationToken, Task<IReadOnlyList<HomeSource>>> sources, RecentSearchStore? store = null, AddonClient? client = null) : ObservableObject
{
    private SearchState _state;
    private IReadOnlyList<MetaPreview> _movies = [];
    private IReadOnlyList<MetaPreview> _tvShows = [];
    private bool _stillChecking;
    private string _query = "";
    private IReadOnlyList<string> _recents = store?.Load(userId) ?? [];
    private int _generation;
    private CancellationTokenSource? _run;

    public const string ErrorText = "Couldn't reach your installed addons. Check your connection and try again.";

    public SearchState State { get => _state; private set => Set(ref _state, value); }

    public IReadOnlyList<MetaPreview> Movies { get => _movies; private set => Set(ref _movies, value); }

    public IReadOnlyList<MetaPreview> TvShows { get => _tvShows; private set => Set(ref _tvShows, value); }

    /// <summary>True while slower addons are still answering and results are already showing.</summary>
    public bool StillChecking { get => _stillChecking; private set => Set(ref _stillChecking, value); }

    /// <summary>The words last searched for (shown in "Nothing found for ...").</summary>
    public string Query { get => _query; private set => Set(ref _query, value); }

    public IReadOnlyList<string> Recents { get => _recents; private set => Set(ref _recents, value); }

    public void Remember(string query) => SetRecents(RecentSearches.Adding(query, Recents));

    public void Forget(string query) => SetRecents(Recents.Where(t => t != query).ToList());

    public void ClearRecents() => SetRecents([]);

    private void SetRecents(IReadOnlyList<string> next)
    {
        Recents = next;
        store?.Save(userId, next);
    }

    /// <summary>Back to the empty page (the box was cleared); a search still under way is dropped.</summary>
    public void Reset()
    {
        _generation++;
        _run?.Cancel();
        State = SearchState.Idle;
        Movies = [];
        TvShows = [];
        StillChecking = false;
    }

    public async Task SearchAsync(string query)
    {
        var q = string.Join(' ', query.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries));
        if (q.Length == 0) return;
        var generation = ++_generation;
        _run?.Cancel();
        var run = _run = new CancellationTokenSource();
        Query = q;

        IReadOnlyList<HomeSource> found;
        try
        {
            found = await sources(run.Token);
        }
        catch (OperationCanceledException)
        {
            return;
        }

        if (generation != _generation) return;
        if (found.Count == 0)
        {
            State = SearchState.None;
            Movies = [];
            TvShows = [];
            StillChecking = false;
            return;
        }

        // keep the old results up until the new ones arrive
        if (State == SearchState.Results) StillChecking = true;
        else State = SearchState.Searching;

        var context = SynchronizationContext.Current;
        void Apply(SearchProgress progress)
        {
            if (generation != _generation) return;
            if (progress.Movies.Count > 0 || progress.TvShows.Count > 0)
            {
                Movies = progress.Movies;
                TvShows = progress.TvShows;
                StillChecking = progress.Pending > 0;
                State = SearchState.Results;
            }
            else if (progress.Pending > 0)
            {
                State = SearchState.Searching;
            }
            else
            {
                Movies = [];
                TvShows = [];
                StillChecking = false;
                State = progress.Failed == found.Count ? SearchState.Error : SearchState.None;
            }
        }

        void Post(SearchProgress progress)
        {
            if (context is null) Apply(progress);
            else context.Post(_ => Apply(progress), null);
        }

        try
        {
            var final = await CatalogSearch.SearchAsync(found, q, client, Post, cancel: run.Token);
            Apply(final);
        }
        catch (OperationCanceledException)
        {
            // a newer search took over
        }
    }
}
