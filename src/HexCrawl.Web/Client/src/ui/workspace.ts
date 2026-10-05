const drawerClosers = new WeakMap<HTMLElement, () => void>();

export type WorkspaceDrawer = {
    element: HTMLElement;
    close: () => void;
};

export function textElement<K extends keyof HTMLElementTagNameMap>(
    tag: K,
    text: string,
    className?: string): HTMLElementTagNameMap[K] {
    const element = document.createElement(tag);
    element.textContent = text;
    if (className) element.className = className;
    return element;
}

export function badge(text: string, tone: "neutral" | "good" | "warning" | "danger" | "info" = "neutral"): HTMLElement {
    const element = textElement("span", text, `hc-ux-badge hc-ux-badge-${tone}`);
    return element;
}

export function statAction(
    label: string,
    value: string,
    detail: string | null,
    activate: () => void,
    status?: "neutral" | "good" | "warning" | "danger" | "info"): HTMLButtonElement {
    const button = document.createElement("button");
    button.type = "button";
    button.className = `hc-stat-action${status && status !== "neutral" ? ` is-${status}` : ""}`;
    const heading = textElement("span", label, "hc-stat-action-label");
    const primary = textElement("strong", value, "hc-stat-action-value");
    button.append(heading, primary);
    if (detail) button.append(textElement("span", detail, "hc-stat-action-detail"));
    button.addEventListener("click", activate);
    return button;
}

export function openWorkspaceDrawer(
    root: HTMLElement,
    title: string,
    build: (body: HTMLElement, close: () => void) => void,
    returnFocus: HTMLElement | null = document.activeElement instanceof HTMLElement ? document.activeElement : null,
    onClose?: () => void): WorkspaceDrawer {
    const existing = root.querySelector<HTMLElement>("[data-phase15-drawer]");
    if (existing) {
        const closeExisting = drawerClosers.get(existing);
        if (closeExisting) closeExisting();
        else existing.remove();
    }

    const panel = document.createElement("section");
    panel.className = "hc-focus-workspace";
    panel.dataset.phase15Drawer = "";
    panel.setAttribute("role", "dialog");
    panel.setAttribute("aria-modal", "false");
    panel.setAttribute("aria-labelledby", `hc-focus-${globalThis.crypto.randomUUID()}`);

    const header = document.createElement("header");
    header.className = "hc-focus-workspace-header";
    const heading = textElement("h2", title);
    heading.id = panel.getAttribute("aria-labelledby")!;
    const closeButton = document.createElement("button");
    closeButton.type = "button";
    closeButton.textContent = "Close";
    closeButton.className = "hc-focus-workspace-close";
    header.append(heading, closeButton);

    const body = document.createElement("div");
    body.className = "hc-focus-workspace-body";
    panel.append(header, body);

    let closed = false;
    const onKeyDown = (event: KeyboardEvent): void => {
        if (event.key === "Escape") {
            event.preventDefault();
            close();
        }
    };
    const close = (): void => {
        if (closed) return;
        closed = true;
        panel.removeEventListener("keydown", onKeyDown);
        drawerClosers.delete(panel);
        onClose?.();
        panel.remove();
        if (returnFocus?.isConnected) returnFocus.focus();
    };

    drawerClosers.set(panel, close);
    closeButton.addEventListener("click", close);
    panel.addEventListener("keydown", onKeyDown);
    root.append(panel);
    build(body, close);
    closeButton.focus();
    return { element: panel, close };
}

export function disclosure(title: string, open = false): HTMLDetailsElement {
    const details = document.createElement("details");
    details.className = "hc-ux-disclosure";
    details.open = open;
    const summary = document.createElement("summary");
    summary.textContent = title;
    details.append(summary);
    return details;
}

export function setButtonPending(button: HTMLButtonElement, pending: boolean, pendingLabel: string): () => void {
    const previous = button.textContent ?? "";
    const previousDisabled = button.disabled;
    button.disabled = pending;
    if (pending) button.textContent = pendingLabel;
    return () => {
        button.disabled = previousDisabled;
        button.textContent = previous;
    };
}

export function humanizeIdentifier(value: string): string {
    const withSpaces = value
        .replace(/[_\.\-]+/g, " ")
        .replace(/([a-z0-9])([A-Z])/g, "$1 $2")
        .trim();
    if (!withSpaces) return value;
    return withSpaces.charAt(0).toUpperCase() + withSpaces.slice(1);
}