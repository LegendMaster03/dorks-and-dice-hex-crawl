using HexCrawl.Domain.Runtime;
using HexCrawl.Domain.Spatial;

namespace HexCrawl.Application.Tests;

internal static class MaplessSessionFixture
{
    public static MaplessCellSessionStart Start(
        string symbol = "<1:1,1,1:6,3>",
        double physicalPerWorldUnit = 12)
    {
        var realized = DelaneyDressHarmonicMetricRealization.Construct(symbol, 1, "world-unit");
        if (realized.Status != "realized")
            throw new InvalidOperationException("Fixture periodic geometry could not be realized.");
        var id = Guid.NewGuid();
        var tiling = new PeriodicWorldTiling(
            id, realized.Topology!, realized.Realization!, new WorldPoint(0, 0),
            PhysicalDistancePerWorldUnit: new DistanceMeasure(physicalPerWorldUnit, DistanceUnit.Miles));
        return MaplessCellSessionGeometry.Create(id, tiling);
    }

    public static MaplessSpatialCrawlSessionContext Context(
        string name = "Mapless test",
        string symbol = "<1:1,1,1:6,3>")
    {
        var start = Start(symbol);
        return new MaplessSpatialCrawlSessionContext(name, start.World);
    }

    public static MaplessSpatialStartOptions Options(
        string name = "Mapless test", double physicalPerWorldUnit = 12) =>
        new(name, new DistanceMeasure(physicalPerWorldUnit, DistanceUnit.Miles));
}
