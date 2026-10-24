import { openWorkspaceDrawer, type WorkspaceDrawer } from "../../ui/workspace";

/** Reuse the nonmodal workspace and its focus/close lifecycle for map tools. */
export function openExpeditionMapPanel(
    root: HTMLElement,
    title: string,
    build: (body: HTMLElement, close: () => void) => void,
    returnFocus: HTMLElement | (() => HTMLElement | null) | null =
        document.activeElement instanceof HTMLElement ? document.activeElement : null,
    onClose?: () => void): WorkspaceDrawer {
    const panel = openWorkspaceDrawer(root, title, build, returnFocus, onClose);
    panel.element.classList.add("hc-expedition-map-tool");
    // Anchor inside the map frame so the toolbar can never be covered by a
    // tool. No backdrop or focus trap intercepts the uncovered canvas.
    root.querySelector(".hc-map-frame")?.append(panel.element);
    panel.element.querySelector<HTMLButtonElement>(".hc-focus-workspace-close")?.focus({ preventScroll: true });
    return panel;
}
