import type { DistanceUnit, DistanceValue, ExpeditionDetail } from "../../types";

type MovementCompositionProjection = {
    suggestedExpectedDistance: DistanceValue | null;
};

type MovementCompositionExpeditionDetail = ExpeditionDetail & {
    movementComposition?: MovementCompositionProjection | null;
};

export function suggestedWatchDistance(runtime: ExpeditionDetail): number | null {
    if (!runtime.expedition.isSpatial) return null;

    const composition = (runtime as MovementCompositionExpeditionDetail).movementComposition;
    const suggestion = composition?.suggestedExpectedDistance;
    if (!suggestion) return null;

    return convertDistanceValue(suggestion, runtime.expedition.distanceTraveled.unit);
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

function sameUnit(left: DistanceUnit, right: DistanceUnit): boolean {
    return left.kind === right.kind
        && left.symbol === right.symbol
        && left.metersPerUnit === right.metersPerUnit;
}
