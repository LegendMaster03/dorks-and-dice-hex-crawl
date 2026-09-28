using HexCrawl.Domain.Procedure;
using HexCrawl.Domain.Runtime;
using HexCrawl.Domain.Spatial;

namespace HexCrawl.Application;

internal static class ProcedureTravelInputPolicy
{
    private const double Epsilon = 0.0000001d;

    public static ResolvedTravelAmount Build(
        GenericProcedureRuntime procedure,
        DistanceUnit unit,
        double? effectiveDistance,
        double? expectedDistance,
        double? actualDistance,
        int? hexSteps,
        ResolutionProvenance provenance)
    {
        ArgumentNullException.ThrowIfNull(procedure);
        ArgumentNullException.ThrowIfNull(provenance);

        if (procedure.Movement.TravelResolution == TravelResolutionMode.HexSteps)
        {
            if (effectiveDistance.HasValue || expectedDistance.HasValue || actualDistance.HasValue)
            {
                throw new InvalidOperationException("The selected hex-step procedure accepts a resolved step count only and does not accept physical distance inputs.");
            }
            return hexSteps.HasValue
                ? ResolvedTravelAmount.Steps(hexSteps.Value, provenance)
                : throw new InvalidOperationException("The selected procedure requires a resolved hex-step count.");
        }

        if (hexSteps.HasValue)
        {
            throw new InvalidOperationException("The selected continuous-distance procedure does not accept a resolved hex-step count.");
        }

        if (procedure.Movement.ActualDistanceResolution == ActualDistanceResolutionMode.Fixed)
        {
            var supplied = new[] { effectiveDistance, expectedDistance, actualDistance }
                .Where(value => value.HasValue)
                .Select(value => value!.Value)
                .ToArray();
            if (supplied.Length == 0)
            {
                throw new InvalidOperationException("The selected fixed-distance procedure requires one effective travel distance.");
            }

            var effective = supplied[0];
            if (supplied.Any(value => Math.Abs(value - effective) > Epsilon))
            {
                throw new InvalidOperationException("The selected fixed-distance procedure requires every supplied distance value to represent the same effective travel distance.");
            }

            var distance = new DistanceMeasure(effective, unit);
            return ResolvedTravelAmount.Distance(distance, distance, provenance);
        }

        if (effectiveDistance.HasValue)
        {
            throw new InvalidOperationException("The selected variable-distance procedure uses separate expected and actual distance values, not an effective-distance input.");
        }
        if (!expectedDistance.HasValue || !actualDistance.HasValue)
        {
            throw new InvalidOperationException("The selected variable-distance procedure requires expected and actual travel distance.");
        }
        return ResolvedTravelAmount.Distance(
            new DistanceMeasure(expectedDistance.Value, unit),
            new DistanceMeasure(actualDistance.Value, unit),
            provenance);
    }
}
