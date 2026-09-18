using HexCrawl.Application.Persistence;
using HexCrawl.Domain.Procedure;
using HexCrawl.Domain.Runtime;

namespace HexCrawl.Application;

public sealed class GeneratedProcedureResolutionVerifier
{
    public GeneratedProcedureResolution? VerifyWorkbench(
        StoredExpedition expedition,
        ExpeditionState state,
        CrawlProcedureProfile runtimeProfile,
        AdvanceExpeditionWorkbenchCommand command)
    {
        if (command.ResolutionSource == ResolutionSource.AutomaticRoll)
        {
            throw new InvalidOperationException(
                "AutomaticRoll can not be supplied as a blanket resolution source. Use a server-generated resolution id with the specific generated component.");
        }
        if (command.BoundaryResolutionSource == ResolutionSource.AutomaticRoll)
        {
            throw new InvalidOperationException("The procedure helper does not generate lost-boundary decisions.");
        }

        var travelAutomatic = command.TravelResolutionSource == ResolutionSource.AutomaticRoll;
        var navigationAutomatic = command.NavigationResolutionSource == ResolutionSource.AutomaticRoll;
        var encounterAutomatic = command.EncounterResolutionSource == ResolutionSource.AutomaticRoll;
        if (!travelAutomatic && !navigationAutomatic && !encounterAutomatic)
        {
            RejectUnusedId(command.GeneratedProcedureResolutionId);
            return null;
        }

        var generated = RequireAvailable(expedition, command.ExpectedVersion, command.GeneratedProcedureResolutionId);

        if (travelAutomatic)
        {
            var expected = generated.Travel
                ?? throw new InvalidOperationException("The selected helper generation did not produce a travel result.");
            if (runtimeProfile.TravelResolution != TravelResolutionMode.ContinuousDistance)
            {
                throw new InvalidOperationException("Automatic physical-distance travel is not applicable to this procedure.");
            }

            if (runtimeProfile.ActualDistanceResolution == ActualDistanceResolutionMode.VariableResolved)
            {
                RequireEqual(command.ExpectedDistance, expected.ExpectedDistance, "expected travel distance");
                RequireEqual(command.ActualDistance, expected.ActualDistance, "actual travel distance");
            }
            else
            {
                var submitted = command.EffectiveDistance ?? command.ActualDistance ?? command.ExpectedDistance;
                RequireEqual(submitted, expected.ActualDistance, "effective travel distance");
            }
        }

        if (navigationAutomatic)
        {
            if (state.ActiveWatch is not null
                || !runtimeProfile.UsesNavigationChecks
                || command.SuppressesNavigationCheck
                || command.DeliberateDoubleBack)
            {
                throw new InvalidOperationException("Automatic navigation resolution is not applicable to this watch.");
            }

            var expected = generated.Navigation
                ?? throw new InvalidOperationException("The selected helper generation did not produce a navigation result.");
            if (command.NavigationOutcome != expected.Outcome
                || command.VeerSteps != expected.VeerSteps)
            {
                throw new InvalidOperationException(
                    "The submitted navigation values do not match the persisted generated result.");
            }
        }

        if (encounterAutomatic)
        {
            if (state.ActiveWatch is not null || runtimeProfile.EncounterCadence == EncounterCheckCadence.None)
            {
                throw new InvalidOperationException("Automatic encounter resolution is not applicable to this watch.");
            }

            var expected = generated.Encounter
                ?? throw new InvalidOperationException("The selected helper generation did not produce an encounter result.");
            if (command.EncounterOutcome != expected.Kind)
            {
                throw new InvalidOperationException(
                    "The submitted encounter outcome does not match the persisted generated result.");
            }
            RequireEqual(command.EncounterHour, expected.OccursAtHours, "encounter time");

            if (expected.LocationId.HasValue && command.LocationId != expected.LocationId)
            {
                throw new InvalidOperationException(
                    "The submitted keyed location does not match the persisted generated result.");
            }
            if (expected.Kind != EncounterOutcomeKind.KeyedLocationDiscovery
                && command.LocationId.HasValue != expected.LocationId.HasValue)
            {
                throw new InvalidOperationException(
                    "The submitted encounter location does not match the persisted generated result.");
            }
        }

        return generated;
    }

    public GeneratedProcedureResolution? VerifyTravelAssistant(
        StoredExpedition expedition,
        TravelWatchAssistantCommand command)
    {
        if (command.ResolutionSource != ResolutionSource.AutomaticRoll)
        {
            RejectUnusedId(command.GeneratedProcedureResolutionId);
            return null;
        }

        var generated = RequireAvailable(expedition, command.ExpectedVersion, command.GeneratedProcedureResolutionId);
        var expected = generated.Travel
            ?? throw new InvalidOperationException("The selected helper generation did not produce a travel result.");
        RequireEqual(command.Distance, expected.ActualDistance, "assistant travel distance");
        RequireEqual(command.ElapsedHours, expected.SegmentHours, "assistant travel duration");
        return generated;
    }

    public GeneratedProcedureResolution? VerifyNavigationAssistant(
        StoredExpedition expedition,
        NavigationAssistantCommand command)
    {
        if (command.ResolutionSource != ResolutionSource.AutomaticRoll)
        {
            RejectUnusedId(command.GeneratedProcedureResolutionId);
            return null;
        }

        var generated = RequireAvailable(expedition, command.ExpectedVersion, command.GeneratedProcedureResolutionId);
        var expected = generated.Navigation
            ?? throw new InvalidOperationException("The selected helper generation did not produce a navigation result.");
        if (command.IsLost != expected.ResultingIsLost || command.VeerSteps != expected.ResultingVeerSteps)
        {
            throw new InvalidOperationException(
                "The submitted navigation assistant state does not match the persisted generated result.");
        }
        return generated;
    }

    public GeneratedProcedureResolution? VerifyEncounterAssistant(
        StoredExpedition expedition,
        EncounterCadenceAssistantCommand command)
    {
        if (command.ResolutionSource != ResolutionSource.AutomaticRoll)
        {
            RejectUnusedId(command.GeneratedProcedureResolutionId);
            return null;
        }

        var generated = RequireAvailable(expedition, command.ExpectedVersion, command.GeneratedProcedureResolutionId);
        var expected = generated.Encounter
            ?? throw new InvalidOperationException("The selected helper generation did not produce an encounter result.");
        if (command.Outcome != expected.Kind)
        {
            throw new InvalidOperationException(
                "The submitted encounter assistant outcome does not match the persisted generated result.");
        }
        RequireEqual(command.EncounterHour, expected.OccursAtHours, "assistant encounter time");
        return generated;
    }

    public ResolutionProvenance Provenance(
        ResolutionSource source,
        string? note,
        GeneratedProcedureResolution? generated,
        Func<GeneratedProcedureResolution, ResolutionProvenance?> automaticComponent)
    {
        if (source == ResolutionSource.AutomaticRoll)
        {
            return generated is null
                ? throw new InvalidOperationException("AutomaticRoll requires a verified persisted helper generation.")
                : automaticComponent(generated)
                    ?? throw new InvalidOperationException("The verified helper generation does not contain the requested component.");
        }
        return new ResolutionProvenance(source, note);
    }

    public (CrawlSessionRuntimeState Runtime, IReadOnlyList<GeneratedProcedureResolution> Resolutions)
        FinalizeUse(
            StoredExpedition expedition,
            CrawlSessionRuntimeState runtime,
            GeneratedProcedureResolution? consumed,
            long expectedVersion)
    {
        var resolutions = expedition.GeneratedProcedureResolutions ?? [];
        if (resolutions.Count == 0)
        {
            return (runtime, resolutions);
        }

        long? consumedSequence = null;
        if (consumed is not null)
        {
            consumedSequence = runtime.History.Count == 0 ? 1 : runtime.History[^1].Sequence + 1;
            var elapsed = runtime switch
            {
                ExpeditionState spatial => spatial.ElapsedTravelTime,
                NonSpatialSessionState nonSpatial => nonSpatial.ElapsedTime,
                _ => throw new InvalidOperationException("Unsupported crawl session runtime state.")
            };
            var hex = runtime is ExpeditionState spatialState ? spatialState.CurrentHex : null;
            var audit = new CrawlRuntimeEvent(
                consumedSequence.Value,
                consumed.WatchNumber,
                CrawlRuntimeEventKind.ProcedureResolutionHelperConsumed,
                elapsed,
                hex,
                $"Generated procedure resolution {consumed.Id:D} consumed for watch {consumed.WatchNumber}.");
            runtime = AppendEvent(runtime, audit);
        }

        var nextVersion = checked(expectedVersion + 1);
        var updated = resolutions.Select(item =>
        {
            if (item.Status != GeneratedProcedureResolutionStatus.Available)
            {
                return item;
            }
            if (consumed is not null && item.Id == consumed.Id)
            {
                return item with
                {
                    Status = GeneratedProcedureResolutionStatus.Consumed,
                    ConsumedAtVersion = nextVersion,
                    ConsumedAuditSequence = consumedSequence
                };
            }
            return item with { Status = GeneratedProcedureResolutionStatus.Superseded };
        }).ToArray();

        return (runtime, updated);
    }

    private static GeneratedProcedureResolution RequireAvailable(
        StoredExpedition expedition,
        long expectedVersion,
        Guid? id)
    {
        var generatedId = id
            ?? throw new InvalidOperationException(
                "AutomaticRoll requires the server-generated procedure-resolution id returned by the helper.");
        var generated = (expedition.GeneratedProcedureResolutions ?? [])
            .SingleOrDefault(item => item.Id == generatedId)
            ?? throw new InvalidOperationException(
                "The supplied generated procedure resolution does not belong to this crawl session.");

        if (generated.SessionId != expedition.Id)
        {
            throw new InvalidOperationException(
                "The supplied generated procedure resolution does not belong to this crawl session.");
        }
        if (generated.Status != GeneratedProcedureResolutionStatus.Available)
        {
            throw new InvalidOperationException(
                $"Generated procedure resolution {generated.Id:D} is {generated.Status} and can not be applied.");
        }
        if (generated.GeneratedAtVersion != expedition.Version
            || generated.GeneratedAtVersion != expectedVersion)
        {
            throw new InvalidOperationException(
                "The generated procedure resolution is not valid for the current crawl-session version.");
        }
        if (generated.WatchNumber != ProcedureResolutionHelperService.CurrentWatchNumber(expedition.Runtime))
        {
            throw new InvalidOperationException(
                "The generated procedure resolution belongs to a different watch context.");
        }

        return generated;
    }

    private static void RejectUnusedId(Guid? id)
    {
        if (id.HasValue)
        {
            throw new InvalidOperationException(
                "A generated procedure-resolution id may only be supplied when applying an AutomaticRoll component.");
        }
    }

    private static void RequireEqual(double? actual, double? expected, string label)
    {
        if (actual.HasValue != expected.HasValue
            || (actual.HasValue && actual.Value != expected!.Value))
        {
            throw new InvalidOperationException(
                $"The submitted {label} does not match the persisted generated result.");
        }
    }

    private static CrawlSessionRuntimeState AppendEvent(
        CrawlSessionRuntimeState runtime,
        CrawlRuntimeEvent audit)
    {
        var history = runtime.History.Concat([audit]).ToArray();
        return runtime switch
        {
            ExpeditionState spatial => spatial with { History = history },
            NonSpatialSessionState nonSpatial => nonSpatial with { History = history },
            _ => throw new InvalidOperationException("Unsupported crawl session runtime state.")
        };
    }
}
