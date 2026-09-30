import type { HexCrawlClientModule } from "../../client-module";
import { renderProcedureComposer } from "./procedure-composer-view";
import { renderProcedureReference } from "./procedure-reference-view";

export const proceduresModule: HexCrawlClientModule = {
    id: "procedures",
    routeKinds: ["procedures", "procedure", "procedure-revision", "procedure-reference"],
    loadingMessage: route => route.kind === "procedure-reference"
        ? "Loading procedure reference…"
        : "Loading Procedure Composer…",
    async render(route, context) {
        if (route.kind === "procedures") {
            return await renderProcedureComposer(context.root, context.api, null, context.navigate);
        }
        if (route.kind === "procedure-reference") {
            return await renderProcedureReference(
                context.root,
                route.procedureId,
                route.revision,
                context.navigate);
        }
        if (route.kind === "procedure" || route.kind === "procedure-revision") {
            const disposeComposer = await renderProcedureComposer(
                context.root,
                context.api,
                route.procedureId,
                context.navigate);
            const disposeReferenceAction = attachReferenceAction(
                context.root,
                route.procedureId,
                context.navigate);

            if (route.kind === "procedure-revision") {
                const revision = context.root.querySelector<HTMLButtonElement>(
                    `button[data-revision="${route.revision}"]`);
                if (revision && !revision.disabled) revision.click();
            }

            return () => {
                disposeReferenceAction();
                disposeComposer();
            };
        }
        throw new Error(`Unsupported procedures route: ${route.kind}`);
    }
};

function attachReferenceAction(
    root: HTMLElement,
    procedureId: string,
    navigate: (route: string, replace?: boolean) => void): () => void {
    let disposed = false;
    const sync = (): void => {
        if (disposed) return;
        const actions = root.querySelector<HTMLElement>(".hc-page-header .hc-button-row");
        if (!actions) return;

        let button = actions.querySelector<HTMLButtonElement>("[data-procedure-reference]");
        if (!button) {
            button = document.createElement("button");
            button.type = "button";
            button.dataset.procedureReference = "";
            button.textContent = "View procedure reference";
            button.addEventListener("click", () => {
                const activeRevision = root.querySelector<HTMLButtonElement>("button[data-revision]:disabled")
                    ?.dataset.revision;
                const base = `/procedures/${encodeURIComponent(procedureId)}`;
                navigate(activeRevision
                    ? `${base}/revisions/${encodeURIComponent(activeRevision)}/reference`
                    : `${base}/reference`);
            });
            actions.prepend(button);
        }
    };

    const observer = new MutationObserver(sync);
    observer.observe(root, { childList: true, subtree: true });
    sync();
    return () => {
        disposed = true;
        observer.disconnect();
    };
}
