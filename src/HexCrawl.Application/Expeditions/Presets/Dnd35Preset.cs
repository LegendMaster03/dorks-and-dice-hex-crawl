using HexCrawl.Domain.Procedure;
using static HexCrawl.Application.CrawlProcedurePresetFactory;
using static HexCrawl.Application.CrawlProcedureCatalog;

namespace HexCrawl.Application.Presets;

internal static class Dnd35Preset
{
    internal static CrawlProcedurePresetDefinition Create() =>
        Preset(
            CampaignProcedureSchema.CurrentHexTilingDsSymbol,
            Dnd35PresetKey,
            "D&D 3.5e",
            "Hourly overland travel with difficult terrain, getting lost, foraging, forced-march checks, and fatigue consequences.",
            "hourly-overland-travel",
            "Hourly overland travel",
            [
                NativeTime(TimeSpan.FromHours(1)),
                Structural(GenericProcedureCatalog.MovementBudgetModule, GenericProcedureCatalog.MovementBudgetMechanic,
                    ("budgetModel", "speed-derived-distance"), ("baseBudget", "1"), ("budgetUnit", "hour"), ("limitingScope", "party-limiting")),
                Structural(GenericProcedureCatalog.TerrainMovementModule, GenericProcedureCatalog.TerrainMovementPolicyMechanic,
                    ("adjustmentModel", "multiplier"), ("terrainAdjustments", "normal=1;difficult=0.5"), ("routeAdjustmentModel", "route-may-ignore-terrain"), ("weatherAdjustmentModel", "manual")),
                Structural(GenericProcedureCatalog.NavigationOutcomeModule, GenericProcedureCatalog.NavigationOutcomePolicyMechanic,
                    ("checkTriggerModel", "hourly-poor-visibility-or-terrain"), ("failureStateModel", "lost-until-recognized"), ("directionalErrorModel", "random-direction"), ("recognitionModel", "periodic-check"), ("reorientationModel", "procedure-check")),
                Structural(GenericProcedureCatalog.ForagingModule, GenericProcedureCatalog.ForagingPolicyMechanic,
                    ("resolutionModel", "skill-check"), ("timeCost", "0.5"), ("timeUnit", "movement-rate"), ("movementTradeoff", "half-speed")),
                Structural(GenericProcedureCatalog.ForcedTravelModule, GenericProcedureCatalog.ForcedTravelPolicyMechanic,
                    ("normalTravelLimit", "8"), ("limitUnit", "hours"), ("checkModel", "escalating-check"), ("failureConsequence", "fatigue-and-nonlethal-effect")),
                Structural(GenericProcedureCatalog.PersistentEffectsModule, GenericProcedureCatalog.ProgressiveExpeditionEffectMechanic,
                    ("effectKinds", "fatigue;nonlethal-damage"), ("accumulationModel", "per-failed-check"), ("recoveryModel", "rest"), ("scope", "participant"))
            ],
            attribution: "D&D 3.5 SRD wilderness, movement, and Survival procedure research.");
}
