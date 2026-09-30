import type { HexCrawlClientModule } from "../../client-module";
import { renderProcedureComposer } from "./procedure-composer-view";

export const proceduresModule: HexCrawlClientModule = {
    id: "procedures",
    routeKinds: ["procedures", "procedure"],

    loadingMessage: () => "Loading Procedure Composer…",

    async render(route, context) {
        if (route.kind === "procedures") {
            return await renderProcedureComposer(
                context.root,
                context.api,
                null,
                context.navigate);
        }

        if (route.kind === "procedure") {
            return await renderProcedureComposer(
                context.root,
                context.api,
                route.procedureId,
                context.navigate);
        }

        throw new Error("Procedures module received an unsupported route.");
    }
};
