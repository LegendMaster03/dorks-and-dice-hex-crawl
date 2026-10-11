import type { Location } from "../../types";

/** Resolve keyed encounter location identity from authoritative saved world locations. */
export function encounterLocationId(
    kind: string, selectedId: string,
    availableLocations: readonly Pick<Location, "id">[]
): string | null {
    if (kind !== "KeyedLocationDiscovery") return null;
    if (!selectedId || !availableLocations.some(location => location.id === selectedId)) {
        throw new Error(
            "Choose an existing keyed world location, or add a location on the world map before recording this discovery.");
    }
    return selectedId;
}
