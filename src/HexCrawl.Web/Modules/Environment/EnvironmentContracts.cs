using HexCrawl.Application;
using HexCrawl.Application.Persistence;
using HexCrawl.Domain.Spatial;
using HexCrawl.Domain.World;

namespace HexCrawl.Web.Api;

public sealed record EnvironmentMeasurementContract(double Value, string Unit)
{
    public static EnvironmentMeasurementContract From(EnvironmentMeasurement value) => new(value.Value, value.Unit);
    public EnvironmentMeasurement ToDomain() => new(Value, Unit);
}

public sealed record EnvironmentFactContract(
    Guid Id,
    string Dimension,
    EnvironmentValueKind ValueKind,
    string? Tag,
    EnvironmentMeasurementContract? Measurement,
    string? Provenance,
    string? Note)
{
    public static EnvironmentFactContract From(EnvironmentFact value) => new(
        value.Id, value.Dimension, value.ValueKind, value.Tag,
        value.Measurement is null ? null : EnvironmentMeasurementContract.From(value.Measurement),
        value.Provenance, value.Note);

    public EnvironmentFact ToDomain() => new()
    {
        Id = Id,
        Dimension = Dimension,
        ValueKind = ValueKind,
        Tag = Tag,
        Measurement = Measurement?.ToDomain(),
        Provenance = Provenance,
        Note = Note
    };
}

public sealed record EnvironmentAnnotationScopeContract(
    EnvironmentAnnotationScopeKind Kind,
    HexCoordinate? Hex,
    Guid? FeatureId)
{
    public static EnvironmentAnnotationScopeContract From(EnvironmentAnnotationScope value) =>
        new(value.Kind, value.Hex, value.FeatureId);

    public EnvironmentAnnotationScope ToDomain() => new() { Kind = Kind, Hex = Hex, FeatureId = FeatureId };
}

public sealed record EnvironmentAnnotationContract(
    Guid Id,
    EnvironmentAnnotationScopeContract Scope,
    IReadOnlyList<EnvironmentFactContract> Facts)
{
    public static EnvironmentAnnotationContract From(EnvironmentAnnotation value) => new(
        value.Id,
        EnvironmentAnnotationScopeContract.From(value.Scope),
        value.Facts.Select(EnvironmentFactContract.From).ToArray());

    public EnvironmentAnnotation ToDomain() => new()
    {
        Id = Id,
        Scope = Scope.ToDomain(),
        Facts = Facts.Select(value => value.ToDomain()).ToArray()
    };
}

public sealed record EnvironmentFactSourceContract(
    EnvironmentFactSourceKind Kind,
    Guid? AnnotationId,
    HexCoordinate? Hex,
    Guid? FeatureId,
    string? FeatureName)
{
    public static EnvironmentFactSourceContract From(EnvironmentFactSource value) => new(
        value.Kind, value.AnnotationId, value.Hex, value.FeatureId, value.FeatureName);
}

public sealed record EffectiveEnvironmentFactContract(
    EnvironmentFactContract Fact,
    EnvironmentFactSourceContract Source,
    int Precedence,
    bool Effective)
{
    public static EffectiveEnvironmentFactContract From(EffectiveEnvironmentFact value) => new(
        EnvironmentFactContract.From(value.Fact), EnvironmentFactSourceContract.From(value.Source),
        value.Precedence, value.Effective);
}

public sealed record EnvironmentConflictContract(
    string Dimension,
    IReadOnlyList<EffectiveEnvironmentFactContract> Candidates,
    string Detail)
{
    public static EnvironmentConflictContract From(EnvironmentConflict value) => new(
        value.Dimension, value.Candidates.Select(EffectiveEnvironmentFactContract.From).ToArray(), value.Detail);
}

public sealed record EffectiveEnvironmentContextContract(
    EnvironmentContextStatus Status,
    IReadOnlyList<EffectiveEnvironmentFactContract> Facts,
    IReadOnlyList<EnvironmentConflictContract> Conflicts)
{
    public static EffectiveEnvironmentContextContract From(EffectiveEnvironmentContext value) => new(
        value.Status,
        value.Facts.Select(EffectiveEnvironmentFactContract.From).ToArray(),
        value.Conflicts.Select(EnvironmentConflictContract.From).ToArray());
}

public sealed record ExpeditionEnvironmentStateContract(
    IReadOnlyList<EnvironmentFactContract> CurrentFacts,
    IReadOnlyList<EnvironmentFactContract> Overrides)
{
    public static ExpeditionEnvironmentStateContract From(ExpeditionEnvironmentState value) => new(
        value.CurrentFacts.Select(EnvironmentFactContract.From).ToArray(),
        value.Overrides.Select(EnvironmentFactContract.From).ToArray());
}

public sealed record EnvironmentProcedureEvaluationContract(
    EnvironmentProcedureEvaluationStatus Status,
    string? TerrainKey,
    string? RouteKey,
    string? WeatherAdjustmentModel,
    IReadOnlyList<string> MissingInputs,
    IReadOnlyList<string> UnsupportedSemantics,
    IReadOnlyList<string> Diagnostics,
    IReadOnlyList<string> Provenance)
{
    public static EnvironmentProcedureEvaluationContract From(EnvironmentProcedureEvaluation value) => new(
        value.Status, value.TerrainKey, value.RouteKey, value.WeatherAdjustmentModel,
        value.MissingInputs, value.UnsupportedSemantics, value.Diagnostics, value.Provenance);
}

public sealed record EnvironmentWorkbenchContract(
    ExpeditionEnvironmentStateContract State,
    EffectiveEnvironmentContextContract EffectiveContext,
    EnvironmentProcedureEvaluationContract Evaluation,
    MovementCapabilityCompositionContract MovementComposition)
{
    public static EnvironmentWorkbenchContract From(StoredExpedition expedition, OverworldDefinition? world = null)
    {
        var context = EnvironmentContextResolver.Resolve(expedition, world);
        var evaluation = EnvironmentProcedureEvaluator.Evaluate(expedition, context);
        var movement = MovementCapabilityComposer.Compose(expedition, evaluation.MovementInput);
        return new(
            ExpeditionEnvironmentStateContract.From(expedition.Environment),
            EffectiveEnvironmentContextContract.From(context),
            EnvironmentProcedureEvaluationContract.From(evaluation),
            MovementCapabilityCompositionContract.From(movement));
    }
}

public sealed record ReplaceWorldEnvironmentRequest(
    long ExpectedVersion,
    IReadOnlyList<EnvironmentAnnotationContract> Annotations)
{
    public ReplaceWorldEnvironmentCommand ToCommand() => new(
        ExpectedVersion, Annotations.Select(value => value.ToDomain()).ToArray());
}

public sealed record UpdateExpeditionEnvironmentRequest(
    long ExpectedVersion,
    IReadOnlyList<EnvironmentFactContract> CurrentFacts,
    IReadOnlyList<EnvironmentFactContract> Overrides)
{
    public UpdateExpeditionEnvironmentCommand ToCommand() => new(
        ExpectedVersion,
        CurrentFacts.Select(value => value.ToDomain()).ToArray(),
        Overrides.Select(value => value.ToDomain()).ToArray());
}
