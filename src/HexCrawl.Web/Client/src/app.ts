import { HexCrawlApi } from "./api";
import { renderExpedition } from "./expedition-view";
import { ensureStyles } from "./styles";
import { deriveToolRoute, navigateTool, parseToolRoute } from "./tool-route";
import { describeUiError } from "./ui-error";
import { renderWorldEditor } from "./world-editor-view";
import { renderWorldList } from "./world-list-view";

const root = document.getElementById("tool-root");
if (!(root instanceof HTMLElement)) throw new Error("Hex Crawl requires #tool-root.");

root.classList.add("hex-crawl-app");
ensureStyles();
void boot(root);

async function boot(rootElement: HTMLElement): Promise<void> {
    renderLoading(rootElement, "Connecting to Dorks & Dice…");
    try {
        const { api, context } = await HexCrawlApi.create(rootElement);
        const basePath = context?.toolBasePath ?? rootElement.dataset.toolBasePath ?? "/";
        const initialRoute = context?.toolRoute ?? rootElement.dataset.toolRoute ?? "/";
        let cleanup: (() => void) | null = null;

        const navigate = (route: string, replace = false): void => navigateTool(basePath, route, replace);

        const renderCurrentRoute = async (): Promise<void> => {
            cleanup?.();
            cleanup = null;
            const path = deriveToolRoute(basePath, window.location.pathname, initialRoute);
            const route = parseToolRoute(path);
            renderLoading(rootElement, loadingMessage(route.kind));

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
                        navigate("/worlds", true);
                        break;
                }
            } catch (value) {
                const error = describeUiError(value);
                const panel = document.createElement("section");
                panel.className = "hc-page hc-error-page";
                const heading = document.createElement("h1");
                heading.textContent = error.kind === "not-found" ? "This Hex Crawl route was not found" : "Hex Crawl could not load this route";
                const detail = document.createElement("p");
                detail.textContent = error.message;
                const back = document.createElement("button");
                back.type = "button";
                back.textContent = "Return to overworlds";
                back.addEventListener("click", () => navigate("/worlds"));
                panel.append(heading, detail, back);
                rootElement.replaceChildren(panel);
            }
        };

        const onPopState = (): void => { void renderCurrentRoute(); };
        window.addEventListener("popstate", onPopState);
        window.addEventListener("pagehide", () => {
            cleanup?.();
            cleanup = null;
            window.removeEventListener("popstate", onPopState);
        }, { once: true });

        if (parseToolRoute(deriveToolRoute(basePath, window.location.pathname, initialRoute)).kind === "unknown") {
            navigateTool(basePath, "/worlds", true);
            return;
        }
        await renderCurrentRoute();
    } catch (value) {
        const error = describeUiError(value);
        rootElement.innerHTML = `
            <section class="hc-page hc-error-page">
                <h1>Hex Crawl could not start</h1>
                <p></p>
            </section>`;
        const paragraph = rootElement.querySelector("p");
        if (paragraph) paragraph.textContent = error.message;
    }
}

function renderLoading(rootElement: HTMLElement, message: string): void {
    rootElement.innerHTML = `
        <section class="hc-page">
            <div class="hc-loading-panel" role="status" aria-live="polite">
                <span class="hc-loading-dot" aria-hidden="true"></span>
                <span></span>
            </div>
        </section>`;
    const label = rootElement.querySelector<HTMLElement>(".hc-loading-panel span:last-child");
    if (label) label.textContent = message;
}

function loadingMessage(kind: ReturnType<typeof parseToolRoute>["kind"]): string {
    switch (kind) {
        case "worlds": return "Loading overworlds…";
        case "world":
        case "edit": return "Loading overworld…";
        case "expedition": return "Loading expedition…";
        default: return "Loading Hex Crawl…";
    }
}
