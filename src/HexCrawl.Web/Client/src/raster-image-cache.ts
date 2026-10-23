type CacheEntry = {
    state: "loading" | "ready" | "failed";
    attempts: number;
    source?: CanvasImageSource;
    objectUrl?: string;
    retryTimer?: number;
    controller: AbortController;
    listeners: Set<() => void>;
    waiters: Set<{ resolve: (source: CanvasImageSource) => void; reject: (reason: Error) => void }>;
};

const MaximumLoadAttempts = 3;
const RetryDelayMilliseconds = 500;

export class RasterImageCache {
    private readonly entries = new Map<string, CacheEntry>();
    private disposed = false;

    public get(url: string, onReady: () => void): CanvasImageSource | null {
        if (this.disposed) return null;
        const entry = this.ensureEntry(url);
        if (entry.state === "ready") return entry.source ?? null;
        entry.listeners.add(onReady);
        return null;
    }

    /** Subscribe to the same load as the renderer, including terminal failure/disposal. */
    public waitUntilReady(url: string, timeoutMilliseconds = 15000): Promise<CanvasImageSource> {
        if (this.disposed) return Promise.reject(new Error("The image cache was disposed."));
        const existing = this.entries.get(url);
        // An explicit export retry may restart a previously exhausted load.
        if (existing?.state === "failed" && existing.retryTimer === undefined) {
            this.release(existing);
            this.entries.delete(url);
        }
        const entry = this.ensureEntry(url);
        if (entry.state === "ready" && entry.source) return Promise.resolve(entry.source);
        return new Promise((resolve, reject) => {
            const waiter = {
                resolve: (source: CanvasImageSource): void => { cleanup(); resolve(source); },
                reject: (reason: Error): void => { cleanup(); reject(reason); }
            };
            const timer = window.setTimeout(() => waiter.reject(new Error("Map image load timed out.")), timeoutMilliseconds);
            const cleanup = (): void => {
                window.clearTimeout(timer);
                entry.waiters.delete(waiter);
            };
            entry.waiters.add(waiter);
        });
    }

    private ensureEntry(url: string): CacheEntry {
        const existing = this.entries.get(url);
        if (existing) return existing;
        const entry: CacheEntry = {
            state: "loading", attempts: 0, controller: new AbortController(),
            listeners: new Set(), waiters: new Set()
        };
        this.entries.set(url, entry);
        void this.load(url, entry);
        return entry;
    }

    public prune(activeUrls: Set<string>): void {
        for (const [url, entry] of this.entries) {
            if (!activeUrls.has(url)) {
                this.release(entry);
                this.entries.delete(url);
            }
        }
    }

    public dispose(): void {
        this.disposed = true;
        for (const entry of this.entries.values()) this.release(entry);
        this.entries.clear();
    }

    private async load(url: string, entry: CacheEntry): Promise<void> {
        entry.attempts += 1;
        try {
            const response = await fetch(url, { signal: entry.controller.signal, headers: { Accept: "image/png,image/jpeg,image/webp" } });
            if (!response.ok) throw new Error(`Map image request failed (${response.status}).`);
            const blob = await response.blob();
            if (this.disposed || this.entries.get(url) !== entry) return;

            if (typeof createImageBitmap === "function") {
                const bitmap = await createImageBitmap(blob);
                if (this.disposed || this.entries.get(url) !== entry) {
                    bitmap.close();
                    return;
                }
                entry.source = bitmap;
            } else {
                const objectUrl = URL.createObjectURL(blob);
                entry.objectUrl = objectUrl;
                entry.source = await loadHtmlImage(objectUrl);
                if (this.disposed || this.entries.get(url) !== entry) {
                    this.release(entry);
                    return;
                }
            }
            entry.state = "ready";
            for (const waiter of [...entry.waiters]) waiter.resolve(entry.source!);
            for (const onReady of entry.listeners) onReady();
            entry.listeners.clear();
        } catch (error) {
            if (this.disposed || this.entries.get(url) !== entry) return;
            this.releaseDecodedSource(entry);
            entry.state = "failed";
            if (entry.attempts < MaximumLoadAttempts) {
                const delay = RetryDelayMilliseconds * entry.attempts;
                entry.retryTimer = window.setTimeout(() => {
                    if (this.disposed || this.entries.get(url) !== entry) return;
                    entry.retryTimer = undefined;
                    entry.state = "loading";
                    void this.load(url, entry);
                }, delay);
                return;
            }

            const detail = error instanceof Error ? error.message : String(error);
            for (const waiter of [...entry.waiters]) waiter.reject(new Error(detail));
            entry.listeners.clear();
            console.warn(`Reference map image could not be loaded after ${entry.attempts} attempts: ${detail}`);
        }
    }

    private releaseDecodedSource(entry: CacheEntry): void {
        if (typeof ImageBitmap !== "undefined" && entry.source instanceof ImageBitmap) entry.source.close();
        if (entry.objectUrl) URL.revokeObjectURL(entry.objectUrl);
        entry.source = undefined;
        entry.objectUrl = undefined;
    }

    private release(entry: CacheEntry): void {
        entry.controller.abort();
        for (const waiter of [...entry.waiters]) waiter.reject(new Error("Map image load was canceled."));
        entry.listeners.clear();
        if (entry.retryTimer !== undefined) {
            window.clearTimeout(entry.retryTimer);
            entry.retryTimer = undefined;
        }
        this.releaseDecodedSource(entry);
    }
}

function loadHtmlImage(url: string): Promise<HTMLImageElement> {
    return new Promise((resolve, reject) => {
        const image = new Image();
        image.onload = () => resolve(image);
        image.onerror = () => reject(new Error("Map image could not be decoded."));
        image.src = url;
    });
}
