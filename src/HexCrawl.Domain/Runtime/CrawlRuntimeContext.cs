using HexCrawl.Domain.Spatial;

namespace HexCrawl.Domain.Runtime;

/// <summary>
/// Map-independent physical scale required by the crawl procedure engine.
/// World geometry, source maps, semantic locations, and presentation remain outside this boundary.
/// </summary>
public sealed record CrawlRuntimeContext(DistanceMeasure HexCenterDistance)
{
    public void Validate()
    {
        if (HexCenterDistance.Value <= 0
            || !double.IsFinite(HexCenterDistance.Value))
        {
            throw new InvalidOperationException("Hex center distance must be finite and positive.");
        }
    }
}
