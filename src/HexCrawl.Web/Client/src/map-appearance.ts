/** Display-only map colors. Category values are normalized for lookup. */
export type MapAppearance = {
    terrainColors: Record<string, string>;
    featureColors: Record<string, string>;
    labelColors?: Record<string, string>;
};

export const defaultTerrainColors: Readonly<Record<string, string>> = {
    forest: "#538A55",
    water: "#438BB5",
    river: "#438BB5",
    urban: "#858C93",
    rural: "#D9BD65",
    grassland: "#9BAE67",
    plains: "#D9BD65",
    mountain: "#967D70",
    desert: "#D9AA70",
    swamp: "#698D73"
};

export const defaultFeatureColors: Readonly<Record<string, string>> = {
    river: "#4479A1",
    road: "#775F41",
    trail: "#917653",
    border: "#947556",
    forest: "#538A55",
    mountain: "#967D70"
};

export function mapCategoryColor(
    appearance: MapAppearance | undefined,
    category: string,
    kind: "terrain" | "feature" | "label"): string | null {
    const key = category.trim().toLowerCase();
    if (!key) return null;
    const custom = kind === "terrain" ? appearance?.terrainColors
        : kind === "feature" ? appearance?.featureColors : appearance?.labelColors;
    const candidate = Object.entries(custom ?? {})
        .find(([name]) => name.trim().toLowerCase() === key)?.[1]
        ?? (kind === "terrain" ? defaultTerrainColors
            : kind === "feature" ? defaultFeatureColors : {})[key];
    return candidate && /^#[0-9a-fA-F]{6}$/.test(candidate) ? candidate : null;
}
