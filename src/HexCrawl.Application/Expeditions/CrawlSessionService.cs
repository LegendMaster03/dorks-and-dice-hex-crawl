using HexCrawl.Application.Persistence;
using HexCrawl.Domain.Procedure;
using HexCrawl.Domain.Runtime;
using HexCrawl.Domain.Spatial;

namespace HexCrawl.Application;

public sealed record StartStandaloneCrawlSessionCommand(
    string Name,
    string ProcedureKey,
    CrawlSessionContext Context,
    HexCoordinate? StartHex = null,
    CrawlProcedureProfile? ProcedureSnapshot = null);

public sealed class CrawlSessionService(IHexCrawlStore store)
{
    public async Task<StoredExpedition> StartAsync(
        string ownerUserId,
        StartStandaloneCrawlSessionCommand command,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(ownerUserId))
        {
            throw new ArgumentException("Owner user id is required.", nameof(ownerUserId));
        }

        var preset = CrawlProcedureCatalog.Resolve(command.ProcedureKey);
        var profile = command.ProcedureSnapshot ?? preset;
        if (!string.Equals(profile.Key, preset.Key, StringComparison.OrdinalIgnoreCase))
        {
            throw new ArgumentException("A customized procedure snapshot must retain the selected preset key as provenance.", nameof(command));
        }
        profile.Validate();

        var id = Guid.NewGuid();
        CrawlSessionRuntimeState runtime = command.Context switch
        {
            AbstractHexCrawlSessionContext abstractContext => CreateAbstractState(
                id,
                abstractContext,
                command.StartHex ?? throw new ArgumentException("Abstract-hex sessions require a starting hex.", nameof(command))),
            NonSpatialCrawlSessionContext nonSpatialContext => CreateNonSpatialState(id, nonSpatialContext),
            WorldBoundCrawlSessionContext => throw new ArgumentException(
                "World-bound sessions must be started through the overworld expedition endpoint.",
                nameof(command)),
            _ => throw new ArgumentException("Unsupported crawl session context.", nameof(command))
        };

        var now = DateTimeOffset.UtcNow;
        return await store.CreateExpeditionAsync(new StoredExpedition(
            RequiredText(command.Name, "Session name"),
            runtime,
            command.Context,
            null,
            profile,
            null,
            TimeSpan.Zero,
            ownerUserId.Trim(),
            1,
            now,
            now), cancellationToken);
    }

    private static ExpeditionState CreateAbstractState(
        Guid id,
        AbstractHexCrawlSessionContext context,
        HexCoordinate startHex)
    {
        context.Validate();
        var unit = context.HexContext.HexCenterDistance.Unit;
        return new ExpeditionState
        {
            Id = id,
            Position = null,
            PositionPrecision = null,
            Traversal = HexTraversalState.StartingIn(startHex, unit),
            Navigation = new NavigationRuntimeState(false, 0),
            DistanceTraveled = new DistanceMeasure(0, unit)
        };
    }

    private static NonSpatialSessionState CreateNonSpatialState(
        Guid id,
        NonSpatialCrawlSessionContext context)
    {
        context.Validate();
        return new NonSpatialSessionState
        {
            Id = id,
            ElapsedTime = TimeSpan.Zero,
            CompletedWatches = 0
        };
    }

    private static string RequiredText(string? value, string label) =>
        !string.IsNullOrWhiteSpace(value)
            ? value.Trim()
            : throw new ArgumentException($"{label} is required.");
}
