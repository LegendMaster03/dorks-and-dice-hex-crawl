import type { HexCrawlApi } from "../../api";
import { clearUiError, showUiError } from "../../ui-error";
import { canonicalDistanceUnit, createOverworldInput, customUnitFieldsVisible } from "./world-form";
import type { DistanceUnitKind } from "./world-form";
import { applyGuidedExperience, attachFieldHelp, guidancePreferenceButton } from "../../ui/guidance";

function optionalPositiveNumber(control: HTMLInputElement): number | undefined {
    const raw = control.value.trim();
    if (!raw) return undefined;
    const value = Number(raw);
    if (!Number.isFinite(value) || value <= 0)
        throw new Error("Repeating distances must be finite and positive.");
    return value;
}

function optionalAngle(control: HTMLInputElement): number | undefined {
    const raw = control.value.trim();
    if (!raw) return undefined;
    const value = Number(raw);
    if (!Number.isFinite(value) || value <= 0 || value >= 180)
        throw new Error("Repeating direction angle must be greater than 0 and less than 180 degrees.");
    return value;
}

export async function renderWorldList(
    root: HTMLElement,
    api: HexCrawlApi,
    navigate: (route: string, replace?: boolean) => void): Promise<() => void> {
    applyGuidedExperience(root);
    root.innerHTML = `
        <section class="hc-page">
            <header class="hc-page-header">
                <div><span class="hc-sheet-kicker">Map preparation</span><h1>Worlds / maps</h1><p>Create and manage the authored maps used by expeditions.</p></div>
                <nav class="hc-button-row"><button type="button" data-home>Expeditions</button><span data-guidance-controls></span></nav>
            </header>
            <div class="hc-error" data-error hidden role="alert"></div>
            <div class="hc-columns">
                <section class="hc-panel">
                    <div class="hc-panel-heading"><h2>Your worlds / maps</h2><p class="hc-muted" data-world-count></p></div>
                    <div class="hc-world-list" data-world-list role="list"></div>
                </section>
                <section class="hc-panel">
                    <h2>New world / map</h2>
                    <form data-create-world class="hc-form">
                        <label>Name <input name="name" required value="New overworld" autocomplete="off"></label>
                        <label>Cell pattern <select name="pattern">
                            <option value="hex">Hexagonal</option>
                            <option value="square">Square</option>
                            <option value="triangle">Triangular</option>
                            <option value="custom">Custom repeating pattern (JSON editor)</option>
                        </select></label>
                        <div data-periodic-controls hidden class="hc-form">
                            <p class="hc-hint">Create a repeating polygonal world without uploading a map. Its geometry is verified by the server; physical travel scale is optional.</p>
                            <label>Repeating-pattern size (world units) <input name="periodUnits" type="number" min="0.000001" max="1000000" step="any" value="1"></label>
                            <div class="hc-inline">
                                <label>Origin X <input name="periodicOriginX" type="number" step="any" value="0"></label>
                                <label>Origin Y <input name="periodicOriginY" type="number" step="any" value="0"></label>
                            </div>
                            <label>Rotation degrees <input name="periodicRotation" type="number" step="any" value="0"></label>
                            <details>
                                <summary>Adjust repeating-cell geometry</summary>
                                <p class="hc-hint">Most maps do not require this. When a repeating pattern needs a nonstandard size or angle, enter measured values; the server checks whether the complete pattern can actually use them.</p>
                                <label>First repeating direction length (world units) <input name="periodU" type="number" step="any" min="0.000001" placeholder="Automatic"></label>
                                <label>Second repeating direction length (world units) <input name="periodV" type="number" step="any" min="0.000001" placeholder="Automatic"></label>
                                <label>Angle between repeating directions (degrees) <input name="periodAngle" type="number" step="any" min="0.001" max="179.999" placeholder="Automatic"></label>
                            </details>
                            <label>Optional physical distance per world unit <input name="physicalScale" type="number" min="0.000001" step="any" placeholder="Leave blank if uncalibrated"></label>
                            <label>Physical distance unit
                                <select name="periodicUnit"><option value="Mile">Miles</option><option value="Kilometer">Kilometers</option></select>
                            </label>
                            <p class="hc-hint">Physical units use the map-scale unit selected below. Uncalibrated worlds can use cell-step travel but not physical-distance travel.</p>
                            <details data-custom-pattern hidden>
                                <summary>Explicitly edit custom tiling JSON</summary>
                                <label>Tiling source JSON <textarea name="patternJson" rows="8" spellcheck="false" placeholder='{"tilingDsSymbol":"...","straightEdgeSubdivisions":[{"motifCellId":"face-0","boundaryVertexIndex":1,"fraction":0.5}]}'></textarea></label>
                                <p class="hc-hint">Optional straightEdgeSubdivisions identify split vertices and their fractional positions on straight geometric sides. For measured patterns, topologyWitness and metricRealization may supply an explicit verified periodic embedding. The server checks every atomic reciprocal interface, geometric-side group, and metric constraint. Explicit coordinates require size 1 and rotation 0.</p>
                            </details>
                        </div>
                        <div data-hex-controls class="hc-form">
                        <label>Grid orientation <select name="orientation"><option value="PointyTop">Pointy top</option><option value="FlatTop">Flat top</option></select></label>
                        <fieldset class="hc-field-group hc-map-scale-field">
                            <legend>Map scale</legend>
                            <div class="hc-inline">
                                <label>Distance <input name="scale" type="number" min="0.001" step="any" required></label>
                                <label>Unit <select name="unit" required><option value="">Select unit</option><option value="Mile">Miles</option><option value="Kilometer">Kilometers</option><option value="Custom">Custom</option></select></label>
                            </div>
                            <p class="hc-hint">Game-world distance from the center of one adjacent cell to the next. No default physical scale is assumed.</p>
                        </fieldset>
                        <div class="hc-custom-unit-fields" data-custom-unit hidden>
                            <label>Custom symbol <input name="symbol" autocomplete="off"></label>
                            <label>Custom meters per unit <input name="meters" type="number" min="0.001" step="any"></label>
                        </div>
                        <details><summary>Advanced grid alignment</summary>
                            <div class="hc-form">
                                <p class="hc-hint hc-guided-only">These values define the internal coordinate frame used to line maps up with the hex grid. Most new worlds should keep the defaults; change them when matching an existing map or imported coordinate system.</p>
                                <div class="hc-inline"><label>Origin X <input name="originX" type="number" step="any" value="0"></label><label>Origin Y <input name="originY" type="number" step="any" value="0"></label></div>
                                <label>Rotation degrees <input name="rotation" type="number" step="any" value="0"></label>
                                <label>Hex radius (world units) <input name="radius" type="number" min="0.001" step="any" value="1"></label>
                            </div>
                        </details>
                        </div>
                        <button type="submit" class="hc-primary-action" data-create-button>Create world</button>
                    </form>
                </section>
            </div>
        </section>`;

    required<HTMLButtonElement>(root, "[data-home]").addEventListener("click", () => navigate("/"));
    required<HTMLElement>(root, "[data-guidance-controls]").append(guidancePreferenceButton(root));
    const error = required<HTMLElement>(root, "[data-error]");
    const list = required<HTMLElement>(root, "[data-world-list]");
    const count = required<HTMLElement>(root, "[data-world-count]");
    const form = required<HTMLFormElement>(root, "[data-create-world]");
    const unitSelect = select(form, "unit");
    const customFields = required<HTMLElement>(form, "[data-custom-unit]");
    const symbolInput = input(form, "symbol");
    const metersInput = input(form, "meters");
    const createButton = required<HTMLButtonElement>(form, "[data-create-button]");
    const pattern = select(form, "pattern");
    const periodicFields = required<HTMLElement>(form, "[data-periodic-controls]");
    const hexFields = required<HTMLElement>(form, "[data-hex-controls]");
    const customPattern = required<HTMLDetailsElement>(form, "[data-custom-pattern]");
    const syncPattern = (): void => {
        const periodic = pattern.value !== "hex";
        periodicFields.hidden = !periodic;
        hexFields.hidden = periodic;
        customPattern.hidden = pattern.value !== "custom";
        hexFields.querySelectorAll<HTMLInputElement | HTMLSelectElement>("input, select")
            .forEach(control => {
                control.disabled = periodic;
            });
        periodicFields.querySelectorAll<HTMLInputElement | HTMLSelectElement | HTMLTextAreaElement>("input, select, textarea")
            .forEach(control => {
                control.disabled = !periodic
                    || (control.name === "patternJson" && pattern.value !== "custom");
            });
        syncCustomUnit();
    };
    pattern.addEventListener("change", syncPattern);

    attachFieldHelp(select(form, "orientation"), "Hex orientation", "Pointy-top and flat-top describe the grid geometry. They do not define map north.");
    attachFieldHelp(input(form, "scale"), "Map scale", "This is the game-world distance from the center of one cell to the center of an adjacent cell.", "A 6-mile map uses 6 with Miles.");
    attachFieldHelp(unitSelect, "Map scale unit", "The physical unit used by the map scale.");
    attachFieldHelp(metersInput, "Custom meters per unit", "Only used for Custom units. Enter the number of meters represented by one custom unit.");
    attachFieldHelp(input(form, "radius"), "Hex radius (world units)", "This is an internal map-coordinate scale used for grid geometry, not the game-world travel distance. Most worlds should leave it at 1.");
    let disposed = false;
    let busy = false;
    let worldCount = 0;

    const syncWorldCount = (): void => {
        count.textContent = worldCount === 1 ? "1 world" : `${worldCount} worlds`;
    };
    const renderEmptyState = (): void => {
        if (list.querySelector(".hc-empty-state")) return;
        const empty = document.createElement("div");
        empty.className = "hc-empty-state";
        empty.innerHTML = "<strong>No worlds / maps yet.</strong><span>Create the first map with the form beside this list.</span>";
        list.append(empty);
    };

    const syncCustomUnit = (): void => {
        const visible = customUnitFieldsVisible(unitSelect.value as DistanceUnitKind);
        customFields.hidden = !visible;
        symbolInput.required = visible;
        metersInput.required = visible;
    };
    unitSelect.addEventListener("change", syncCustomUnit);
    syncCustomUnit();
    syncPattern();

    try {
        const worlds = await api.listOverworlds();
        if (disposed) return () => {};
        worldCount = worlds.length;
        syncWorldCount();
        if (worlds.length === 0) {
            renderEmptyState();
        } else {
            for (const world of worlds) {
                const row = document.createElement("article");
                row.className = "hc-world-card";
                row.setAttribute("role", "listitem");
                const copy = document.createElement("div");
                copy.className = "hc-world-card-copy";
                const title = document.createElement("strong");
                title.className = "hc-world-card-title";
                title.textContent = world.name;
                const meta = document.createElement("span");
                meta.className = "hc-world-card-meta";
                meta.textContent = `Updated ${formatTimestamp(world.updatedAt)}`;
                copy.append(title, meta);

                const actions = document.createElement("div");
                actions.className = "hc-button-row";
                const editButton = document.createElement("button");
                editButton.type = "button";
                editButton.textContent = "Edit map";
                editButton.setAttribute("aria-label", `Edit map for ${world.name}`);
                editButton.addEventListener("click", () => navigate(`/worlds/${world.id}/edit`));

                const deleteButton = document.createElement("button");
                deleteButton.type = "button";
                deleteButton.className = "hc-danger-action";
                deleteButton.textContent = "Delete";
                deleteButton.setAttribute("aria-label", `Delete overworld ${world.name}`);
                let deleting = false;
                deleteButton.addEventListener("click", () => {
                    if (deleting) return;
                    const confirmed = window.confirm(
                        `Delete “${world.name}”?\n\nThis permanently deletes the overworld and its uploaded map files. Overworlds with saved expeditions can not be deleted.`);
                    if (!confirmed) return;
                    void (async () => {
                        clearUiError(error);
                        deleting = true;
                        editButton.disabled = true;
                        deleteButton.disabled = true;
                        deleteButton.textContent = "Deleting…";
                        try {
                            await api.deleteOverworld(world.id, world.version);
                            if (disposed) return;
                            row.remove();
                            worldCount = Math.max(0, worldCount - 1);
                            syncWorldCount();
                            if (worldCount === 0) renderEmptyState();
                        } catch (value) {
                            if (!disposed) showUiError(error, value);
                            deleting = false;
                            if (!disposed) {
                                editButton.disabled = false;
                                deleteButton.disabled = false;
                                deleteButton.textContent = "Delete";
                            }
                        }
                    })();
                });

                actions.append(editButton, deleteButton);
                row.append(copy, actions);
                list.append(row);
            }
        }
    } catch (value) {
        if (!disposed) showUiError(error, value);
    }

    form.addEventListener("submit", event => {
        event.preventDefault();
        if (busy) return;
        void (async () => {
            clearUiError(error);
            busy = true;
            createButton.disabled = true;
            createButton.textContent = "Creating…";
            try {
                if (pattern.value !== "hex") {
                    const known: Record<string, string> = {
                        square: "<1:1,1,1:4,4>",
                        triangle: "<1:1,1,1:3,6>"
                    };
                    let dsSymbol = known[pattern.value];
                    let customExtras: {
                        straightEdgeSubdivisions?: Array<{
                            motifCellId: string; boundaryVertexIndex: number; fraction: number;
                        }>;
                        topologyWitness?: unknown;
                        metricRealization?: unknown;
                    } = {};
                    if (pattern.value === "custom") {
                        let custom: unknown;
                        try {
                            custom = JSON.parse(required<HTMLTextAreaElement>(form, 'textarea[name="patternJson"]').value);
                        } catch {
                            throw new Error("The custom tiling JSON must be valid JSON.");
                        }
                        if (typeof custom !== "object" || custom === null
                            || !("tilingDsSymbol" in custom)
                            || typeof custom.tilingDsSymbol !== "string") {
                            throw new Error("Custom tiling JSON needs a tilingDsSymbol string.");
                        }
                        const source = custom as Record<string, unknown>;
                        const allowed = new Set([
                            "tilingDsSymbol", "straightEdgeSubdivisions", "topologyWitness", "metricRealization"
                        ]);
                        if (Object.keys(source).some(key => !allowed.has(key)))
                            throw new Error("Custom tiling JSON has an unrecognized field.");
                        if (source.straightEdgeSubdivisions !== undefined) {
                            if (!Array.isArray(source.straightEdgeSubdivisions)
                                || source.straightEdgeSubdivisions.some((split: unknown) => {
                                    if (typeof split !== "object" || split === null) return true;
                                    const v = split as Record<string, unknown>;
                                    return typeof v.motifCellId !== "string"
                                        || !Number.isInteger(v.boundaryVertexIndex)
                                        || typeof v.fraction !== "number"
                                        || !(v.fraction > 0 && v.fraction < 1);
                                })) throw new Error("Each straight-edge subdivision requires a motif cell, vertex index, and fraction between zero and one.");
                            customExtras.straightEdgeSubdivisions = source.straightEdgeSubdivisions as typeof customExtras.straightEdgeSubdivisions;
                        }
                        customExtras.topologyWitness = source.topologyWitness;
                        customExtras.metricRealization = source.metricRealization;
                        dsSymbol = custom.tilingDsSymbol;
                    }
                    if (!dsSymbol) throw new Error("Select a supported repeating cell pattern.");
                    const physicalText = input(form, "physicalScale").value.trim();
                    const physicalValue = physicalText ? numeric(input(form, "physicalScale")) : null;
                    if (physicalValue !== null && physicalValue <= 0)
                        throw new Error("Physical map scale must be positive.");
                    const period = numeric(input(form, "periodUnits"));
                    if (!(period > 0)) throw new Error("Repeating-pattern size must be positive.");
                    const unit = select(form, "periodicUnit").value as DistanceUnitKind;
                    const physicalDistancePerWorldUnit = physicalValue === null ? undefined : {
                        value: physicalValue,
                        unit: canonicalDistanceUnit(unit)
                    };
                    const world = await api.createPeriodicOverworld({
                        name: input(form, "name").value.trim(),
                        tilingDsSymbol: dsSymbol,
                        worldUnitsPerAbstractPeriod: period,
                        origin: {
                            x: numeric(input(form, "periodicOriginX")),
                            y: numeric(input(form, "periodicOriginY"))
                        },
                        rotationDegrees: numeric(input(form, "periodicRotation")),
                        physicalDistancePerWorldUnit,
                        periodULengthWorldUnits: optionalPositiveNumber(input(form, "periodU")),
                        periodVLengthWorldUnits: optionalPositiveNumber(input(form, "periodV")),
                        periodAngleDegrees: optionalAngle(input(form, "periodAngle")),
                        ...customExtras
                    });
                    if (!disposed) navigate(`/worlds/${world.id}/edit`);
                    return;
                }
                const unitKind = unitSelect.value as DistanceUnitKind;
                const customMeters = unitKind === "Custom" ? numeric(metersInput) : null;
                const world = await api.createOverworld(createOverworldInput({
                    name: input(form, "name").value,
                    orientation: select(form, "orientation").value === "FlatTop" ? "FlatTop" : "PointyTop",
                    centerDistance: numeric(input(form, "scale")),
                    unitKind,
                    customSymbol: symbolInput.value,
                    customMetersPerUnit: customMeters,
                    origin: { x: numeric(input(form, "originX")), y: numeric(input(form, "originY")) },
                    rotationDegrees: numeric(input(form, "rotation")),
                    hexRadiusWorldUnits: numeric(input(form, "radius"))
                }));
                if (!disposed) navigate(`/worlds/${world.id}/edit`);
            } catch (value) {
                if (!disposed) showUiError(error, value);
            } finally {
                busy = false;
                if (!disposed) {
                    createButton.disabled = false;
                    createButton.textContent = "Create overworld";
                }
            }
        })();
    });

    return () => { disposed = true; };
}

function required<T extends Element>(root: ParentNode, selector: string): T {
    const value = root.querySelector<T>(selector);
    if (!value) throw new Error(`Missing ${selector}`);
    return value;
}

function input(root: ParentNode, name: string): HTMLInputElement {
    const value = root.querySelector<HTMLInputElement>(`input[name="${name}"]`);
    if (!value) throw new Error(`Missing input ${name}`);
    return value;
}

function select(root: ParentNode, name: string): HTMLSelectElement {
    const value = root.querySelector<HTMLSelectElement>(`select[name="${name}"]`);
    if (!value) throw new Error(`Missing select ${name}`);
    return value;
}

function numeric(element: HTMLInputElement): number {
    const value = Number(element.value);
    if (!Number.isFinite(value)) throw new Error(`${element.name} must be a finite number.`);
    return value;
}

function formatTimestamp(value: string): string {
    const date = new Date(value);
    if (Number.isNaN(date.valueOf())) return value;
    return new Intl.DateTimeFormat(undefined, { dateStyle: "medium", timeStyle: "short" }).format(date);
}
