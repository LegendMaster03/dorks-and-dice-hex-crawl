import { renderExpedition } from "./expedition-view";
import { ExpeditionEnvironmentPanel } from "./environment-panel";
import type { HexCrawlClientModule } from "../../client-module";
import { clearUiError, showUiError } from "../../ui-error";

export const expeditionsModule: HexCrawlClientModule = {
    id: "expeditions",
    routeKinds: ["expedition", "tracker"],

    loadingMessage(route) {
        return route.kind === "expedition"
            ? "Loading full crawl workbench…"
            : "Loading expedition tracker…";
    },

    async render(route, context) {
        if (route.kind !== "expedition" && route.kind !== "tracker") {
            throw new Error("Expeditions module received an unsupported route.");
        }

        const expeditionId = route.expeditionId;
        const disposeView = route.kind === "expedition"
            ? await renderExpedition(
                context.root,
                context.api,
                expeditionId,
                "map",
                context.navigate,
                route.worldId)
            : await renderExpedition(
                context.root,
                context.api,
                expeditionId,
                "tracker",
                context.navigate);

        if (!context.root.querySelector(".hc-page")) {
            return disposeView;
        }

        let panelRuntime = await context.api.getExpedition(expeditionId);
        let disposed = false;
        const routePath = route.kind === "expedition"
            ? `/worlds/${route.worldId}/expeditions/${expeditionId}`
            : `/expeditions/${expeditionId}`;
        const mutate = async (
            control: HTMLButtonElement | null,
            action: () => Promise<void>): Promise<void> => {
            const error = context.root.querySelector<HTMLElement>("[data-error]");
            if (error) clearUiError(error);
            const idleText = control?.textContent ?? "";
            if (control) control.disabled = true;
            try {
                await action();
            } catch (value) {
                if (!disposed && error) showUiError(error, value);
            } finally {
                if (!disposed && control) {
                    control.disabled = false;
                    control.textContent = idleText;
                }
            }
        };
        const panel = new ExpeditionEnvironmentPanel(
            context.root,
            context.api,
            () => panelRuntime,
            next => {
                panelRuntime = next;
                context.navigate(routePath, true);
            },
            mutate);
        panel.sync();

        return () => {
            disposed = true;
            panel.dispose();
            disposeView();
        };
    }
};
