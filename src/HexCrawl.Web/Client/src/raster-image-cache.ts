type CacheEntry = {
    state: "loading" | "ready" | "failed";
    attempts: number;
    source?: CanvasImageSource;
    objectUrl?: string;
    retryTimer?: number;
};

const MaximumLoadAttempts = 3;
const RetryDelayMilliseconds = 500;

export class RasterImageCache {
    private readonly entries = new Map<string, CacheEntry>();
    private disposed = false;

    public get(url: string, onReady: () => void): CanvasImageSource | null {
        const existing = this.entries.get(url);
        if (existing?.state === "ready") return existing.source ?? null;
        if (existing) return null;

        const entry: CacheEntry = { state: "loading", attempts: 0 };
        this.entries.set(url, entry);
        void this.load(url, entry, onReady);
        return null;
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

    private async load(url: string, entry: CacheEntry, onReady: () => void): Promise<void> {
        entry.attempts += 1;
        try {
            const response = await fetch(url, { headers: { Accept: "image/png,image/jpeg,image/webp" } });
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
            onReady();
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
                    void this.load(url, entry, onReady);
                }, delay);
                return;
            }

            const detail = error instanceof Error ? error.message : String(error);
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
