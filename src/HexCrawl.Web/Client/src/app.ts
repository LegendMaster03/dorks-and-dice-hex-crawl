import { HexCrawlApi } from "./api";
import { findClientModule } from "./client-module-catalog";
import { ensureStyles } from "./styles";
import { deriveToolRoute, navigateTool, parseToolRoute } from "./tool-route";
import { describeUiError } from "./ui-error";

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

        const navigate = (route: string, replace = false): void =>
            navigateTool(basePath, route, replace);

        const renderCurrentRoute = async (): Promise<void> => {
            cleanup?.();
            cleanup = null;

            const path = deriveToolRoute(basePath, window.location.pathname, initialRoute);
            const route = parseToolRoute(path);
            if (route.kind === "unknown") {
                navigate("/", true);
                return;
            }

            const module = findClientModule(route);
            renderLoading(rootElement, module.loadingMessage(route));

            try {
                if (hostedAnonymous) {
                    renderAnonymousAccess(rootElement, route.kind !== "home");
                    return;
                }

                cleanup = await module.render(route, {
                    root: rootElement,
                    api,
                    navigate
                });
            } catch (value) {
                const error = describeUiError(value);
                const panel = document.createElement("section");
                panel.className = "hc-page hc-error-page";
                const heading = document.createElement("h1");
                heading.textContent = error.kind === "not-found"
                    ? "This Hex Crawl route was not found"
                    : "Hex Crawl could not load this route";
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

        if (parseToolRoute(
            deriveToolRoute(basePath, window.location.pathname, initialRoute)).kind === "unknown") {
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
