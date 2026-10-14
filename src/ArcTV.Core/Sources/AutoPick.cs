namespace ArcTV.Core.Sources;

/// <summary>When the source page should skip itself: which source to start without asking, if any.</summary>
public static class AutoPick
{
    /// <summary>
    /// The next episode of a binge: the best source that plays at once. Otherwise (unless the person asked to choose) the best source that plays, but only with smart source picking
    /// on (a setting the person turns on). Playing something watched before does not skip the list by itself: the list shows, with the source last used marked. Null means show the list.
    /// </summary>
    public static SourceItem? Choose(PlayTarget target, IReadOnlyList<SourceItem> items, bool smartPicking)
    {
        SourceItem? Best() => items.FirstOrDefault(i => i.CanStream);
        if (target.AutoPick) return Best();
        if (target.ChooseSource) return null;
        return smartPicking ? Best() : null;
    }
}
