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

public sealed record MaplessSpatialStartOptions(
    string Name, DistanceMeasure PhysicalDistancePerWorldUnit)
{
    public void Validate()
    {
        if (string.IsNullOrWhiteSpace(Name))
            throw new ArgumentException("Mapless setup name is required.");
        if (!double.IsFinite(PhysicalDistancePerWorldUnit.Value)
            || PhysicalDistancePerWorldUnit.Value <= 0)
            throw new ArgumentException("Mapless physical scale must be finite and positive.");
        if (string.IsNullOrWhiteSpace(PhysicalDistancePerWorldUnit.Unit.Symbol))
            throw new ArgumentException("Mapless physical scale unit is required.");
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
    /// <summary>
    /// Begin a generalized world-bound expedition at an authoritative motif
    /// cell. All spatial authority belongs to the actual persisted tiling;
    /// no separate coordinate model or presentation projection is fabricated.
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
        var tiling = world.World.SpatialTiling;

        // Check the cell address before materializing or loading a procedure.
        var cell = tiling.Resolve(startCell);
        selection.Validate();
        // Saved revisions remain pinned verbatim.
        // Validate the recipe before storing a new campaign revision so an
        // incompatible preset does not create an orphan revision.
        var geometricPreset = !string.IsNullOrWhiteSpace(selection.PresetKey)
            ? CampaignProcedureMaterializer.MaterializeExecutableSpatial(CrawlProcedureCatalog.Resolve(selection.PresetKey!))
            : null;
        var selected = geometricPreset is null
            ? await ResolveProcedureAsync(owner, selection, cancellationToken)
            : null;
        var procedure = geometricPreset?.Procedure ?? selected!.Procedure;
        GenericProcedureRuntime.Bind(procedure);

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
        // create a campaign revision for the selected preset.
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
    /// Materialize one periodic tiling from the selected executable procedure.
    /// This same realized geometry is retained in the session context and
    /// reloaded without any shape-specific constructor or axial projection.
    /// </summary>
    public async Task<StoredExpedition> StartStandaloneCellsAsync(
        string ownerUserId,
        string name,
        ProcedureStartSelection selection,
        MaplessSpatialStartOptions options,
        PeriodicCellAddress? startCell = null,
        CancellationToken cancellationToken = default)
    {
        var owner = RequiredText(ownerUserId, "Owner user id");
        var sessionName = RequiredText(name, "Session name");
        ArgumentNullException.ThrowIfNull(options);
        options.Validate();
        ArgumentNullException.ThrowIfNull(selection);
        selection.Validate();

        var geometricPreset = selection.PresetKey is { } presetKey
            ? CampaignProcedureMaterializer.MaterializeExecutableSpatial(CrawlProcedureCatalog.Resolve(presetKey))
            : null;
        var selected = geometricPreset is null
            ? await ResolveProcedureAsync(owner, selection, cancellationToken)
            : null;
        var procedure = geometricPreset?.Procedure ?? selected!.Procedure;
        GenericProcedureRuntime.Bind(procedure);

        var realized = DelaneyDressHarmonicMetricRealization.Construct(
            procedure.TilingDsSymbol, 1d, "world-unit");
        if (realized.Status != "realized" || realized.Topology is null || realized.Realization is null)
            throw new InvalidOperationException("The selected ruleset does not provide realizable periodic mapless geometry.");
        var sessionId = Guid.NewGuid();
        var tiling = new PeriodicWorldTiling(
            sessionId, realized.Topology, realized.Realization, new WorldPoint(0, 0),
            PhysicalDistancePerWorldUnit: options.PhysicalDistancePerWorldUnit);
        tiling.Validate();

        var comparison = DelaneyDressTopology.Compare(
            procedure.TilingDsSymbol,
            tiling.Topology.QuotientDsSymbol,
            tiling.Topology.TranslationDsSymbol);
        if (comparison.Status is not (
            TilingComparisonStatus.ExactIdentity
            or TilingComparisonStatus.ChamberIsomorphic
            or TilingComparisonStatus.ProvenEquivalent))
            throw new InvalidOperationException(
                "The selected ruleset has not been proven equivalent to the realized mapless geometry.");

        var initial = MaplessCellSessionGeometry.Create(sessionId, tiling, startCell);
        var context = new MaplessSpatialCrawlSessionContext(options.Name.Trim(), tiling);
        context.Validate();
        if (geometricPreset is not null)
            selected = await procedures.CreateAsync(owner, geometricPreset.Procedure,
                geometricPreset.Origin, cancellationToken: cancellationToken);

        var pinned = selected ?? throw new InvalidOperationException(
            "Mapless expedition start did not produce a pinned procedure.");
        var now = DateTimeOffset.UtcNow;
        return await store.CreateExpeditionAsync(new StoredExpedition(
            sessionName,
            initial.Expedition,
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

    public async Task<StoredExpedition> StartNonSpatialAsync(
        string ownerUserId,
        string name,
        ProcedureStartSelection selection,
        NonSpatialCrawlSessionContext context,
        CancellationToken cancellationToken = default)
    {
        var owner = RequiredText(ownerUserId, "Owner user id");
        var sessionName = RequiredText(name, "Session name");
        ArgumentNullException.ThrowIfNull(context);
        context.Validate();
        var runtime = CreateNonSpatialState(Guid.NewGuid(), context);
        var selected = await ResolveProcedureAsync(owner, selection, cancellationToken);

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
