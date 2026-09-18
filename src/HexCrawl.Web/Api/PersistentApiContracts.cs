using HexCrawl.Application;
using HexCrawl.Application.Persistence;
using HexCrawl.Domain.Knowledge;
using HexCrawl.Domain.Procedure;
using HexCrawl.Domain.Runtime;
using HexCrawl.Domain.Spatial;
using HexCrawl.Domain.World;

namespace HexCrawl.Web.Api;

public sealed record DistanceUnitContract(DistanceUnitKind Kind, string Symbol, double? MetersPerUnit)
{
    public static DistanceUnitContract From(DistanceUnit unit) => new(unit.Kind, unit.Symbol, unit.MetersPerUnit);

    public DistanceUnit ToDomain() => Kind switch
    {
        DistanceUnitKind.Mile => DistanceUnit.Miles,
        DistanceUnitKind.Kilometer => DistanceUnit.Kilometers,
        DistanceUnitKind.Custom => DistanceUnit.Custom(Symbol, MetersPerUnit),
        _ => throw new ArgumentOutOfRangeException(nameof(Kind))
    };
}

public sealed record DistanceContract(double Value, DistanceUnitContract Unit)
{
    public static DistanceContract From(DistanceMeasure value) => new(value.Value, DistanceUnitContract.From(value.Unit));
}

public sealed record GridContract(
    Guid Id,
    HexOrientation Orientation,
    HexCoordinateConvention CoordinateConvention,
    WorldPoint Origin,
    double RotationDegrees,
    double HexRadiusWorldUnits,
    DistanceContract NeighborCenterDistance)
{
    public static GridContract From(HexGridDefinition grid) => new(
        grid.Id,
        grid.Orientation,
        grid.CoordinateConvention,
        grid.Origin,
        grid.RotationDegrees,
        grid.HexRadiusWorldUnits,
        DistanceContract.From(grid.NeighborCenterDistance));

    public HexGridDefinition ToDomain() => new()
    {
        Id = Id,
        Orientation = Orientation,
        CoordinateConvention = CoordinateConvention,
        Origin = Origin,
        RotationDegrees = RotationDegrees,
        HexRadiusWorldUnits = HexRadiusWorldUnits,
        NeighborCenterDistance = new DistanceMeasure(NeighborCenterDistance.Value, NeighborCenterDistance.Unit.ToDomain())
    };
}

public sealed record FeatureContract(
    Guid Id,
    string Name,
    string Category,
    SpatialFeatureKind Kind,
    WorldPoint? Position,
    IReadOnlyList<WorldPoint>? Path,
    IReadOnlyList<WorldPoint>? Boundary)
{
    public static FeatureContract From(SpatialFeature feature) => feature switch
    {
        PointFeature point => new(point.Id, point.Name, point.Category, point.Kind, point.Position, null, null),
        LinearFeature line => new(line.Id, line.Name, line.Category, line.Kind, null, line.Path, null),
        RegionFeature region => new(region.Id, region.Name, region.Category, region.Kind, null, null, region.Boundary),
        _ => throw new ArgumentOutOfRangeException(nameof(feature))
    };
}

public sealed record LocationContract(
    Guid Id,
    string Name,
    string Category,
    WorldPoint Position,
    LocationDiscoverability Discoverability)
{
    public static LocationContract From(Location location) => new(
        location.Id,
        location.Name,
        location.Category,
        location.Position,
        location.Discoverability);
}

public sealed record SourceMapContract(
    Guid Id,
    string GeographyKey,
    string Name,
    SourceMapRole Role,
    string AssetKey,
    bool ContainsBakedGrid,
    MapRegistrationTransform? Alignment,
    IReadOnlyList<WorldPoint> WorldCoverageBoundary)
{
    public static SourceMapContract From(SourceMapRepresentation sourceMap) => new(
        sourceMap.Id,
        sourceMap.GeographyKey,
        sourceMap.Name,
        sourceMap.Role,
        sourceMap.AssetKey,
        sourceMap.ContainsBakedGrid,
        sourceMap.Alignment,
        sourceMap.WorldCoverageBoundary);
}

public sealed record OverworldContract(
    Guid Id,
    string Name,
    long Version,
    DateTimeOffset CreatedAt,
    DateTimeOffset UpdatedAt,
    GridContract Grid,
    IReadOnlyList<FeatureContract> Features,
    IReadOnlyList<LocationContract> Locations,
    IReadOnlyList<SourceMapContract> SourceMaps)
{
    public static OverworldContract From(StoredOverworld world) => new(
        world.World.Id,
        world.World.Name,
        world.Version,
        world.CreatedAt,
        world.UpdatedAt,
        GridContract.From(world.World.Grid),
        world.World.Features.Select(FeatureContract.From).ToArray(),
        world.World.Locations.Select(LocationContract.From).ToArray(),
        world.World.SourceMaps.Select(SourceMapContract.From).ToArray());
}

public sealed record CreateOverworldRequest(
    string Name,
    HexOrientation Orientation,
    WorldPoint Origin,
    double RotationDegrees,
    double HexRadiusWorldUnits,
    double NeighborCenterDistance,
    DistanceUnitContract DistanceUnit)
{
    public CreateOverworldCommand ToCommand() => new(
        Name,
        Orientation,
        Origin,
        RotationDegrees,
        HexRadiusWorldUnits,
        NeighborCenterDistance,
        DistanceUnit.ToDomain());
}

public sealed record UpdateOverworldRequest(string Name, GridContract Grid, long ExpectedVersion)
{
    public UpdateOverworldCommand ToCommand() => new(Name, Grid.ToDomain(), ExpectedVersion);
}

public sealed record LocationMutationRequest(
    string Name,
    string Category,
    WorldPoint Position,
    LocationDiscoverability Discoverability,
    long ExpectedVersion)
{
    public CreateLocationCommand ToCreateCommand() => new(Name, Category, Position, Discoverability, ExpectedVersion);
    public UpdateLocationCommand ToUpdateCommand() => new(Name, Category, Position, Discoverability, ExpectedVersion);
}

public sealed record FeatureMutationRequest(
    string Name,
    string Category,
    SpatialFeatureKind Kind,
    WorldPoint? Position,
    IReadOnlyList<WorldPoint>? Path,
    IReadOnlyList<WorldPoint>? Boundary,
    long ExpectedVersion)
{
    public CreateFeatureCommand ToCreateCommand() => new(Name, Category, Kind, Position, Path, Boundary, ExpectedVersion);
    public UpdateFeatureCommand ToUpdateCommand() => new(Name, Category, Kind, Position, Path, Boundary, ExpectedVersion);
}

public sealed record SourceMapMutationRequest(
    string GeographyKey,
    string Name,
    SourceMapRole Role,
    string AssetKey,
    bool ContainsBakedGrid,
    MapRegistrationTransform? Alignment,
    IReadOnlyList<WorldPoint> WorldCoverageBoundary,
    long ExpectedVersion)
{
    public CreateSourceMapCommand ToCreateCommand() => new(
        GeographyKey, Name, Role, AssetKey, ContainsBakedGrid, Alignment, WorldCoverageBoundary, ExpectedVersion);
    public UpdateSourceMapCommand ToUpdateCommand() => new(
        GeographyKey, Name, Role, AssetKey, ContainsBakedGrid, Alignment, WorldCoverageBoundary, ExpectedVersion);
}

public sealed record DiceRollFormulaContract(int DiceCount, int DieSides, int Modifier)
{
    public static DiceRollFormulaContract From(DiceRollFormula formula) =>
        new(formula.DiceCount, formula.DieSides, formula.Modifier);

    public DiceRollFormula ToDomain() => new(DiceCount, DieSides, Modifier);
}

public sealed record TravelResolutionHelperProfileContract(
    DiceRollFormulaContract Roll,
    double DistanceFactorPerRollPoint)
{
    public static TravelResolutionHelperProfileContract From(TravelResolutionHelperProfile profile) =>
        new(DiceRollFormulaContract.From(profile.Roll), profile.DistanceFactorPerRollPoint);

    public TravelResolutionHelperProfile ToDomain() =>
        new(Roll.ToDomain(), DistanceFactorPerRollPoint);
}

public sealed record NavigationResolutionHelperProfileContract(DiceRollFormulaContract CheckRoll)
{
    public static NavigationResolutionHelperProfileContract From(NavigationResolutionHelperProfile profile) =>
        new(DiceRollFormulaContract.From(profile.CheckRoll));

    public NavigationResolutionHelperProfile ToDomain() => new(CheckRoll.ToDomain());
}

public sealed record EncounterResolutionHelperProfileContract(
    DiceRollFormulaContract CheckRoll,
    IReadOnlyList<int> WanderingResults,
    IReadOnlyList<int> KeyedLocationResults,
    int TimingSlots)
{
    public static EncounterResolutionHelperProfileContract From(EncounterResolutionHelperProfile profile) =>
        new(
            DiceRollFormulaContract.From(profile.CheckRoll),
            profile.WanderingResults.Values,
            profile.KeyedLocationResults.Values,
            profile.TimingSlots);

    public EncounterResolutionHelperProfile ToDomain() =>
        new(
            CheckRoll.ToDomain(),
            DiceRollResultSet.From(WanderingResults),
            DiceRollResultSet.From(KeyedLocationResults),
            TimingSlots);
}

public sealed record ProcedureResolutionHelperProfileContract(
    TravelResolutionHelperProfileContract? Travel,
    NavigationResolutionHelperProfileContract? Navigation,
    EncounterResolutionHelperProfileContract? Encounter)
{
    public static ProcedureResolutionHelperProfileContract From(ProcedureResolutionHelperProfile profile) =>
        new(
            profile.Travel is null ? null : TravelResolutionHelperProfileContract.From(profile.Travel),
            profile.Navigation is null ? null : NavigationResolutionHelperProfileContract.From(profile.Navigation),
            profile.Encounter is null ? null : EncounterResolutionHelperProfileContract.From(profile.Encounter));

    public ProcedureResolutionHelperProfile ToDomain() =>
        new(Travel?.ToDomain(), Navigation?.ToDomain(), Encounter?.ToDomain());
}

public sealed record RuntimeProfileContract(
    string Key,
    string Name,
    double WatchHours,
    TravelResolutionMode TravelResolution,
    ActualDistanceResolutionMode ActualDistanceResolution,
    EncounterCheckCadence EncounterCadence,
    bool UsesNavigationChecks,
    bool UsesPersistentVeer,
    bool TracksIntraHexProgress,
    bool DirectionChangesCostProgress,
    bool SupportsDeliberateDoubleBack,
    double StartingExitProgressFactor,
    double NearExitProgressFactor,
    double FarExitProgressFactor,
    double BackExitProgressFactor,
    double DirectionChangeProgressCostFactor,
    ProcedureResolutionHelperProfileContract? ResolutionHelpers = null)
{
    public static RuntimeProfileContract From(CrawlProcedureProfile profile) => new(
        profile.Key,
        profile.Name,
        profile.WatchLength.TotalHours,
        profile.TravelResolution,
        profile.ActualDistanceResolution,
        profile.EncounterCadence,
        profile.UsesNavigationChecks,
        profile.UsesPersistentVeer,
        profile.TracksIntraHexProgress,
        profile.DirectionChangesCostProgress,
        profile.SupportsDeliberateDoubleBack,
        profile.StartingExitProgressFactor,
        profile.NearExitProgressFactor,
        profile.FarExitProgressFactor,
        profile.BackExitProgressFactor,
        profile.DirectionChangeProgressCostFactor,
        profile.ResolutionHelpers is null ? null : ProcedureResolutionHelperProfileContract.From(profile.ResolutionHelpers));
}

public sealed record ExpeditionStateContract(
    Guid Id,
    HexCoordinate CurrentHex,
    WorldPoint? Position,
    WorldPositionPrecision? PositionPrecision,
    int? IntendedDirection,
    int? ActualDirection,
    bool IsLost,
    int VeerSteps,
    double VeerDegrees,
    DistanceContract DistanceTraveled,
    DistanceContract HexProgress,
    DistanceContract? ExitRequirement,
    double ElapsedTravelHours,
    int CompletedWatches,
    int? ActiveWatchNumber,
    string? ActivePaceKey,
    IReadOnlyList<string> ActiveActivities,
    string? ActiveNavigationAidKey)
{
    public static ExpeditionStateContract From(ExpeditionState expedition) => new(
        expedition.Id,
        expedition.CurrentHex,
        expedition.Position,
        expedition.PositionPrecision,
        expedition.IntendedDirection?.Value,
        expedition.ActualDirection?.Value,
        expedition.Navigation.IsLost,
        expedition.Navigation.VeerSteps,
        expedition.Navigation.VeerDegrees,
        DistanceContract.From(expedition.DistanceTraveled),
        DistanceContract.From(expedition.Traversal.Progress),
        expedition.Traversal.CurrentExitRequirement is { } requirement ? DistanceContract.From(requirement) : null,
        expedition.ElapsedTravelTime.TotalHours,
        expedition.CompletedWatches,
        expedition.ActiveWatch?.WatchNumber,
        expedition.ActiveWatch?.Plan.Mode.PaceKey,
        expedition.ActiveWatch?.Plan.Mode.Activities ?? [],
        expedition.ActiveWatch?.Plan.NavigationAid.Key);
}

public sealed record KnowledgeEntryContract(
    Guid SubjectId,
    KnowledgeSubjectType SubjectType,
    KnowledgeState State,
    DateTimeOffset? LearnedAt,
    string? Source)
{
    public static KnowledgeEntryContract From(KnowledgeEntry entry) => new(
        entry.SubjectId, entry.SubjectType, entry.State, entry.LearnedAt, entry.Source);
}

public sealed record RuntimeEventContract(
    long Sequence,
    int WatchNumber,
    CrawlRuntimeEventKind Kind,
    double ExpeditionElapsedHours,
    HexCoordinate? Hex,
    string Message,
    double? DistanceValue,
    string? DistanceUnit,
    Guid? SubjectId,
    KnowledgeSubjectType? SubjectType)
{
    public static RuntimeEventContract From(CrawlRuntimeEvent runtimeEvent) => new(
        runtimeEvent.Sequence,
        runtimeEvent.WatchNumber,
        runtimeEvent.Kind,
        runtimeEvent.ExpeditionElapsedTime.TotalHours,
        runtimeEvent.Hex,
        runtimeEvent.Message,
        runtimeEvent.DistanceValue,
        runtimeEvent.DistanceUnit,
        runtimeEvent.SubjectId,
        runtimeEvent.SubjectType);
}

public sealed record ExpeditionContract(
    Guid Id,
    CrawlSessionContextKind ContextKind,
    Guid? OverworldId,
    string Name,
    long Version,
    DateTimeOffset CreatedAt,
    DateTimeOffset UpdatedAt,
    RuntimeProfileContract Profile,
    RuntimePauseReason? PauseReason,
    double RemainingWatchHours,
    ExpeditionStateContract? Expedition,
    IReadOnlyList<KnowledgeEntryContract> Knowledge,
    IReadOnlyList<RuntimeEventContract> History)
{
    public static ExpeditionContract From(StoredExpedition expedition) => new(
        expedition.Id,
        expedition.Context.Kind,
        expedition.Context.OverworldId,
        expedition.Name,
        expedition.Version,
        expedition.CreatedAt,
        expedition.UpdatedAt,
        RuntimeProfileContract.From(expedition.Procedure),
        expedition.PauseReason,
        expedition.RemainingWatchTime.TotalHours,
        expedition.Runtime is ExpeditionState spatial ? ExpeditionStateContract.From(spatial) : null,
        expedition.Knowledge?.Entries.Values
            .OrderBy(item => item.SubjectType)
            .ThenBy(item => item.SubjectId)
            .Select(KnowledgeEntryContract.From)
            .ToArray() ?? [],
        expedition.Runtime.History.Select(RuntimeEventContract.From).ToArray());
}

public sealed record StartExpeditionRequest(string Name, string ProcedureKey, HexCoordinate StartHex)
{
    public StartExpeditionCommand ToCommand() => new(Name, ProcedureKey, StartHex);
}

public sealed record AdvanceExpeditionRequest
{
    public long ExpectedVersion { get; init; }
    public int IntendedDirection { get; init; }
    public string PaceKey { get; init; } = "normal";
    public IReadOnlyList<string> Activities { get; init; } = [];
    public string NavigationAidKey { get; init; } = "none";
    public bool SuppressesNavigationCheck { get; init; }
    public bool ResetsVeerAtBoundary { get; init; }
    public double? ExpectedDistance { get; init; }
    public double? ActualDistance { get; init; }
    public int? HexSteps { get; init; }
    public ResolutionSource ResolutionSource { get; init; } = ResolutionSource.ManualRoll;
    public NavigationCheckOutcome? NavigationOutcome { get; init; }
    public int? VeerSteps { get; init; }
    public EncounterOutcomeKind? EncounterOutcome { get; init; }
    public double? EncounterHour { get; init; }
    public Guid? LocationId { get; init; }
    public string? EncounterNote { get; init; }
    public bool DeliberateDoubleBack { get; init; }
    public bool ContinueAcrossBoundaries { get; init; }
    public bool? RecognizedLost { get; init; }
    public bool? Reorient { get; init; }
    public string? DmOverrideNote { get; init; }

    public AdvanceExpeditionCommand ToCommand() => new()
    {
        ExpectedVersion = ExpectedVersion,
        IntendedDirection = IntendedDirection,
        PaceKey = PaceKey,
        Activities = Activities,
        NavigationAidKey = NavigationAidKey,
        SuppressesNavigationCheck = SuppressesNavigationCheck,
        ResetsVeerAtBoundary = ResetsVeerAtBoundary,
        ExpectedDistance = ExpectedDistance,
        ActualDistance = ActualDistance,
        HexSteps = HexSteps,
        ResolutionSource = ResolutionSource,
        NavigationOutcome = NavigationOutcome,
        VeerSteps = VeerSteps,
        EncounterOutcome = EncounterOutcome,
        EncounterHour = EncounterHour,
        LocationId = LocationId,
        EncounterNote = EncounterNote,
        DeliberateDoubleBack = DeliberateDoubleBack,
        ContinueAcrossBoundaries = ContinueAcrossBoundaries,
        RecognizedLost = RecognizedLost,
        Reorient = Reorient,
        DmOverrideNote = DmOverrideNote
    };
}

public sealed record DiscoverSubjectRequest(
    long ExpectedVersion,
    Guid SubjectId,
    KnowledgeSubjectType SubjectType,
    string? Source)
{
    public DiscoverSubjectCommand ToCommand() => new(ExpectedVersion, SubjectId, SubjectType, Source);
}
