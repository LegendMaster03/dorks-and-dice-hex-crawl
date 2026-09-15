import type {
    DemoWorld,
    RuntimeAdvanceRequest,
    RuntimeProfile,
    RuntimeState,
    ToolHostContext
} from "./types";

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
        return await this.getJson<DemoWorld>(`/api/demo/world?${params}`, "Demo world");
    }

    public async getRuntimeProfiles(): Promise<RuntimeProfile[]> {
        return await this.getJson<RuntimeProfile[]>("/api/demo/runtime/profiles", "Runtime profiles");
    }

    public async getRuntime(): Promise<RuntimeState> {
        return await this.getJson<RuntimeState>("/api/demo/runtime", "Runtime state");
    }

    public async resetRuntime(
        profileKey: string,
        orientation: "pointy" | "flat",
        scale: number,
        unit: "mi" | "km"): Promise<RuntimeState> {
        return await this.postJson<RuntimeState>(
            "/api/demo/runtime/reset",
            { profileKey, orientation, scale, unit },
            "Runtime reset");
    }

    public async advanceRuntime(request: RuntimeAdvanceRequest): Promise<RuntimeState> {
        return await this.postJson<RuntimeState>(
            "/api/demo/runtime/advance",
            request,
            "Runtime advance");
    }

    public async discover(subjectId: string, subjectType: "Location" | "Feature"): Promise<RuntimeState> {
        return await this.postJson<RuntimeState>(
            "/api/demo/runtime/discover",
            { subjectId, subjectType, source: "dm:manual-discovery" },
            "Runtime discovery");
    }

    private async getJson<T>(path: string, label: string): Promise<T> {
        const response = await fetch(`${this.backendBaseUrl}${path}`, { headers: { Accept: "application/json" } });
        if (!response.ok) throw new Error(await errorMessage(response, label));
        return await response.json() as T;
    }

    private async postJson<T>(path: string, body: unknown, label: string): Promise<T> {
        const response = await fetch(`${this.backendBaseUrl}${path}`, {
            method: "POST",
            headers: {
                Accept: "application/json",
                "Content-Type": "application/json"
            },
            body: JSON.stringify(body)
        });
        if (!response.ok) throw new Error(await errorMessage(response, label));
        return await response.json() as T;
    }
}

async function errorMessage(response: Response, label: string): Promise<string> {
    try {
        const body = await response.json() as { error?: string };
        if (body.error) return `${label} failed: ${body.error}`;
    } catch {
        // Fall through to the status-only error.
    }
    return `${label} request failed (${response.status}).`;
}
