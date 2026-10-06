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
        direction: runtime.expedition.intendedDirection ?? current.direction,
        pace: runtime.expedition.activePaceKey ?? current.pace
    };
}
