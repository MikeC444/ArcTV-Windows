namespace ArcTV.Core.Sources;

/// <summary>When the source page should skip itself: which source to start without asking, if any.</summary>
public static class AutoPick
{
    /// <summary>
    /// The next episode of a binge: the best source that plays at once. Otherwise (unless the person asked to choose) the source this title or episode was last watched on, and
    /// otherwise, with smart source picking on, the best source that plays here. Null means show the list.
    /// </summary>
    public static SourceItem? Choose(PlayTarget target, IReadOnlyList<SourceItem> items, LastSource? last, bool smartPicking)
    {
        SourceItem? Best() => items.FirstOrDefault(i => i.IsPlayable);
        if (target.AutoPick) return Best();
        if (target.ChooseSource) return null;
        if (last?.Match(items) is { IsPlayable: true } same) return same;
        return smartPicking ? Best() : null;
    }
}
