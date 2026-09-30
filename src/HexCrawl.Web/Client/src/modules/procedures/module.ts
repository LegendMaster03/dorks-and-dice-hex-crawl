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
    ensureReferenceActionStyles();
    const button = document.createElement("button");
    button.type = "button";
    button.className = "hc-procedure-reference-launcher";
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
    document.body.append(button);
    return () => button.remove();
}

function ensureReferenceActionStyles(): void {
    if (document.getElementById("hc-procedure-reference-launcher-styles")) return;
    const style = document.createElement("style");
    style.id = "hc-procedure-reference-launcher-styles";
    style.textContent = `
        .hc-procedure-reference-launcher {
            position: fixed;
            right: 1.25rem;
            bottom: 1.25rem;
            z-index: 20;
            box-shadow: 0 .35rem 1rem rgb(0 0 0 / .25);
        }
        @media print {
            .hc-procedure-reference-launcher { display: none !important; }
        }
    `;
    document.head.append(style);
}
