import type { DistanceUnit } from "../../types";

export type DistanceUnitKind = DistanceUnit["kind"];

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

