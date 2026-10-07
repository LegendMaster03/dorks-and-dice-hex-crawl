using HexCrawl.Domain.Presentation;
using HexCrawl.Domain.Runtime;
using HexCrawl.Domain.Spatial;
using HexCrawl.Domain.World;
using HexCrawl.Infrastructure.Persistence;

namespace HexCrawl.Application.Tests;

public sealed class ExpeditionWorkbenchTests
{
    [Fact]
    public void BuiltInProcedureAndPresentationPresetsAreValid()
    {
        Assert.NotEmpty(CrawlProcedureCatalog.All);
        foreach (var preset in CrawlProcedureCatalog.All)
        {
            preset.Validate();
            preset.MaterializeGeneric().Procedure.Validate();
        }

        Assert.Equal(4, MapPresentationPolicyCatalog.All.Count);
        foreach (var policy in MapPresentationPolicyCatalog.All)
        {
            policy.Validate();
        }

        Assert.Contains(MapPresentationPolicyCatalog.All, item => item.Key == "traditional-hidden-hexcrawl");
        Assert.Contains(MapPresentationPolicyCatalog.All, item => item.Key == "exploration-map");
        Assert.Contains(MapPresentationPolicyCatalog.All, item => item.Key == "open-regional-map");
        Assert.Contains(MapPresentationPolicyCatalog.All, item => item.Key == "dm-controlled");
    }

    [Fact]
    public async Task ProcedureAndPresentationSnapshotsSurviveRestart()
    {
        await using var database = await TestDatabase.CreateAsync();
        var (core, workbench) = await database.ServicesAsync();
        var world = await core.CreateOverworldAsync("alice", WorldCommand());
        var startHex = new HexCoordinate(2, -1);

        var started = await workbench.StartAsync(
            world.World.Id,
            "alice",
            new StartExpeditionWorkbenchCommand(
                "Survey",
                "simple-fixed-distance",
                "exploration-map",
                startHex));
        var pinned = started.CampaignProcedure;

        var (restartedCore, _) = await database.ServicesAsync();
        var loaded = await restartedCore.GetExpeditionAsync(started.State.Id, "alice");

        Assert.Equal(pinned, loaded.CampaignProcedure);
        Assert.Equal("exploration-map", loaded.RequireKnowledge().PresentationPolicy?.Key);
        Assert.Contains(startHex, loaded.RequireKnowledge().KnownHexes);
    }

    [Fact]
    public async Task PausedWatchSurvivesRestartAndResumesTheSameWatch()
    {
        await using var database = await TestDatabase.CreateAsync();
        var (core, workbench) = await database.ServicesAsync();
        var world = await core.CreateOverworldAsync("alice", WorldCommand());
        var started = await workbench.StartAsync(
            world.World.Id,
            "alice",
            new StartExpeditionWorkbenchCommand(
                "Restart proof",
                "simple-fixed-distance",
                "exploration-map",
                new HexCoordinate(0, 0)));

        var paused = await workbench.AdvanceAsync(started.State.Id, "alice", new AdvanceExpeditionWorkbenchCommand
        {
            ExpectedVersion = started.Version,
            IntendedDirection = 0,
            EffectiveDistance = 12,
            ContinueAcrossBoundaries = false,
            TravelResolutionSource = ResolutionSource.ManualRoll,
            TravelResolutionNote = "first segment"
        });
        Assert.Equal(RuntimePauseReason.ConditionsReviewRequired, paused.PauseReason);
        Assert.Equal(TimeSpan.FromHours(2), paused.RemainingWatchTime);
        Assert.Equal(1, paused.State.ActiveWatch?.WatchNumber);

        var (restartedCore, restartedWorkbench) = await database.ServicesAsync();
        var loaded = await restartedCore.GetExpeditionAsync(paused.State.Id, "alice");
        Assert.Equal(paused.CampaignProcedure, loaded.CampaignProcedure);
        Assert.Equal(RuntimePauseReason.ConditionsReviewRequired, loaded.PauseReason);
        Assert.Equal(TimeSpan.FromHours(2), loaded.RemainingWatchTime);
        Assert.Equal(1, loaded.State.ActiveWatch?.WatchNumber);

        var resumed = await restartedWorkbench.AdvanceAsync(loaded.State.Id, "alice", new AdvanceExpeditionWorkbenchCommand
        {
            ExpectedVersion = loaded.Version,
            IntendedDirection = 0,
            EffectiveDistance = 6,
            ContinueAcrossBoundaries = true,
            TravelResolutionSource = ResolutionSource.ManualRoll,
            TravelResolutionNote = "resume after terrain review"
        });
        Assert.Null(resumed.PauseReason);
        Assert.Null(resumed.State.ActiveWatch);
        Assert.Equal(1, resumed.State.CompletedWatches);
        Assert.Equal(TimeSpan.FromHours(4), resumed.State.ElapsedTravelTime);
    }

    [Fact]
    public async Task ChangedCourseFeedsFocusedNavigationWithoutAdvancingTravel()
    {
        await using var database = await TestDatabase.CreateAsync();
        var (core, workbench) = await database.ServicesAsync();
        var assistants = await database.AssistantServiceAsync();
        var world = await core.CreateOverworldAsync("alice", WorldCommand());
        var expedition = await workbench.StartAsync(
            world.World.Id,
            "alice",
            new StartExpeditionWorkbenchCommand(
                "Changed course navigation",
                "alexandrian-advanced",
                "exploration-map",
                new HexCoordinate(1, -1)));

        expedition = await core.SetExpeditionCourseIntentAsync(
            expedition.State.Id,
            "alice",
            new SetExpeditionCourseIntentCommand(expedition.Version, 0));
        expedition = await core.SetExpeditionCourseIntentAsync(
            expedition.State.Id,
            "alice",
            new SetExpeditionCourseIntentCommand(expedition.Version, 5));
        var beforeNavigation = expedition.State;

        expedition = await assistants.RecordNavigationAsync(
            expedition.State.Id,
            "alice",
            new NavigationAssistantCommand
            {
                ExpectedVersion = expedition.Version,
                IsLost = true,
                VeerSteps = 1,
                IntendedDirection = 5,
                ResolutionSource = ResolutionSource.ManualRoll,
                ResolutionNote = "changed-course navigation"
            });

        Assert.Equal(new HexDirection(5), expedition.State.IntendedDirection);
        Assert.Equal(new HexDirection(0), expedition.State.ActualDirection);
        Assert.True(expedition.State.Navigation.IsLost);
        Assert.Equal(1, expedition.State.Navigation.VeerSteps);
        Assert.Equal(beforeNavigation.CurrentHex, expedition.State.CurrentHex);
        Assert.Equal(beforeNavigation.Traversal.Progress, expedition.State.Traversal.Progress);
        Assert.Equal(beforeNavigation.DistanceTraveled, expedition.State.DistanceTraveled);
        Assert.Equal(beforeNavigation.ElapsedTravelTime, expedition.State.ElapsedTravelTime);
        Assert.Equal(beforeNavigation.CompletedWatches, expedition.State.CompletedWatches);

        var reloaded = await core.GetExpeditionAsync(expedition.State.Id, "alice");
        Assert.Equal(new HexDirection(5), reloaded.State.IntendedDirection);
        Assert.Equal(new HexDirection(0), reloaded.State.ActualDirection);
    }

    [Fact]
    public async Task FocusedEncounterResolutionUsesPinnedGenericRuntimeState()
    {
        await using var database = await TestDatabase.CreateAsync();
        var (core, workbench) = await database.ServicesAsync();
        var assistants = await database.AssistantServiceAsync();
        var world = await core.CreateOverworldAsync("alice", WorldCommand());
        var expedition = await workbench.StartAsync(
            world.World.Id,
            "alice",
            new StartExpeditionWorkbenchCommand(
                "Shared cadence state",
                "alexandrian-advanced",
                "exploration-map",
                new HexCoordinate(0, 0)));
        var runtime = GenericProcedureRuntime.Bind(expedition.CampaignProcedure);

        Assert.True(ExpeditionProcedureRequirements.IsEncounterCheckDue(runtime, expedition.State));

        expedition = await assistants.RecordEncounterCadenceAsync(
            expedition.State.Id,
            "alice",
            new EncounterCadenceAssistantCommand
            {
                ExpectedVersion = expedition.Version,
                Outcome = EncounterOutcomeKind.WanderingEncounter,
                ResolutionSource = ResolutionSource.ManualRoll,
                Note = "focused assistant result"
            });

        Assert.False(ExpeditionProcedureRequirements.IsEncounterCheckDue(runtime, expedition.State));
        Assert.Single(
            expedition.State.History,
            item => item.Kind == CrawlRuntimeEventKind.EncounterCheckPerformed
                && item.WatchNumber == 1);
    }

    [Fact]
    public async Task FocusedEncounterBlocksTravelAcrossReloadUntilExactOccurrenceIsResolved()
    {
        await using var database = await TestDatabase.CreateAsync();
        var (core, workbench) = await database.ServicesAsync();
        var assistants = await database.AssistantServiceAsync();
        var world = await core.CreateOverworldAsync("alice", WorldCommand());
        var expedition = await workbench.StartAsync(
            world.World.Id,
            "alice",
            new StartExpeditionWorkbenchCommand(
                "Encounter interruption",
                "alexandrian-advanced",
                "exploration-map",
                new HexCoordinate(0, 0)));

        expedition = await core.SetExpeditionCourseIntentAsync(
            expedition.State.Id,
            "alice",
            new SetExpeditionCourseIntentCommand(expedition.Version, 0));
        expedition = await core.SetExpeditionCourseIntentAsync(
            expedition.State.Id,
            "alice",
            new SetExpeditionCourseIntentCommand(expedition.Version, 2));
        Assert.Equal(new HexDirection(2), expedition.State.IntendedDirection);

        expedition = await assistants.RecordNavigationAsync(
            expedition.State.Id,
            "alice",
            new NavigationAssistantCommand
            {
                ExpectedVersion = expedition.Version,
                IsLost = false,
                VeerSteps = 0,
                IntendedDirection = 2,
                ResolutionSource = ResolutionSource.ManualRoll
            });
        expedition = await assistants.RecordEncounterCadenceAsync(
            expedition.State.Id,
            "alice",
            new EncounterCadenceAssistantCommand
            {
                ExpectedVersion = expedition.Version,
                Outcome = EncounterOutcomeKind.WanderingEncounter,
                ResolutionSource = ResolutionSource.ManualRoll,
                Note = "roadside encounter"
            });

        var pending = Assert.IsType<PendingEncounterOccurrence>(expedition.State.PendingEncounter);
        Assert.Equal(RuntimePauseReason.EncounterTriggered, expedition.PauseReason);
        Assert.Equal(
            pending.Id,
            Assert.Single(
                expedition.State.History,
                item => item.Kind == CrawlRuntimeEventKind.EncounterTriggered).EncounterOccurrenceId);

        var reloaded = await core.GetExpeditionAsync(expedition.State.Id, "alice");
        Assert.Equal(pending, reloaded.State.PendingEncounter);
        Assert.Equal(RuntimePauseReason.EncounterTriggered, reloaded.PauseReason);
        Assert.Equal(new HexDirection(2), reloaded.State.IntendedDirection);

        var courseChangeBlocked = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            core.SetExpeditionCourseIntentAsync(
                reloaded.State.Id,
                "alice",
                new SetExpeditionCourseIntentCommand(reloaded.Version, 4)));
        Assert.Contains("pending encounter", courseChangeBlocked.Message, StringComparison.OrdinalIgnoreCase);

        var navigationBlocked = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            assistants.RecordNavigationAsync(
                reloaded.State.Id,
                "alice",
                new NavigationAssistantCommand
                {
                    ExpectedVersion = reloaded.Version,
                    IsLost = true,
                    VeerSteps = 1,
                    IntendedDirection = 3,
                    ResolutionSource = ResolutionSource.ManualRoll
                }));
        Assert.Contains("pending encounter", navigationBlocked.Message, StringComparison.OrdinalIgnoreCase);

        var blocked = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            workbench.AdvanceAsync(
                reloaded.State.Id,
                "alice",
                new AdvanceExpeditionWorkbenchCommand
                {
                    ExpectedVersion = reloaded.Version,
                    IntendedDirection = 2,
                    PaceKey = "normal",
                    ExpectedDistance = 1,
                    ActualDistance = 1,
                    TravelResolutionSource = ResolutionSource.ManualRoll
                }));
        Assert.Contains("pending encounter", blocked.Message, StringComparison.OrdinalIgnoreCase);

        var resolved = await assistants.ResolveEncounterAsync(
            reloaded.State.Id,
            "alice",
            new ResolveEncounterCommand(
                reloaded.Version,
                pending.Id,
                ResolutionSource.DmOverride,
                "table resolution",
                "encounter complete"));

        Assert.Null(resolved.State.PendingEncounter);
        Assert.Null(resolved.PauseReason);
        var resolution = Assert.Single(
            resolved.State.History,
            item => item.Kind == CrawlRuntimeEventKind.EncounterResolved);
        Assert.Equal(pending.Id, resolution.EncounterOccurrenceId);
        Assert.True(resolution.Sequence > pending.TriggerSequence);
        Assert.Equal(new HexDirection(2), resolved.State.IntendedDirection);

        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            assistants.ResolveEncounterAsync(
                resolved.State.Id,
                "alice",
                new ResolveEncounterCommand(
                    resolved.Version,
                    pending.Id,
                    ResolutionSource.DmOverride)));

        var advanced = await workbench.AdvanceAsync(
            resolved.State.Id,
            "alice",
            new AdvanceExpeditionWorkbenchCommand
            {
                ExpectedVersion = resolved.Version,
                IntendedDirection = 2,
                PaceKey = "normal",
                ExpectedDistance = 1,
                ActualDistance = 1,
                TravelResolutionSource = ResolutionSource.ManualRoll
            });

        Assert.True(advanced.State.DistanceTraveled.Value > resolved.State.DistanceTraveled.Value);
        Assert.Single(
            advanced.State.History,
            item => item.Kind == CrawlRuntimeEventKind.EncounterTriggered
                && item.EncounterOccurrenceId == pending.Id);
        Assert.Single(
            advanced.State.History,
            item => item.Kind == CrawlRuntimeEventKind.EncounterResolved
                && item.EncounterOccurrenceId == pending.Id);
    }

    [Fact]
    public async Task NavigationThenFocusedEncounterResolutionPreservesTravelUntilAdvance()
    {
        await using var database = await TestDatabase.CreateAsync();
        var (core, workbench) = await database.ServicesAsync();
        var assistants = await database.AssistantServiceAsync();
        var world = await core.CreateOverworldAsync("alice", WorldCommand());
        var expedition = await workbench.StartAsync(
            world.World.Id,
            "alice",
            new StartExpeditionWorkbenchCommand(
                "Staged travel resolution",
                "alexandrian-advanced",
                "exploration-map",
                new HexCoordinate(0, 0)));
        var procedure = GenericProcedureRuntime.Bind(expedition.CampaignProcedure);
        var originalDistance = expedition.State.DistanceTraveled;
        var originalElapsed = expedition.State.ElapsedTravelTime;
        var originalHex = expedition.State.CurrentHex;

        Assert.True(
            ExpeditionProcedureRequirements.IsNavigationResolutionPotentiallyRequired(
                procedure,
                expedition.State));
        Assert.True(ExpeditionProcedureRequirements.IsEncounterCheckDue(procedure, expedition.State));

        expedition = await assistants.RecordNavigationAsync(
            expedition.State.Id,
            "alice",
            new NavigationAssistantCommand
            {
                ExpectedVersion = expedition.Version,
                IsLost = false,
                VeerSteps = 0,
                IntendedDirection = 2,
                ResolutionSource = ResolutionSource.ManualRoll,
                ResolutionNote = "focused navigation"
            });

        Assert.Equal(new HexDirection(2), expedition.State.IntendedDirection);
        Assert.Equal(originalDistance, expedition.State.DistanceTraveled);
        Assert.Equal(originalElapsed, expedition.State.ElapsedTravelTime);
        Assert.Equal(originalHex, expedition.State.CurrentHex);
        Assert.False(
            ExpeditionProcedureRequirements.IsNavigationResolutionPotentiallyRequired(
                procedure,
                expedition.State));
        Assert.True(ExpeditionProcedureRequirements.IsEncounterCheckDue(procedure, expedition.State));

        expedition = await assistants.RecordEncounterCadenceAsync(
            expedition.State.Id,
            "alice",
            new EncounterCadenceAssistantCommand
            {
                ExpectedVersion = expedition.Version,
                Outcome = EncounterOutcomeKind.None,
                ResolutionSource = ResolutionSource.ManualRoll,
                ResolutionNote = "focused encounter cadence"
            });

        Assert.Equal(new HexDirection(2), expedition.State.IntendedDirection);
        Assert.Equal(originalDistance, expedition.State.DistanceTraveled);
        Assert.Equal(originalElapsed, expedition.State.ElapsedTravelTime);
        Assert.Equal(originalHex, expedition.State.CurrentHex);
        Assert.False(ExpeditionProcedureRequirements.IsEncounterCheckDue(procedure, expedition.State));
        Assert.Single(
            expedition.State.History,
            item => item.Kind == CrawlRuntimeEventKind.EncounterCheckPerformed
                && item.WatchNumber == 1);
        Assert.Single(
            expedition.State.History,
            item => item.Kind == CrawlRuntimeEventKind.NavigationCheckResolved
                && item.WatchNumber == 1);

        var reloaded = await core.GetExpeditionAsync(expedition.State.Id, "alice");
        Assert.False(ExpeditionProcedureRequirements.IsEncounterCheckDue(procedure, reloaded.State));
        Assert.Equal(new HexDirection(2), reloaded.State.IntendedDirection);
        Assert.Single(
            reloaded.State.History,
            item => item.Kind == CrawlRuntimeEventKind.EncounterCheckPerformed
                && item.WatchNumber == 1);

        var advanced = await workbench.AdvanceAsync(
            reloaded.State.Id,
            "alice",
            new AdvanceExpeditionWorkbenchCommand
            {
                ExpectedVersion = reloaded.Version,
                IntendedDirection = 2,
                PaceKey = "normal",
                ExpectedDistance = 1,
                ActualDistance = 1,
                TravelResolutionSource = ResolutionSource.ManualRoll,
                TravelResolutionNote = "resolved movement"
            });

        Assert.True(advanced.State.DistanceTraveled.Value > originalDistance.Value);
        Assert.True(advanced.State.ElapsedTravelTime > originalElapsed);
        Assert.Single(
            advanced.State.History,
            item => item.Kind == CrawlRuntimeEventKind.EncounterCheckPerformed
                && item.WatchNumber == 1);
        Assert.Single(
            advanced.State.History,
            item => item.Kind == CrawlRuntimeEventKind.NavigationCheckResolved
                && item.WatchNumber == 1);
    }

    [Fact]
    public async Task ResolutionTypesKeepIndependentProvenance()
    {
        await using var database = await TestDatabase.CreateAsync();
        var (core, workbench) = await database.ServicesAsync();
        var world = await core.CreateOverworldAsync("alice", WorldCommand());
        var expedition = await workbench.StartAsync(
            world.World.Id,
            "alice",
            new StartExpeditionWorkbenchCommand(
                "Provenance",
                "alexandrian-advanced",
                "exploration-map",
                new HexCoordinate(0, 0)));

        expedition = await workbench.AdvanceAsync(expedition.State.Id, "alice", new AdvanceExpeditionWorkbenchCommand
        {
            ExpectedVersion = expedition.Version,
            IntendedDirection = 0,
            ExpectedDistance = 1,
            ActualDistance = 1,
            NavigationOutcome = NavigationCheckOutcome.Succeeded,
            EncounterOutcome = EncounterOutcomeKind.None,
            TravelResolutionSource = ResolutionSource.ManualRoll,
            TravelResolutionNote = "physical dice",
            NavigationResolutionSource = ResolutionSource.ExternalSystem,
            NavigationResolutionNote = "Rules Core result",
            EncounterResolutionSource = ResolutionSource.ProcedureDefault,
            EncounterResolutionNote = "procedure result"
        });

        var audit = Assert.Single(expedition.State.History, item => item.Kind == CrawlRuntimeEventKind.ResolutionProvenanceRecorded);
        Assert.Contains("travel=ManualRoll (physical dice)", audit.Message);
        Assert.Contains("navigation=ExternalSystem (Rules Core result)", audit.Message);
        Assert.Contains("encounter=ProcedureDefault (procedure result)", audit.Message);
    }

    [Fact]
    public async Task DmControlledPresentationSuppressesAutomaticKeyedDiscoveryButKeepsMechanicalHistory()
    {
        await using var database = await TestDatabase.CreateAsync();
        var (core, workbench) = await database.ServicesAsync();
        var world = await core.CreateOverworldAsync("alice", WorldCommand());
        world = await core.CreateLocationAsync(world.World.Id, "alice", new CreateLocationCommand(
            "Hidden ruin",
            "ruin",
            new WorldPoint(0, 0),
            LocationDiscoverability.Hidden,
            world.Version));
        var location = Assert.Single(world.World.Locations);
        var expedition = await workbench.StartAsync(
            world.World.Id,
            "alice",
            new StartExpeditionWorkbenchCommand(
                "DM reveal control",
                "alexandrian-advanced",
                "dm-controlled",
                new HexCoordinate(0, 0)));

        expedition = await workbench.AdvanceAsync(expedition.State.Id, "alice", new AdvanceExpeditionWorkbenchCommand
        {
            ExpectedVersion = expedition.Version,
            IntendedDirection = 0,
            ExpectedDistance = 1,
            ActualDistance = 1,
            NavigationOutcome = NavigationCheckOutcome.Succeeded,
            EncounterOutcome = EncounterOutcomeKind.KeyedLocationDiscovery,
            EncounterHour = 0,
            LocationId = location.Id,
            TravelResolutionSource = ResolutionSource.ManualRoll,
            NavigationResolutionSource = ResolutionSource.ManualRoll,
            EncounterResolutionSource = ResolutionSource.ManualRoll
        });

        Assert.False(expedition.RequireKnowledge().Entries.ContainsKey(location.Id));
        Assert.Empty(expedition.RequireKnowledge().KnownHexes);
        Assert.Equal(RuntimePauseReason.EncounterTriggered, expedition.PauseReason);
        Assert.Contains(expedition.State.History, item => item.Kind == CrawlRuntimeEventKind.KeyedLocationEncountered && item.SubjectId == location.Id);
        Assert.Contains(expedition.State.History, item => item.Kind == CrawlRuntimeEventKind.LocationDiscovered && item.SubjectId == location.Id);
    }

    [Fact]
    public async Task MissingPresentationSnapshotFallsBackToDmControlled()
    {
        await using var database = await TestDatabase.CreateAsync();
        var (core, workbench) = await database.ServicesAsync();
        var world = await core.CreateOverworldAsync("alice", WorldCommand());
        var expedition = await core.StartExpeditionAsync(world.World.Id, "alice", new StartExpeditionCommand(
            "No presentation snapshot",
            "simple-fixed-distance",
            new HexCoordinate(0, 0)));
        Assert.Null(expedition.RequireKnowledge().PresentationPolicy);

        var advanced = await workbench.AdvanceAsync(expedition.State.Id, "alice", new AdvanceExpeditionWorkbenchCommand
        {
            ExpectedVersion = expedition.Version,
            IntendedDirection = 0,
            EffectiveDistance = 1,
            TravelResolutionSource = ResolutionSource.ManualRoll
        });

        Assert.Equal("dm-controlled", advanced.RequireKnowledge().PresentationPolicy?.Key);
        Assert.Empty(advanced.RequireKnowledge().KnownHexes);
    }

    private static CreateOverworldCommand WorldCommand() => new(
        "Workbench world",
        HexOrientation.PointyTop,
        new WorldPoint(0, 0),
        0,
        1,
        12,
        DistanceUnit.Miles);

    private sealed class TestDatabase : IAsyncDisposable
    {
        private readonly PostgresTestDatabase _database;
        public string ConnectionString => _database.ConnectionString;

        private TestDatabase(PostgresTestDatabase database)
        {
            _database = database;
        }

        public static async Task<TestDatabase> CreateAsync()
        {
            var database = await PostgresTestDatabase.CreateAsync();
            var store = new PostgresHexCrawlStore(database.ConnectionString);
            await store.InitializeAsync();
            return new TestDatabase(database);
        }

        public async Task<ExpeditionAssistantService> AssistantServiceAsync()
        {
            var store = new PostgresHexCrawlStore(ConnectionString);
            await store.InitializeAsync();
            var core = new HexCrawlService(store);
            var resolver = new CrawlSessionContextResolver(core);
            return new ExpeditionAssistantService(store, core, resolver);
        }

        public async Task<(HexCrawlService Core, ExpeditionWorkbenchService Workbench)> ServicesAsync()
        {
            var store = new PostgresHexCrawlStore(ConnectionString);
            await store.InitializeAsync();
            var core = new HexCrawlService(store);
            var resolver = new CrawlSessionContextResolver(core);
            return (core, new ExpeditionWorkbenchService(store, core, resolver));
        }

        public ValueTask DisposeAsync() => _database.DisposeAsync();
    }
}