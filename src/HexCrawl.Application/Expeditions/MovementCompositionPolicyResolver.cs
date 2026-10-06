using System.Globalization;
using HexCrawl.Domain.Procedure;

namespace HexCrawl.Application;

public enum MovementCompositionPolicySupport
{
    None,
    Supported,
    Unsupported
}

public enum MovementTerrainPolicySupport
{
    None,
    Supported,
    Unsupported
}

public sealed record MovementTerrainPolicy(
    MovementTerrainPolicySupport Support,
    string? AdjustmentModel,
    IReadOnlyDictionary<string, string> TerrainAdjustments,
    string? RouteAdjustmentModel,
    string? WeatherAdjustmentModel,
    string? MechanicKey,
    int? MechanicVersion,
    string? ExecutionHandler,
    string? UnsupportedReason)
{
    public static MovementTerrainPolicy None { get; } = new(
        MovementTerrainPolicySupport.None,
        null,
        new Dictionary<string, string>(StringComparer.Ordinal),
        null,
        null,
        null,
        null,
        null,
        null);
}

public sealed record MovementCompositionPolicy(
    MovementCompositionPolicySupport Support,
    string? BudgetModel,
    double? BaseBudget,
    string? BudgetUnit,
    string? LimitingScope,
    IReadOnlyList<string> TravelModeKeys,
    MovementTerrainPolicy Terrain,
    string? MechanicKey,
    int? MechanicVersion,
    string? ExecutionHandler,
    string? UnsupportedReason)
{
    public static MovementCompositionPolicy None { get; } = new(
        MovementCompositionPolicySupport.None,
        null,
        null,
        null,
        null,
        [],
        MovementTerrainPolicy.None,
        null,
        null,
        null,
        null);
}

/// <summary>
/// Focused Phase 8 projection over the exact movement modules stored on a CampaignProcedure.
/// Preset identity, origin metadata, and the current catalog recipe are intentionally not inputs.
/// </summary>
public static class MovementCompositionPolicyResolver
{
    private static readonly HashSet<string> SupportedBudgetModels = new(StringComparer.Ordinal)
    {
        "fixed-per-day",
        "movement-points",
        "speed-derived-distance",
        "speed-and-pace",
        "speed-derived-activities",
        "quarter-day-activities",
        "distance-per-hour",
        "activity-and-distance",
        "journey-progress"
    };

    private static readonly HashSet<string> SupportedLimitingScopes = new(StringComparer.Ordinal)
    {
        "party",
        "party-limiting",
        "slowest-traveler",
        "guide"
    };

    private static readonly HashSet<string> SupportedTerrainModels = new(StringComparer.Ordinal)
    {
        "multiplier",
        "distance-per-hour-multiplier",
        "activity-cost",
        "movement-points-per-distance",
        "maximum-pace",
        "terrain-difficulty",
        "hexes-per-quarter-day"
    };

    public static MovementCompositionPolicy Resolve(CampaignProcedure procedure)
    {
        ArgumentNullException.ThrowIfNull(procedure);
        var budget = procedure.Modules.SingleOrDefault(module =>
            string.Equals(module.Module.Key, GenericProcedureCatalog.MovementBudgetModule, StringComparison.Ordinal));
        var terrain = ResolveTerrain(procedure);
        if (budget is null)
        {
            return MovementCompositionPolicy.None with { Terrain = terrain };
        }

        if (!IsSupportedBudgetMechanic(budget.Mechanic))
        {
            return Unsupported(
                budget,
                terrain,
                $"Movement mechanic '{budget.Mechanic.Key}' version {budget.Mechanic.Version} is not supported by movement composition.");
        }

        if (!budget.Parameters.TryGetValue("budgetModel", out var budgetModel)
            || !SupportedBudgetModels.Contains(budgetModel))
        {
            return Unsupported(budget, terrain, "The stored movement policy has an unsupported budgetModel value.");
        }
        if (!budget.Parameters.TryGetValue("baseBudget", out var rawBase)
            || !double.TryParse(rawBase, NumberStyles.Float, CultureInfo.InvariantCulture, out var baseBudget)
            || !double.IsFinite(baseBudget)
            || baseBudget < 0)
        {
            return Unsupported(budget, terrain, "The stored movement policy requires a finite non-negative baseBudget value.");
        }
        if (!budget.Parameters.TryGetValue("budgetUnit", out var budgetUnit)
            || string.IsNullOrWhiteSpace(budgetUnit))
        {
            return Unsupported(budget, terrain, "The stored movement policy requires a budgetUnit value.");
        }
        if (!budget.Parameters.TryGetValue("limitingScope", out var limitingScope)
            || !SupportedLimitingScopes.Contains(limitingScope))
        {
            return Unsupported(budget, terrain, "The stored movement policy has an unsupported limitingScope value.");
        }
        if (string.Equals(budget.Mechanic.Key, GenericProcedureCatalog.JourneyProgressBudgetMechanic, StringComparison.Ordinal)
            && !string.Equals(budgetModel, "journey-progress", StringComparison.Ordinal))
        {
            return Unsupported(budget, terrain, "The stored journey-progress mechanic does not declare the journey-progress budget model.");
        }

        var travelModeKeys = budget.Parameters.TryGetValue("travelModeKeys", out var rawTravelModeKeys)
            ? rawTravelModeKeys
                .Split(';', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                .Distinct(StringComparer.Ordinal)
                .ToArray()
            : [];

        return new MovementCompositionPolicy(
            MovementCompositionPolicySupport.Supported,
            budgetModel,
            baseBudget,
            budgetUnit.Trim(),
            limitingScope,
            travelModeKeys,
            terrain,
            budget.Mechanic.Key,
            budget.Mechanic.Version,
            budget.Mechanic.ExecutionHandler,
            null);
    }

    private static MovementTerrainPolicy ResolveTerrain(CampaignProcedure procedure)
    {
        var selected = procedure.Modules.SingleOrDefault(module =>
            string.Equals(module.Module.Key, GenericProcedureCatalog.TerrainMovementModule, StringComparison.Ordinal));
        if (selected is null)
        {
            return MovementTerrainPolicy.None;
        }
        if (!string.Equals(selected.Mechanic.Key, GenericProcedureCatalog.TerrainMovementPolicyMechanic, StringComparison.Ordinal)
            || selected.Mechanic.Version != 1
            || !string.Equals(selected.Mechanic.ExecutionHandler, GenericProcedureExecutionHandlers.DeclarativeContract, StringComparison.Ordinal))
        {
            return new MovementTerrainPolicy(
                MovementTerrainPolicySupport.Unsupported,
                null,
                new Dictionary<string, string>(StringComparer.Ordinal),
                null,
                null,
                selected.Mechanic.Key,
                selected.Mechanic.Version,
                selected.Mechanic.ExecutionHandler,
                $"Terrain mechanic '{selected.Mechanic.Key}' version {selected.Mechanic.Version} is not supported by movement composition.");
        }
        if (!selected.Parameters.TryGetValue("adjustmentModel", out var adjustmentModel)
            || !SupportedTerrainModels.Contains(adjustmentModel))
        {
            return UnsupportedTerrain(selected, "The stored terrain policy has an unsupported adjustmentModel value.");
        }
        if (!selected.Parameters.TryGetValue("terrainAdjustments", out var rawAdjustments)
            || !TryParseMap(rawAdjustments, out var adjustments))
        {
            return UnsupportedTerrain(selected, "The stored terrain policy has an invalid terrainAdjustments mapping.");
        }
        if (!selected.Parameters.TryGetValue("routeAdjustmentModel", out var routeModel)
            || string.IsNullOrWhiteSpace(routeModel)
            || !selected.Parameters.TryGetValue("weatherAdjustmentModel", out var weatherModel)
            || string.IsNullOrWhiteSpace(weatherModel))
        {
            return UnsupportedTerrain(selected, "The stored terrain policy requires routeAdjustmentModel and weatherAdjustmentModel values.");
        }

        return new MovementTerrainPolicy(
            MovementTerrainPolicySupport.Supported,
            adjustmentModel,
            adjustments,
            routeModel.Trim(),
            weatherModel.Trim(),
            selected.Mechanic.Key,
            selected.Mechanic.Version,
            selected.Mechanic.ExecutionHandler,
            null);
    }

    private static bool IsSupportedBudgetMechanic(MechanicDefinition mechanic) =>
        mechanic.Version == 1
        && string.Equals(mechanic.ExecutionHandler, GenericProcedureExecutionHandlers.DeclarativeContract, StringComparison.Ordinal)
        && (string.Equals(mechanic.Key, GenericProcedureCatalog.MovementBudgetMechanic, StringComparison.Ordinal)
            || string.Equals(mechanic.Key, GenericProcedureCatalog.JourneyProgressBudgetMechanic, StringComparison.Ordinal));

    private static MovementCompositionPolicy Unsupported(
        MaterializedProcedureModule selected,
        MovementTerrainPolicy terrain,
        string reason) => new(
            MovementCompositionPolicySupport.Unsupported,
            null,
            null,
            null,
            null,
            [],
            terrain,
            selected.Mechanic.Key,
            selected.Mechanic.Version,
            selected.Mechanic.ExecutionHandler,
            reason);

    private static MovementTerrainPolicy UnsupportedTerrain(MaterializedProcedureModule selected, string reason) => new(
        MovementTerrainPolicySupport.Unsupported,
        null,
        new Dictionary<string, string>(StringComparer.Ordinal),
        null,
        null,
        selected.Mechanic.Key,
        selected.Mechanic.Version,
        selected.Mechanic.ExecutionHandler,
        reason);

    private static bool TryParseMap(string value, out IReadOnlyDictionary<string, string> result)
    {
        var values = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var entry in value.Split(';', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            var separator = entry.IndexOf('=');
            if (separator <= 0 || separator == entry.Length - 1)
            {
                result = values;
                return false;
            }
            var key = entry[..separator].Trim();
            var mapped = entry[(separator + 1)..].Trim();
            if (key.Length == 0 || mapped.Length == 0 || !values.TryAdd(key, mapped))
            {
                result = values;
                return false;
            }
        }
        result = values;
        return true;
    }
}
