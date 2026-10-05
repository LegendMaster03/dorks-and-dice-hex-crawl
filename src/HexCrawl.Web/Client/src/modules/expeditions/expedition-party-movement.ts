import type {
    DistanceUnit,
    DistanceValue,
    ExpeditionDetail,
    MovementCapabilityContributor
} from "../../types";

export function suggestedWatchDistance(runtime: ExpeditionDetail): number | null {
    if (!runtime.expedition.isSpatial) return null;

    const suggestion = runtime.movementComposition.suggestedExpectedDistance;
    if (!suggestion) return null;

    return convertDistanceValue(suggestion, runtime.expedition.distanceTraveled.unit);
}

export function authoritativeFixedWatchDistance(runtime: ExpeditionDetail): number | null {
    if (!runtime.expedition.isSpatial) return null;
    const execution = runtime.procedure.runtime;
    if (!execution
        || execution.travelResolution !== "ContinuousDistance"
        || execution.actualDistanceResolution !== "Fixed"
        || runtime.movementComposition.missingInputs.length > 0) {
        return null;
    }

    return suggestedWatchDistance(runtime);
}

export function convertDistanceValue(
    distance: DistanceValue,
    targetUnit: DistanceUnit): number | null {
    if (sameUnit(distance.unit, targetUnit)) return distance.value;

    const sourceMeters = distance.unit.metersPerUnit;
    const targetMeters = targetUnit.metersPerUnit;
    if (sourceMeters === null
        || targetMeters === null
        || !Number.isFinite(sourceMeters)
        || !Number.isFinite(targetMeters)
        || sourceMeters <= 0
        || targetMeters <= 0) {
        return null;
    }

    return distance.value * sourceMeters / targetMeters;
}

export function movementContributorsAfterMemberRemoval(
    contributors: MovementCapabilityContributor[] | undefined,
    memberId: string): MovementCapabilityContributor[] | undefined {
    if (contributors === undefined) return undefined;

    return contributors
        .filter(contributor => contributor.participantId !== memberId)
        .map(contributor => ({
            ...contributor,
            distanceUnit: contributor.distanceUnit ? { ...contributor.distanceUnit } : null,
            replacesParticipantIds:
                contributor.kind === "Mount" || contributor.kind === "Vehicle"
                    ? contributor.replacesParticipantIds.filter(id => id !== memberId)
                    : [...contributor.replacesParticipantIds]
        }));
}

function sameUnit(left: DistanceUnit, right: DistanceUnit): boolean {
    return left.kind === right.kind
        && left.symbol === right.symbol
        && left.metersPerUnit === right.metersPerUnit;
}