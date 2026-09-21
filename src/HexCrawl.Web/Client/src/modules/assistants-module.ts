import { renderAssistantEntry } from "../assistant-entry-view";
import { renderExpeditionAssistant } from "../expedition-assistant-view";
import type { HexCrawlClientModule } from "../client-module";

export const assistantsModule: HexCrawlClientModule = {
    id: "assistants",
    routeKinds: ["assistant", "assistant-entry"],

    loadingMessage: () => "Loading focused assistant…",

    async render(route, context) {
        if (route.kind === "assistant") {
            return await renderExpeditionAssistant(
                context.root,
                context.api,
                route.expeditionId,
                route.assistant,
                context.navigate);
        }

        if (route.kind === "assistant-entry") {
            return await renderAssistantEntry(
                context.root,
                context.api,
                route.assistant,
                context.navigate);
        }

        throw new Error("Assistants module received an unsupported route.");
    }
};
