using HexCrawl.Application.Persistence;
using HexCrawl.Domain.Knowledge;
using HexCrawl.Domain.Presentation;
using HexCrawl.Domain.Runtime;
using HexCrawl.Domain.Spatial;

namespace HexCrawl.Application;

public sealed record ProcedureStartSelection(
    string? PresetKey = null,
    Guid? ProcedureId = null,
    int? ProcedureRevision = null)
{
    public void Validate()
    {
        var hasPreset = !string.IsNullOrWhiteSpace(PresetKey);
        var hasProcedure = ProcedureId.HasValue;
        if (hasPreset == hasProcedure)
        {
            throw new ArgumentException("Choose either a procedure preset or a saved campaign procedure.");
        }
        if (ProcedureId == Guid.Empty)
        {
            throw new ArgumentException("Saved procedure id can not be empty.");
        }
        if (hasProcedure && !ProcedureRevision.HasValue)
        {
            throw new ArgumentException("A saved procedure start requires an explicit revision.");
        }
        if (!hasProcedure && ProcedureRevision.HasValue)
        {
            throw new ArgumentException("A procedure revision requires a saved procedure id.");
        }
        if (ProcedureRevision.HasValue && ProcedureRevision.Value <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(ProcedureRevision));
        }
    }
}

/// <summary>
/// Owns the creation-time procedure selection boundary for new crawl sessions.
/// Presets are materialized into campaign-owned procedure revisions before use; saved
/// procedures are owner-authorized and pinned by copying the selected revision snapshot.
/// Runtime execution never resolves a named preset.
/// </summary>
public sealed class ExpeditionStartService(
    IHexCrawlStore store,
    HexCrawlService coreService,
    CampaignProcedureService procedures)
{
    public async Task<StoredExpedition> StartWorldBoundAsync(
        Guid overworldId,
        string ownerUserId,
        string name,
        ProcedureStartSelection selection,
        string presentationKey,
        HexCoordinate startHex,
        CancellationToken cancellationToken = default)
    {
        var owner = RequiredText(ownerUserId, "Owner user id");
        var expeditionName = RequiredText(name, "Expedition name");
        var presentation = MapPresentationPolicyCatalog.Resolve(presentationKey);
        presentation.Validate();

        var world = await coreService.GetOverworldAsync(overworldId, owner, cancellationToken);
        // Phase 18 is responsible for generalized boundary-based movement.
        // Avoid creating a materialized procedure revision for an unsupported start.
        if (!world.World.HasLegacyHexGrid)
            throw new NotSupportedException(
                "Generalized world movement is gated until Phase 18. This world was not changed.");
        var selected = await ResolveProcedureAsync(owner, selection, cancellationToken);
        if (selected.Procedure.TilingDsSymbol != LegacyHexTilingCompatibility.HexQuotient)
            throw new NotSupportedException(
                "The selected procedure requires a different tiling; this runtime supports hexagonal worlds only.");

        var expeditionId = Guid.NewGuid();
        var state = new ExpeditionState
        {
            Id = expeditionId,
            Position = HexGeometry.HexToWorld(world.World.Grid, startHex),
            PositionPrecision = WorldPositionPrecision.HexAnchor,
            Traversal = HexTraversalState.StartingIn(startHex, world.World.Grid.NeighborCenterDistance.Unit),
            Navigation = new NavigationRuntimeState(false, 0),
            DistanceTraveled = new DistanceMeasure(0, world.World.Grid.NeighborCenterDistance.Unit)
        };
        var knowledge = new PlayerKnowledgeState
        {
            ScopeId = Guid.NewGuid(),
            OverworldId = world.World.Id,
            PresentationPolicy = presentation
        };
        knowledge = PresentationKnowledgeProjection.Initialize(world.World, presentation, knowledge, startHex);

        var now = DateTimeOffset.UtcNow;
        return await store.CreateExpeditionAsync(new StoredExpedition(
            expeditionName,
            state,
            new WorldBoundCrawlSessionContext(world.World.Id),
            knowledge,
            CampaignProcedureSnapshot.Copy(selected.Procedure),
            null,
            TimeSpan.Zero,
            world.OwnerUserId,
            1,
            now,
            now)
        {
            CampaignId = selected.CampaignId,
            ProcedureOrigin = selected.ProcedureOrigin
        }, cancellationToken);
    }

    public async Task<StoredExpedition> StartStandaloneAsync(
        string ownerUserId,
        string name,
        ProcedureStartSelection selection,
        CrawlSessionContext context,
        HexCoordinate? startHex = null,
        CancellationToken cancellationToken = default)
    {
        var owner = RequiredText(ownerUserId, "Owner user id");
        var sessionName = RequiredText(name, "Session name");
        ArgumentNullException.ThrowIfNull(context);

        var id = Guid.NewGuid();
        CrawlSessionRuntimeState runtime = context switch
        {
            AbstractHexCrawlSessionContext abstractContext => CreateAbstractState(
                id,
                abstractContext,
                startHex ?? throw new ArgumentException("Abstract-hex sessions require a starting hex.", nameof(startHex))),
            NonSpatialCrawlSessionContext nonSpatialContext => CreateNonSpatialState(id, nonSpatialContext),
            WorldBoundCrawlSessionContext => throw new ArgumentException(
                "World-bound sessions must be started through the overworld expedition endpoint.",
                nameof(context)),
            _ => throw new ArgumentException("Unsupported crawl session context.", nameof(context))
        };
        var selected = await ResolveProcedureAsync(owner, selection, cancellationToken);
        if (context is AbstractHexCrawlSessionContext
            && selected.Procedure.TilingDsSymbol != LegacyHexTilingCompatibility.HexQuotient)
            throw new NotSupportedException(
                "This abstract hex runtime cannot execute a nonhex tiling until Phase 18.");

        var now = DateTimeOffset.UtcNow;
        return await store.CreateExpeditionAsync(new StoredExpedition(
            sessionName,
            runtime,
            context,
            null,
            CampaignProcedureSnapshot.Copy(selected.Procedure),
            null,
            TimeSpan.Zero,
            owner,
            1,
            now,
            now)
        {
            CampaignId = selected.CampaignId,
            ProcedureOrigin = selected.ProcedureOrigin
        }, cancellationToken);
    }

    private async Task<StoredCampaignProcedureRevision> ResolveProcedureAsync(
        string ownerUserId,
        ProcedureStartSelection selection,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(selection);
        selection.Validate();
        if (selection.ProcedureId.HasValue)
        {
            return await procedures.GetAsync(
                ownerUserId,
                selection.ProcedureId.Value,
                selection.ProcedureRevision!.Value,
                cancellationToken);
        }

        return await procedures.CreateFromPresetAsync(
            ownerUserId,
            selection.PresetKey!,
            cancellationToken: cancellationToken);
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
