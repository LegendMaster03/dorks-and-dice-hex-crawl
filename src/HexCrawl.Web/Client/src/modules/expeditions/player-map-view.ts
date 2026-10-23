import type { HexCrawlApi } from "../../api";
import { MapSurface } from "../../map-surface";
import type { ExpeditionPlayerMapView } from "../../types";
import { clearUiError, showUiError } from "../../ui-error";

/**
 * A separate interactive, read-only player display. This route never
 * downloads the GM's world or expedition workbench payload. Owner sign-in is
 * required; there is no share token or unauthenticated map access.
 */
export async function renderExpeditionPlayerMap(
    root: HTMLElement,
    api: HexCrawlApi,
    expeditionId: string): Promise<() => void> {
    let projection = await api.getExpeditionPlayerMap(expeditionId);
    let disposed = false;
    let fetching = false;
    let currentRequest = 0;

    root.innerHTML = `
        <section class="hc-page hc-workspace hc-player-map">
            <header class="hc-page-header">
                <div><span class="hc-sheet-kicker">Player presentation</span>
                    <h1>Expedition map</h1>
                    <p>Interactive read-only map · only released or discovered information</p>
                </div>
                <nav class="hc-button-row">
                    <button type="button" data-reload-map>Refresh</button>
                    <button type="button" data-center-map>Center party</button>
                </nav>
            </header>
            <div class="hc-error" data-map-error hidden role="alert"></div>
            <section class="hc-map-panel" aria-label="Player map">
                <div class="hc-map-frame"><div class="hc-map-host" data-player-map></div></div>
                <p class="hc-hint" data-map-status role="status" aria-live="polite">
                    The map refreshes automatically while this view is open.
                </p>
                <p class="hc-hint">Pan by dragging, zoom using the wheel, or use the map keyboard controls.
                    The route requires the owning account. It does not grant other accounts access.</p>
            </section>
        </section>`;
    const error = root.querySelector<HTMLElement>("[data-map-error]")!;
    const status = root.querySelector<HTMLElement>("[data-map-status]")!;
    const surface = new MapSurface(root.querySelector<HTMLElement>("[data-player-map]")!,
        () => projection.world, undefined, api);
    // No map cell selection is needed in a player display.
    surface.setClickInterceptor(() => true);
    const present = (next: ExpeditionPlayerMapView): void => {
        if (disposed) return;
        if (next.world.id !== projection.world.id)
            throw new Error("The expedition changed worlds. Reopen the player map.");
        const versionChanged = projection.expeditionVersion !== next.expeditionVersion;
        projection = next;
        surface.renderer.terrainByCell = new Map();
        surface.setTerrainAnnotations(next.terrainAnnotations);
        surface.renderer.showGrid = next.showGrid;
        surface.renderer.showLabels = true;
        surface.renderer.labelAllowedSubjectIds = new Set([
            ...next.featureLabelIds, ...next.locationLabelIds
        ]);
        surface.renderer.expeditionPosition = next.partyPosition;
        surface.renderer.currentWorldCell = null;
        // Do not disclose any hidden subjects through selection highlighting.
        surface.renderer.discoveredSubjectIds = new Set();
        surface.renderer.selectedWorldCell = null;
        status.textContent = versionChanged
            ? "Player map updated with the latest expedition discoveries."
            : "Player map synchronized.";
        surface.requestRender();
    };
    present(projection);
    if (projection.partyPosition) surface.centerOn(projection.partyPosition);

    const reload = async (): Promise<void> => {
        if (disposed || fetching) return;
        fetching = true;
        const request = ++currentRequest;
        try {
            const next = await api.getExpeditionPlayerMap(expeditionId);
            if (disposed || request !== currentRequest) return;
            clearUiError(error);
            present(next);
        } catch (reason) {
            if (!disposed && request === currentRequest) {
                status.textContent = "Player map refresh failed. Showing the last approved view.";
                showUiError(error, reason);
            }
        } finally {
            fetching = false;
        }
    };
    root.querySelector<HTMLButtonElement>("[data-reload-map]")!
        .addEventListener("click", () => { void reload(); });
    root.querySelector<HTMLButtonElement>("[data-center-map]")!
        .addEventListener("click", () => {
            if (projection.partyPosition) surface.centerOn(projection.partyPosition);
            else surface.resetView();
        });
    const poll = window.setInterval(() => {
        if (document.visibilityState === "visible") void reload();
    }, 5000);
    return () => {
        disposed = true;
        currentRequest++;
        window.clearInterval(poll);
        surface.dispose();
    };
}
