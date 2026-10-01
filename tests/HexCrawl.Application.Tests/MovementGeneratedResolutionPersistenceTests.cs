using HexCrawl.Application.Persistence;
using HexCrawl.Domain.Runtime;
using HexCrawl.Domain.Spatial;
using HexCrawl.Infrastructure.Persistence;

namespace HexCrawl.Application.Tests;

public sealed class MovementGeneratedResolutionPersistenceTests
{
    [Fact]
    public async Task PartyEditsDoNotRewriteConsumedMovementResolutionOrProvenance()
    {
        await using var database = await PostgresTestDatabase.CreateAsync();
        var store = new PostgresHexCrawlStore(database.ConnectionString);
        await store.InitializeAsync();

        var now = DateTimeOffset.UtcNow;
        var procedure = CrawlProcedureCatalog.Resolve("simple-fixed-distance")
            .MaterializeGeneric()
            .Procedure;
        var state = new ExpeditionState
        {
            Id = Guid.NewGuid(),
            Traversal = HexTraversalState.StartingIn(new HexCoordinate(0, 0), DistanceUnit.Miles),
            DistanceTraveled = new DistanceMeasure(0, DistanceUnit.Miles)
        };
        var generatedId = Guid.NewGuid();
        var generated = new GeneratedProcedureResolution(
            generatedId,
            state.Id,
            2,
            3,
            41,
            1,
            [],
            new ProcedureResolvedTravel(
                12,
                10,
                new ResolutionProvenance(
                    ResolutionSource.ExternalSystem,
                    "Movement composition: participant=3 mi/hour; interval=4h; expected=12 mi.")),
            null,
            null,
            GeneratedProcedureResolutionStatus.Consumed,
            ConsumedAtVersion: 3,
            ConsumedAuditSequence: 42);
        var expedition = new StoredExpedition(
            "Movement history",
            state,
            new AbstractHexCrawlSessionContext(
                "Movement history",
                HexOrientation.PointyTop,
                new CrawlRuntimeContext(new DistanceMeasure(12, DistanceUnit.Miles))),
            null,
            procedure,
            null,
            TimeSpan.FromHours(4),
            "owner",
            3,
            now,
            now)
        {
            GeneratedProcedureResolutions = [generated]
        };
        var created = await store.CreateExpeditionAsync(expedition);
        var partyService = new ExpeditionPartyService(store, new HexCrawlService(store));
        var member = new CrawlPartyMember(Guid.NewGuid(), "Later party member");

        var updated = await partyService.UpdateAsync(
            created.Id,
            created.OwnerUserId,
            new UpdateExpeditionPartyCommand(
                created.Version,
                new CrawlPartySheet { Members = [member] }));

        AssertResolutionPreserved(Assert.Single(updated.GeneratedProcedureResolutions), generatedId);

        var reloaded = await store.GetExpeditionAsync(created.Id, created.OwnerUserId);
        Assert.NotNull(reloaded);
        AssertResolutionPreserved(Assert.Single(reloaded!.GeneratedProcedureResolutions), generatedId);
    }

    private static void AssertResolutionPreserved(GeneratedProcedureResolution resolution, Guid generatedId)
    {
        Assert.Equal(generatedId, resolution.Id);
        Assert.Equal(GeneratedProcedureResolutionStatus.Consumed, resolution.Status);
        Assert.Equal(3, resolution.ConsumedAtVersion);
        Assert.Equal(42, resolution.ConsumedAuditSequence);
        Assert.NotNull(resolution.Travel);
        Assert.Equal(12, resolution.Travel!.ExpectedDistance);
        Assert.Equal(10, resolution.Travel.ActualDistance);
        Assert.Equal(ResolutionSource.ExternalSystem, resolution.Travel.Provenance.Source);
        Assert.Equal(
            "Movement composition: participant=3 mi/hour; interval=4h; expected=12 mi.",
            resolution.Travel.Provenance.Note);
    }
}
