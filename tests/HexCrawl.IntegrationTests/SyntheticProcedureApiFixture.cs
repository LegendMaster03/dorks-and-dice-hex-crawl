using System.Net.Http.Json;
using System.Text.Json;
using HexCrawl.Application;

namespace HexCrawl.IntegrationTests;

internal sealed record SyntheticProcedureRevision(Guid ProcedureId, int Revision);

/// <summary>
/// Builds test-only mixed procedures through the production authoring API.
/// The recipe deliberately composes independent generic procedure families without
/// introducing a production preset identity.
/// </summary>
internal static class SyntheticProcedureApiFixture
{
    public static async Task<SyntheticProcedureRevision> CreateMixedAsync(HttpClient client)
    {
        using var response = await client.PostAsJsonAsync("/api/procedures", new
        {
            presetKey = (string?)null,
            campaignId = (Guid?)null,
            name = "Synthetic mixed procedure",
            moduleSelections = new[]
            {
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
            },
            overrides = new object[]
            {
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
            }
        });
        response.EnsureSuccessStatusCode();
        var stored = await response.Content.ReadFromJsonAsync<JsonElement>();
        return new SyntheticProcedureRevision(
            stored.GetProperty("procedureId").GetGuid(),
            stored.GetProperty("revision").GetInt32());
    }

    public static async Task<SyntheticProcedureRevision> CreatePacedExecutableAsync(HttpClient client)
    {
        using var response = await client.PostAsJsonAsync("/api/procedures", new
        {
            presetKey = "simple-fixed-distance",
            campaignId = (Guid?)null,
            name = "Synthetic paced executable procedure",
            moduleSelections = new[]
            {
                Include(GenericProcedureCatalog.MovementBudgetModule)
            },
            overrides = new object[]
            {
                Change(GenericProcedureCatalog.MovementBudgetModule,
                    ("budgetModel", "fixed-watch-distance"),
                    ("baseBudget", "12"),
                    ("budgetUnit", "mi"),
                    ("limitingScope", "party"),
                    ("travelModeKeys", "normal;fast;slow"))
            }
        });
        response.EnsureSuccessStatusCode();
        var stored = await response.Content.ReadFromJsonAsync<JsonElement>();
        return new SyntheticProcedureRevision(
            stored.GetProperty("procedureId").GetGuid(),
            stored.GetProperty("revision").GetInt32());
    }

    private static object Include(string moduleKey) => new { moduleKey, included = true };

    private static object Change(string moduleKey, params (string Key, string Value)[] parameters) => new
    {
        overrideId = $"synthetic-{moduleKey}",
        moduleKey,
        replacementMechanicKey = (string?)null,
        replacementMechanicVersion = (int?)null,
        parameters = parameters.ToDictionary(value => value.Key, value => value.Value, StringComparer.Ordinal),
        note = (string?)null
    };
}
