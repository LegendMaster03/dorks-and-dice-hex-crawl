using System.Security.Claims;
using HexCrawl.Application;
using HexCrawl.Application.Assets;
using HexCrawl.Domain.Procedure;
using HexCrawl.Domain.Runtime;
using HexCrawl.Web.Api;
using HexCrawl.Web.Framework;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;

namespace HexCrawl.Web.Modules.Worlds;

public sealed class WorldModule : IHexCrawlModule
{
    public HexCrawlModuleManifest Manifest { get; } = new(
        Id: "worlds",
        DisplayName: "Overworld authoring");

    public void RegisterServices(IServiceCollection services) =>
        services.AddScoped<HexCrawlService>();

    public void MapEndpoints(RouteGroupBuilder api)
    {
        api.MapGet("/overworlds", ListOverworldsAsync);
        api.MapPost("/overworlds/periodic", CreatePeriodicOverworldAsync);
        api.MapPost("/overworlds/{overworldId:guid}/cell-procedures", CreateCellTravelProcedureAsync);
        api.MapGet("/overworlds/{overworldId:guid}/cell-procedures/compatibility", AssessCellProcedureAsync);
        api.MapGet("/overworlds/{overworldId:guid}", GetOverworldAsync);
        api.MapGet("/overworlds/{overworldId:guid}/cells/viewport", GetVisibleCellsAsync);
        api.MapGet("/overworlds/{overworldId:guid}/cells/containing", GetContainingCellsAsync);
        api.MapGet("/overworlds/{overworldId:guid}/cells/resolve", ResolveCellAsync);
        api.MapPut("/overworlds/{overworldId:guid}/name", RenameOverworldAsync);
        api.MapDelete("/overworlds/{overworldId:guid}", DeleteOverworldAsync);

        api.MapPost("/overworlds/{overworldId:guid}/locations", CreateLocationAsync);
        api.MapPut("/overworlds/{overworldId:guid}/locations/{locationId:guid}", UpdateLocationAsync);
        api.MapDelete("/overworlds/{overworldId:guid}/locations/{locationId:guid}", DeleteLocationAsync);

        api.MapPost("/overworlds/{overworldId:guid}/features", CreateFeatureAsync);
        api.MapPut("/overworlds/{overworldId:guid}/features/{featureId:guid}", UpdateFeatureAsync);
        api.MapDelete("/overworlds/{overworldId:guid}/features/{featureId:guid}", DeleteFeatureAsync);
    }

    private static async Task<IResult> ListOverworldsAsync(
        HttpContext context,
        HexCrawlService service,
        CancellationToken cancellationToken) =>
        Results.Ok(await service.ListOverworldsAsync(UserId(context), cancellationToken));

    // An image-independent, general authoring route. Unresolved harmonic
    // realizations fail closed rather than producing a fabricated grid.
    private sealed record CreatePeriodicOverworldRequest(
        string Name,
        string TilingDsSymbol,
        double WorldUnitsPerAbstractPeriod,
        HexCrawl.Domain.Spatial.WorldPoint Origin,
        double RotationDegrees = 0,
        HexCrawl.Domain.Spatial.DistanceMeasure? PhysicalDistancePerWorldUnit = null,
        HexCrawl.Domain.Spatial.WorldBounds? PlayableBounds = null,
        double? PeriodULengthWorldUnits = null,
        double? PeriodVLengthWorldUnits = null,
        double? PeriodAngleDegrees = null,
        IReadOnlyList<HexCrawl.Domain.Spatial.PeriodicStraightEdgeSubdivision>? StraightEdgeSubdivisions = null,
        HexCrawl.Domain.Spatial.PeriodicTopologyWitness? TopologyWitness = null,
        HexCrawl.Domain.Spatial.PeriodicMetricRealization? MetricRealization = null);

    private static async Task<IResult> CreatePeriodicOverworldAsync(
        CreatePeriodicOverworldRequest request,
        HttpContext context,
        HexCrawlService service,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(request.Name)
            || request.TilingDsSymbol is null or { Length: > 131072 }
            || !double.IsFinite(request.Origin.X) || !double.IsFinite(request.Origin.Y))
            throw new ArgumentException("A world name, supported pattern and finite origin are required.");
        // Optional metric fitting works for every realizable D-symbol, not a
        // branch per familiar polygon shape. Invalid measurements fail closed.
        var constraints = new List<HexCrawl.Domain.Spatial.TilingMetricConstraint>();
        static double RequireLength(double value, string label)
        {
            if (!double.IsFinite(value) || value <= 0)
                throw new ArgumentOutOfRangeException(label,
                    "The repeating distance must be finite and positive.");
            return value;
        }
        if (request.PeriodULengthWorldUnits is { } u)
        {
            u = RequireLength(u, nameof(request.PeriodULengthWorldUnits));
            constraints.Add(new("period-u-length", "world-unit", u, u));
        }
        if (request.PeriodVLengthWorldUnits is { } v)
        {
            v = RequireLength(v, nameof(request.PeriodVLengthWorldUnits));
            constraints.Add(new("period-v-length", "world-unit", v, v));
        }
        if (request.PeriodAngleDegrees is { } angle)
        {
            if (!double.IsFinite(angle) || angle <= 0 || angle >= 180)
                throw new ArgumentOutOfRangeException(
                    nameof(request.PeriodAngleDegrees),
                    "The angle between repeating directions must be between 0 and 180 degrees.");
            constraints.Add(new("period-angle-degrees", "degrees", angle, angle));
        }
        HexCrawl.Domain.Spatial.PeriodicTopologyWitness topology;
        HexCrawl.Domain.Spatial.PeriodicMetricRealization metric;
        if (request.MetricRealization is { } supplied)
        {
            // An explicitly measured polygon witness is an alternative to
            // harmonic fitting, not a guessed geometry from chamber incidence.
            // It must validate against its independently proved atomic cover.
            if (request.RotationDegrees != 0 || request.WorldUnitsPerAbstractPeriod != 1)
                throw new ArgumentException(
                    "Explicit metric realization uses absolute coordinates; repeating-pattern scale must be 1 and rotation 0.");
            if (request.TopologyWitness is { } provided)
            {
                provided.ValidateAdjacency();
                var source = HexCrawl.Domain.Spatial.DelaneyDressTopology.Inspect(request.TilingDsSymbol);
                if (source.Status != HexCrawl.Domain.Spatial.DelaneyDressStatus.Euclidean
                    || provided.QuotientDsSymbol != source.Symbol!.Canonical)
                    throw new ArgumentException("Supplied periodic topology does not match the requested D-symbol.");
                topology = provided;
            }
            else
            {
                var cover = HexCrawl.Domain.Spatial.DelaneyDressGeneralQuotientUnfolding.Construct(
                    request.TilingDsSymbol);
                if (cover.Status != HexCrawl.Domain.Spatial.DelaneyDressCoverStatus.Constructed
                    || cover.Witness is null)
                    throw new NotSupportedException(cover.Reason ?? "No proved translation topology could be constructed.");
                topology = cover.Witness;
            }
            var splits = HexCrawl.Domain.Spatial.PeriodicStraightEdgeGeometry.Normalize(
                topology, request.StraightEdgeSubdivisions);
            topology = HexCrawl.Domain.Spatial.PeriodicStraightEdgeGeometry.Apply(topology, splits);
            if (supplied.Units != "world-unit" || supplied.Constraints is null)
                throw new ArgumentException("Explicit periodic geometry must use world-unit and include a constraints list.");
            metric = supplied with
            {
                Constraints = supplied.Constraints.Concat(constraints)
                    .Concat(splits.Select(HexCrawl.Domain.Spatial.PeriodicStraightEdgeGeometry.Constraint))
                    .ToArray()
            };
            HexCrawl.Domain.Spatial.PeriodicMetricWitnessValidator.Validate(topology, metric);
        }
        else
        {
            if (request.TopologyWitness is not null)
                throw new ArgumentException("An explicit topology witness requires an explicit metric realization.");
            var realization = HexCrawl.Domain.Spatial.DelaneyDressHarmonicMetricRealization.Construct(
                request.TilingDsSymbol, request.WorldUnitsPerAbstractPeriod, "world-unit",
                request.RotationDegrees,
                constraints: constraints.Count == 0 ? null : constraints,
                straightEdgeSubdivisions: request.StraightEdgeSubdivisions);
            if (realization.Status != "realized" || realization.Topology is null || realization.Realization is null)
                throw new NotSupportedException(
                    "This repeating tile pattern needs a different or more complete geometric realization. "
                    + (realization.Reason ?? "No valid periodic geometry was constructed."));
            topology = realization.Topology;
            metric = realization.Realization;
        }
        var worldTiling = new HexCrawl.Domain.Spatial.PeriodicWorldTiling(
            Guid.NewGuid(), topology, metric, request.Origin,
            PhysicalDistancePerWorldUnit: request.PhysicalDistancePerWorldUnit)
        {
            PlayableBounds = request.PlayableBounds
        };
        worldTiling.Validate();
        var stored = await service.CreatePeriodicOverworldAsync(
            UserId(context), request.Name, worldTiling, cancellationToken);
        return Results.Created($"/api/overworlds/{stored.World.Id:D}", OverworldContract.From(stored));
    }

    // Generate a normal editable, pinned campaign revision using existing
    // cell movement capabilities. Preset templates and past revisions are not
    // edited. No image or manually authored notation is required.
    private static async Task<IResult> CreateCellTravelProcedureAsync(
        Guid overworldId,
        HttpContext context,
        HexCrawlService service,
        CampaignProcedureService procedures,
        CancellationToken cancellationToken)
    {
        var owner = UserId(context);
        var stored = await service.GetOverworldAsync(overworldId, owner, cancellationToken);
        var materialized = CrawlProcedureCatalog.Resolve("simple-fixed-distance")
            .MaterializeGeneric();
        var template = materialized.Procedure;
        var modules = template.Modules
            .Select(module =>
            {
                if (module.Mechanic.ExecutionHandler !=
                    GenericProcedureExecutionHandlers.MovementResolutionPolicy)
                    return module;
                var parameters = module.Parameters.ToDictionary(item => item.Key, item => item.Value);
                parameters["travelResolution"] = TravelResolutionMode.CellSteps.ToString();
                parameters["actualDistanceResolution"] = ActualDistanceResolutionMode.Fixed.ToString();
                return module with
                {
                    Parameters = parameters
                };
            }).ToArray();
        var edition = template with
        {
            ProcedureId = Guid.NewGuid(),
            Name = "Cell-step exploration for " + stored.World.Name,
            TilingDsSymbol = stored.World.SpatialTiling.Topology.QuotientDsSymbol,
            Modules = modules
        };
        edition.Validate();
        var bound = GenericProcedureRuntime.Bind(edition);
        if (bound.Movement.TravelResolution != TravelResolutionMode.CellSteps)
            throw new InvalidOperationException("The generated cell ruleset is not executable.");
        var saved = await procedures.CreateAsync(owner, edition, materialized.Origin,
            cancellationToken: cancellationToken);
        return Results.Created($"/api/procedures/{saved.ProcedureId:D}",
            new { procedureId = saved.ProcedureId, revision = saved.Revision,
                name = saved.Procedure.Name });
    }

    private static async Task<IResult> AssessCellProcedureAsync(
        Guid overworldId, Guid procedureId, int revision,
        HttpContext context, HexCrawlService service,
        CampaignProcedureService procedures, CancellationToken cancellationToken)
    {
        if (procedureId == Guid.Empty || revision <= 0)
            throw new ArgumentException("A saved ruleset id and pinned revision are required.");
        var owner = UserId(context);
        var stored = await service.GetOverworldAsync(overworldId, owner, cancellationToken);
        var procedure = (await procedures.GetAsync(
            owner, procedureId, revision, cancellationToken)).Procedure;
        var comparison = HexCrawl.Domain.Spatial.DelaneyDressTopology.Compare(
            procedure.TilingDsSymbol,
            stored.World.SpatialTiling.Topology.QuotientDsSymbol,
            stored.World.SpatialTiling.Topology.TranslationDsSymbol);
        if (comparison.Status is not (
            HexCrawl.Domain.Spatial.TilingComparisonStatus.ExactIdentity
            or HexCrawl.Domain.Spatial.TilingComparisonStatus.ChamberIsomorphic
            or HexCrawl.Domain.Spatial.TilingComparisonStatus.ProvenEquivalent))
            return Results.Ok(new
            {
                status = comparison.Status == HexCrawl.Domain.Spatial.TilingComparisonStatus.Inconclusive
                    ? "review" : "incompatible",
                explanation = comparison.Status == HexCrawl.Domain.Spatial.TilingComparisonStatus.Inconclusive
                    ? "Equivalence with this world's cell pattern has not been established."
                    : "The selected ruleset describes a different cell pattern from this world.",
                worldVersion = stored.Version,
                procedureId,
                revision
            });
        GenericProcedureRuntime bound;
        try
        {
            bound = GenericProcedureRuntime.Bind(procedure);
        }
        catch (Exception error) when (
            error is InvalidOperationException or NotSupportedException)
        {
            return Results.Ok(new
            {
                status = "unsupported",
                explanation = "This saved ruleset does not currently provide executable generalized travel.",
                worldVersion = stored.Version,
                procedureId,
                revision
            });
        }
        if (bound.Movement.TravelResolution == TravelResolutionMode.ContinuousDistance
            && stored.World.SpatialTiling.PhysicalDistancePerWorldUnit is null)
            return Results.Ok(new
            {
                status = "needs-calibration",
                explanation = "Physical-distance travel needs a calibrated world. Cell-step travel works without physical scale.",
                worldVersion = stored.Version,
                procedureId,
                revision
            });
        return Results.Ok(new
        {
            status = "ready",
            explanation = "This saved ruleset supports the selected world geometry and travel mode.",
            worldVersion = stored.Version,
            procedureId,
            revision
        });
    }

    private static async Task<IResult> GetOverworldAsync(
        Guid overworldId,
        HttpContext context,
        HexCrawlService service,
        CancellationToken cancellationToken) =>
        Results.Ok(OverworldContract.From(await service.GetOverworldAsync(
            overworldId,
            UserId(context),
            cancellationToken)));

    // Geometry is calculated only by the authoritative domain world, and only after
    // the existing owner-scoped world lookup. Never derive tiles in the browser.
    private static async Task<IResult> GetVisibleCellsAsync(
        Guid overworldId,
        double minX,
        double minY,
        double maxX,
        double maxY,
        HttpContext context,
        HexCrawlService service,
        CancellationToken cancellationToken)
    {
        var bounds = new HexCrawl.Domain.Spatial.WorldBounds(minX, minY, maxX, maxY);
        bounds.Validate();
        var world = await service.GetOverworldAsync(overworldId, UserId(context), cancellationToken);
        // Hard cap on candidate enumeration, not merely on response length.
        return Results.Ok(world.World.SpatialTiling.Intersecting(bounds, limit: 2500)
            .Where(world.World.SpatialTiling.IsPlayable).ToArray());
    }

    private static async Task<IResult> GetContainingCellsAsync(
        Guid overworldId,
        double x,
        double y,
        HttpContext context,
        HexCrawlService service,
        CancellationToken cancellationToken)
    {
        if (!double.IsFinite(x) || !double.IsFinite(y))
            throw new ArgumentOutOfRangeException(nameof(x), "Point coordinates must be finite.");
        var world = await service.GetOverworldAsync(overworldId, UserId(context), cancellationToken);
        var lookup = world.World.SpatialTiling.Containing(
            new HexCrawl.Domain.Spatial.WorldPoint(x, y), limit: 2500);
        var admitted = lookup.Candidates
            .Where(cell => world.World.SpatialTiling.IsPlayable(cell.Address)).ToArray();
        return Results.Ok(new HexCrawl.Domain.Spatial.WorldCellLookup(
            admitted.Length switch
            {
                0 => HexCrawl.Domain.Spatial.WorldCellLookupStatus.NotFound,
                1 => HexCrawl.Domain.Spatial.WorldCellLookupStatus.Unique,
                _ => HexCrawl.Domain.Spatial.WorldCellLookupStatus.Ambiguous
            }, admitted));
    }

    private static async Task<IResult> ResolveCellAsync(
        Guid overworldId,
        string motifCellId,
        long u,
        long v,
        HttpContext context,
        HexCrawlService service,
        CancellationToken cancellationToken)
    {
        var world = await service.GetOverworldAsync(overworldId, UserId(context), cancellationToken);
        var address = new HexCrawl.Domain.Spatial.PeriodicCellAddress(
            motifCellId, new HexCrawl.Domain.Spatial.LatticeDisplacement(u, v));
        var geometry = world.World.SpatialTiling.Resolve(address);
        if (!world.World.SpatialTiling.IsPlayable(geometry))
            return Results.NotFound();
        return Results.Ok(geometry);
    }

    private sealed record RenameOverworldRequest(string Name, long ExpectedVersion);

    private static async Task<IResult> RenameOverworldAsync(
        Guid overworldId, RenameOverworldRequest request,
        HttpContext context, HexCrawlService service, CancellationToken cancellationToken) =>
        Results.Ok(OverworldContract.From(await service.RenameOverworldAsync(
            overworldId, UserId(context), request.Name, request.ExpectedVersion,
            cancellationToken)));

    private static async Task<IResult> DeleteOverworldAsync(
        Guid overworldId,
        long expectedVersion,
        HttpContext context,
        HexCrawlService service,
        IMapAssetStore assetStore,
        ILoggerFactory loggerFactory,
        CancellationToken cancellationToken)
    {
        var deleted = await service.DeleteOverworldAsync(
            overworldId,
            UserId(context),
            expectedVersion,
            cancellationToken);
        var logger = loggerFactory.CreateLogger("HexCrawl.OverworldDelete");
        var cleanupKeys = deleted.World.SourceMaps
            .SelectMany(map => new[] { map.AssetKey, map.SourceArchive?.AssetKey })
            .Where(key => !string.IsNullOrWhiteSpace(key))
            .Cast<string>()
            .Distinct(StringComparer.Ordinal)
            .ToArray();
        var cleanupProblems = new List<string>();
        foreach (var assetKey in cleanupKeys)
        {
            try
            {
                if (!await assetStore.DeleteAsync(assetKey, CancellationToken.None))
                {
                    logger.LogWarning(
                        "Overworld {OverworldId} was deleted, but owned asset {AssetKey} was already missing.",
                        overworldId,
                        assetKey);
                }
            }
            catch (Exception exception)
            {
                logger.LogError(
                    exception,
                    "Overworld {OverworldId} was deleted, but owned asset {AssetKey} cleanup failed.",
                    overworldId,
                    assetKey);
                cleanupProblems.Add(assetKey);
            }
        }

        if (cleanupProblems.Count > 0)
        {
            return Results.Problem(
                title: "Overworld deleted with asset cleanup problem",
                detail: "The overworld was deleted, but one or more owned binary map assets could not be cleaned up. Manual cleanup may be required.",
                statusCode: StatusCodes.Status500InternalServerError);
        }

        return Results.Ok(OverworldContract.From(deleted));
    }

    private static async Task<IResult> CreateLocationAsync(
        Guid overworldId,
        LocationMutationRequest request,
        HttpContext context,
        HexCrawlService service,
        CancellationToken cancellationToken) =>
        Results.Ok(OverworldContract.From(await service.CreateLocationAsync(
            overworldId,
            UserId(context),
            request.ToCreateCommand(),
            cancellationToken)));

    private static async Task<IResult> UpdateLocationAsync(
        Guid overworldId,
        Guid locationId,
        LocationMutationRequest request,
        HttpContext context,
        HexCrawlService service,
        CancellationToken cancellationToken) =>
        Results.Ok(OverworldContract.From(await service.UpdateLocationAsync(
            overworldId,
            locationId,
            UserId(context),
            request.ToUpdateCommand(),
            cancellationToken)));

    private static async Task<IResult> DeleteLocationAsync(
        Guid overworldId,
        Guid locationId,
        long expectedVersion,
        HttpContext context,
        HexCrawlService service,
        CancellationToken cancellationToken) =>
        Results.Ok(OverworldContract.From(await service.DeleteLocationAsync(
            overworldId,
            locationId,
            UserId(context),
            expectedVersion,
            cancellationToken)));

    private static async Task<IResult> CreateFeatureAsync(
        Guid overworldId,
        FeatureMutationRequest request,
        HttpContext context,
        HexCrawlService service,
        CancellationToken cancellationToken) =>
        Results.Ok(OverworldContract.From(await service.CreateFeatureAsync(
            overworldId,
            UserId(context),
            request.ToCreateCommand(),
            cancellationToken)));

    private static async Task<IResult> UpdateFeatureAsync(
        Guid overworldId,
        Guid featureId,
        FeatureMutationRequest request,
        HttpContext context,
        HexCrawlService service,
        CancellationToken cancellationToken) =>
        Results.Ok(OverworldContract.From(await service.UpdateFeatureAsync(
            overworldId,
            featureId,
            UserId(context),
            request.ToUpdateCommand(),
            cancellationToken)));

    private static async Task<IResult> DeleteFeatureAsync(
        Guid overworldId,
        Guid featureId,
        long expectedVersion,
        HttpContext context,
        HexCrawlService service,
        CancellationToken cancellationToken) =>
        Results.Ok(OverworldContract.From(await service.DeleteFeatureAsync(
            overworldId,
            featureId,
            UserId(context),
            expectedVersion,
            cancellationToken)));

    private static string UserId(HttpContext context) =>
        context.User.FindFirstValue(ClaimTypes.NameIdentifier) is { Length: > 0 } value
            ? value
            : throw new UnauthorizedAccessException(
                "An authenticated Tool Host or explicitly configured standalone development identity is required.");
}
