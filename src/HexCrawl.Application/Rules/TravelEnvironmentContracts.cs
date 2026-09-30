namespace HexCrawl.Application.Rules;

public static class TravelEnvironmentMechanicStates
{
    public const string Resolved = "resolved";
    public const string RequiresAdjudication = "requires-adjudication";
    public const string Conflicted = "conflicted";
}

public static class TravelEnvironmentEvaluationStates
{
    public const string Resolved = "resolved";
    public const string InputRequired = "input-required";
    public const string NotApplicable = "not-applicable";
}

public static class TravelEnvironmentProviderAvailabilityStates
{
    public const string Available = "available";
    public const string Unavailable = "unavailable";
    public const string Failed = "failed";
}

public static class TravelEnvironmentProviderResolutionStates
{
    public const string Resolved = "resolved";
    public const string InputRequired = "input-required";
    public const string NotApplicable = "not-applicable";
    public const string RequiresAdjudication = "requires-adjudication";
    public const string Unsupported = "unsupported";
    public const string Unavailable = "unavailable";
    public const string Failed = "failed";
}

public static class TravelEnvironmentMechanicKeys
{
    public const string WalkDistance = "travel.overland.walk-distance";
    public const string HustleDistance = "travel.overland.hustle-distance";
    public const string StandardTravelDuration = "travel.overland.standard-travel-duration";
    public const string TerrainDistanceFactor = "travel.overland.terrain-distance-factor";
    public const string ForcedMarchCheck = "travel.overland.forced-march-check";
    public const string MountVehicleDistance = "travel.overland.mount-vehicle-distance";
    public const string HamperedMovement = "travel.environment.hampered-movement";
    public const string DifficultTerrainMovementCost = "travel.environment.difficult-terrain-movement-cost";
    public const string HighAltitudeTravelTimeCost = "travel.environment.high-altitude-travel-time-cost";
    public const string DownstreamCurrentSpeedBonus = "travel.water.downstream-current-speed-bonus";
    public const string GuidedDownstreamFloatDuration = "travel.water.guided-downstream-float-duration";
    public const string AvoidGettingLost = "travel.navigation.avoid-getting-lost";
    public const string RecognizeLost = "travel.navigation.recognize-lost";
    public const string SetNewCourse = "travel.navigation.set-new-course";
}

public sealed record TravelEnvironmentProviderMetadata(
    string ProviderKey,
    string DisplayName,
    bool IsDefault = false);

public sealed record TravelEnvironmentProviderCatalogResult(
    TravelEnvironmentProviderMetadata? Provider,
    string Availability,
    TravelEnvironmentCatalogView? Catalog,
    string? Detail = null);

public sealed record TravelEnvironmentProviderResolutionResult(
    TravelEnvironmentProviderMetadata? Provider,
    string MechanicKey,
    string Status,
    TravelEnvironmentEvaluationView? Evaluation,
    IReadOnlyList<string> MissingInputKeys,
    string? Detail = null);

public sealed class OptionalProviderResolutionException : InvalidOperationException
{
    public OptionalProviderResolutionException(
        string mechanicKey,
        string status,
        string message,
        IReadOnlyList<string>? missingInputKeys = null,
        TravelEnvironmentProviderMetadata? provider = null)
        : base(message)
    {
        MechanicKey = mechanicKey;
        Status = status;
        MissingInputKeys = missingInputKeys ?? [];
        Provider = provider;
    }

    public string MechanicKey { get; }
    public string Status { get; }
    public IReadOnlyList<string> MissingInputKeys { get; }
    public TravelEnvironmentProviderMetadata? Provider { get; }
}

public sealed record TravelEnvironmentResolutionRequest(
    Dictionary<string, int>? IntegerInputs = null,
    Dictionary<string, bool>? BooleanInputs = null,
    Dictionary<string, string>? StringInputs = null,
    Dictionary<string, IReadOnlyList<string>>? StringListInputs = null);

public sealed record TravelEnvironmentCatalogView(
    string Scope,
    Guid? CampaignId,
    int? RevisionNumber,
    DateTimeOffset? PublishedAt,
    IReadOnlyList<TravelEnvironmentMechanicView> Mechanics);

public sealed record TravelEnvironmentMechanicView(
    string MechanicKey,
    string State,
    bool CanResolve,
    TravelEnvironmentMechanicDefinition? Definition,
    IReadOnlyList<TravelEnvironmentSourceAttributionView> SourceAttributions);

public sealed record TravelEnvironmentInputDefinition(
    string Key,
    string ValueKind,
    bool Required,
    IReadOnlyList<string>? AllowedValues = null);

public sealed record TravelEnvironmentSelector(string InputKey, string Value);

public sealed record TravelEnvironmentQuantity(
    decimal Value,
    string Unit,
    string? PerUnit = null);

public sealed record TravelEnvironmentQuantityRow(
    IReadOnlyList<TravelEnvironmentSelector> Selectors,
    TravelEnvironmentQuantity Quantity);

public sealed record TravelEnvironmentFactorRow(
    IReadOnlyList<TravelEnvironmentSelector> Selectors,
    decimal Factor);

public sealed record TravelEnvironmentLinearCheckDefinition(
    int BaseDc,
    string StepInputKey,
    int DcPerStep,
    int MinimumStepValue,
    string? AbilityKey,
    string? CompetencyConceptKey,
    string? Cadence,
    string? FailureConsequenceKey);

public sealed record TravelEnvironmentCheckOption(string Key, int Dc);

public sealed record TravelEnvironmentMaximumCheckDefinition(
    string OptionInputKey,
    IReadOnlyList<TravelEnvironmentCheckOption> Options,
    string? AbilityKey,
    string? CompetencyConceptKey,
    string? Cadence,
    string? FailureConsequenceKey);

public sealed record TravelEnvironmentThresholdFactorDefinition(
    string ThresholdInputKey,
    int MinimumInclusive,
    string? ApplicabilityBooleanInputKey,
    decimal Factor);

public sealed record TravelEnvironmentMechanicDefinition(
    string MechanicKey,
    string Kind,
    string DisplayName,
    string ResolutionKind,
    IReadOnlyList<TravelEnvironmentInputDefinition> Inputs,
    IReadOnlyList<TravelEnvironmentQuantityRow>? QuantityRows = null,
    IReadOnlyList<TravelEnvironmentFactorRow>? FactorRows = null,
    decimal? ConstantFactor = null,
    TravelEnvironmentLinearCheckDefinition? LinearCheck = null,
    TravelEnvironmentMaximumCheckDefinition? MaximumCheck = null,
    TravelEnvironmentThresholdFactorDefinition? ThresholdFactor = null,
    string? FactorSemantic = null,
    string? Scale = null);

public sealed record TravelEnvironmentCheckResolution(
    int Dc,
    string? AbilityKey,
    string? CompetencyConceptKey,
    string? Cadence,
    string? FailureConsequenceKey);

public sealed record TravelEnvironmentEvaluationView(
    string MechanicKey,
    string MechanicState,
    string EvaluationState,
    TravelEnvironmentQuantity? Quantity,
    decimal? Factor,
    TravelEnvironmentCheckResolution? Check,
    IReadOnlyList<string> MissingInputKeys,
    IReadOnlyList<TravelEnvironmentSourceAttributionView> SourceAttributions);

public sealed record TravelEnvironmentSourceAttributionView(
    string? PackageKey,
    string? PackageDisplayName,
    string Provider,
    string? SourceCode,
    int? SourceRevisionNumber,
    string? WorkKey,
    string? WorkDisplayName,
    string? GameEdition,
    string? ReleaseKind,
    DateOnly? PublicationDate,
    string? ReferenceKey,
    string? ReferenceTitle,
    string? ReferenceUri,
    bool PresentationRequired,
    bool ReferenceLinkRequired);

public interface ITravelEnvironmentProvider
{
    TravelEnvironmentProviderMetadata Metadata { get; }

    Task<TravelEnvironmentProviderCatalogResult> GetCatalogAsync(
        Guid? campaignId,
        CancellationToken cancellationToken = default);

    Task<TravelEnvironmentProviderResolutionResult> ResolveAsync(
        Guid? campaignId,
        string mechanicKey,
        TravelEnvironmentResolutionRequest request,
        CancellationToken cancellationToken = default);
}
