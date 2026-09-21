import { renderExpedition } from "../expedition-view";
import type { HexCrawlClientModule } from "../client-module";

export const expeditionsModule: HexCrawlClientModule = {
    id: "expeditions",
    routeKinds: ["expedition", "tracker"],

    loadingMessage(route) {
        return route.kind === "expedition"
            ? "Loading full crawl workbench…"
            : "Loading expedition tracker…";
    },

    async render(route, context) {
        if (route.kind === "expedition") {
            return await renderExpedition(
                context.root,
                context.api,
                route.expeditionId,
                "map",
                context.navigate,
                route.worldId);
        }

        if (route.kind === "tracker") {
            return await renderExpedition(
                context.root,
                context.api,
                route.expeditionId,
                "tracker",
                context.navigate);
        }

        throw new Error("Expeditions module received an unsupported route.");
    }
};
