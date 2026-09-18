import type { HexCrawlApi } from "./api";
import type { ExpeditionSummary, OverworldSummary } from "./types";
import { showUiError } from "./ui-error";

export async function renderToolHome(
    root: HTMLElement,
    api: HexCrawlApi,
    navigate: (route: string, replace?: boolean) => void): Promise<() => void> {
    let disposed = false;
    root.innerHTML = `
        <section class="hc-page">
            <header class="hc-page-header">
                <div>
                    <h1>Hex Crawl DM tools</h1>
                    <p>Use crawl bookkeeping with the rendered Hex Crawl map, another map, or no rendered map.</p>
                </div>
                <nav><button type="button" data-worlds>Overworlds & maps</button></nav>
            </header>
            <div class="hc-error" data-error hidden role="alert"></div>
            <section class="hc-panel">
                <div class="hc-panel-heading">
                    <div><h2>Expeditions</h2><p class="hc-muted">Persistent crawl procedure state is available independently of the map view.</p></div>
                    <span class="hc-muted" data-expedition-count></span>
                </div>
                <div class="hc-expedition-list" data-expedition-list></div>
            </section>
            <section class="hc-mode-section" aria-labelledby="hc-mode-title">
                <h2 id="hc-mode-title">Ways to use the crawl engine</h2>
                <div class="hc-mode-grid">
                    <article class="hc-mode-card">
                        <h3>Mapless expedition tracker</h3>
                        <p>Run watches, travel, navigation, encounter cadence, overrides, and history without constructing a rendered map surface.</p>
                    </article>
                    <article class="hc-mode-card">
                        <h3>Full crawl workbench</h3>
                        <p>Compose the same expedition engine with an authored overworld, map rendering, discovery controls, and player-knowledge presentation.</p>
                    </article>
                    <article class="hc-mode-card">
                        <h3>Focused assistants</h3>
                        <p>Open travel/watch, navigation, or encounter-focused views over the same authoritative expedition state.</p>
                    </article>
                </div>
            </section>
        </section>`;

    required<HTMLButtonElement>(root, "[data-worlds]").addEventListener("click", () => navigate("/worlds"));
    const error = required<HTMLElement>(root, "[data-error]");
    const list = required<HTMLElement>(root, "[data-expedition-list]");
    const count = required<HTMLElement>(root, "[data-expedition-count]");

    try {
        const [expeditions, worlds] = await Promise.all([api.listExpeditions(), api.listOverworlds()]);
        if (disposed) return () => {};
        count.textContent = expeditions.length === 1 ? "1 expedition" : `${expeditions.length} expeditions`;
        renderExpeditions(list, expeditions, worlds, navigate);
    } catch (value) {
        if (!disposed) showUiError(error, value);
    }

    return () => { disposed = true; };
}

function renderExpeditions(
    host: HTMLElement,
    expeditions: ExpeditionSummary[],
    worlds: OverworldSummary[],
    navigate: (route: string, replace?: boolean) => void): void {
    host.replaceChildren();
    const worldNames = new Map(worlds.map(world => [world.id, world.name]));
    if (expeditions.length === 0) {
        const empty = document.createElement("div");
        empty.className = "hc-empty-state";
        empty.innerHTML = "<strong>No expeditions yet.</strong><span>Expedition creation currently starts from an overworld configuration. After creation, the tracker and focused assistants do not require the rendered map.</span>";
        host.append(empty);
        return;
    }

    for (const expedition of expeditions) {
        const card = document.createElement("article");
        card.className = "hc-expedition-card";

        const copy = document.createElement("div");
        const title = document.createElement("h3");
        title.textContent = expedition.name;
        const meta = document.createElement("p");
        meta.className = "hc-muted";
        meta.textContent = `${expedition.procedureName} · crawl context ${worldNames.get(expedition.overworldId) ?? expedition.overworldId} · updated ${formatTimestamp(expedition.updatedAt)}`;
        copy.append(title, meta);

        const actions = document.createElement("div");
        actions.className = "hc-button-row";
        actions.append(
            action("Open tracker", () => navigate(`/expeditions/${expedition.id}`), true),
            action("Full map", () => navigate(`/worlds/${expedition.overworldId}/expeditions/${expedition.id}`)),
            action("Travel / watch", () => navigate(`/expeditions/${expedition.id}/travel`)),
            action("Navigation", () => navigate(`/expeditions/${expedition.id}/navigation`)),
            action("Encounters", () => navigate(`/expeditions/${expedition.id}/encounters`))
        );

        card.append(copy, actions);
        host.append(card);
    }
}

function action(label: string, onClick: () => void, primary = false): HTMLButtonElement {
    const button = document.createElement("button");
    button.type = "button";
    button.textContent = label;
    if (primary) button.classList.add("hc-primary-action");
    button.addEventListener("click", onClick);
    return button;
}

function formatTimestamp(value: string): string {
    const date = new Date(value);
    if (Number.isNaN(date.valueOf())) return value;
    return new Intl.DateTimeFormat(undefined, { dateStyle: "medium", timeStyle: "short" }).format(date);
}

function required<T extends Element>(root: ParentNode, selector: string): T {
    const value = root.querySelector<T>(selector);
    if (!value) throw new Error(`Missing ${selector}`);
    return value;
}
