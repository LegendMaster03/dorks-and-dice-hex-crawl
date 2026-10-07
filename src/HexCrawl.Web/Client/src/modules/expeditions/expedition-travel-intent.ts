import type { ExpeditionDetail } from "../../types";

export type TravelPreferences = {
    direction: number | null;
    pace: string;
};

export function defaultTravelPreferences(runtime: ExpeditionDetail): TravelPreferences {
    return {
        direction: runtime.expedition.isSpatial ? runtime.expedition.intendedDirection : null,
        pace: runtime.expedition.isSpatial ? runtime.expedition.activePaceKey ?? "normal" : "normal"
    };
}

export function mergeRuntimeTravelPreferences(
    runtime: ExpeditionDetail,
    current: TravelPreferences): TravelPreferences {
    if (!runtime.expedition.isSpatial) return current;
    return {
        direction: runtime.expedition.intendedDirection,
        pace: runtime.expedition.activePaceKey ?? current.pace
    };
}

export type TravelPreferenceStorage = {
    getItem(key: string): string | null;
    setItem(key: string, value: string): void;
};

export function travelPreferenceStorageKey(expeditionId: string): string {
    return `hex-crawl.expedition.${expeditionId}.travel-intent`;
}

export function loadTravelPreferences(
    runtime: ExpeditionDetail,
    storage: TravelPreferenceStorage = window.localStorage): TravelPreferences {
    const fallback = defaultTravelPreferences(runtime);
    try {
        const raw = storage.getItem(travelPreferenceStorageKey(runtime.id));
        if (!raw) return fallback;
        const parsed = JSON.parse(raw) as Partial<TravelPreferences>;
        const pace = typeof parsed.pace === "string" && parsed.pace.trim()
            ? parsed.pace.trim()
            : fallback.pace;
        // Intended course is expedition runtime authority. Normalize legacy development
        // storage immediately so a stale browser direction can not remain a competing value.
        storage.setItem(travelPreferenceStorageKey(runtime.id), JSON.stringify({ pace }));
        return {
            direction: fallback.direction,
            pace
        };
    } catch {
        return fallback;
    }
}

export function saveTravelPreferences(
    expeditionId: string,
    preferences: TravelPreferences,
    storage: TravelPreferenceStorage = window.localStorage): void {
    try {
        storage.setItem(travelPreferenceStorageKey(expeditionId), JSON.stringify({ pace: preferences.pace }));
    } catch {
        // UI preference persistence is optional; runtime state remains authoritative.
    }
}

export function normalizeTravelModePreference(
    pace: string,
    choices: readonly string[],
    authoritativePace: string | null = null): string {
    if (choices.length > 0) {
        if (choices.includes(authoritativePace ?? "")) return authoritativePace!;
        return choices.includes(pace) ? pace : choices[0];
    }
    return authoritativePace?.trim() || "normal";
}

export function normalizeTravelDirectionPreference(
    direction: number | null,
    choices: readonly number[]): number | null {
    return direction !== null && choices.includes(direction) ? direction : null;
}
