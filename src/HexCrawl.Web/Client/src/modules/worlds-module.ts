import { enhanceExpeditionSetup } from "../expedition-setup";
import { renderWorldEditor } from "../world-editor-view";
import { renderWorldList } from "../world-list-view";
import type { HexCrawlClientModule } from "../client-module";

export const worldsModule: HexCrawlClientModule = {
    id: "worlds",
    routeKinds: ["worlds", "world", "edit"],

    loadingMessage(route) {
        return route.kind === "worlds" ? "Loading overworlds…" : "Loading overworld…";
    },

    async render(route, context) {
        if (route.kind === "worlds") {
            return await renderWorldList(context.root, context.api, context.navigate);
        }

        if (route.kind !== "world" && route.kind !== "edit") {
            throw new Error("Worlds module received an unsupported route.");
        }

        const editorCleanup = await renderWorldEditor(
            context.root,
            context.api,
            route.worldId,
            context.navigate);
        const setupCleanup = await enhanceExpeditionSetup(
            context.root,
            context.api,
            route.worldId,
            context.navigate);

        return () => {
            setupCleanup();
            editorCleanup();
        };
    }
};
