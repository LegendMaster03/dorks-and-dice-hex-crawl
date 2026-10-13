import type { HexCrawlApi } from "../../api";
import { clearUiError, showUiError } from "../../ui-error";
import { canonicalDistanceUnit } from "./world-form";
import type { DistanceUnitKind } from "./world-form";
import { applyGuidedExperience, guidancePreferenceButton } from "../../ui/guidance";

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
                        <fieldset class="hc-field-group hc-map-scale-field">
                            <legend>Physical map scale</legend>
                            <label>Distance per world geometry unit <input name="physicalScale" type="number" min="0.000001" step="any" required placeholder="Enter distance"></label>
                            <label>Distance unit <select name="periodicUnit" required>
                                <option value="Mile">Miles</option>
                                <option value="Kilometer">Kilometers</option>
                            </select></label>
                        </fieldset>
                        <p class="hc-hint">New maps currently use the established hexagonal tiling preset through the periodic-cell engine. Additional tiling controls will be available after review.</p>
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
    const createButton = required<HTMLButtonElement>(form, "[data-create-button]");
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
                const distance = numeric(input(form, "physicalScale"));
                if (distance <= 0) throw new Error("Physical map scale must be positive.");
                const unit = select(form, "periodicUnit").value as DistanceUnitKind;
                const world = await api.createPeriodicOverworld({
                    name: input(form, "name").value.trim(),
                    tilingDsSymbol: "<1:1,1,1:6,3>",
                    worldUnitsPerAbstractPeriod: 1,
                    origin: { x: 0, y: 0 },
                    rotationDegrees: 0,
                    physicalDistancePerWorldUnit: {
                        value: distance,
                        unit: canonicalDistanceUnit(unit)
                    }
                });
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
