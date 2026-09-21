import { renderToolHome } from "../tool-home-view";
import type { HexCrawlClientModule } from "../client-module";

export const homeModule: HexCrawlClientModule = {
    id: "home",
    routeKinds: ["home"],

    loadingMessage: () => "Loading DM tools…",

    async render(route, context) {
        if (route.kind !== "home") throw new Error("Home module received an unsupported route.");
        return await renderToolHome(context.root, context.api, context.navigate);
    }
};
