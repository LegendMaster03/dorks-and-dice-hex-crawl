import { HexCrawlApiError } from "./api";

export type UiErrorKind = "validation" | "auth" | "not-found" | "conflict" | "server" | "unknown";
export type UiError = { kind: UiErrorKind; message: string };

export function describeUiError(value: unknown): UiError {
    if (value instanceof HexCrawlApiError) {
        switch (value.kind) {
            case "validation": return { kind: "validation", message: value.message };
            case "auth": return { kind: "auth", message: "Your Dorks & Dice session is no longer available. Refresh or sign in again, then retry." };
            case "not-found": return { kind: "not-found", message: value.message };
            case "conflict": return { kind: "conflict", message: "This resource changed in another session. Reload the latest state before saving again." };
            case "server": return { kind: "server", message: value.message };
        }
    }
    if (value instanceof Error) return { kind: "unknown", message: value.message };
    return { kind: "unknown", message: "The operation could not be completed." };
}

export function showUiError(element: HTMLElement, value: unknown): void {
    const error = describeUiError(value);
    element.dataset.kind = error.kind;
    element.textContent = error.message;
    element.hidden = false;
}

export function clearUiError(element: HTMLElement): void {
    element.hidden = true;
    element.textContent = "";
    delete element.dataset.kind;
}
