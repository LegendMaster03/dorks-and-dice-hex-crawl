import type { HexCoordinate } from "./types";

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
    destinationReference: string | null;
    routeReference: string | null;
    locationReference: string | null;
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

export interface EncounterHandoffStorage {
    setItem(key: string, value: string): void;
}

export const encounterHandoffStoragePrefix = "dorks-and-dice:hex-encounter-handoff:";

export function blockInitiativeHandoffHref(
    handoff: HexCrawlEncounterHandoffV2,
    storage: EncounterHandoffStorage = window.sessionStorage): string {
    const handoffId = handoff.identity.handoffId;
    storage.setItem(`${encounterHandoffStoragePrefix}${handoffId}`, JSON.stringify(handoff));
    const params = new URLSearchParams();
    params.set("hexEncounterId", handoffId);
    return `/tools/block-initiative?${params.toString()}`;
}
