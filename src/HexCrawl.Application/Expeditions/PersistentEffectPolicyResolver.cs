using HexCrawl.Domain.Procedure;
using HexCrawl.Domain.Runtime;

namespace HexCrawl.Application;

public enum PersistentEffectPolicySupport
{
    None,
    Supported,
    Unsupported
}

public sealed record PersistentEffectPolicy(
    PersistentEffectPolicySupport Support,
    IReadOnlyList<string> EffectKinds,
    string? AccumulationModel,
    string? RecoveryModel,
    ExpeditionEffectScope? Scope,
    string? MechanicKey,
    int? MechanicVersion,
    string? ExecutionHandler,
    string? UnsupportedReason)
{
    public static PersistentEffectPolicy None { get; } = new(
        PersistentEffectPolicySupport.None,
        [], null, null, null, null, null, null, null);
}

/// <summary>
/// Focused Phase 10 projection over the exact effects.expedition module stored on a CampaignProcedure.
/// Preset identity, origin metadata, and the current catalog are deliberately not execution inputs.
/// </summary>
public static class PersistentEffectPolicyResolver
{
    public static PersistentEffectPolicy Resolve(CampaignProcedure procedure)
    {
        ArgumentNullException.ThrowIfNull(procedure);
        var selected = procedure.Modules.SingleOrDefault(value =>
            string.Equals(value.Module.Key, GenericProcedureCatalog.PersistentEffectsModule, StringComparison.Ordinal));
        if (selected is null)
        {
            return PersistentEffectPolicy.None;
        }

        if (!string.Equals(selected.Mechanic.Key, GenericProcedureCatalog.ProgressiveExpeditionEffectMechanic, StringComparison.Ordinal))
        {
            return Unsupported(selected, $"Effect module mechanic '{selected.Mechanic.Key}' is not supported by Phase 10.");
        }
        if (selected.Mechanic.Version != 1)
        {
            return Unsupported(selected, $"Effect mechanic version {selected.Mechanic.Version} is not supported by Phase 10.");
        }
        if (!string.Equals(selected.Mechanic.ExecutionHandler, GenericProcedureExecutionHandlers.DeclarativeContract, StringComparison.Ordinal))
        {
            return Unsupported(selected, $"Effect mechanic handler '{selected.Mechanic.ExecutionHandler}' is not supported by Phase 10.");
        }

        if (!TryRequired(selected.Parameters, "effectKinds", out var effectKindsValue)
            || !TryRequired(selected.Parameters, "accumulationModel", out var accumulationModel)
            || !TryRequired(selected.Parameters, "recoveryModel", out var recoveryModel)
            || !TryRequired(selected.Parameters, "scope", out var scopeValue))
        {
            return Unsupported(selected, "The pinned effect module is missing required Phase 10 parameters.");
        }

        var effectKinds = effectKindsValue
            .Split(';', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Distinct(StringComparer.Ordinal)
            .ToArray();
        if (effectKinds.Length == 0 || effectKinds.Any(string.IsNullOrWhiteSpace))
        {
            return Unsupported(selected, "The pinned effect module does not declare any effect kinds.");
        }
        if (!TryScope(scopeValue, out var scope))
        {
            return Unsupported(selected, $"Effect scope '{scopeValue}' is not understood by Phase 10.");
        }

        return new PersistentEffectPolicy(
            PersistentEffectPolicySupport.Supported,
            effectKinds,
            accumulationModel,
            recoveryModel,
            scope,
            selected.Mechanic.Key,
            selected.Mechanic.Version,
            selected.Mechanic.ExecutionHandler,
            null);
    }

    private static PersistentEffectPolicy Unsupported(MaterializedProcedureModule selected, string reason) => new(
        PersistentEffectPolicySupport.Unsupported,
        ParseKinds(selected.Parameters),
        selected.Parameters.GetValueOrDefault("accumulationModel"),
        selected.Parameters.GetValueOrDefault("recoveryModel"),
        selected.Parameters.TryGetValue("scope", out var scopeValue) && TryScope(scopeValue, out var scope) ? scope : null,
        selected.Mechanic.Key,
        selected.Mechanic.Version,
        selected.Mechanic.ExecutionHandler,
        reason);

    private static IReadOnlyList<string> ParseKinds(IReadOnlyDictionary<string, string> parameters) =>
        parameters.TryGetValue("effectKinds", out var value)
            ? value.Split(';', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                .Distinct(StringComparer.Ordinal).ToArray()
            : [];

    private static bool TryRequired(
        IReadOnlyDictionary<string, string> parameters,
        string key,
        out string value)
    {
        if (parameters.TryGetValue(key, out var raw) && !string.IsNullOrWhiteSpace(raw))
        {
            value = raw.Trim();
            return true;
        }
        value = string.Empty;
        return false;
    }

    private static bool TryScope(string value, out ExpeditionEffectScope scope)
    {
        scope = value.Trim().ToLowerInvariant() switch
        {
            "participant" => ExpeditionEffectScope.Participant,
            "party" => ExpeditionEffectScope.Party,
            "mount" => ExpeditionEffectScope.Mount,
            "vehicle" => ExpeditionEffectScope.Vehicle,
            "expedition" => ExpeditionEffectScope.Expedition,
            _ => default
        };
        return value.Trim().ToLowerInvariant() is "participant" or "party" or "mount" or "vehicle" or "expedition";
    }
}
