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
        api.MapPost("/overworlds", CreateOverworldAsync);
        api.MapPost("/overworlds/periodic", CreatePeriodicOverworldAsync);
        api.MapPost("/overworlds/{overworldId:guid}/cell-procedures", CreateCellTravelProcedureAsync);
        api.MapGet("/overworlds/{overworldId:guid}", GetOverworldAsync);
        api.MapGet("/overworlds/{overworldId:guid}/cells/viewport", GetVisibleCellsAsync);
        api.MapGet("/overworlds/{overworldId:guid}/cells/containing", GetContainingCellsAsync);
        api.MapGet("/overworlds/{overworldId:guid}/cells/resolve", ResolveCellAsync);
        api.MapPut("/overworlds/{overworldId:guid}", UpdateOverworldAsync);
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

    private static async Task<IResult> CreateOverworldAsync(
        CreateOverworldRequest request,
        HttpContext context,
        HexCrawlService service,
        CancellationToken cancellationToken) =>
        Results.Created(
            "/api/overworlds",
            OverworldContract.From(await service.CreateOverworldAsync(
                UserId(context),
                request.ToCommand(),
                cancellationToken)));

    // An image-independent, general authoring route. Unresolved harmonic
    // realizations fail closed rather than producing a fabricated grid.
    private sealed record CreatePeriodicOverworldRequest(
        string Name,
        string TilingDsSymbol,
        double WorldUnitsPerAbstractPeriod,
        HexCrawl.Domain.Spatial.WorldPoint Origin,
        double RotationDegrees = 0,
        HexCrawl.Domain.Spatial.DistanceMeasure? PhysicalDistancePerWorldUnit = null);

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
        var realization = HexCrawl.Domain.Spatial.DelaneyDressHarmonicMetricRealization.Construct(
            request.TilingDsSymbol, request.WorldUnitsPerAbstractPeriod, "world-unit",
            request.RotationDegrees);
        if (realization.Status != "realized" || realization.Topology is null || realization.Realization is null)
            throw new NotSupportedException(
                "This repeating tile pattern needs a different or more complete geometric realization. "
                + (realization.Reason ?? "No valid periodic geometry was constructed."));
        var worldTiling = new HexCrawl.Domain.Spatial.PeriodicWorldTiling(
            Guid.NewGuid(), realization.Topology, realization.Realization, request.Origin,
            PhysicalDistancePerWorldUnit: request.PhysicalDistancePerWorldUnit);
        worldTiling.Validate();
        var stored = await service.CreatePeriodicOverworldAsync(
            UserId(context), request.Name, worldTiling, cancellationToken);
        return Results.Created($"/api/overworlds/{stored.World.Id:D}", OverworldContract.From(stored));
    }

    // Generate a normal editable, pinned campaign revision using existing
    // movement-v2 capabilities. Preset templates and past revisions are not
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
        if (stored.World.HasLegacyHexGrid || stored.World.Tiling is null)
            throw new NotSupportedException("This world already uses legacy hex procedures.");
        var template = CrawlProcedureCatalog.Resolve("simple-fixed-distance")
            .MaterializeGeneric().Procedure;
        var modules = template.Modules
            .Where(module => module.Mechanic.ExecutionHandler !=
                GenericProcedureExecutionHandlers.HexProgressPolicy)
            .Select(module =>
            {
                if (module.Mechanic.ExecutionHandler !=
                    GenericProcedureExecutionHandlers.MovementResolutionPolicy)
                    return module;
                var parameters = module.Parameters.ToDictionary(item => item.Key, item => item.Value);
                parameters["travelResolution"] = TravelResolutionMode.CellSteps.ToString();
                parameters["actualDistanceResolution"] = ActualDistanceResolutionMode.Fixed.ToString();
                parameters["tracksIntraHexProgress"] = "false";
                return module with
                {
                    Mechanic = module.Mechanic with { Version = 2 },
                    Parameters = parameters
                };
            }).ToArray();
        var edition = template with
        {
            ProcedureId = Guid.NewGuid(),
            Name = "Cell-step exploration for " + stored.World.Name,
            TilingDsSymbol = stored.World.Tiling.Topology.QuotientDsSymbol,
            Modules = modules
        };
        edition.Validate();
        var bound = GenericProcedureRuntime.Bind(edition);
        if (bound.Movement.MechanicVersion != 2
            || bound.Movement.TravelResolution != TravelResolutionMode.CellSteps)
            throw new InvalidOperationException("The generated cell ruleset is not executable.");
        var saved = await procedures.CreateAsync(owner, edition, null,
            cancellationToken: cancellationToken);
        return Results.Created($"/api/procedures/{saved.ProcedureId:D}",
            new { procedureId = saved.ProcedureId, revision = saved.Revision,
                name = saved.Procedure.Name });
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
        return Results.Ok(world.World.SpatialTiling.Intersecting(bounds, limit: 2500));
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
        return Results.Ok(world.World.SpatialTiling.Containing(
            new HexCrawl.Domain.Spatial.WorldPoint(x, y), limit: 2500));
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
        return Results.Ok(world.World.SpatialTiling.Resolve(address));
    }

    private static async Task<IResult> UpdateOverworldAsync(
        Guid overworldId,
        UpdateOverworldRequest request,
        HttpContext context,
        HexCrawlService service,
        CancellationToken cancellationToken) =>
        Results.Ok(OverworldContract.From(await service.UpdateOverworldAsync(
            overworldId,
            UserId(context),
            request.ToCommand(),
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
