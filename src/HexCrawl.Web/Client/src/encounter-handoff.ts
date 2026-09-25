import type { ExpeditionDetail, HexCoordinate } from "./types";

export type EncounterHandoffCombatant = {
    name: string;
    side: "players" | "enemies";
    quantity?: number;
    initiativeModifier?: number | null;
    rulesCoreConceptKey?: string | null;
};

export type HexCrawlEncounterHandoff = {
    version: 1;
    sourceTool: "hex-crawl";
    expeditionId: string;
    expeditionName: string;
    contextName: string;
    overworldId: string | null;
    returnPath: string | null;
    watchNumber: number;
    day: number;
    outcome: string;
    summary: string;
    note: string | null;
    occursAtHours: number | null;
    hex: HexCoordinate | null;
    locationId: string | null;
    locationName: string | null;
    combatants: EncounterHandoffCombatant[];
};

export function encounterHandoffFromRuntime(
    runtime: ExpeditionDetail,
    options: {
        outcome?: string | null;
        note?: string | null;
        locationName?: string | null;
        returnPath?: string | null;
        combatants?: EncounterHandoffCombatant[];
    } = {}): HexCrawlEncounterHandoff | null {
    const event = [...runtime.history].reverse().find(item => item.kind === "EncounterTriggered");
    const activeOutcome = runtime.expedition.isSpatial
        ? runtime.expedition.activeEncounterKind
        : null;
    const outcome = options.outcome?.trim()
        || activeOutcome
        || outcomeFromMessage(event?.message ?? "")
        || null;

    if (!event && !outcome) return null;
    if (outcome === "None") return null;

    return {
        version: 1,
        sourceTool: "hex-crawl",
        expeditionId: runtime.id,
        expeditionName: runtime.name,
        contextName: runtime.context.name,
        overworldId: runtime.overworldId,
        returnPath: safeReturnPath(options.returnPath),
        watchNumber: event?.watchNumber
            ?? runtime.expedition.activeWatchNumber
            ?? runtime.expedition.completedWatches + 1,
        day: runtime.expedition.currentDay,
        outcome: outcome ?? "Encounter",
        summary: event?.message ?? options.note?.trim() ?? outcome ?? "Encounter",
        note: options.note?.trim() || null,
        occursAtHours: runtime.expedition.isSpatial
            ? runtime.expedition.activeEncounterHour
            : null,
        hex: event?.hex ?? (runtime.expedition.isSpatial ? runtime.expedition.currentHex : null),
        locationId: event?.subjectId ?? null,
        locationName: options.locationName?.trim() || null,
        combatants: [...(options.combatants ?? [])]
    };
}

export function blockInitiativeHandoffHref(handoff: HexCrawlEncounterHandoff): string {
    const params = new URLSearchParams();
    params.set("hexEncounter", JSON.stringify(handoff));
    return `/tools/block-initiative?${params.toString()}`;
}

function outcomeFromMessage(message: string): string | null {
    const triggered = /^([A-Za-z]+)(?::| triggered\.)/.exec(message.trim());
    if (triggered?.[1]) return triggered[1];
    const recorded = /recorded\s+([A-Za-z]+)/i.exec(message);
    return recorded?.[1] ?? null;
}

function safeReturnPath(value: string | null | undefined): string | null {
    const normalized = value?.trim();
    if (!normalized || !normalized.startsWith("/tools/hex-crawl")) return null;
    return normalized;
}
