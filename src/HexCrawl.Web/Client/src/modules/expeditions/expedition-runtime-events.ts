import type { ExpeditionDetail } from "../../types";

const runtimeChangedEventName = "hex-crawl:expedition-runtime-changed";

export function publishExpeditionRuntimeChanged(
    root: HTMLElement,
    runtime: ExpeditionDetail): void {
    root.dispatchEvent(new CustomEvent<ExpeditionDetail>(runtimeChangedEventName, {
        detail: runtime
    }));
}

export function subscribeExpeditionRuntimeChanged(
    root: HTMLElement,
    accept: (runtime: ExpeditionDetail) => void): () => void {
    const listener = (event: Event): void => {
        if (!(event instanceof CustomEvent)) return;
        const runtime = event.detail as ExpeditionDetail | undefined;
        if (runtime) accept(runtime);
    };
    root.addEventListener(runtimeChangedEventName, listener);
    return () => root.removeEventListener(runtimeChangedEventName, listener);
}
