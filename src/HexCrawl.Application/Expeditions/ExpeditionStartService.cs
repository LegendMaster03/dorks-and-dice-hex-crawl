using HexCrawl.Domain.Procedure;
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
    public Task<StoredExpedition> StartWorldBoundAsync(
        Guid overworldId,
        string ownerUserId,
        string name,
        ProcedureStartSelection selection,
        string presentationKey,
        HexCoordinate startHex,
        CancellationToken cancellationToken = default)
        => Task.FromException<StoredExpedition>(
            new NotSupportedException(
                "Legacy V1 world starts have been retired. Use StartWorldBoundCellsAsync with an authoritative cell."));

    /// <summary>
    /// Begin a generalized world-bound expedition at an authoritative motif
    /// cell. No hex coordinate, fake center distance or hex presentation
    /// projection is created. This is an application capability; the existing
    /// hex-only HTTP start route stays gated until its cell contracts are ready.
    /// </summary>
    public Task<StoredExpedition> StartWorldBoundCellsAsync(
        Guid overworldId,
        string ownerUserId,
        string name,
        ProcedureStartSelection selection,
        PeriodicCellAddress startCell,
        CancellationToken cancellationToken = default) =>
        StartWorldBoundCellsAsync(overworldId, ownerUserId, name, selection,
            startCell, "dm-controlled", cancellationToken);

    public async Task<StoredExpedition> StartWorldBoundCellsAsync(
        Guid overworldId,
        string ownerUserId,
        string name,
        ProcedureStartSelection selection,
        PeriodicCellAddress startCell,
        string presentationKey,
        CancellationToken cancellationToken = default)
    {
        var presentation = MapPresentationPolicyCatalog.Resolve(presentationKey);
        presentation.Validate();
        var owner = RequiredText(ownerUserId, "Owner user id");
        var expeditionName = RequiredText(name, "Expedition name");
        var world = await coreService.GetOverworldAsync(overworldId, owner, cancellationToken);
        // A verified legacy hex grid is an adapter to the same periodic-cell
        // authority, not a reason to select the V1 movement executor.
        var tiling = world.World.SpatialTiling;

        // Check the cell address before materializing or loading a procedure.
        var cell = tiling.Resolve(startCell);
        selection.Validate();
        // Only an explicit generalized start uses the three executable
        // hex-origin geometric recipes. Saved revisions remain pinned verbatim.
        // Validate the recipe before storing a new campaign revision so an
        // incompatible preset does not create an orphan revision.
        var geometricPreset = !string.IsNullOrWhiteSpace(selection.PresetKey)
            ? GeometricNativeProcedureMaterializer.Materialize(selection.PresetKey!)
            : null;
        var selected = geometricPreset is null
            ? await ResolveProcedureAsync(owner, selection, cancellationToken)
            : null;
        var procedure = geometricPreset?.Procedure ?? selected!.Procedure;
        var bound = GenericProcedureRuntime.Bind(procedure);
        if (bound.Movement.MechanicVersion != 2)
            throw new NotSupportedException("Generalized world traversal requires movement version 2.");

        // Alternative legitimate symmetry quotients must not be rejected only
        // because their canonical strings differ. The world's independently
        // validated translation cover can prove structural equivalence.
        var comparison = DelaneyDressTopology.Compare(
            procedure.TilingDsSymbol,
            tiling.Topology.QuotientDsSymbol,
            tiling.Topology.TranslationDsSymbol);
        if (comparison.Status is not (
            TilingComparisonStatus.ExactIdentity
            or TilingComparisonStatus.ChamberIsomorphic
            or TilingComparisonStatus.ProvenEquivalent))
            throw new InvalidOperationException(
                comparison.Status == TilingComparisonStatus.Inconclusive
                    ? "Equivalence between this world and the selected ruleset has not been proven. Review the ruleset before starting."
                    : "The selected ruleset is incompatible with this world's topology.");

        var state = new CellExpeditionState
        {
            Id = Guid.NewGuid(),
            Traversal = new PeriodicCellTraversal
            {
                CurrentCell = cell.Id,
                Position = cell.Center
            },
            DistanceTraveled = tiling.PhysicalDistancePerWorldUnit is { } calibration
                ? new DistanceMeasure(0, calibration.Unit)
                : null
        };
        state.Validate(tiling);

        var knowledge = PresentationKnowledgeProjection.InitializeCells(
            world.World, presentation,
            new PlayerKnowledgeState
            {
                ScopeId = Guid.NewGuid(),
                OverworldId = world.World.Id,
                PresentationPolicy = presentation
            },
            cell.Id);
        // All local spatial and knowledge validation has succeeded. Only now
        // create a campaign revision for the explicitly selected V2 preset.
        if (geometricPreset is not null)
            selected = await procedures.CreateAsync(owner, geometricPreset.Procedure,
                geometricPreset.Origin, cancellationToken: cancellationToken);
        var pinned = selected ?? throw new InvalidOperationException(
            "Generalized expedition start did not produce a pinned procedure.");
        var now = DateTimeOffset.UtcNow;
        return await store.CreateExpeditionAsync(new StoredExpedition(
            expeditionName,
            state,
            new WorldBoundCrawlSessionContext(world.World.Id),
            knowledge,
            CampaignProcedureSnapshot.Copy(pinned.Procedure),
            null,
            TimeSpan.Zero,
            owner,
            1,
            now,
            now)
        {
            CampaignId = pinned.CampaignId,
            ProcedureOrigin = pinned.ProcedureOrigin
        }, cancellationToken);
    }

    /// <summary>
    /// Explicit geometric mapless start. The existing standalone endpoint remains
    /// a V1 compatibility surface until the Phase 15 UI is replaced.
    /// The session id is also the identity of its reproducible periodic geometry.
    /// </summary>
    public async Task<StoredExpedition> StartStandaloneCellsAsync(
        string ownerUserId,
        string name,
        ProcedureStartSelection selection,
        AbstractHexCrawlSessionContext context,
        HexCoordinate startHex,
        CancellationToken cancellationToken = default)
    {
        var owner = RequiredText(ownerUserId, "Owner user id");
        var sessionName = RequiredText(name, "Session name");
        ArgumentNullException.ThrowIfNull(context);
        context.Validate();
        ArgumentNullException.ThrowIfNull(selection);
        selection.Validate();

        var sessionId = Guid.NewGuid();
        var geometry = AbstractCellSessionGeometry.CreateNewHexSession(sessionId, context, startHex);
        // Saved revisions are never rematerialized. Only explicitly selected
        // catalog presets use the new geometric creation recipe.
        var geometricPreset = selection.PresetKey is { } presetKey
            ? GeometricNativeProcedureMaterializer.Materialize(presetKey)
            : null;
        var selected = geometricPreset is null
            ? await ResolveProcedureAsync(owner, selection, cancellationToken)
            : null;
        var procedure = geometricPreset?.Procedure ?? selected!.Procedure;
        var movement = GenericProcedureRuntime.Bind(procedure).Movement;
        if (movement.MechanicVersion != 2)
            throw new NotSupportedException("Geometric mapless travel requires a version 2 movement procedure.");
        var comparison = DelaneyDressTopology.Compare(
            procedure.TilingDsSymbol,
            geometry.World.Topology.QuotientDsSymbol,
            geometry.World.Topology.TranslationDsSymbol);
        if (comparison.Status is not (
            TilingComparisonStatus.ExactIdentity
            or TilingComparisonStatus.ChamberIsomorphic
            or TilingComparisonStatus.ProvenEquivalent))
            throw new InvalidOperationException(
                "The saved ruleset's tiling has not been proven equivalent to the mapless hex geometry.");

        // Never create a campaign revision for a rejected procedure. Preserve
        // source attribution without inventing a new published preset revision.
        if (geometricPreset is not null)
            selected = await procedures.CreateAsync(owner, geometricPreset.Procedure,
                geometricPreset.Origin, cancellationToken: cancellationToken);

        var pinned = selected ?? throw new InvalidOperationException(
            "Generalized mapless start did not produce a pinned procedure.");
        var now = DateTimeOffset.UtcNow;
        return await store.CreateExpeditionAsync(new StoredExpedition(
            sessionName,
            geometry.Expedition,
            context,
            null,
            CampaignProcedureSnapshot.Copy(pinned.Procedure),
            null,
            TimeSpan.Zero,
            owner,
            1,
            now,
            now)
        {
            CampaignId = pinned.CampaignId,
            ProcedureOrigin = pinned.ProcedureOrigin
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
        if (context is AbstractHexCrawlSessionContext)
            throw new NotSupportedException(
                "Legacy V1 abstract-hex starts have been retired. Use StartStandaloneCellsAsync.");

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
            && !CampaignProcedureSchema.RequiresLegacyHexTiling(selected.Procedure.TilingDsSymbol))
            throw new NotSupportedException(
                "This abstract hex runtime cannot execute a nonhex tiling until Phase 18.");
        if (context is AbstractHexCrawlSessionContext)
            RequireLegacyHexMovement(selected.Procedure);

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

    private static void RequireLegacyHexMovement(CampaignProcedure procedure)
    {
        // Existing legacy starts can select symbolic/non-native procedures.
        // Preserve that established contract; only a concrete generalized
        // movement handler must be rejected before constructing ExpeditionState.
        var movement = procedure.Modules.SingleOrDefault(module =>
            module.Mechanic.ExecutionHandler ==
                GenericProcedureExecutionHandlers.MovementResolutionPolicy);
        if (movement is not null && movement.Mechanic.Version != 1)
            throw new NotSupportedException(
                "Legacy world-bound and abstract-hex expeditions require movement version 1. " +
                "Use the explicit cell expedition start for movement version 2.");
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
