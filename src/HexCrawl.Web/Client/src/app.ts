import { HexCrawlApi } from "./api";
import { renderExpedition } from "./expedition-view";
import { renderExpeditionAssistant } from "./expedition-assistant-view";
import { renderAssistantEntry } from "./assistant-entry-view";
import { enhanceExpeditionSetup } from "./expedition-setup";
import { ensureStyles } from "./styles";
import { renderToolHome } from "./tool-home-view";
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
        const hostedAnonymous = context !== null && context.user == null;
        let cleanup: (() => void) | null = null;

        const navigate = (route: string, replace = false): void => navigateTool(basePath, route, replace);

        const renderCurrentRoute = async (): Promise<void> => {
            cleanup?.();
            cleanup = null;
            const path = deriveToolRoute(basePath, window.location.pathname, initialRoute);
            const route = parseToolRoute(path);
            renderLoading(rootElement, loadingMessage(route.kind));

            try {
                if (hostedAnonymous) {
                    renderAnonymousAccess(rootElement, route.kind !== "home");
                    return;
                }

                switch (route.kind) {
                    case "home":
                        cleanup = await renderToolHome(rootElement, api, navigate);
                        break;
                    case "worlds":
                        cleanup = await renderWorldList(rootElement, api, navigate);
                        break;
                    case "world":
                    case "edit": {
                        const editorCleanup = await renderWorldEditor(rootElement, api, route.worldId, navigate);
                        const setupCleanup = await enhanceExpeditionSetup(rootElement, api, route.worldId, navigate);
                        cleanup = () => {
                            setupCleanup();
                            editorCleanup();
                        };
                        break;
                    }
                    case "expedition":
                        cleanup = await renderExpedition(rootElement, api, route.expeditionId, "map", navigate, route.worldId);
                        break;
                    case "tracker":
                        cleanup = await renderExpedition(rootElement, api, route.expeditionId, "tracker", navigate);
                        break;
                    case "assistant":
                        cleanup = await renderExpeditionAssistant(rootElement, api, route.expeditionId, route.assistant, navigate);
                        break;
                    case "assistant-entry":
                        cleanup = await renderAssistantEntry(rootElement, api, route.assistant, navigate);
                        break;
                    default:
                        navigate("/", true);
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
                back.textContent = "Return to DM tools";
                back.addEventListener("click", () => navigate("/"));
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
            navigateTool(basePath, "/", true);
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
        case "home": return "Loading DM tools…";
        case "worlds": return "Loading overworlds…";
        case "world":
        case "edit": return "Loading overworld…";
        case "expedition": return "Loading full crawl workbench…";
        case "tracker": return "Loading expedition tracker…";
        case "assistant":
        case "assistant-entry": return "Loading focused assistant…";
        default: return "Loading Hex Crawl…";
    }
}


function renderAnonymousAccess(rootElement: HTMLElement, routeRequiresSignIn: boolean): void {
    rootElement.innerHTML = `
        <section class="hc-page">
            <header class="hc-page-header">
                <div>
                    <h1>Hex Crawl DM tools</h1>
                    <p>Hex Crawl is available through Dorks & Dice, but saved worlds and crawl sessions are account-owned.</p>
                </div>
            </header>
            <section class="hc-panel hc-public-access">
                <h2>${routeRequiresSignIn ? "Sign in to open this Hex Crawl route" : "Sign in to use persistent Hex Crawl tools"}</h2>
                <p>Use the Dorks & Dice account controls to sign in. Once signed in, Hex Crawl can load your Overworlds, saved crawl sessions, and focused DM assistants.</p>
                <p class="hc-muted">Anonymous access does not create a shared or placeholder owner. Temporary anonymous crawl sessions are not enabled.</p>
            </section>
        </section>`;
}
