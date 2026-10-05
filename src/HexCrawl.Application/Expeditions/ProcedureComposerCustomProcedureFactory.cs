using System.Globalization;
using HexCrawl.Domain.Procedure;
using HexCrawl.Domain.Runtime;

namespace HexCrawl.Application;

internal static class ProcedureComposerCustomProcedureFactory
{
    public static CampaignProcedure Create()
    {
        // A blank custom procedure is an authoring draft, not a persisted CampaignProcedure.
        // Persistence still requires at least one selected module and validates the final snapshot.
        return new CampaignProcedure
        {
            ProcedureId = Guid.NewGuid(),
            Revision = 1,
            Key = "custom-expedition-procedure",
            Name = "Custom expedition procedure",
            Modules = [],
            Overrides = []
        };
    }

    public static MaterializedProcedureModule CreateDefaultModule(string moduleKey) => moduleKey switch
    {
        GenericProcedureCatalog.TimeIntervalModule => Select(
            GenericProcedureCatalog.TimeIntervalModule,
            GenericProcedureCatalog.FixedIntervalDurationMechanic,
            ("durationTicks", TimeSpan.FromHours(4).Ticks.ToString(CultureInfo.InvariantCulture))),
        GenericProcedureCatalog.MovementResolutionModule => Select(
            GenericProcedureCatalog.MovementResolutionModule,
            GenericProcedureCatalog.MovementResolutionPolicyMechanic,
            ("travelResolution", TravelResolutionMode.ContinuousDistance.ToString()),
            ("actualDistanceResolution", ActualDistanceResolutionMode.Fixed.ToString()),
            ("tracksIntraHexProgress", "true")),
        GenericProcedureCatalog.HexProgressModule => Select(
            GenericProcedureCatalog.HexProgressModule,
            GenericProcedureCatalog.HexProgressPolicyMechanic,
            ("startingExitProgressFactor", "0.5"),
            ("nearExitProgressFactor", "0.5"),
            ("farExitProgressFactor", "1"),
            ("backExitProgressFactor", "0.5"),
            ("directionChangesCostProgress", "false"),
            ("directionChangeProgressCostFactor", "0"),
            ("supportsDeliberateDoubleBack", "false")),
        GenericProcedureCatalog.NavigationModule => Select(
            GenericProcedureCatalog.NavigationModule,
            GenericProcedureCatalog.NavigationCheckPolicyMechanic,
            ("usesNavigationChecks", "false"),
            ("usesPersistentVeer", "false")),
        GenericProcedureCatalog.EncounterCadenceModule => Select(
            GenericProcedureCatalog.EncounterCadenceModule,
            GenericProcedureCatalog.EncounterCheckCadenceMechanic,
            ("cadence", EncounterCheckCadence.None.ToString())),
        GenericProcedureCatalog.ResolutionHelpersModule => Select(
            GenericProcedureCatalog.ResolutionHelpersModule,
            GenericProcedureCatalog.DeterministicResolutionHelpersMechanic,
            ("travel.enabled", "false"),
            ("navigation.enabled", "false"),
            ("encounter.enabled", "false")),
        GenericProcedureCatalog.MovementBudgetModule => Select(
            GenericProcedureCatalog.MovementBudgetModule,
            GenericProcedureCatalog.MovementBudgetMechanic,
            ("budgetModel", "fixed-per-interval"),
            ("baseBudget", "1"),
            ("budgetUnit", "interval"),
            ("limitingScope", "party")),
        GenericProcedureCatalog.TerrainMovementModule => Select(
            GenericProcedureCatalog.TerrainMovementModule,
            GenericProcedureCatalog.TerrainMovementPolicyMechanic,
            ("adjustmentModel", "multiplier"),
            ("terrainAdjustments", "default=1"),
            ("routeAdjustmentModel", "none"),
            ("weatherAdjustmentModel", "manual")),
        GenericProcedureCatalog.PartyActivitiesModule => Select(
            GenericProcedureCatalog.PartyActivitiesModule,
            GenericProcedureCatalog.ParticipantActivityPolicyMechanic,
            ("assignmentScope", "participant"),
            ("activityBudgetModel", "per-interval"),
            ("activityKeys", "travel;navigate;forage;search;watch"),
            ("roleKeys", "navigator;lookout")),
        GenericProcedureCatalog.NavigationOutcomeModule => Select(
            GenericProcedureCatalog.NavigationOutcomeModule,
            GenericProcedureCatalog.NavigationOutcomePolicyMechanic,
            ("checkTriggerModel", "manual-or-procedure"),
            ("failureStateModel", "lost-state"),
            ("directionalErrorModel", "manual-off-course"),
            ("recognitionModel", "manual"),
            ("reorientationModel", "manual")),
        GenericProcedureCatalog.EncounterScheduleModule => Select(
            GenericProcedureCatalog.EncounterScheduleModule,
            GenericProcedureCatalog.EncounterSchedulePolicyMechanic,
            ("scheduleModel", "cadence-backed"),
            ("travelChecksPerInterval", "0"),
            ("campCheck", "false"),
            ("terrainProbabilityModel", "none")),
        GenericProcedureCatalog.ResourceConsumptionModule => Select(
            GenericProcedureCatalog.ResourceConsumptionModule,
            GenericProcedureCatalog.ResourceConsumptionPolicyMechanic,
            ("resourceKinds", "food;water"),
            ("inventoryModel", "counted"),
            ("consumptionModel", "manual"),
            ("consumptionInterval", "interval")),
        GenericProcedureCatalog.ForagingModule => Select(
            GenericProcedureCatalog.ForagingModule,
            GenericProcedureCatalog.ForagingPolicyMechanic,
            ("resolutionModel", "manual-check"),
            ("timeCost", "1"),
            ("timeUnit", "activity"),
            ("movementTradeoff", "replaces-activity")),
        GenericProcedureCatalog.CampingModule => Select(
            GenericProcedureCatalog.CampingModule,
            GenericProcedureCatalog.CampingPolicyMechanic,
            ("resolutionModel", "manual-camp"),
            ("timeCost", "1"),
            ("timeUnit", "activity"),
            ("watchModel", "manual")),
        GenericProcedureCatalog.ForcedTravelModule => Select(
            GenericProcedureCatalog.ForcedTravelModule,
            GenericProcedureCatalog.ForcedTravelPolicyMechanic,
            ("normalTravelLimit", "2"),
            ("limitUnit", "intervals"),
            ("checkModel", "manual-check"),
            ("failureConsequence", "fatigue")),
        GenericProcedureCatalog.PersistentEffectsModule => Select(
            GenericProcedureCatalog.PersistentEffectsModule,
            GenericProcedureCatalog.ProgressiveExpeditionEffectMechanic,
            ("effectKinds", "fatigue"),
            ("accumulationModel", "levels"),
            ("recoveryModel", "rest"),
            ("scope", "participant")),
        Phase11GenericProcedureCatalog.ExposureModule => Select(
            Phase11GenericProcedureCatalog.ExposureModuleDefinition,
            Phase11GenericProcedureCatalog.ExposureMechanicDefinition,
            ("dimensions", "temperature;weather"),
            ("evaluationModel", "resolved-check"),
            ("evaluationInterval", "travel-day"),
            ("targetScope", "party"),
            ("consequenceModel", "resolved-structured-consequence")),
        GenericProcedureCatalog.JourneyEventsModule => Select(
            GenericProcedureCatalog.JourneyEventsModule,
            GenericProcedureCatalog.JourneyEventPolicyMechanic,
            ("triggerModel", "manual-or-landmark"),
            ("targetingModel", "explicit-target"),
            ("terrainInfluence", "manual"),
            ("consequenceModel", "event"),
            ("triggerSources", "explicit;landmark;watch-completed"),
            ("linkMode", "both"),
            ("requiresResolvedTrigger", "true"),
            ("blocksRelevantTravelWhileResolutionRequired", "false")),
        GenericProcedureCatalog.JourneyProcessModule => Select(
            GenericProcedureCatalog.JourneyProcessModule,
            GenericProcedureCatalog.MultiStageExpeditionProcessMechanic,
            ("stageModel", "manual-stages"),
            ("progressModel", "progress-points"),
            ("completionModel", "explicit-completion"),
            ("roleDriven", "false"),
            ("stageTransitionModel", "explicit"),
            ("progressKind", "numeric"),
            ("progressUnit", "progress-points"),
            ("allowNegativeProgress", "false"),
            ("roleAssignmentModel", "current-at-resolution"),
            ("intervalIntegrationModel", "completed-watch-resolution-opportunity"),
            ("blocksRelevantTravelWhileResolutionRequired", "false")),
        _ => throw new InvalidOperationException(
            $"Module '{moduleKey}' does not have a generic Composer default selection.")
    };

    private static MaterializedProcedureModule Select(
        string moduleKey,
        string mechanicKey,
        params (string Key, string Value)[] parameters)
    {
        var module = JourneyProcedureContractSchema.ExtendModule(GenericProcedureCatalog.ResolveModule(moduleKey));
        var mechanic = JourneyProcedureContractSchema.ExtendMechanic(
            moduleKey,
            GenericProcedureCatalog.ResolveMechanic(mechanicKey));
        return Select(module, mechanic, parameters);
    }

    private static MaterializedProcedureModule Select(
        ProcedureModuleDefinition module,
        MechanicDefinition mechanic,
        params (string Key, string Value)[] parameters) =>
        new(
            CampaignProcedureSnapshot.Copy(module),
            CampaignProcedureSnapshot.Copy(mechanic),
            parameters.ToDictionary(value => value.Key, value => value.Value, StringComparer.Ordinal));
}
