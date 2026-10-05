import type { ExpeditionDetail, HexCoordinate } from "./types";

export type EncounterHandoffCombatant = {
    name: string;
    side: "players" | "enemies";
    quantity?: number;
    initiativeModifier?: number | null;
    rulesCoreConceptKey?: string | null;
};

export type EncounterHandoffTarget = {
    scope: string;
    targetId: string | null;
};

export type EncounterHandoffProvenance = {
    sourceKind: string;
    sourceKey: string;
    sourceReference: string | null;
    providerName: string | null;
    note: string | null;
};

export type EncounterHandoffCircumstance = {
    consequenceId: string;
    consequenceKey: string;
    circumstanceKey: string;
    value: string | null;
    target: EncounterHandoffTarget;
    provenance: EncounterHandoffProvenance;
};

export type EncounterHandoffEffect = {
    id: string;
    effectKey: string;
    target: EncounterHandoffTarget;
    level: number | null;
    magnitude: number | null;
    unit: string | null;
    state: string | null;
    sourceConsequenceIds: string[];
};

export type EncounterHandoffResource = {
    id: string;
    resourceKey: string;
    target: EncounterHandoffTarget;
    inventoryModel: string;
    isDepleted: boolean;
    quantity: number | null;
    unit: string | null;
    symbolicState: string | null;
    supplyDieSides: number | null;
};

export type EncounterHandoffJourneyProvenance = {
    eventOccurrenceId: string;
    processId: string | null;
    processKey: string | null;
    stageKey: string | null;
    eventKey: string;
    eventType: string | null;
    triggerReference: string;
    provenance: EncounterHandoffProvenance;
};

export type EncounterHandoffLinkedScene = {
    id: string;
    kind: string;
    referenceKey: string;
};

export type HexCrawlEncounterHandoffV2 = {
    version: 2;
    sourceTool: "hex-crawl";
    identity: {
        handoffId: string;
        encounterOccurrenceId: string;
        expeditionId: string;
        expeditionName: string;
    };
    returnContext: { returnPath: string | null };
    timeContext: {
        day: number;
        watchNumber: number | null;
        expeditionElapsedHours: number;
    };
    worldContext: {
        overworldId: string | null;
        hex: HexCoordinate | null;
        location: { id: string; name: string; category: string } | null;
    };
    encounter: {
        outcome: string;
        summary: string;
        dmNote: string | null;
    };
    combatants: EncounterHandoffCombatant[];
    circumstances: EncounterHandoffCircumstance[];
    effects: EncounterHandoffEffect[];
    resources: EncounterHandoffResource[];
    journeyProvenance: EncounterHandoffJourneyProvenance | null;
    linkedScenes: EncounterHandoffLinkedScene[];
};

export type CreateEncounterHandoffRequest = {
    expectedVersion: number;
    handoffId: string;
    returnPath: string | null;
    runtimeEncounterSequence: number | null;
    journeyEventOccurrenceId: string | null;
};

/** @deprecated Phase 14 transition only. Remove after all call sites use v2. */
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

/** @deprecated Phase 14 transition only. Remove after all call sites use v2. */
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

export function blockInitiativeHandoffHref(
    handoff: HexCrawlEncounterHandoff | HexCrawlEncounterHandoffV2): string {
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
