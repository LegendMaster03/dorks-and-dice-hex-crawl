import { HexCrawlApi } from "./api";
import { renderExpedition } from "./expedition-view";
import { ensureStyles } from "./styles";
import { deriveToolRoute, navigateTool, parseToolRoute } from "./tool-route";
import { renderWorldEditor } from "./world-editor-view";
import { renderWorldList } from "./world-list-view";

const root = document.getElementById("tool-root");
if (!(root instanceof HTMLElement)) throw new Error("Hex Crawl requires #tool-root.");

ensureStyles();
void boot(root);

async function boot(rootElement: HTMLElement): Promise<void> {
    const { api, context } = await HexCrawlApi.create(rootElement);
    const basePath = context?.toolBasePath ?? rootElement.dataset.toolBasePath ?? "/";
    const initialRoute = context?.toolRoute ?? rootElement.dataset.toolRoute ?? "/";
    let cleanup: (() => void) | null = null;

    const navigate = (route: string): void => navigateTool(basePath, route);

    const renderCurrentRoute = async (): Promise<void> => {
        cleanup?.();
        cleanup = null;
        const path = deriveToolRoute(basePath, window.location.pathname, initialRoute);
        const route = parseToolRoute(path);
        rootElement.replaceChildren();

        try {
            switch (route.kind) {
                case "worlds":
                    cleanup = await renderWorldList(rootElement, api, navigate);
                    break;
                case "world":
                case "edit":
                    cleanup = await renderWorldEditor(rootElement, api, route.worldId, navigate);
                    break;
                case "expedition":
                    cleanup = await renderExpedition(rootElement, api, route.worldId, route.expeditionId, navigate);
                    break;
                default:
                    navigate("/worlds");
                    break;
            }
        } catch (error) {
            const panel = document.createElement("section");
            panel.className = "hc-page hc-error-page";
            const heading = document.createElement("h1");
            heading.textContent = "Hex Crawl could not load this route";
            const detail = document.createElement("p");
            detail.textContent = error instanceof Error ? error.message : String(error);
            const back = document.createElement("button");
            back.type = "button";
            back.textContent = "Return to overworlds";
            back.addEventListener("click", () => navigate("/worlds"));
            panel.append(heading, detail, back);
            rootElement.replaceChildren(panel);
        }
    };

    window.addEventListener("popstate", () => void renderCurrentRoute());
    if (parseToolRoute(deriveToolRoute(basePath, window.location.pathname, initialRoute)).kind === "unknown") {
        navigateTool(basePath, "/worlds", true);
        return;
    }
    await renderCurrentRoute();
}
