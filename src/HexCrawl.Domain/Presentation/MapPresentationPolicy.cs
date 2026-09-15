namespace HexCrawl.Domain.Presentation;

public enum GridVisibility
{
    Hidden,
    Visible
}

public enum TerrainPresentationMode
{
    HiddenUntilKnown,
    AlwaysVisible,
    Manual
}

public sealed record MapPresentationPolicy
{
    public GridVisibility PlayerGrid { get; init; } = GridVisibility.Visible;
    public TerrainPresentationMode TerrainMode { get; init; } = TerrainPresentationMode.HiddenUntilKnown;
    public IReadOnlySet<string> AutomaticallyRevealFeatureCategories { get; init; } = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
    public bool AllowPlayerAnnotations { get; init; } = true;
}
