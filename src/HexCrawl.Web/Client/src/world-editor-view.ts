import type { HexCrawlApi } from "./api";
import { MapSurface } from "./map-surface";
import type { Location, Overworld, SpatialFeature, WorldPoint } from "./types";

export async function renderWorldEditor(
    root: HTMLElement,
    api: HexCrawlApi,
    worldId: string,
    navigate: (route: string) => void): Promise<() => void> {
    let world = await api.getOverworld(worldId);
    let selectedLocation: Location | null = null;
    let selectedFeature: SpatialFeature | null = null;
    let placement: "location" | "point" | "line" | "region" | null = null;
    let draft: WorldPoint[] = [];

    root.innerHTML = `
        <section class="hc-page hc-workspace">
            <header class="hc-page-header">
                <div><h1 data-title></h1><p>Authoring · version <span data-version></span></p></div>
                <nav><button type="button" data-worlds>Worlds</button><button type="button" data-reset-view>Reset view</button></nav>
            </header>
            <div class="hc-error" data-error hidden></div>
            <div class="hc-workspace-grid">
                <div class="hc-map-panel"><div class="hc-map-host" data-map></div><p class="hc-hint" data-map-hint>Use the authoring buttons to place geometry. Shift-drag or middle-drag pans; wheel zooms.</p></div>
                <aside class="hc-sidebar">
                    <details open><summary>Grid</summary><form class="hc-form" data-grid-form>
                        <label>Name <input name="name" required></label>
                        <label>Orientation <select name="orientation"><option value="PointyTop">Pointy top</option><option value="FlatTop">Flat top</option></select></label>
                        <label>Center distance <input name="scale" type="number" min="0.001" step="any"></label>
                        <label>Unit symbol <input name="unitSymbol"></label>
                        <label>Meters/unit <input name="metersPerUnit" type="number" min="0.001" step="any"></label>
                        <label>Origin X <input name="originX" type="number" step="any"></label>
                        <label>Origin Y <input name="originY" type="number" step="any"></label>
                        <label>Rotation <input name="rotation" type="number" step="any"></label>
                        <label>Hex radius <input name="radius" type="number" min="0.001" step="any"></label>
                        <button type="submit">Save grid/world</button>
                    </form></details>

                    <details open><summary>Locations</summary>
                        <div data-location-list></div>
                        <form class="hc-form" data-location-form>
                            <input name="id" type="hidden">
                            <label>Name <input name="name" required></label>
                            <label>Category <input name="category" required></label>
                            <label>Discoverability <select name="discoverability"><option>Obvious</option><option>Hidden</option><option>Conditional</option></select></label>
                            <div class="hc-inline"><label>X <input name="x" type="number" step="any" required></label><label>Y <input name="y" type="number" step="any" required></label></div>
                            <div class="hc-button-row"><button type="button" data-place-location>Place on map</button><button type="submit">Save location</button><button type="button" data-new-location>New</button><button type="button" data-delete-location>Delete</button></div>
                        </form>
                    </details>

                    <details open><summary>Spatial features</summary>
                        <datalist id="hc-feature-categories"><option value="forest"><option value="swamp"><option value="mountain"><option value="grassland"><option value="desert"><option value="road"><option value="trail"><option value="river"><option value="border"></datalist>
                        <div data-feature-list></div>
                        <form class="hc-form" data-feature-form>
                            <input name="id" type="hidden">
                            <label>Name <input name="name" required></label>
                            <label>Category <input name="category" list="hc-feature-categories" required></label>
                            <label>Kind <select name="kind"><option value="Point">Point</option><option value="Line">Line/polyline</option><option value="Region">Region/polygon</option></select></label>
                            <div class="hc-inline" data-point-fields><label>X <input name="x" type="number" step="any"></label><label>Y <input name="y" type="number" step="any"></label></div>
                            <p class="hc-hint" data-draft>Geometry: none.</p>
                            <div class="hc-button-row"><button type="button" data-author-geometry>Author geometry on map</button><button type="button" data-clear-geometry>Clear geometry</button><button type="submit">Save feature</button><button type="button" data-new-feature>New</button><button type="button" data-delete-feature>Delete</button></div>
                        </form>
                    </details>

                    <details open><summary>Expeditions</summary><div data-expedition-list></div>
                        <form class="hc-form" data-expedition-form>
                            <label>Name <input name="name" required value="Expedition"></label>
                            <label>Procedure <select name="procedure"></select></label>
                            <div class="hc-inline"><label>Start q <input name="q" type="number" step="1" value="0"></label><label>Start r <input name="r" type="number" step="1" value="0"></label></div>
                            <button type="submit">Start expedition</button>
                        </form>
                    </details>
                    <details><summary>Source-map metadata</summary><p data-source-maps></p><p class="hc-hint">Binary image upload and registration UI are deferred. Persisted representation metadata is reserved for the import cycle.</p></details>
                </aside>
            </div>
        </section>`;

    const error = required<HTMLElement>(root, "[data-error]");
    const title = required<HTMLElement>(root, "[data-title]");
    const version = required<HTMLElement>(root, "[data-version]");
    const mapHint = required<HTMLElement>(root, "[data-map-hint]");
    const mapSurface = new MapSurface(required(root, "[data-map]"), () => world, point => onMapClick(point));
    const gridForm = required<HTMLFormElement>(root, "[data-grid-form]");
    const locationForm = required<HTMLFormElement>(root, "[data-location-form]");
    const featureForm = required<HTMLFormElement>(root, "[data-feature-form]");
    const expeditionForm = required<HTMLFormElement>(root, "[data-expedition-form]");
    const draftLabel = required<HTMLElement>(root, "[data-draft]");

    const showError = (value: unknown): void => {
        error.hidden = false;
        error.textContent = value instanceof Error ? value.message : String(value);
    };
    const run = async (action: () => Promise<void>): Promise<void> => {
        error.hidden = true;
        try { await action(); } catch (value) { showError(value); }
    };

    const applyWorld = (next: Overworld): void => {
        world = next;
        title.textContent = next.name;
        version.textContent = String(next.version);
        fillGrid();
        renderLocations();
        renderFeatures();
        required<HTMLElement>(root, "[data-source-maps]").textContent = `${next.sourceMaps.length} registered source-map representation(s).`;
        mapSurface.requestRender();
    };

    const fillGrid = (): void => {
        input(gridForm, "name").value = world.name;
        select(gridForm, "orientation").value = world.grid.orientation;
        input(gridForm, "scale").value = String(world.grid.neighborCenterDistance.value);
        input(gridForm, "unitSymbol").value = world.grid.neighborCenterDistance.unit.symbol;
        input(gridForm, "metersPerUnit").value = world.grid.neighborCenterDistance.unit.metersPerUnit?.toString() ?? "";
        input(gridForm, "originX").value = String(world.grid.origin.x);
        input(gridForm, "originY").value = String(world.grid.origin.y);
        input(gridForm, "rotation").value = String(world.grid.rotationDegrees);
        input(gridForm, "radius").value = String(world.grid.hexRadiusWorldUnits);
    };

    const renderLocations = (): void => {
        const host = required<HTMLElement>(root, "[data-location-list]");
        host.replaceChildren();
        for (const location of world.locations) {
            const button = resourceButton(`${location.name} · ${location.category}`, () => loadLocation(location));
            host.append(button);
        }
    };

    const renderFeatures = (): void => {
        const host = required<HTMLElement>(root, "[data-feature-list]");
        host.replaceChildren();
        for (const feature of world.features) {
            host.append(resourceButton(`${feature.name} · ${feature.kind} · ${feature.category}`, () => loadFeature(feature)));
        }
    };

    const loadLocation = (location: Location): void => {
        selectedLocation = location;
        input(locationForm, "id").value = location.id;
        input(locationForm, "name").value = location.name;
        input(locationForm, "category").value = location.category;
        select(locationForm, "discoverability").value = location.discoverability;
        input(locationForm, "x").value = String(location.position.x);
        input(locationForm, "y").value = String(location.position.y);
    };

    const newLocation = (): void => {
        selectedLocation = null;
        locationForm.reset();
        input(locationForm, "id").value = "";
        input(locationForm, "x").value = "0";
        input(locationForm, "y").value = "0";
    };

    const loadFeature = (feature: SpatialFeature): void => {
        selectedFeature = feature;
        input(featureForm, "id").value = feature.id;
        input(featureForm, "name").value = feature.name;
        input(featureForm, "category").value = feature.category;
        select(featureForm, "kind").value = feature.kind;
        if (feature.position) {
            input(featureForm, "x").value = String(feature.position.x);
            input(featureForm, "y").value = String(feature.position.y);
        }
        draft = feature.kind === "Line" ? [...(feature.path ?? [])] : feature.kind === "Region" ? [...(feature.boundary ?? [])] : [];
        updateDraftLabel();
    };

    const newFeature = (): void => {
        selectedFeature = null;
        featureForm.reset();
        input(featureForm, "id").value = "";
        draft = [];
        updateDraftLabel();
    };

    const updateDraftLabel = (): void => {
        const kind = select(featureForm, "kind").value;
        draftLabel.textContent = kind === "Point"
            ? `Point: ${input(featureForm, "x").value || "?"}, ${input(featureForm, "y").value || "?"}`
            : `Geometry vertices (${draft.length}): ${draft.map(point => `${point.x.toFixed(2)},${point.y.toFixed(2)}`).join(" · ") || "none"}`;
    };

    const onMapClick = (point: WorldPoint): void => {
        if (placement === "location") {
            input(locationForm, "x").value = String(point.x);
            input(locationForm, "y").value = String(point.y);
            placement = null;
            mapHint.textContent = "Location position captured. Save the location to persist it.";
            return;
        }
        if (placement === "point") {
            input(featureForm, "x").value = String(point.x);
            input(featureForm, "y").value = String(point.y);
            placement = null;
            updateDraftLabel();
            mapHint.textContent = "Point position captured. Save the feature to persist it.";
            return;
        }
        if (placement === "line" || placement === "region") {
            draft.push(point);
            updateDraftLabel();
            mapHint.textContent = `${placement === "line" ? "Polyline" : "Polygon"} authoring: ${draft.length} vertices captured. Continue clicking or save the feature.`;
        }
    };

    required<HTMLButtonElement>(root, "[data-worlds]").addEventListener("click", () => navigate("/worlds"));
    required<HTMLButtonElement>(root, "[data-reset-view]").addEventListener("click", () => mapSurface.resetView());
    required<HTMLButtonElement>(root, "[data-place-location]").addEventListener("click", () => {
        placement = "location";
        mapHint.textContent = "Click the map to position the location.";
    });
    required<HTMLButtonElement>(root, "[data-new-location]").addEventListener("click", newLocation);
    required<HTMLButtonElement>(root, "[data-new-feature]").addEventListener("click", newFeature);
    required<HTMLButtonElement>(root, "[data-author-geometry]").addEventListener("click", () => {
        const kind = select(featureForm, "kind").value;
        placement = kind === "Point" ? "point" : kind === "Line" ? "line" : "region";
        if (kind !== "Point") draft = [];
        updateDraftLabel();
        mapHint.textContent = kind === "Point" ? "Click the map for the point." : `Click the map to add ${kind === "Line" ? "polyline" : "polygon"} vertices.`;
    });
    required<HTMLButtonElement>(root, "[data-clear-geometry]").addEventListener("click", () => {
        draft = [];
        updateDraftLabel();
    });
    select(featureForm, "kind").addEventListener("change", () => {
        draft = [];
        updateDraftLabel();
    });

    gridForm.addEventListener("submit", event => {
        event.preventDefault();
        void run(async () => {
            const unit = world.grid.neighborCenterDistance.unit;
            const symbol = input(gridForm, "unitSymbol").value.trim();
            const metersRaw = input(gridForm, "metersPerUnit").value.trim();
            const nextKind = unit.kind === "Custom" ? "Custom" : symbol === "mi" ? "Mile" : symbol === "km" ? "Kilometer" : "Custom";
            const nextGrid = {
                ...world.grid,
                orientation: select(gridForm, "orientation").value === "FlatTop" ? "FlatTop" as const : "PointyTop" as const,
                origin: { x: numeric(input(gridForm, "originX")), y: numeric(input(gridForm, "originY")) },
                rotationDegrees: numeric(input(gridForm, "rotation")),
                hexRadiusWorldUnits: numeric(input(gridForm, "radius")),
                neighborCenterDistance: {
                    value: numeric(input(gridForm, "scale")),
                    unit: {
                        kind: nextKind,
                        symbol,
                        metersPerUnit: metersRaw ? Number(metersRaw) : null
                    }
                }
            };
            applyWorld(await api.updateOverworld(world.id, input(gridForm, "name").value, nextGrid, world.version));
        });
    });

    locationForm.addEventListener("submit", event => {
        event.preventDefault();
        void run(async () => {
            const payload = {
                name: input(locationForm, "name").value,
                category: input(locationForm, "category").value,
                position: { x: numeric(input(locationForm, "x")), y: numeric(input(locationForm, "y")) },
                discoverability: select(locationForm, "discoverability").value as "Obvious" | "Hidden" | "Conditional",
                expectedVersion: world.version
            };
            applyWorld(selectedLocation
                ? await api.updateLocation(world.id, selectedLocation.id, payload)
                : await api.createLocation(world.id, payload));
            newLocation();
        });
    });

    required<HTMLButtonElement>(root, "[data-delete-location]").addEventListener("click", () => void run(async () => {
        if (!selectedLocation) throw new Error("Select a location to delete.");
        applyWorld(await api.deleteLocation(world.id, selectedLocation.id, world.version));
        newLocation();
    }));

    featureForm.addEventListener("submit", event => {
        event.preventDefault();
        void run(async () => {
            const kind = select(featureForm, "kind").value as "Point" | "Line" | "Region";
            const payload = {
                name: input(featureForm, "name").value,
                category: input(featureForm, "category").value,
                kind,
                position: kind === "Point" ? { x: numeric(input(featureForm, "x")), y: numeric(input(featureForm, "y")) } : null,
                path: kind === "Line" ? [...draft] : null,
                boundary: kind === "Region" ? [...draft] : null,
                expectedVersion: world.version
            };
            applyWorld(selectedFeature
                ? await api.updateFeature(world.id, selectedFeature.id, payload)
                : await api.createFeature(world.id, payload));
            newFeature();
        });
    });

    required<HTMLButtonElement>(root, "[data-delete-feature]").addEventListener("click", () => void run(async () => {
        if (!selectedFeature) throw new Error("Select a feature to delete.");
        applyWorld(await api.deleteFeature(world.id, selectedFeature.id, world.version));
        newFeature();
    }));

    const profiles = await api.getRuntimeProfiles();
    const procedure = select(expeditionForm, "procedure");
    for (const profile of profiles) {
        const option = document.createElement("option");
        option.value = profile.key;
        option.textContent = profile.name;
        procedure.append(option);
    }

    const renderExpeditions = async (): Promise<void> => {
        const host = required<HTMLElement>(root, "[data-expedition-list]");
        host.replaceChildren();
        for (const expedition of await api.listExpeditions(world.id)) {
            host.append(resourceButton(`${expedition.name} · ${expedition.procedureName}`, () =>
                navigate(`/worlds/${world.id}/expeditions/${expedition.id}`)));
        }
    };
    await renderExpeditions();

    expeditionForm.addEventListener("submit", event => {
        event.preventDefault();
        void run(async () => {
            const expedition = await api.startExpedition(
                world.id,
                input(expeditionForm, "name").value,
                procedure.value,
                { q: integer(input(expeditionForm, "q")), r: integer(input(expeditionForm, "r")) });
            navigate(`/worlds/${world.id}/expeditions/${expedition.id}`);
        });
    });

    applyWorld(world);
    return () => mapSurface.dispose();
}

function resourceButton(text: string, action: () => void): HTMLButtonElement {
    const button = document.createElement("button");
    button.type = "button";
    button.className = "hc-resource-button";
    button.textContent = text;
    button.addEventListener("click", action);
    return button;
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

function integer(element: HTMLInputElement): number {
    const value = numeric(element);
    if (!Number.isInteger(value)) throw new Error(`${element.name} must be an integer.`);
    return value;
}
