using HexCrawl.Domain.Procedure;

namespace HexCrawl.Application.Tests;

/// <summary>
/// Test-only composition recipes for architectural proofs that must not become production presets.
/// </summary>
internal static class SyntheticProcedureFixtures
{
    public static CampaignProcedure MixedProcedure()
    {
        var source = new CampaignProcedure
        {
            ProcedureId = Guid.NewGuid(),
            Revision = 1,
            Key = "synthetic-mixed-procedure",
            Name = "Synthetic mixed procedure",
            Modules = [],
            Overrides = []
        };

        ProcedureModuleSelection[] modules =
        [
            Include(GenericProcedureCatalog.TimeIntervalModule),
            Include(GenericProcedureCatalog.MovementResolutionModule),
            Include(GenericProcedureCatalog.HexProgressModule),
            Include(GenericProcedureCatalog.NavigationModule),
            Include(GenericProcedureCatalog.EncounterCadenceModule),
            Include(GenericProcedureCatalog.ResolutionHelpersModule),
            Include(GenericProcedureCatalog.MovementBudgetModule),
            Include(GenericProcedureCatalog.TerrainMovementModule),
            Include(GenericProcedureCatalog.PartyActivitiesModule),
            Include(GenericProcedureCatalog.NavigationOutcomeModule),
            Include(GenericProcedureCatalog.ResourceConsumptionModule),
            Include(GenericProcedureCatalog.ForagingModule),
            Include(GenericProcedureCatalog.CampingModule),
            Include(GenericProcedureCatalog.ForcedTravelModule),
            Include(GenericProcedureCatalog.PersistentEffectsModule),
            Include(GenericProcedureCatalog.JourneyEventsModule)
        ];

        CampaignProcedureOverride[] overrides =
        [
            Change(GenericProcedureCatalog.MovementBudgetModule,
                ("budgetModel", "activity-and-distance"),
                ("baseBudget", "1"),
                ("budgetUnit", "watch"),
                ("limitingScope", "party-limiting")),
            Change(GenericProcedureCatalog.TerrainMovementModule,
                ("adjustmentModel", "activity-cost"),
                ("terrainAdjustments", "open=1;difficult=2;severe=3"),
                ("routeAdjustmentModel", "road-improves-one-step"),
                ("weatherAdjustmentModel", "manual")),
            Change(GenericProcedureCatalog.PartyActivitiesModule,
                ("assignmentScope", "participant"),
                ("activityBudgetModel", "per-watch"),
                ("activityKeys", "travel;reconnoiter;forage;make-camp;lookout"),
                ("roleKeys", "navigator;lookout;forager;scout")),
            Change(GenericProcedureCatalog.NavigationOutcomeModule,
                ("checkTriggerModel", "per-watch-when-navigation-required"),
                ("failureStateModel", "lost-until-recognized"),
                ("directionalErrorModel", "persistent-veer"),
                ("recognitionModel", "boundary-check"),
                ("reorientationModel", "procedure-check")),
            Change(GenericProcedureCatalog.ResourceConsumptionModule,
                ("resourceKinds", "food;water;light"),
                ("inventoryModel", "supply-die"),
                ("consumptionModel", "usage-roll"),
                ("consumptionInterval", "watch")),
            Change(GenericProcedureCatalog.ForagingModule,
                ("resolutionModel", "activity-check"),
                ("timeCost", "1"),
                ("timeUnit", "watch-activity"),
                ("movementTradeoff", "replaces-activity")),
            Change(GenericProcedureCatalog.CampingModule,
                ("resolutionModel", "activity-check"),
                ("timeCost", "1"),
                ("timeUnit", "watch"),
                ("watchModel", "assigned-lookout")),
            Change(GenericProcedureCatalog.ForcedTravelModule,
                ("normalTravelLimit", "2"),
                ("limitUnit", "watches"),
                ("checkModel", "escalating-check"),
                ("failureConsequence", "fatigue")),
            Change(GenericProcedureCatalog.PersistentEffectsModule,
                ("effectKinds", "fatigue"),
                ("accumulationModel", "levels"),
                ("recoveryModel", "safe-rest"),
                ("scope", "participant")),
            Change(GenericProcedureCatalog.JourneyEventsModule,
                ("triggerModel", "per-watch-or-landmark"),
                ("triggerSources", "watch-completed;landmark;explicit"),
                ("linkMode", "standalone"),
                ("targetingModel", "travel-role"),
                ("terrainInfluence", "difficulty"),
                ("consequenceModel", "event-and-fatigue"),
                ("requiresResolvedTrigger", "true"),
                ("blocksRelevantTravelWhileResolutionRequired", "false"))
        ];

        var procedure = CampaignProcedureMaterializer.CreateInitialRevision(source, modules, overrides);
        procedure.Validate();
        return procedure;
    }

    private static ProcedureModuleSelection Include(string moduleKey) => new(moduleKey, true);

    private static CampaignProcedureOverride Change(
        string moduleKey,
        params (string Key, string Value)[] parameters) =>
        new(
            $"synthetic-{moduleKey}",
            moduleKey,
            null,
            null,
            parameters.ToDictionary(value => value.Key, value => value.Value, StringComparer.Ordinal));
}
