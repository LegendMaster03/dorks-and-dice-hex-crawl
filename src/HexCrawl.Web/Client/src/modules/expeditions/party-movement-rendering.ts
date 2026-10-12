import type {
    DistanceUnit, DistanceValue, ExpeditionDetail, PartyMovementReference
} from "../../types";

const NONSPATIAL_INPUT_UNIT: DistanceUnit = {
    kind: "Mile", symbol: "mi", metersPerUnit: 1609.344
};

/**
 * Uncalibrated cell-step worlds must NOT gain a fabricated distance unit
 * merely because the party editor renders an empty movement-reference input.
 * The user may explicitly choose a unit when they author a physical rate.
 */
export function preferredPartyMovementUnit(
    runtime: ExpeditionDetail, references: PartyMovementReference | null
): DistanceUnit | null {
    const existing = references?.perHour?.unit
        ?? references?.perWatch?.unit
        ?? references?.perMarch?.unit;
    if (existing) return existing;
    if (!runtime.expedition.isSpatial) return NONSPATIAL_INPUT_UNIT;
    return runtime.context.tiling?.physicalDistancePerWorldUnit?.unit
        ?? runtime.movementComposition.suggestedExpectedDistance?.unit
        ?? runtime.movementComposition.effectiveDistanceUnit
        ?? runtime.expedition.distanceTraveled?.unit
        ?? null;
}

export function movementReferenceFieldMarkup(
    key: "perHour" | "perWatch" | "perMarch",
    label: string,
    distance: DistanceValue | null,
    preferred: DistanceUnit | null
): string {
    const unit = distance?.unit ?? preferred;
    return `
        <div class="hc-movement-reference" data-movement-key="${key}">
            <label>${label} <input data-movement-value type="number" min="0" step="any" value="${distance?.value ?? ""}"></label>
            <label>Unit
                <select data-movement-unit>
                    <option value=""${unit ? "" : " selected"}>Choose a unit</option>
                    <option value="Mile"${unit?.kind === "Mile" ? " selected" : ""}>Miles</option>
                    <option value="Kilometer"${unit?.kind === "Kilometer" ? " selected" : ""}>Kilometers</option>
                    <option value="Custom"${unit?.kind === "Custom" ? " selected" : ""}>Custom</option>
                </select>
            </label>
            <label data-custom-symbol>Symbol <input data-movement-symbol maxlength="24" value="${escapeAttribute(unit?.kind === "Custom" ? unit.symbol : "")}"></label>
            <label data-custom-meters>Meters / unit <input data-movement-meters type="number" min="0.0000001" step="any" value="${unit?.kind === "Custom" ? unit.metersPerUnit ?? "" : ""}"></label>
        </div>`;
}

function escapeAttribute(value: string): string {
    return value.replaceAll("&", "&amp;").replaceAll("<", "&lt;")
        .replaceAll(">", "&gt;").replaceAll('"', "&quot;")
        .replaceAll("'", "&#39;");
}
