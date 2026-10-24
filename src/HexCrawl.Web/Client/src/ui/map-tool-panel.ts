import { openWorkspaceDrawer, type WorkspaceDrawer } from "./workspace";

export function ensureMapToolStyles(): void {
    if (document.getElementById("hex-crawl-map-tool-styles")) return;
    const style = document.createElement("style");
    style.id = "hex-crawl-map-tool-styles";
    style.textContent = `
        .hex-crawl-app .hc-map-tool-workspace.hc-page > .hc-workspace-grid {
            display:grid; grid-template-columns:minmax(0,1fr);
        }
        .hex-crawl-app .hc-map-tool-workspace .hc-map-panel {
            display:flex; flex-direction:column; position:static; min-width:0;
        }
        .hex-crawl-app .hc-map-tool-workspace .hc-map-host {
            height:clamp(25rem,70vh,60rem); min-height:0;
        }
        .hex-crawl-app .hc-map-tool-workspace .hc-map-canvas { height:100%; min-height:0; }
        .hc-map-tool-frame { position:relative; min-width:0; }
        .hc-map-tool-toolbar {
            display:flex; flex-wrap:wrap; gap:.4rem; margin-bottom:.7rem;
            position:sticky; top:.25rem; z-index:60;
            padding:.5rem; border-radius:.6rem;
            background:var(--hc-surface); border:1px solid var(--hc-border);
        }
        .hc-map-tool-toolbar > .hc-error { flex-basis:100%; margin:0; }
        .hc-map-tool-panel.hc-focus-workspace {
            position:absolute; z-index:45; top:auto; bottom:.75rem; right:.75rem; left:auto;
            width:min(30rem,44vw); height:min(38rem,calc(100% - 1.5rem)); max-height:calc(100% - 1.5rem);
            box-sizing:border-box; border:1px solid var(--hc-border); border-radius:1rem;
            background:var(--hc-surface); color:var(--hc-text); color-scheme:inherit;
            box-shadow:0 .8rem 2.4rem rgba(0,0,0,.28);
            display:grid; grid-template-rows:auto minmax(0,1fr); overflow:hidden;
        }
        .hc-map-tool-panel > .hc-focus-workspace-header {
            display:flex; align-items:center; justify-content:space-between; gap:1rem;
            padding:1rem; border-bottom:1px solid var(--hc-border); background:var(--hc-surface);
        }
        .hc-map-tool-panel > .hc-focus-workspace-header h2 { margin:0; color:var(--hc-text-strong); }
        .hc-map-tool-panel > .hc-focus-workspace-body {
            overflow:auto; overscroll-behavior:contain; padding:1rem 1rem 2rem;
            display:grid; gap:1rem; align-content:start; background:var(--hc-surface);
        }
        .hc-map-tool-panel [hidden] { display:none !important; }
        .hc-map-tool-panel input, .hc-map-tool-panel select, .hc-map-tool-panel textarea {
            min-width:0; max-width:100%; box-sizing:border-box;
        }
        @media (max-width:760px) {
            .hex-crawl-app .hc-map-tool-workspace .hc-map-host { height:64dvh; min-height:20rem; }
            .hc-map-tool-panel.hc-focus-workspace {
                inset:auto .5rem .5rem; width:auto;
                height:min(48dvh,calc(100% - 1rem)); max-height:calc(100% - 1rem);
            }
        }
    `;
    document.head.append(style);
}

/** Shared nonmodal tools anchored to the map, with the existing drawer lifecycle. */
export function openMapToolPanel(
    root: HTMLElement,
    title: string,
    build: (body: HTMLElement, close: () => void) => void,
    returnFocus: HTMLElement | (() => HTMLElement | null) | null =
        document.activeElement instanceof HTMLElement ? document.activeElement : null,
    onClose?: () => void): WorkspaceDrawer {
    ensureMapToolStyles();
    const panel = openWorkspaceDrawer(root, title, build, returnFocus, onClose);
    panel.element.classList.add("hc-map-tool-panel");
    root.querySelector(".hc-map-tool-frame, .hc-map-frame")?.append(panel.element);
    panel.element.querySelector<HTMLButtonElement>(".hc-focus-workspace-close")?.focus({ preventScroll: true });
    return panel;
}

/** Move persistent editor hosts as a unit; switching tools never rebuilds forms. */
export function createMapToolHost(
    root: HTMLElement, host: HTMLElement, dock: HTMLElement,
    getReturnFocus?: () => HTMLElement | (() => HTMLElement | null) | null
): { open: (section: HTMLDetailsElement) => void; close: () => void; active: () => HTMLDetailsElement | null } {
    let drawer: WorkspaceDrawer | null = null;
    let active: HTMLDetailsElement | null = null;
    return {
        open(section) {
            if (section.parentElement !== host) throw new Error("Map tool must belong to its persistent host.");
            if (active === section && drawer) return;
            const opener = getReturnFocus ? getReturnFocus()
                : document.activeElement instanceof HTMLElement ? document.activeElement : null;
            // Opening another drawer closes the previous one first. Its cleanup
            // restores the same host, including file inputs and unsaved drafts.
            drawer = openMapToolPanel(root, section.querySelector("summary")?.textContent ?? "Map tool",
                body => { body.append(host); }, opener, () => {
                    dock.append(host);
                    drawer = null;
                    active = null;
                });
            active = section;
            for (const child of host.children) (child as HTMLElement).hidden = child !== section;
            section.open = true;
        },
        close() { drawer?.close(); },
        active: () => active
    };
}
