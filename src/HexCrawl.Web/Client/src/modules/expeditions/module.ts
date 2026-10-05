import { renderExpedition } from "./expedition-view";
import type { HexCrawlClientModule } from "../../client-module";

export const expeditionsModule: HexCrawlClientModule = {
    id: "expeditions",
    routeKinds: ["expedition", "tracker"],

    loadingMessage() {
        return "Loading expedition workspace…";
    },

    async render(route, context) {
        if (route.kind !== "expedition" && route.kind !== "tracker") {
            throw new Error("Expeditions module received an unsupported route.");
        }

        return route.kind === "expedition"
            ? await renderExpedition(
                context.root,
                context.api,
                route.expeditionId,
                "map",
                context.navigate,
                route.worldId,
                context.toolContext)
            : await renderExpedition(
                context.root,
                context.api,
                route.expeditionId,
                "tracker",
                context.navigate,
                undefined,
                context.toolContext);
    }
};