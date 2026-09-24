import type {
    ExpeditionDetail,
    ExpeditionSummary,
    GridDefinition,
    HexCoordinate,
    HexOrientation,
    Location,
    Overworld,
    OverworldSummary,
    PresentationProfile,
    RegistrationControlPoint,
    RuntimeAdvanceRequest,
    RuntimeProfile,
    TravelWatchAssistantRequest,
    NonSpatialWatchAssistantRequest,
    NavigationAssistantRequest,
    EncounterCadenceAssistantRequest,
    SourceMapList,
    SourceMapRole,
    SpatialFeature,
    StartExpeditionInput,
    StartStandaloneCrawlSessionInput,
    ToolHostContext,
    WorldPoint
} from "./types";

let activeBackendBaseUrl = "";

export function backendBaseFromContext(apiBaseUrl?: string | null): string {
    return apiBaseUrl ? `${apiBaseUrl.replace(/\/$/, "")}/upstream` : "";
}

export function sourceMapAssetUrl(worldId: string, sourceMapId: string): string {
    return `${activeBackendBaseUrl}/api/overworlds/${encodeURIComponent(worldId)}/source-maps/${encodeURIComponent(sourceMapId)}/asset`;
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
export type SourceMapUploadInput = {
    file: File;
    name: string;
    geographyKey: string;
    role: SourceMapRole;
    containsBakedGrid: boolean;
    expectedVersion: number;
};
export type SourceMapMetadataInput = Omit<SourceMapUploadInput, "file">;
export type WonderdraftInspection = {
    formatVersion: number | null;
    pixelWidth: number;
    pixelHeight: number;
    symbolCount: number;
    labelCount: number;
    pathCount: number;
    territoryCount: number;
    hasGrid: boolean;
    includedPacks: string[];
    includedDefaultPacks: string[];
    gridMetadata: Record<string, string>;
    scaleMetadata: Record<string, string>;
    physicalScale: {
        unitLabel: string;
        distancePerSegment: number;
        segmentCount: number;
        pixelLength: number;
        unitsPerPixel: number;
    } | null;
};
export type WonderdraftCandidate = {
    key: string;
    sourceKind: "Label" | "Symbol" | "Path" | "Territory";
    geometryKind: "Point" | "Line" | "Region";
    displayName: string;
    descriptor: string | null;
    problem: string | null;
    properties: Record<string, string>;
    sourcePosition: WorldPoint | null;
    sourcePoints: WorldPoint[];
    worldPosition: WorldPoint | null;
    worldPoints: WorldPoint[];
};
export type WonderdraftCandidatePreview = {
    sourceMapId: string;
    sourceScaleX: number;
    sourceScaleY: number;
    summary: WonderdraftInspection;
    candidates: WonderdraftCandidate[];
};
export type WonderdraftSourceImportResult = {
    world: Overworld;
    summary: WonderdraftInspection;
    sourceMapId: string;
    sourceRecordCount: number;
    registrationMode: "Existing" | "PhysicalScale" | "SourceOnly";
    registrationNote: string | null;
};

export type WonderdraftImportSelection = {
    candidateKey: string;
    target: "Location" | "PointFeature" | "LineFeature" | "RegionFeature";
    name: string;
    category: string;
    discoverability: "Obvious" | "Hidden" | "Conditional" | null;
};
export type ApiErrorKind = "validation" | "auth" | "not-found" | "conflict" | "server";

export class HexCrawlApiError extends Error {
    public constructor(
        public readonly status: number,
        public readonly kind: ApiErrorKind,
        message: string) {
        super(message);
        this.name = "HexCrawlApiError";
    }
}

export class HexCrawlApi {
    private constructor(private readonly backendBaseUrl: string) {
        activeBackendBaseUrl = backendBaseUrl;
    }

    public static async create(root: HTMLElement): Promise<{ api: HexCrawlApi; context: ToolHostContext | null }> {
        const contextUrl = root.dataset.toolContextUrl;
        if (!contextUrl) return { api: new HexCrawlApi(""), context: null };

        const response = await fetch(contextUrl, { headers: { Accept: "application/json" } });
        if (!response.ok) throw await apiError(response, "Tool Host context");
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

    public listSourceMaps(worldId: string): Promise<SourceMapList> {
        return this.getJson(`/api/overworlds/${encodeURIComponent(worldId)}/source-maps`, "Source maps");
    }

    public uploadSourceMap(worldId: string, input: SourceMapUploadInput): Promise<Overworld> {
        const form = new FormData();
        form.append("file", input.file, input.file.name);
        form.append("name", input.name);
        form.append("geographyKey", input.geographyKey);
        form.append("role", input.role);
        form.append("containsBakedGrid", String(input.containsBakedGrid));
        form.append("expectedVersion", String(input.expectedVersion));
        return this.sendForm("POST", `/api/overworlds/${encodeURIComponent(worldId)}/source-maps`, form, "Upload source map");
    }

    public inspectWonderdraftProject(worldId: string, file: File): Promise<WonderdraftInspection> {
        const form = new FormData();
        form.append("file", file, file.name);
        return this.sendForm(
            "POST",
            `/api/overworlds/${encodeURIComponent(worldId)}/source-maps/wonderdraft/inspect`,
            form,
            "Inspect Wonderdraft project");
    }

    public importWonderdraftSource(
        worldId: string,
        sourceMapId: string,
        file: File,
        expectedVersion: number): Promise<WonderdraftSourceImportResult> {
        const form = new FormData();
        form.append("file", file, file.name);
        form.append("expectedVersion", String(expectedVersion));
        return this.sendForm(
            "POST",
            `/api/overworlds/${encodeURIComponent(worldId)}/source-maps/${encodeURIComponent(sourceMapId)}/wonderdraft/source`,
            form,
            "Import Wonderdraft source");
    }

    public previewWonderdraftCandidates(
        worldId: string,
        sourceMapId: string,
        file: File): Promise<WonderdraftCandidatePreview> {
        const form = new FormData();
        form.append("file", file, file.name);
        return this.sendForm(
            "POST",
            `/api/overworlds/${encodeURIComponent(worldId)}/source-maps/${encodeURIComponent(sourceMapId)}/wonderdraft/candidates`,
            form,
            "Review Wonderdraft candidates");
    }

    public previewStoredWonderdraftCandidates(
        worldId: string,
        sourceMapId: string): Promise<WonderdraftCandidatePreview> {
        return this.getJson(
            `/api/overworlds/${encodeURIComponent(worldId)}/source-maps/${encodeURIComponent(sourceMapId)}/wonderdraft/candidates`,
            "Review retained Wonderdraft source");
    }

    public importWonderdraftCandidates(
        worldId: string,
        sourceMapId: string,
        file: File,
        selections: WonderdraftImportSelection[],
        expectedVersion: number): Promise<Overworld> {
        const form = new FormData();
        form.append("file", file, file.name);
        form.append("selections", JSON.stringify(selections));
        form.append("expectedVersion", String(expectedVersion));
        return this.sendForm(
            "POST",
            `/api/overworlds/${encodeURIComponent(worldId)}/source-maps/${encodeURIComponent(sourceMapId)}/wonderdraft/import`,
            form,
            "Import Wonderdraft candidates");
    }

    public promoteStoredWonderdraftCandidates(
        worldId: string,
        sourceMapId: string,
        selections: WonderdraftImportSelection[],
        expectedVersion: number): Promise<Overworld> {
        return this.sendJson(
            "POST",
            `/api/overworlds/${encodeURIComponent(worldId)}/source-maps/${encodeURIComponent(sourceMapId)}/wonderdraft/promote`,
            { selections, expectedVersion },
            "Promote retained Wonderdraft candidates");
    }

    public updateSourceMap(worldId: string, sourceMapId: string, input: SourceMapMetadataInput): Promise<Overworld> {
        return this.sendJson("PUT", `/api/overworlds/${encodeURIComponent(worldId)}/source-maps/${encodeURIComponent(sourceMapId)}`, {
            geographyKey: input.geographyKey,
            name: input.name,
            role: input.role,
            containsBakedGrid: input.containsBakedGrid,
            expectedVersion: input.expectedVersion
        }, "Update source map");
    }

    public registerSourceMap(
        worldId: string,
        sourceMapId: string,
        controlPoints: RegistrationControlPoint[],
        expectedVersion: number): Promise<Overworld> {
        return this.sendJson("PUT", `/api/overworlds/${encodeURIComponent(worldId)}/source-maps/${encodeURIComponent(sourceMapId)}/registration`, {
            controlPoints,
            expectedVersion
        }, "Register source map");
    }

    public deleteSourceMap(worldId: string, sourceMapId: string, expectedVersion: number): Promise<Overworld> {
        return this.deleteJson(`/api/overworlds/${encodeURIComponent(worldId)}/source-maps/${encodeURIComponent(sourceMapId)}?expectedVersion=${expectedVersion}`, "Delete source map");
    }

    public sourceMapAssetUrl(worldId: string, sourceMapId: string): string {
        return `${this.backendBaseUrl}/api/overworlds/${encodeURIComponent(worldId)}/source-maps/${encodeURIComponent(sourceMapId)}/asset`;
    }

    public getRuntimeProfiles(): Promise<RuntimeProfile[]> {
        return this.getJson("/api/runtime/profiles", "Runtime profiles");
    }

    public getPresentationProfiles(): Promise<PresentationProfile[]> {
        return this.getJson("/api/presentation/presets", "Presentation presets");
    }

    public listExpeditions(worldId?: string): Promise<ExpeditionSummary[]> {
        return worldId
            ? this.getJson(`/api/overworlds/${encodeURIComponent(worldId)}/expeditions`, "Expedition list")
            : this.getJson("/api/expeditions", "Expedition list");
    }

    public startExpedition(worldId: string, name: string, procedureKey: string, startHex: HexCoordinate): Promise<ExpeditionDetail> {
        return this.startConfiguredExpedition(worldId, {
            name,
            procedureKey,
            presentationKey: "exploration-map",
            startHex
        });
    }

    public startConfiguredExpedition(worldId: string, input: StartExpeditionInput): Promise<ExpeditionDetail> {
        return this.sendJson("POST", `/api/overworlds/${encodeURIComponent(worldId)}/expeditions`, input, "Start expedition");
    }

    public startStandaloneSession(input: StartStandaloneCrawlSessionInput): Promise<ExpeditionDetail> {
        return this.sendJson("POST", "/api/expeditions", input, "Start crawl session");
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

    public recordTravelAssistant(expeditionId: string, input: TravelWatchAssistantRequest): Promise<ExpeditionDetail> {
        return this.sendJson("POST", `/api/expeditions/${encodeURIComponent(expeditionId)}/assistants/travel`, input, "Travel/watch assistant");
    }

    public recordWatchAssistant(expeditionId: string, input: NonSpatialWatchAssistantRequest): Promise<ExpeditionDetail> {
        return this.sendJson("POST", `/api/expeditions/${encodeURIComponent(expeditionId)}/assistants/watch`, input, "Watch/time assistant");
    }

    public recordNavigationAssistant(expeditionId: string, input: NavigationAssistantRequest): Promise<ExpeditionDetail> {
        return this.sendJson("POST", `/api/expeditions/${encodeURIComponent(expeditionId)}/assistants/navigation`, input, "Navigation assistant");
    }

    public recordEncounterAssistant(expeditionId: string, input: EncounterCadenceAssistantRequest): Promise<ExpeditionDetail> {
        return this.sendJson("POST", `/api/expeditions/${encodeURIComponent(expeditionId)}/assistants/encounters`, input, "Encounter cadence assistant");
    }

    private async getJson<T>(path: string, label: string): Promise<T> {
        const response = await fetch(`${this.backendBaseUrl}${path}`, { headers: { Accept: "application/json" } });
        if (!response.ok) throw await apiError(response, label);
        return await response.json() as T;
    }

    private async sendJson<T>(method: "POST" | "PUT", path: string, body: unknown, label: string): Promise<T> {
        const response = await fetch(`${this.backendBaseUrl}${path}`, {
            method,
            headers: { Accept: "application/json", "Content-Type": "application/json" },
            body: JSON.stringify(body)
        });
        if (!response.ok) throw await apiError(response, label);
        return await response.json() as T;
    }

    private async sendForm<T>(method: "POST", path: string, body: FormData, label: string): Promise<T> {
        const response = await fetch(`${this.backendBaseUrl}${path}`, {
            method,
            headers: { Accept: "application/json" },
            body
        });
        if (!response.ok) throw await apiError(response, label);
        return await response.json() as T;
    }

    private async deleteJson<T>(path: string, label: string): Promise<T> {
        const response = await fetch(`${this.backendBaseUrl}${path}`, { method: "DELETE", headers: { Accept: "application/json" } });
        if (!response.ok) throw await apiError(response, label);
        return await response.json() as T;
    }
}

export async function apiError(response: Response, label: string): Promise<HexCrawlApiError> {
    const kind = errorKind(response.status);
    const detail = kind === "validation" ? await validationDetail(response) : null;
    const message = detail
        ? `${label} failed: ${detail}`
        : kind === "auth"
            ? `${label} failed because the current session is not authorized.`
            : kind === "not-found"
                ? `${label} was not found.`
                : kind === "conflict"
                    ? `${label} could not be saved because the stored version changed.`
                    : `${label} request failed (${response.status}).`;
    return new HexCrawlApiError(response.status, kind, message);
}

function errorKind(status: number): ApiErrorKind {
    if (status === 400 || status === 413 || status === 422) return "validation";
    if (status === 401 || status === 403) return "auth";
    if (status === 404) return "not-found";
    if (status === 409 || status === 412) return "conflict";
    return "server";
}

async function validationDetail(response: Response): Promise<string | null> {
    try {
        const body = await response.json() as { error?: unknown; detail?: unknown };
        if (typeof body.error === "string" && body.error.trim()) return body.error.trim();
        return typeof body.detail === "string" && body.detail.trim() ? body.detail.trim() : null;
    } catch {
        return null;
    }
}
