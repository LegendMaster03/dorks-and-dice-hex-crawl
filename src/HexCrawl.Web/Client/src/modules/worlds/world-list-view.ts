import type { HexCrawlApi } from "../../api";
import { clearUiError, showUiError } from "../../ui-error";
import { createOverworldInput, customUnitFieldsVisible } from "./world-form";
import type { DistanceUnitKind } from "./world-form";

export async function renderWorldList(
    root: HTMLElement,
    api: HexCrawlApi,
    navigate: (route: string, replace?: boolean) => void): Promise<() => void> {
    root.innerHTML = `
        <section class="hc-page">
            <header class="hc-page-header">
                <div><span class="hc-sheet-kicker">Map preparation</span><h1>Overworlds</h1><p>Create and manage persistent hex-crawl worlds separately from the running sheet.</p></div>
                <nav><button type="button" data-home>Running sheets</button></nav>
            </header>
            <div class="hc-error" data-error hidden role="alert"></div>
            <div class="hc-columns">
                <section class="hc-panel">
                    <div class="hc-panel-heading"><h2>Your overworlds</h2><p class="hc-muted" data-world-count></p></div>
                    <div class="hc-world-list" data-world-list role="list"></div>
                </section>
                <section class="hc-panel">
                    <h2>New overworld</h2>
                    <form data-create-world class="hc-form">
                        <label>Name <input name="name" required value="New overworld" autocomplete="off"></label>
                        <label>Orientation <select name="orientation"><option value="PointyTop">Pointy top</option><option value="FlatTop">Flat top</option></select></label>
                        <label>Hex center distance <input name="scale" type="number" min="0.001" step="any" required><span class="hc-hint">Center-to-center distance between adjacent hexes. No default physical scale is assumed.</span></label>
                        <label>Unit <select name="unit" required><option value="">Select distance unit</option><option value="Mile">Miles</option><option value="Kilometer">Kilometers</option><option value="Custom">Custom</option></select></label>
                        <div class="hc-custom-unit-fields" data-custom-unit hidden>
                            <label>Custom symbol <input name="symbol" autocomplete="off"></label>
                            <label>Custom meters per unit <input name="meters" type="number" min="0.001" step="any"></label>
                        </div>
                        <details><summary>Advanced grid alignment</summary>
                            <div class="hc-form">
                                <div class="hc-inline"><label>Origin X <input name="originX" type="number" step="any" value="0"></label><label>Origin Y <input name="originY" type="number" step="any" value="0"></label></div>
                                <label>Rotation degrees <input name="rotation" type="number" step="any" value="0"></label>
                                <label>Hex radius (world units) <input name="radius" type="number" min="0.001" step="any" value="1"></label>
                            </div>
                        </details>
                        <button type="submit" class="hc-primary-action" data-create-button>Create overworld</button>
                    </form>
                </section>
            </div>
        </section>`;

    required<HTMLButtonElement>(root, "[data-home]").addEventListener("click", () => navigate("/"));
    const error = required<HTMLElement>(root, "[data-error]");
    const list = required<HTMLElement>(root, "[data-world-list]");
    const count = required<HTMLElement>(root, "[data-world-count]");
    const form = required<HTMLFormElement>(root, "[data-create-world]");
    const unitSelect = select(form, "unit");
    const customFields = required<HTMLElement>(form, "[data-custom-unit]");
    const symbolInput = input(form, "symbol");
    const metersInput = input(form, "meters");
    const createButton = required<HTMLButtonElement>(form, "[data-create-button]");
    let disposed = false;
    let busy = false;

    const syncCustomUnit = (): void => {
        const visible = customUnitFieldsVisible(unitSelect.value as DistanceUnitKind);
        customFields.hidden = !visible;
        symbolInput.required = visible;
        metersInput.required = visible;
    };
    unitSelect.addEventListener("change", syncCustomUnit);
    syncCustomUnit();

    try {
        const worlds = await api.listOverworlds();
        if (disposed) return () => {};
        count.textContent = worlds.length === 1 ? "1 world" : `${worlds.length} worlds`;
        if (worlds.length === 0) {
            const empty = document.createElement("div");
            empty.className = "hc-empty-state";
            empty.innerHTML = "<strong>No overworlds yet.</strong><span>Create the first overworld with the form beside this list.</span>";
            list.append(empty);
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
                const button = document.createElement("button");
                button.type = "button";
                button.textContent = "Edit map";
                button.setAttribute("aria-label", `Edit map for ${world.name}`);
                button.addEventListener("click", () => navigate(`/worlds/${world.id}/edit`));
                row.append(copy, button);
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
