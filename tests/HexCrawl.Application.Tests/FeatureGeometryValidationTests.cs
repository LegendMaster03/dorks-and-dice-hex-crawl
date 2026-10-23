using System.Reflection;
using HexCrawl.Application.Persistence;
using HexCrawl.Domain.Spatial;
using HexCrawl.Domain.World;

namespace HexCrawl.Application.Tests;

public sealed class FeatureGeometryValidationTests
{
    [Fact]
    public async Task DegenerateLineCreateAndUpdateFailWithoutSavingPreviouslyStoredData()
    {
        var (service, store) = Fixture();
        var original = store.World;
        var path = new[] { new WorldPoint(2, 3), new WorldPoint(2, 3) };
        var exception = await Assert.ThrowsAsync<ArgumentException>(() => service.CreateFeatureAsync(
            original.World.Id, "owner", new CreateFeatureCommand(
                "Invalid", "road", SpatialFeatureKind.Line, null, path, null, original.Version)));
        Assert.Contains("distinct positions", exception.Message);
        Assert.Same(original, store.World);
        Assert.Equal(0, store.Saves);

        var existing = Assert.Single(original.World.Features);
        await Assert.ThrowsAsync<ArgumentException>(() => service.UpdateFeatureAsync(
            original.World.Id, existing.Id, "owner", new UpdateFeatureCommand(
                "Invalid replacement", "road", SpatialFeatureKind.Line, null, path, null, original.Version)));
        Assert.Same(original, store.World);
        Assert.Equal(0, store.Saves);
    }

    [Fact]
    public async Task NonzeroLineAllowsRepeatedVerticesAndNameOnlyUpdatesPreservePath()
    {
        var (service, store) = Fixture();
        var path = new[] { new WorldPoint(2, 3), new WorldPoint(2, 3), new WorldPoint(4, 5) };
        var created = await service.CreateFeatureAsync(store.World.World.Id, "owner", new CreateFeatureCommand(
            "Road", "road", SpatialFeatureKind.Line, null, path, null, store.World.Version));
        var line = Assert.IsType<LinearFeature>(created.World.Features.Last());
        var updated = await service.UpdateFeatureAsync(created.World.Id, line.Id, "owner", new UpdateFeatureCommand(
            "Renamed road", "road", SpatialFeatureKind.Line, null, line.Path, null, created.Version));
        var renamed = Assert.IsType<LinearFeature>(updated.World.Features.Last());
        Assert.Equal("Renamed road", renamed.Name);
        Assert.Equal(path, renamed.Path);
        Assert.Equal(2, store.Saves);
    }

    private static (HexCrawlService Service, WorldStore Store) Fixture()
    {
        var proxy = DispatchProxy.Create<IHexCrawlStore, WorldStore>();
        var store = (WorldStore)(object)proxy;
        var world = new OverworldDefinition
        {
            Id = Guid.NewGuid(), Name = "Validation fixture", Tiling = MaplessSessionFixture.Start().World,
            Features = [new LinearFeature(Guid.NewGuid(), "Existing road", "road",
                [new WorldPoint(0, 0), new WorldPoint(1, 1)])]
        };
        store.World = new(world, "owner", 1, DateTimeOffset.UtcNow, DateTimeOffset.UtcNow);
        return (new HexCrawlService(proxy), store);
    }

    // Only the existing aggregate read/save boundary is needed for validation tests.
    public class WorldStore : DispatchProxy
    {
        public StoredOverworld World { get; set; } = null!;
        public int Saves { get; private set; }

        protected override object? Invoke(MethodInfo? targetMethod, object?[]? args)
        {
            if (targetMethod!.Name == nameof(IHexCrawlStore.GetOverworldAsync))
                return Task.FromResult<StoredOverworld?>(World);
            if (targetMethod.Name == nameof(IHexCrawlStore.SaveOverworldAsync))
            {
                Saves++;
                World = ((StoredOverworld)args![0]!) with { Version = World.Version + 1 };
                return Task.FromResult(new SaveResult<StoredOverworld>(SaveOutcome.Saved, World));
            }
            throw new NotSupportedException(targetMethod.Name);
        }
    }
}
