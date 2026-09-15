import type { DemoWorld, ToolHostContext } from "./types";

export function backendBaseFromContext(apiBaseUrl?: string | null): string {
    return apiBaseUrl ? `${apiBaseUrl.replace(/\/$/, "")}/upstream` : "";
}

export class HexCrawlApi {
    private constructor(private readonly backendBaseUrl: string) {}

    public static async create(root: HTMLElement): Promise<{ api: HexCrawlApi; context: ToolHostContext | null }> {
        const contextUrl = root.dataset.toolContextUrl;
        if (!contextUrl) {
            return { api: new HexCrawlApi(""), context: null };
        }

        const response = await fetch(contextUrl, { headers: { Accept: "application/json" } });
        if (!response.ok) throw new Error(`Tool Host context request failed (${response.status}).`);
        const context = await response.json() as ToolHostContext;
        return { api: new HexCrawlApi(backendBaseFromContext(context.apiBaseUrl)), context };
    }

    public async getDemoWorld(orientation: "pointy" | "flat", scale: number, unit: "mi" | "km"): Promise<DemoWorld> {
        const params = new URLSearchParams({ orientation, scale: String(scale), unit });
        const response = await fetch(`${this.backendBaseUrl}/api/demo/world?${params}`, { headers: { Accept: "application/json" } });
        if (!response.ok) throw new Error(`Demo world request failed (${response.status}).`);
        return await response.json() as DemoWorld;
    }
}
