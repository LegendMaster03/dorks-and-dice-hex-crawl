import type { CreateOverworldInput } from "../../api";
import type { DistanceUnit, GridDefinition, HexOrientation, WorldPoint } from "../../types";

export type DistanceUnitKind = DistanceUnit["kind"];

export type CreateOverworldDraft = {
    name: string;
    orientation: HexOrientation;
    centerDistance: number;
    unitKind: DistanceUnitKind;
    customSymbol: string;
    customMetersPerUnit: number | null;
    origin: WorldPoint;
    rotationDegrees: number;
    hexRadiusWorldUnits: number;
};

export function customUnitFieldsVisible(kind: DistanceUnitKind): boolean {
    return kind === "Custom";
}

export function canonicalDistanceUnit(
    kind: DistanceUnitKind,
    customSymbol = "",
    customMetersPerUnit: number | null = null): DistanceUnit {
    switch (kind) {
        case "Mile":
            return { kind: "Mile", symbol: "mi", metersPerUnit: 1609.344 };
        case "Kilometer":
            return { kind: "Kilometer", symbol: "km", metersPerUnit: 1000 };
        case "Custom": {
            const symbol = customSymbol.trim();
            if (!symbol) throw new Error("Custom unit symbol is required.");
            if (customMetersPerUnit === null || !Number.isFinite(customMetersPerUnit) || customMetersPerUnit <= 0) {
                throw new Error("Custom meters per unit must be greater than zero.");
            }
            return { kind: "Custom", symbol, metersPerUnit: customMetersPerUnit };
        }
    }
}

export function createOverworldInput(draft: CreateOverworldDraft): CreateOverworldInput {
    if (!draft.name.trim()) throw new Error("Name is required.");
    if (!Number.isFinite(draft.centerDistance) || draft.centerDistance <= 0) {
        throw new Error("Hex center distance must be greater than zero.");
    }
    return {
        name: draft.name.trim(),
        orientation: draft.orientation,
        origin: draft.origin,
        rotationDegrees: draft.rotationDegrees,
        hexRadiusWorldUnits: draft.hexRadiusWorldUnits,
        neighborCenterDistance: draft.centerDistance,
        distanceUnit: canonicalDistanceUnit(draft.unitKind, draft.customSymbol, draft.customMetersPerUnit)
    };
}

export function gridWithSelectedUnit(
    grid: GridDefinition,
    kind: DistanceUnitKind,
    centerDistance: number,
    customSymbol: string,
    customMetersPerUnit: number | null): GridDefinition {
    return {
        ...grid,
        neighborCenterDistance: {
            value: centerDistance,
            unit: canonicalDistanceUnit(kind, customSymbol, customMetersPerUnit)
        }
    };
}
