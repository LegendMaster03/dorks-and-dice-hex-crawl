import type { DistanceUnit, DistanceValue, ExpeditionDetail } from "../../types";

export function suggestedWatchDistance(runtime: ExpeditionDetail): number | null {
    if (!runtime.expedition.isSpatial) return null;

    const movement = runtime.party.baseMovement;
    if (!movement) return null;

    const targetUnit = runtime.expedition.distanceTraveled.unit;
    const remainingHours = runtime.expedition.activeWatchNumber === null
        ? runtime.profile.watchHours
        : runtime.expedition.activeWatchRemainingHours ?? runtime.remainingWatchHours;

    if (runtime.expedition.activeWatchNumber === null && movement.perWatch) {
        return convertDistanceValue(movement.perWatch, targetUnit);
    }

    if (movement.perHour && Number.isFinite(remainingHours) && remainingHours >= 0) {
        const hourly = convertDistanceValue(movement.perHour, targetUnit);
        return hourly === null ? null : hourly * remainingHours;
    }

    if (runtime.expedition.activeWatchNumber !== null
        && movement.perWatch
        && nearlyEqual(
            remainingHours,
            runtime.expedition.activeWatchTotalHours ?? runtime.profile.watchHours)) {
        return convertDistanceValue(movement.perWatch, targetUnit);
    }

    return null;
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

function nearlyEqual(left: number, right: number): boolean {
    return Math.abs(left - right) < 0.000001;
}
