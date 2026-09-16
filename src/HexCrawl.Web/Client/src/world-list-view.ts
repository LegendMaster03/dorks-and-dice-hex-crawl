import type { HexCrawlApi } from "./api";

export async function renderWorldList(
    root: HTMLElement,
    api: HexCrawlApi,
    navigate: (route: string) => void): Promise<() => void> {
    root.innerHTML = `
        <section class="hc-page">
            <header class="hc-page-header"><div><h1>Hex Crawl</h1><p>Persistent overworlds</p></div></header>
            <div class="hc-error" data-error hidden></div>
            <div class="hc-columns">
                <section class="hc-panel"><h2>Your overworlds</h2><div data-world-list></div></section>
                <section class="hc-panel">
                    <h2>Create overworld</h2>
                    <form data-create-world class="hc-form">
                        <label>Name <input name="name" required value="New overworld"></label>
                        <label>Orientation <select name="orientation"><option value="PointyTop">Pointy top</option><option value="FlatTop">Flat top</option></select></label>
                        <label>Center distance <input name="scale" type="number" min="0.001" step="any" value="12" required></label>
                        <label>Unit <select name="unit"><option value="Mile">Miles</option><option value="Kilometer">Kilometers</option><option value="Custom">Custom</option></select></label>
                        <label>Custom symbol <input name="symbol" value="u"></label>
                        <label>Custom meters/unit <input name="meters" type="number" min="0.001" step="any"></label>
                        <details><summary>Grid alignment</summary>
                            <label>Origin X <input name="originX" type="number" step="any" value="0"></label>
                            <label>Origin Y <input name="originY" type="number" step="any" value="0"></label>
                            <label>Rotation degrees <input name="rotation" type="number" step="any" value="0"></label>
                            <label>Hex radius (world units) <input name="radius" type="number" min="0.001" step="any" value="1"></label>
                        </details>
                        <button type="submit">Create overworld</button>
                    </form>
                </section>
            </div>
        </section>`;

    const error = required<HTMLElement>(root, "[data-error]");
    const list = required<HTMLElement>(root, "[data-world-list]");
    const form = required<HTMLFormElement>(root, "[data-create-world]");

    const showError = (value: unknown): void => {
        error.hidden = false;
        error.textContent = value instanceof Error ? value.message : String(value);
    };

    try {
        const worlds = await api.listOverworlds();
        if (worlds.length === 0) list.textContent = "No overworlds yet.";
        for (const world of worlds) {
            const button = document.createElement("button");
            button.type = "button";
            button.className = "hc-resource-button";
            button.textContent = `${world.name} · v${world.version}`;
            button.addEventListener("click", () => navigate(`/worlds/${world.id}/edit`));
            list.append(button);
        }
    } catch (value) {
        showError(value);
    }

    form.addEventListener("submit", event => {
        event.preventDefault();
        void (async () => {
            error.hidden = true;
            try {
                const data = new FormData(form);
                const unitKind = String(data.get("unit")) as "Mile" | "Kilometer" | "Custom";
                const distanceUnit = unitKind === "Mile"
                    ? { kind: "Mile" as const, symbol: "mi", metersPerUnit: 1609.344 }
                    : unitKind === "Kilometer"
                        ? { kind: "Kilometer" as const, symbol: "km", metersPerUnit: 1000 }
                        : {
                            kind: "Custom" as const,
                            symbol: requiredString(data, "symbol"),
                            metersPerUnit: optionalNumber(data, "meters")
                        };
                const world = await api.createOverworld({
                    name: requiredString(data, "name"),
                    orientation: String(data.get("orientation")) === "FlatTop" ? "FlatTop" : "PointyTop",
                    origin: { x: number(data, "originX"), y: number(data, "originY") },
                    rotationDegrees: number(data, "rotation"),
                    hexRadiusWorldUnits: number(data, "radius"),
                    neighborCenterDistance: number(data, "scale"),
                    distanceUnit
                });
                navigate(`/worlds/${world.id}/edit`);
            } catch (value) {
                showError(value);
            }
        })();
    });

    return () => {};
}

function required<T extends Element>(root: ParentNode, selector: string): T {
    const value = root.querySelector<T>(selector);
    if (!value) throw new Error(`Missing ${selector}`);
    return value;
}

function requiredString(data: FormData, name: string): string {
    const value = String(data.get(name) ?? "").trim();
    if (!value) throw new Error(`${name} is required.`);
    return value;
}

function number(data: FormData, name: string): number {
    const value = Number(data.get(name));
    if (!Number.isFinite(value)) throw new Error(`${name} must be a number.`);
    return value;
}

function optionalNumber(data: FormData, name: string): number | null {
    const raw = String(data.get(name) ?? "").trim();
    if (!raw) return null;
    const value = Number(raw);
    if (!Number.isFinite(value)) throw new Error(`${name} must be a number.`);
    return value;
}
