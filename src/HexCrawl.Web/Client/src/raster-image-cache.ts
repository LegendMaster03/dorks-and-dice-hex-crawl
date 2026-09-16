type CacheEntry = {
    state: "loading" | "ready" | "failed";
    source?: CanvasImageSource;
    objectUrl?: string;
};

export class RasterImageCache {
    private readonly entries = new Map<string, CacheEntry>();
    private disposed = false;

    public get(url: string, onReady: () => void): CanvasImageSource | null {
        const existing = this.entries.get(url);
        if (existing?.state === "ready") return existing.source ?? null;
        if (existing) return null;

        const entry: CacheEntry = { state: "loading" };
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
        try {
            const response = await fetch(url, { headers: { Accept: "image/png,image/jpeg,image/webp" } });
            if (!response.ok) throw new Error(`Raster request failed (${response.status}).`);
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
        } catch {
            if (this.entries.get(url) === entry) entry.state = "failed";
        }
    }

    private release(entry: CacheEntry): void {
        if (typeof ImageBitmap !== "undefined" && entry.source instanceof ImageBitmap) entry.source.close();
        if (entry.objectUrl) URL.revokeObjectURL(entry.objectUrl);
        entry.source = undefined;
        entry.objectUrl = undefined;
    }
}

function loadHtmlImage(url: string): Promise<HTMLImageElement> {
    return new Promise((resolve, reject) => {
        const image = new Image();
        image.onload = () => resolve(image);
        image.onerror = () => reject(new Error("Raster image could not be decoded."));
        image.src = url;
    });
}
