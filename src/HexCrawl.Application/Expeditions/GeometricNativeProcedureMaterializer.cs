using HexCrawl.Domain.Procedure;
using HexCrawl.Domain.Runtime;

namespace HexCrawl.Application;

/// <summary>
/// An explicitly selected, creation-time geometric recipe for an existing
/// executable hex-origin preset. The ordinary catalog materialization remains
/// V1 until Phase 20.2 moves existing callers and plans pinned snapshot replay.
/// No runtime dispatch depends on a preset key.
/// </summary>
public static class GeometricNativeProcedureMaterializer
{
    public static MaterializedCampaignProcedure Materialize(
        string presetKey, Guid? procedureId = null)
    {
        var original = CrawlProcedureCatalog.Resolve(presetKey);
        var recipe = original.Recipe;
        var movement = recipe.ModuleSelections.SingleOrDefault(item =>
            item.ModuleKey == GenericProcedureCatalog.MovementResolutionModule);
        var progress = recipe.ModuleSelections.SingleOrDefault(item =>
            item.ModuleKey == GenericProcedureCatalog.HexProgressModule);
        if (movement is null || progress is null
            || movement.MechanicKey != GenericProcedureCatalog.MovementResolutionPolicyMechanic
            || movement.MechanicVersion != 1
            || progress.MechanicKey != GenericProcedureCatalog.HexProgressPolicyMechanic
            || progress.MechanicVersion != 1)
            throw new NotSupportedException(
                "Only an explicitly executable V1 hex-origin preset can be materialized as a new geometric native procedure. Declarative presets remain declarative.");

        var travelMode = Required(movement, "travelResolution");
        var geometricMode = travelMode switch
        {
            nameof(TravelResolutionMode.HexSteps) => nameof(TravelResolutionMode.CellSteps),
            nameof(TravelResolutionMode.ContinuousDistance) => nameof(TravelResolutionMode.ContinuousDistance),
            _ => throw new NotSupportedException($"Legacy travel mode '{travelMode}' has no approved geometric counterpart.")
        };
        var directionChangesCost = Bool(progress, "directionChangesCostProgress");
        var turnFactor = Required(progress, "directionChangeProgressCostFactor");
        var supportsDoubleBack = Required(progress, "supportsDeliberateDoubleBack");

        // These four V1 values represented approximate boundary geometry, not
        // real effort multipliers. They are intentionally NOT carried into V2.
        foreach (var name in new[]
                 {
                     "startingExitProgressFactor", "nearExitProgressFactor",
                     "farExitProgressFactor", "backExitProgressFactor"
                 })
            _ = Required(progress, name);

        var parameters = movement.Parameters.ToDictionary(p => p.Key, p => p.Value, StringComparer.Ordinal);
        parameters["travelResolution"] = geometricMode;
        parameters["tracksIntraHexProgress"] = "false";
        var geometricMovement = movement with { MechanicVersion = 2, Parameters = parameters };
        var turning = new ProcedureModuleRecipe(
            GenericProcedureCatalog.SpatialTurnCostModule,
            GenericProcedureCatalog.SpatialTurnCostPolicyMechanic,
            1,
            new Dictionary<string, string>(StringComparer.Ordinal)
            {
                ["neighborCenterDistanceFactor"] = directionChangesCost ? turnFactor : "0",
                ["supportsDeliberateDoubleBack"] = supportsDoubleBack
            });
        var modules = recipe.ModuleSelections
            .Where(m => m.ModuleKey != GenericProcedureCatalog.HexProgressModule)
            .Select(m => m.ModuleKey == GenericProcedureCatalog.MovementResolutionModule
                ? geometricMovement : m)
            .Append(turning)
            .ToArray();
        // This opt-in authoring pathway is not an officially cataloged preset
        // revision. Preserve the source preset identity, but do not manufacture
        // a published revision number.
        var geometric = original with
        {
            Recipe = recipe with { ModuleSelections = modules }
        };
        var result = CampaignProcedureMaterializer.Materialize(geometric, procedureId)
            with { Origin = new ProcedureOriginMetadata(original.PresetKey, original.DisplayName, null) };
        var bound = GenericProcedureRuntime.Bind(result.Procedure);
        if (bound.Movement.MechanicVersion != 2 || bound.TurnPolicy is null)
            throw new InvalidOperationException("Geometric preset did not bind the expected spatial policy.");
        return result;
    }

    private static string Required(ProcedureModuleRecipe selection, string key) =>
        selection.Parameters.TryGetValue(key, out var value)
            ? value
            : throw new InvalidOperationException($"Preset '{selection.ModuleKey}' is missing '{key}'.");

    private static bool Bool(ProcedureModuleRecipe selection, string key) =>
        bool.TryParse(Required(selection, key), out var value)
            ? value
            : throw new InvalidOperationException($"Preset parameter '{key}' is not a boolean.");
}
