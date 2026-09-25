import { formatHours } from "../../runtime-view";
import type { ExpeditionDetail, RuntimeEvent } from "../../types";

export type WatchLedgerRow = {
    day: number;
    watchNumber: number;
    progress: string;
    route: string;
    navigation: string;
    encounter: string;
    status: string;
};

export function buildWatchLedger(runtime: ExpeditionDetail): WatchLedgerRow[] {
    const grouped = new Map<number, RuntimeEvent[]>();
    for (const event of runtime.history) {
        const events = grouped.get(event.watchNumber) ?? [];
        events.push(event);
        grouped.set(event.watchNumber, events);
    }

    return [...grouped.entries()]
        .sort(([left], [right]) => right - left)
        .map(([watchNumber, events]) => buildRow(runtime, watchNumber, events))
        .slice(0, 20);
}

function buildRow(
    runtime: ExpeditionDetail,
    watchNumber: number,
    sourceEvents: RuntimeEvent[]): WatchLedgerRow {
    const events = [...sourceEvents].sort((left, right) =>
        left.sequence - right.sequence);
    const firstElapsed = events.reduce(
        (value, event) => Math.min(value, event.expeditionElapsedHours),
        Number.POSITIVE_INFINITY);
    const lastElapsed = events.reduce(
        (value, event) => Math.max(value, event.expeditionElapsedHours),
        0);

    return {
        day: Math.floor((Number.isFinite(firstElapsed) ? firstElapsed : lastElapsed) / 24) + 1,
        watchNumber,
        progress: progressLabel(events, lastElapsed),
        route: routeLabel(events),
        navigation: navigationLabel(events),
        encounter: encounterLabel(runtime, watchNumber, events),
        status: statusLabel(runtime, watchNumber, events)
    };
}

function progressLabel(events: RuntimeEvent[], elapsed: number): string {
    const totals = new Map<string, number>();
    for (const event of events) {
        if (event.kind !== "DistanceTraveled"
            || event.distanceValue === null
            || event.distanceUnit === null) {
            continue;
        }
        totals.set(
            event.distanceUnit,
            (totals.get(event.distanceUnit) ?? 0) + event.distanceValue);
    }

    const distance = [...totals.entries()]
        .map(([unit, value]) => `${formatNumber(value)} ${unit}`)
        .join(" + ");
    return distance
        ? `${distance} · ${formatHours(elapsed)} elapsed`
        : `${formatHours(elapsed)} elapsed`;
}

function routeLabel(events: RuntimeEvent[]): string {
    const route: string[] = [];
    for (const event of events) {
        if (!event.hex) continue;
        const label = `${event.hex.q},${event.hex.r}`;
        if (route.at(-1) !== label) route.push(label);
    }
    if (route.length === 0) return "—";
    if (route.length <= 4) return route.join(" → ");
    return `${route[0]} → ${route[1]} → … → ${route.at(-1)}`;
}

function navigationLabel(events: RuntimeEvent[]): string {
    if (events.some(event => event.kind === "NavigationDecisionRequired")) {
        return "Decision required";
    }
    if (events.some(event => event.kind === "ExpeditionReoriented")) {
        return "Reoriented";
    }
    if (events.some(event => event.kind === "ExpeditionBecameLost")) {
        return "Lost";
    }

    const check = [...events].reverse().find(event =>
        event.kind === "NavigationCheckResolved");
    if (!check) return "—";
    const lower = check.message.toLowerCase();
    if (lower.includes("failed")) return "Failed";
    if (lower.includes("succeeded")) return "Succeeded";
    if (lower.includes("notrequired") || lower.includes("not required")) return "Not required";
    return "Resolved";
}

function encounterLabel(
    runtime: ExpeditionDetail,
    watchNumber: number,
    events: RuntimeEvent[]): string {
    const triggered = [...events].reverse().find(event =>
        event.kind === "EncounterTriggered");
    if (triggered) return triggered.message;

    if (runtime.expedition.activeWatchNumber === watchNumber
        && runtime.expedition.activeEncounterKind
        && runtime.expedition.activeEncounterKind !== "None"
        && runtime.expedition.activeEncounterHandled === false) {
        return `Pending ${splitEnum(runtime.expedition.activeEncounterKind)}`;
    }

    if (events.some(event => event.kind === "EncounterCheckPerformed")) {
        return "No encounter triggered";
    }
    return "—";
}

function statusLabel(
    runtime: ExpeditionDetail,
    watchNumber: number,
    events: RuntimeEvent[]): string {
    if (events.some(event => event.kind === "WatchCompleted")
        || watchNumber <= runtime.expedition.completedWatches) {
        return "Complete";
    }
    if (runtime.expedition.activeWatchNumber === watchNumber) {
        return runtime.pauseReason
            ? `Paused · ${splitEnum(runtime.pauseReason)}`
            : "Active";
    }
    if (!events.some(event => event.kind === "WatchStarted")
        && watchNumber === runtime.expedition.completedWatches + 1) {
        return "Prepared";
    }
    return "Recorded";
}

function splitEnum(value: string): string {
    return value.replace(/([a-z])([A-Z])/g, "$1 $2").toLowerCase();
}

function formatNumber(value: number): string {
    return Number.isInteger(value)
        ? String(value)
        : value.toFixed(2).replace(/0+$/, "").replace(/\.$/, "");
}
