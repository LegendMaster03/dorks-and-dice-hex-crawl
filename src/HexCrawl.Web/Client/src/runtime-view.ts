import type { DistanceValue, RuntimeState } from "./types";

export function alexandrianActualDistance(expectedDistance: number, firstD6: number, secondD6: number): number {
    validateD6(firstD6);
    validateD6(secondD6);
    if (!Number.isFinite(expectedDistance) || expectedDistance < 0) throw new Error("Expected distance must be non-negative.");
    return expectedDistance * ((firstD6 + secondD6 + 3) / 10);
}

export function discoveredSubjectIds(runtime: RuntimeState | null): Set<string> {
    return new Set(runtime?.knowledge
        .filter(entry => entry.state === "Discovered" || entry.state === "Revealed")
        .map(entry => entry.subjectId) ?? []);
}

export function formatDistance(distance: DistanceValue | null): string {
    if (!distance) return "—";
    return `${formatNumber(distance.value)} ${distance.unit.symbol}`;
}

export function formatHours(hours: number): string {
    if (Math.abs(hours) < 0.000001) return "0 h";
    return `${formatNumber(hours)} h`;
}

export function directionLabel(direction: number | null): string {
    return direction === null ? "—" : `Direction ${direction}`;
}

function validateD6(value: number): void {
    if (!Number.isInteger(value) || value < 1 || value > 6) throw new Error("A d6 result must be between 1 and 6.");
}

function formatNumber(value: number): string {
    return Number.isInteger(value) ? String(value) : value.toFixed(2).replace(/0+$/, "").replace(/\.$/, "");
}
