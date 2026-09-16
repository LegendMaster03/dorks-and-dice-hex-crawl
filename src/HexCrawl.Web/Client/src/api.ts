import type {
    ExpeditionDetail,
    ExpeditionSummary,
    GridDefinition,
    HexCoordinate,
    HexOrientation,
    Location,
    Overworld,
    OverworldSummary,
    RuntimeAdvanceRequest,
    RuntimeProfile,
    SpatialFeature,
    ToolHostContext,
    WorldPoint
} from "./types";

export function backendBaseFromContext(apiBaseUrl?: string | null): string {
    return apiBaseUrl ? `${apiBaseUrl.replace(/\/$/, "")}/upstream` : "";
}

export type CreateOverworldInput = {
    name: string;
    orientation: HexOrientation;
    origin: WorldPoint;
    rotationDegrees: number;
    hexRadiusWorldUnits: number;
    neighborCenterDistance: number;
    distanceUnit: { kind: "Mile" | "Kilometer" | "Custom"; symbol: string; metersPerUnit: number | null };
};

export type LocationMutationInput = Omit<Location, "id"> & { expectedVersion: number };
export type FeatureMutationInput = Omit<SpatialFeature, "id"> & { expectedVersion: number };

export class HexCrawlApi {
    private constructor(private readonly backendBaseUrl: string) {}

    public static async create(root: HTMLElement): Promise<{ api: HexCrawlApi; context: ToolHostContext | null }> {
        const contextUrl = root.dataset.toolContextUrl;
        if (!contextUrl) return { api: new HexCrawlApi(""), context: null };

        const response = await fetch(contextUrl, { headers: { Accept: "application/json" } });
        if (!response.ok) throw new Error(`Tool Host context request failed (${response.status}).`);
        const context = await response.json() as ToolHostContext;
        return { api: new HexCrawlApi(backendBaseFromContext(context.apiBaseUrl)), context };
    }

    public listOverworlds(): Promise<OverworldSummary[]> {
        return this.getJson("/api/overworlds", "Overworld list");
    }

    public getOverworld(id: string): Promise<Overworld> {
        return this.getJson(`/api/overworlds/${encodeURIComponent(id)}`, "Overworld");
    }

    public createOverworld(input: CreateOverworldInput): Promise<Overworld> {
        return this.sendJson("POST", "/api/overworlds", input, "Create overworld");
    }

    public updateOverworld(id: string, name: string, grid: GridDefinition, expectedVersion: number): Promise<Overworld> {
        return this.sendJson("PUT", `/api/overworlds/${encodeURIComponent(id)}`, { name, grid, expectedVersion }, "Update overworld");
    }

    public createLocation(worldId: string, input: LocationMutationInput): Promise<Overworld> {
        return this.sendJson("POST", `/api/overworlds/${encodeURIComponent(worldId)}/locations`, input, "Create location");
    }

    public updateLocation(worldId: string, locationId: string, input: LocationMutationInput): Promise<Overworld> {
        return this.sendJson("PUT", `/api/overworlds/${encodeURIComponent(worldId)}/locations/${encodeURIComponent(locationId)}`, input, "Update location");
    }

    public deleteLocation(worldId: string, locationId: string, expectedVersion: number): Promise<Overworld> {
        return this.deleteJson(`/api/overworlds/${encodeURIComponent(worldId)}/locations/${encodeURIComponent(locationId)}?expectedVersion=${expectedVersion}`, "Delete location");
    }

    public createFeature(worldId: string, input: FeatureMutationInput): Promise<Overworld> {
        return this.sendJson("POST", `/api/overworlds/${encodeURIComponent(worldId)}/features`, input, "Create feature");
    }

    public updateFeature(worldId: string, featureId: string, input: FeatureMutationInput): Promise<Overworld> {
        return this.sendJson("PUT", `/api/overworlds/${encodeURIComponent(worldId)}/features/${encodeURIComponent(featureId)}`, input, "Update feature");
    }

    public deleteFeature(worldId: string, featureId: string, expectedVersion: number): Promise<Overworld> {
        return this.deleteJson(`/api/overworlds/${encodeURIComponent(worldId)}/features/${encodeURIComponent(featureId)}?expectedVersion=${expectedVersion}`, "Delete feature");
    }

    public getRuntimeProfiles(): Promise<RuntimeProfile[]> {
        return this.getJson("/api/runtime/profiles", "Runtime profiles");
    }

    public listExpeditions(worldId: string): Promise<ExpeditionSummary[]> {
        return this.getJson(`/api/overworlds/${encodeURIComponent(worldId)}/expeditions`, "Expedition list");
    }

    public startExpedition(worldId: string, name: string, procedureKey: string, startHex: HexCoordinate): Promise<ExpeditionDetail> {
        return this.sendJson("POST", `/api/overworlds/${encodeURIComponent(worldId)}/expeditions`, { name, procedureKey, startHex }, "Start expedition");
    }

    public getExpedition(expeditionId: string): Promise<ExpeditionDetail> {
        return this.getJson(`/api/expeditions/${encodeURIComponent(expeditionId)}`, "Expedition");
    }

    public advanceExpedition(expeditionId: string, input: RuntimeAdvanceRequest): Promise<ExpeditionDetail> {
        return this.sendJson("POST", `/api/expeditions/${encodeURIComponent(expeditionId)}/advance`, input, "Advance expedition");
    }

    public discover(expeditionId: string, expectedVersion: number, subjectId: string, subjectType: "Location" | "Feature"): Promise<ExpeditionDetail> {
        return this.sendJson("POST", `/api/expeditions/${encodeURIComponent(expeditionId)}/discover`, {
            expectedVersion,
            subjectId,
            subjectType,
            source: "dm:manual-discovery"
        }, "Runtime discovery");
    }

    private async getJson<T>(path: string, label: string): Promise<T> {
        const response = await fetch(`${this.backendBaseUrl}${path}`, { headers: { Accept: "application/json" } });
        if (!response.ok) throw new Error(await errorMessage(response, label));
        return await response.json() as T;
    }

    private async sendJson<T>(method: "POST" | "PUT", path: string, body: unknown, label: string): Promise<T> {
        const response = await fetch(`${this.backendBaseUrl}${path}`, {
            method,
            headers: { Accept: "application/json", "Content-Type": "application/json" },
            body: JSON.stringify(body)
        });
        if (!response.ok) throw new Error(await errorMessage(response, label));
        return await response.json() as T;
    }

    private async deleteJson<T>(path: string, label: string): Promise<T> {
        const response = await fetch(`${this.backendBaseUrl}${path}`, { method: "DELETE", headers: { Accept: "application/json" } });
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
