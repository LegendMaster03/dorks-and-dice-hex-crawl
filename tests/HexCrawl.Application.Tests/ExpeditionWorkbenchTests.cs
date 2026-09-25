using HexCrawl.Application;
using HexCrawl.Domain.Presentation;
using HexCrawl.Domain.Procedure;
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
        Assert.Equal(3, CrawlProcedureCatalog.All.Count);
        foreach (var profile in CrawlProcedureCatalog.All)
        {
            profile.Validate();
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
    public async Task CustomizedProcedureAndPresentationSnapshotsSurviveRestart()
    {
        await using var database = await TestDatabase.CreateAsync();
        var (core, workbench) = await database.ServicesAsync();
        var world = await core.CreateOverworldAsync("alice", WorldCommand());
        var startHex = new HexCoordinate(2, -1);
        var customized = CrawlProcedureProfile.SimplifiedFixedDistance() with
        {
            Name = "Six-hour house procedure",
            WatchLength = TimeSpan.FromHours(6),
            EncounterCadence = EncounterCheckCadence.PerDay
        };

        var started = await workbench.StartAsync(
            world.World.Id,
            "alice",
            new StartExpeditionWorkbenchCommand(
                "Survey",
                customized.Key,
                "exploration-map",
                startHex,
                customized));

        var (restartedCore, _) = await database.ServicesAsync();
        var loaded = await restartedCore.GetExpeditionAsync(started.State.Id, "alice");
        Assert.Equal(customized, loaded.Procedure);
        Assert.Equal(TimeSpan.FromHours(6), loaded.Procedure.WatchLength);
        Assert.Equal(EncounterCheckCadence.PerDay, loaded.Procedure.EncounterCadence);
        Assert.Equal("exploration-map", loaded.RequireKnowledge().PresentationPolicy?.Key);
        Assert.Contains(startHex, loaded.RequireKnowledge().KnownHexes);
    }

    [Fact]
    public async Task InvalidCustomizedProcedureIsRejectedByDomainValidation()
    {
        await using var database = await TestDatabase.CreateAsync();
        var (core, workbench) = await database.ServicesAsync();
        var world = await core.CreateOverworldAsync("alice", WorldCommand());
        var invalid = CrawlProcedureProfile.SimplifiedFixedDistance() with
        {
            TravelResolution = TravelResolutionMode.HexSteps,
            TracksIntraHexProgress = true
        };

        await Assert.ThrowsAsync<InvalidOperationException>(() => workbench.StartAsync(
            world.World.Id,
            "alice",
            new StartExpeditionWorkbenchCommand(
                "Invalid",
                invalid.Key,
                "exploration-map",
                new HexCoordinate(0, 0),
                invalid)));
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
    public async Task PerDayEncounterCadenceChecksOncePerTravelDay()
    {
        await using var database = await TestDatabase.CreateAsync();
        var (core, workbench) = await database.ServicesAsync();
        var world = await core.CreateOverworldAsync("alice", WorldCommand());
        var profile = CrawlProcedureProfile.SimplifiedFixedDistance() with
        {
            Name = "Daily encounter procedure",
            EncounterCadence = EncounterCheckCadence.PerDay
        };
        var expedition = await workbench.StartAsync(
            world.World.Id,
            "alice",
            new StartExpeditionWorkbenchCommand(
                "Daily checks",
                profile.Key,
                "exploration-map",
                new HexCoordinate(0, 0),
                profile));

        for (var watch = 0; watch < 7; watch++)
        {
            expedition = await workbench.AdvanceAsync(expedition.State.Id, "alice", new AdvanceExpeditionWorkbenchCommand
            {
                ExpectedVersion = expedition.Version,
                IntendedDirection = 0,
                EffectiveDistance = 1,
                ContinueAcrossBoundaries = true,
                TravelResolutionSource = ResolutionSource.ProcedureDefault,
                EncounterResolutionSource = ResolutionSource.ManualRoll,
                EncounterOutcome = EncounterOutcomeKind.None
            });
        }

        Assert.Equal(7, expedition.State.CompletedWatches);
        Assert.Equal(TimeSpan.FromHours(28), expedition.State.ElapsedTravelTime);
        Assert.Equal(2, expedition.State.History.Count(item => item.Kind == CrawlRuntimeEventKind.EncounterCheckPerformed));
    }

    [Fact]
    public async Task NoneAndPerWatchEncounterCadenceRemainUnchanged()
    {
        await using var database = await TestDatabase.CreateAsync();
        var (core, workbench) = await database.ServicesAsync();
        var world = await core.CreateOverworldAsync("alice", WorldCommand());

        var noneProfile = CrawlProcedureProfile.SimplifiedFixedDistance() with
        {
            Name = "No encounter checks",
            EncounterCadence = EncounterCheckCadence.None
        };
        var none = await workbench.StartAsync(
            world.World.Id,
            "alice",
            new StartExpeditionWorkbenchCommand(
                "No encounters",
                noneProfile.Key,
                "exploration-map",
                new HexCoordinate(0, 0),
                noneProfile));
        for (var watch = 0; watch < 2; watch++)
        {
            none = await workbench.AdvanceAsync(none.State.Id, "alice", new AdvanceExpeditionWorkbenchCommand
            {
                ExpectedVersion = none.Version,
                IntendedDirection = 0,
                EffectiveDistance = 1,
                ContinueAcrossBoundaries = true,
                TravelResolutionSource = ResolutionSource.ProcedureDefault
            });
        }
        Assert.DoesNotContain(none.State.History, item => item.Kind == CrawlRuntimeEventKind.EncounterCheckPerformed);

        var perWatchProfile = CrawlProcedureProfile.SimplifiedFixedDistance() with
        {
            Name = "Per-watch encounter checks",
            EncounterCadence = EncounterCheckCadence.PerWatch
        };
        var perWatch = await workbench.StartAsync(
            world.World.Id,
            "alice",
            new StartExpeditionWorkbenchCommand(
                "Per-watch encounters",
                perWatchProfile.Key,
                "exploration-map",
                new HexCoordinate(0, 0),
                perWatchProfile));
        for (var watch = 0; watch < 2; watch++)
        {
            perWatch = await workbench.AdvanceAsync(perWatch.State.Id, "alice", new AdvanceExpeditionWorkbenchCommand
            {
                ExpectedVersion = perWatch.Version,
                IntendedDirection = 0,
                EffectiveDistance = 1,
                ContinueAcrossBoundaries = true,
                TravelResolutionSource = ResolutionSource.ProcedureDefault,
                EncounterResolutionSource = ResolutionSource.ManualRoll,
                EncounterOutcome = EncounterOutcomeKind.None
            });
        }
        Assert.Equal(2, perWatch.State.History.Count(item => item.Kind == CrawlRuntimeEventKind.EncounterCheckPerformed));
    }

    [Fact]
    public async Task FocusedEncounterResolutionSatisfiesUpcomingPerWatchCheck()
    {
        await using var database = await TestDatabase.CreateAsync();
        var (core, workbench) = await database.ServicesAsync();
        var assistants = await database.AssistantServiceAsync();
        var world = await core.CreateOverworldAsync("alice", WorldCommand());
        var profile = CrawlProcedureProfile.SimplifiedFixedDistance() with
        {
            Name = "Shared encounter cadence",
            EncounterCadence = EncounterCheckCadence.PerWatch
        };
        var expedition = await workbench.StartAsync(
            world.World.Id,
            "alice",
            new StartExpeditionWorkbenchCommand(
                "Shared cadence state",
                profile.Key,
                "exploration-map",
                new HexCoordinate(0, 0),
                profile));

        expedition = await workbench.AdvanceAsync(expedition.State.Id, "alice", new AdvanceExpeditionWorkbenchCommand
        {
            ExpectedVersion = expedition.Version,
            IntendedDirection = 0,
            EffectiveDistance = 1,
            ContinueAcrossBoundaries = true,
            TravelResolutionSource = ResolutionSource.ProcedureDefault,
            EncounterResolutionSource = ResolutionSource.ManualRoll,
            EncounterOutcome = EncounterOutcomeKind.None
        });
        Assert.Equal(1, expedition.State.CompletedWatches);
        Assert.True(ExpeditionProcedureRequirements.IsEncounterCheckDue(profile, expedition.State));

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

        Assert.False(ExpeditionProcedureRequirements.IsEncounterCheckDue(profile, expedition.State));
        Assert.Single(
            expedition.State.History,
            item => item.Kind == CrawlRuntimeEventKind.EncounterCheckPerformed
                && item.WatchNumber == 2);

        expedition = await workbench.AdvanceAsync(expedition.State.Id, "alice", new AdvanceExpeditionWorkbenchCommand
        {
            ExpectedVersion = expedition.Version,
            IntendedDirection = 0,
            EffectiveDistance = 1,
            ContinueAcrossBoundaries = true,
            TravelResolutionSource = ResolutionSource.ProcedureDefault
        });

        Assert.Equal(2, expedition.State.CompletedWatches);
        Assert.Single(
            expedition.State.History,
            item => item.Kind == CrawlRuntimeEventKind.EncounterCheckPerformed
                && item.WatchNumber == 2);
        Assert.DoesNotContain(
            expedition.State.History,
            item => item.Kind == CrawlRuntimeEventKind.EncounterCheckPerformed
                && item.WatchNumber == 2
                && item.Message.Contains("None", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public async Task FocusedNavigationResolutionSatisfiesUpcomingWatchCheck()
    {
        await using var database = await TestDatabase.CreateAsync();
        var (core, workbench) = await database.ServicesAsync();
        var assistants = await database.AssistantServiceAsync();
        var world = await core.CreateOverworldAsync("alice", WorldCommand());
        var profile = CrawlProcedureProfile.SimplifiedFixedDistance() with
        {
            Name = "Shared navigation state",
            UsesNavigationChecks = true,
            UsesPersistentVeer = true
        };
        var expedition = await workbench.StartAsync(
            world.World.Id,
            "alice",
            new StartExpeditionWorkbenchCommand(
                "Shared navigation resolution",
                profile.Key,
                "exploration-map",
                new HexCoordinate(0, 0),
                profile));

        expedition = await assistants.RecordNavigationAsync(
            expedition.State.Id,
            "alice",
            new NavigationAssistantCommand
            {
                ExpectedVersion = expedition.Version,
                IsLost = true,
                VeerSteps = 1,
                IntendedDirection = 0,
                ResolutionSource = ResolutionSource.ManualRoll,
                Note = "focused navigation result"
            });

        Assert.False(ExpeditionProcedureRequirements.IsNavigationResolutionPotentiallyRequired(profile, expedition.State));
        Assert.Single(
            expedition.State.History,
            item => item.Kind == CrawlRuntimeEventKind.NavigationCheckResolved
                && item.WatchNumber == 1);

        expedition = await workbench.AdvanceAsync(expedition.State.Id, "alice", new AdvanceExpeditionWorkbenchCommand
        {
            ExpectedVersion = expedition.Version,
            IntendedDirection = 0,
            EffectiveDistance = 1,
            ContinueAcrossBoundaries = true,
            TravelResolutionSource = ResolutionSource.ProcedureDefault
        });

        Assert.Equal(1, expedition.State.CompletedWatches);
        Assert.True(expedition.State.Navigation.IsLost);
        Assert.Equal(1, expedition.State.Navigation.VeerSteps);
        Assert.Equal(new HexDirection(1), expedition.State.ActualDirection);
        Assert.Single(
            expedition.State.History,
            item => item.Kind == CrawlRuntimeEventKind.NavigationCheckResolved
                && item.WatchNumber == 1);
        Assert.True(ExpeditionProcedureRequirements.IsNavigationResolutionPotentiallyRequired(profile, expedition.State));
    }

    [Fact]
    public async Task LegacyCustomEncounterCadenceSurvivesRestartAndRetainsPerWatchBehavior()
    {
        await using var database = await TestDatabase.CreateAsync();
        var (core, workbench) = await database.ServicesAsync();
        var world = await core.CreateOverworldAsync("alice", WorldCommand());
        var legacyProfile = CrawlProcedureProfile.SimplifiedFixedDistance() with
        {
            Name = "Legacy custom encounter cadence",
            EncounterCadence = EncounterCheckCadence.Custom
        };
        var expedition = await workbench.StartAsync(
            world.World.Id,
            "alice",
            new StartExpeditionWorkbenchCommand(
                "Legacy custom cadence",
                legacyProfile.Key,
                "exploration-map",
                new HexCoordinate(0, 0),
                legacyProfile));

        expedition = await workbench.AdvanceAsync(expedition.State.Id, "alice", new AdvanceExpeditionWorkbenchCommand
        {
            ExpectedVersion = expedition.Version,
            IntendedDirection = 0,
            EffectiveDistance = 1,
            ContinueAcrossBoundaries = true,
            TravelResolutionSource = ResolutionSource.ProcedureDefault,
            EncounterResolutionSource = ResolutionSource.ManualRoll,
            EncounterOutcome = EncounterOutcomeKind.None
        });

        var (restartedCore, restartedWorkbench) = await database.ServicesAsync();
        var loaded = await restartedCore.GetExpeditionAsync(expedition.State.Id, "alice");
        Assert.Equal(EncounterCheckCadence.Custom, loaded.Procedure.EncounterCadence);

        var resumed = await restartedWorkbench.AdvanceAsync(loaded.State.Id, "alice", new AdvanceExpeditionWorkbenchCommand
        {
            ExpectedVersion = loaded.Version,
            IntendedDirection = 0,
            EffectiveDistance = 1,
            ContinueAcrossBoundaries = true,
            TravelResolutionSource = ResolutionSource.ProcedureDefault,
            EncounterResolutionSource = ResolutionSource.ManualRoll,
            EncounterOutcome = EncounterOutcomeKind.None
        });

        Assert.Equal(2, resumed.State.CompletedWatches);
        Assert.Equal(2, resumed.State.History.Count(item => item.Kind == CrawlRuntimeEventKind.EncounterCheckPerformed));
    }

    [Fact]
    public async Task ResolutionTypesKeepIndependentProvenance()
    {
        await using var database = await TestDatabase.CreateAsync();
        var (core, workbench) = await database.ServicesAsync();
        var world = await core.CreateOverworldAsync("alice", WorldCommand());
        var profile = CrawlProcedureProfile.SimplifiedFixedDistance() with
        {
            Name = "Resolved-input procedure",
            UsesNavigationChecks = true,
            EncounterCadence = EncounterCheckCadence.PerWatch
        };
        var expedition = await workbench.StartAsync(
            world.World.Id,
            "alice",
            new StartExpeditionWorkbenchCommand(
                "Provenance",
                profile.Key,
                "exploration-map",
                new HexCoordinate(0, 0),
                profile));

        expedition = await workbench.AdvanceAsync(expedition.State.Id, "alice", new AdvanceExpeditionWorkbenchCommand
        {
            ExpectedVersion = expedition.Version,
            IntendedDirection = 0,
            EffectiveDistance = 1,
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
        var profile = CrawlProcedureProfile.SimplifiedFixedDistance() with
        {
            Name = "Encounter procedure",
            EncounterCadence = EncounterCheckCadence.PerWatch
        };
        var expedition = await workbench.StartAsync(
            world.World.Id,
            "alice",
            new StartExpeditionWorkbenchCommand(
                "DM reveal control",
                profile.Key,
                "dm-controlled",
                new HexCoordinate(0, 0),
                profile));

        expedition = await workbench.AdvanceAsync(expedition.State.Id, "alice", new AdvanceExpeditionWorkbenchCommand
        {
            ExpectedVersion = expedition.Version,
            IntendedDirection = 0,
            EffectiveDistance = 1,
            EncounterOutcome = EncounterOutcomeKind.KeyedLocationDiscovery,
            EncounterHour = 0,
            LocationId = location.Id,
            EncounterResolutionSource = ResolutionSource.ManualRoll
        });

        Assert.False(expedition.RequireKnowledge().Entries.ContainsKey(location.Id));
        Assert.Empty(expedition.RequireKnowledge().KnownHexes);
        Assert.Equal(RuntimePauseReason.EncounterTriggered, expedition.PauseReason);
        Assert.Contains(expedition.State.History, item => item.Kind == CrawlRuntimeEventKind.KeyedLocationEncountered && item.SubjectId == location.Id);
        Assert.Contains(expedition.State.History, item => item.Kind == CrawlRuntimeEventKind.LocationDiscovered && item.SubjectId == location.Id);
    }

    [Fact]
    public async Task LegacyExpeditionWithoutPresentationSnapshotFallsBackToDmControlled()
    {
        await using var database = await TestDatabase.CreateAsync();
        var (core, workbench) = await database.ServicesAsync();
        var world = await core.CreateOverworldAsync("alice", WorldCommand());
        var legacy = await core.StartExpeditionAsync(world.World.Id, "alice", new StartExpeditionCommand(
            "Legacy",
            "simple-fixed-distance",
            new HexCoordinate(0, 0)));
        Assert.Null(legacy.RequireKnowledge().PresentationPolicy);

        var advanced = await workbench.AdvanceAsync(legacy.State.Id, "alice", new AdvanceExpeditionWorkbenchCommand
        {
            ExpectedVersion = legacy.Version,
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
        private readonly string _path;
        public string ConnectionString { get; }

        private TestDatabase(string path)
        {
            _path = path;
            ConnectionString = $"Data Source={path}";
        }

        public static async Task<TestDatabase> CreateAsync()
        {
            var database = new TestDatabase(Path.Combine(Path.GetTempPath(), $"hex-crawl-workbench-{Guid.NewGuid():N}.db"));
            var store = new SqliteHexCrawlStore(database.ConnectionString);
            await store.InitializeAsync();
            return database;
        }

        public async Task<ExpeditionAssistantService> AssistantServiceAsync()
        {
            var store = new SqliteHexCrawlStore(ConnectionString);
            await store.InitializeAsync();
            var core = new HexCrawlService(store);
            var resolver = new CrawlSessionContextResolver(core);
            return new ExpeditionAssistantService(store, core, resolver);
        }

        public async Task<(HexCrawlService Core, ExpeditionWorkbenchService Workbench)> ServicesAsync()
        {
            var store = new SqliteHexCrawlStore(ConnectionString);
            await store.InitializeAsync();
            var core = new HexCrawlService(store);
            var resolver = new CrawlSessionContextResolver(core);
            return (core, new ExpeditionWorkbenchService(store, core, resolver));
        }

        public ValueTask DisposeAsync()
        {
            foreach (var suffix in new[] { "", "-wal", "-shm" })
            {
                var file = _path + suffix;
                if (File.Exists(file)) File.Delete(file);
            }
            return ValueTask.CompletedTask;
        }
    }
}