using HexCrawl.Domain.Procedure;
using static HexCrawl.Application.CrawlProcedurePresetFactory;
using static HexCrawl.Application.CrawlProcedureCatalog;

namespace HexCrawl.Application.Presets;

internal static class OneRing2ePreset
{
    internal static CrawlProcedurePresetDefinition Create() =>
        Preset(
            CampaignProcedureSchema.CurrentHexTilingDsSymbol,
            OneRing2ePresetKey,
            "The One Ring 2e",
            "Role-driven journeys with route planning, Guide progress, journey events, terrain influence, and fatigue.",
            "role-driven-journey",
            "Role-driven journey",
            [
                Structural(GenericProcedureCatalog.MovementBudgetModule, GenericProcedureCatalog.JourneyProgressBudgetMechanic,
                    ("budgetModel", "journey-progress"), ("baseBudget", "1"), ("budgetUnit", "journey-leg"), ("limitingScope", "guide")),
                Structural(GenericProcedureCatalog.TerrainMovementModule, GenericProcedureCatalog.TerrainMovementPolicyMechanic,
                    ("adjustmentModel", "terrain-difficulty"), ("terrainAdjustments", "open=1;hard=2;severe=3"), ("routeAdjustmentModel", "road-assistance"), ("weatherAdjustmentModel", "manual")),
                Structural(GenericProcedureCatalog.PartyActivitiesModule, GenericProcedureCatalog.JourneyRoleActivityPolicyMechanic,
                    ("assignmentScope", "role"), ("activityBudgetModel", "journey-role"), ("activityKeys", "guide;hunt;lookout;scout"), ("roleKeys", "guide;hunter;lookout;scout")),
                Structural(GenericProcedureCatalog.JourneyProcessModule, GenericProcedureCatalog.MultiStageExpeditionProcessMechanic,
                    ("stageModel", "route-then-events-then-arrival"),
                    ("stageKeys", "route;events;arrival"),
                    ("stageTransitionModel", "sequential"),
                    ("progressModel", "guide-marching-progress"),
                    ("progressKind", "numeric"),
                    ("progressUnit", "journey-progress"),
                    ("allowNegativeProgress", "false"),
                    ("completionModel", "final-stage-completion"),
                    ("roleDriven", "true"),
                    ("roleAssignmentModel", "current-at-resolution"),
                    ("intervalIntegrationModel", "none"),
                    ("blocksRelevantTravelWhileResolutionRequired", "false")),
                Structural(GenericProcedureCatalog.JourneyEventsModule, GenericProcedureCatalog.ProgressTriggeredJourneyEventPolicyMechanic,
                    ("triggerModel", "guide-progress-test"),
                    ("triggerSources", "process-progress"),
                    ("linkMode", "process-linked"),
                    ("targetingModel", "travel-role"),
                    ("terrainInfluence", "difficulty-and-road"),
                    ("consequenceModel", "event-and-fatigue"),
                    ("requiresResolvedTrigger", "true"),
                    ("blocksRelevantTravelWhileResolutionRequired", "false")),
                Structural(GenericProcedureCatalog.PersistentEffectsModule, GenericProcedureCatalog.ProgressiveExpeditionEffectMechanic,
                    ("effectKinds", "fatigue"), ("accumulationModel", "journey-events"), ("recoveryModel", "safe-prolonged-rest"), ("scope", "participant"))
            ],
            attribution: "The One Ring 2e journey procedure research from publisher materials and secondary summaries.",
            disclaimer: "Exact event tables, distances, modifiers, and fatigue values are intentionally not encoded; resolved journey details remain explicit DM/provider input.");
}
