using HexCrawl.Domain.Spatial;

namespace HexCrawl.Domain.Runtime;

public enum CrawlSessionContextKind
{
    WorldBound,
    AbstractHex,
    NonSpatial
}

public abstract record CrawlSessionContext
{
    public abstract CrawlSessionContextKind Kind { get; }
    public virtual Guid? OverworldId => null;
    public virtual CrawlRuntimeContext? RuntimeContext => null;
    public virtual string DisplayName => Kind.ToString();
}

public sealed record WorldBoundCrawlSessionContext(Guid WorldId) : CrawlSessionContext
{
    public override CrawlSessionContextKind Kind => CrawlSessionContextKind.WorldBound;
    public override Guid? OverworldId => WorldId;
    public override string DisplayName => "World-bound crawl";
}

public sealed record AbstractHexCrawlSessionContext(
    string Name,
    HexOrientation Orientation,
    CrawlRuntimeContext HexContext) : CrawlSessionContext
{
    public override CrawlSessionContextKind Kind => CrawlSessionContextKind.AbstractHex;
    public override CrawlRuntimeContext? RuntimeContext => HexContext;
    public override string DisplayName => string.IsNullOrWhiteSpace(Name) ? "Abstract hex crawl" : Name.Trim();

    public void Validate()
    {
        if (string.IsNullOrWhiteSpace(Name))
        {
            throw new InvalidOperationException("Abstract-hex context name is required.");
        }
        HexContext.Validate();
    }
}

public sealed record NonSpatialCrawlSessionContext(string Name) : CrawlSessionContext
{
    public override CrawlSessionContextKind Kind => CrawlSessionContextKind.NonSpatial;
    public override string DisplayName => string.IsNullOrWhiteSpace(Name) ? "Non-spatial session" : Name.Trim();

    public void Validate()
    {
        if (string.IsNullOrWhiteSpace(Name))
        {
            throw new InvalidOperationException("Non-spatial context name is required.");
        }
    }
}

public abstract record CrawlSessionRuntimeState
{
    public required Guid Id { get; init; }
    public IReadOnlyList<CrawlRuntimeEvent> History { get; init; } = [];
}

public sealed record NonSpatialSessionState : CrawlSessionRuntimeState
{
    public TimeSpan ElapsedTime { get; init; }
    public int CompletedWatches { get; init; }
}
