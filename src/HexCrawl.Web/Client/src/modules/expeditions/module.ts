import { renderExpedition } from "./expedition-view";
import { renderExpeditionPlayerMap } from "./player-map-view";
import { ensureExpeditionWorkspaceStyles } from "./expedition-workspace-styles";
import type { HexCrawlClientModule } from "../../client-module";

export const expeditionsModule: HexCrawlClientModule = {
    id: "expeditions",
    routeKinds: ["expedition", "player-map"],

    loadingMessage() {
        return "Loading expedition workspace…";
    },

    async render(route, context) {
        if (route.kind === "player-map") {
            ensureExpeditionWorkspaceStyles();
            return await renderExpeditionPlayerMap(
                context.root, context.api, route.expeditionId);
        }
        if (route.kind !== "expedition") {
            throw new Error("Expeditions module received an unsupported route.");
        }
        ensureExpeditionWorkspaceStyles();

        return await renderExpedition(
            context.root,
            context.api,
            route.expeditionId,
            context.navigate,
            route.worldId,
            context.toolContext);
    }
};