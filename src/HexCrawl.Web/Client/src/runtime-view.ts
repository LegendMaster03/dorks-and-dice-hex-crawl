import type { DistanceValue, RuntimeState } from "./types";

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
    if (direction === null) return "—";
    const labels = [
        "Toward +q",
        "Toward +q / -r",
        "Toward -r",
        "Toward -q",
        "Toward -q / +r",
        "Toward +r"
    ];
    return labels[direction] ?? `Direction ${direction}`;
}

function formatNumber(value: number): string {
    return Number.isInteger(value) ? String(value) : value.toFixed(2).replace(/0+$/, "").replace(/\.$/, "");
}
