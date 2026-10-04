import { backendBaseFromContext, HexCrawlApiError } from "./api";
import type { ToolHostContext } from "./types";
import type {
    ApplyPendingResourceRequest,
    RecordForcedTravelUsageRequest,
    RecoverFromRestRequest,
    RemoveResourceRequest,
    ResetForcedTravelRequest,
    ResolveCampRequest,
    ResolveConsumptionRequest,
    ResolveExposureRequest,
    ResolveForagingRequest,
    ResolveForcedTravelCheckRequest,
    SurvivalOperation,
    SurvivalResources,
    UpsertResourceRequest
} from "./survival-types";

export class SurvivalResourcesApi {
    private readonly backendBaseUrl: string;

    public constructor(context: ToolHostContext | null) {
        this.backendBaseUrl = backendBaseFromContext(context?.apiBaseUrl);
    }

    public get(expeditionId: string): Promise<SurvivalResources> {
        return this.getJson(`/api/expeditions/${encodeURIComponent(expeditionId)}/survival`, "Survival and resources");
    }

    public upsertResource(expeditionId: string, resourceId: string, request: UpsertResourceRequest): Promise<SurvivalOperation> {
        return this.sendJson("PUT", `/api/expeditions/${encodeURIComponent(expeditionId)}/survival/resources/${encodeURIComponent(resourceId)}`, request, "Update expedition resource");
    }

    public removeResource(expeditionId: string, resourceId: string, request: RemoveResourceRequest): Promise<SurvivalOperation> {
        return this.sendJson("DELETE", `/api/expeditions/${encodeURIComponent(expeditionId)}/survival/resources/${encodeURIComponent(resourceId)}`, request, "Remove expedition resource");
    }

    public applyPendingResource(expeditionId: string, consequenceId: string, request: ApplyPendingResourceRequest): Promise<SurvivalOperation> {
        return this.sendJson("POST", `/api/expeditions/${encodeURIComponent(expeditionId)}/survival/resources/pending/${encodeURIComponent(consequenceId)}/apply`, request, "Apply pending resource consequence");
    }

    public resolveConsumption(expeditionId: string, request: ResolveConsumptionRequest): Promise<SurvivalOperation> {
        return this.sendJson("POST", `/api/expeditions/${encodeURIComponent(expeditionId)}/survival/consumption`, request, "Resolve resource consumption");
    }

    public resolveForaging(expeditionId: string, request: ResolveForagingRequest): Promise<SurvivalOperation> {
        return this.sendJson("POST", `/api/expeditions/${encodeURIComponent(expeditionId)}/survival/foraging`, request, "Resolve foraging");
    }

    public recordForcedTravelUsage(expeditionId: string, request: RecordForcedTravelUsageRequest): Promise<SurvivalOperation> {
        return this.sendJson("POST", `/api/expeditions/${encodeURIComponent(expeditionId)}/survival/forced-travel/usage`, request, "Record forced-travel usage");
    }

    public resolveForcedTravelCheck(expeditionId: string, request: ResolveForcedTravelCheckRequest): Promise<SurvivalOperation> {
        return this.sendJson("POST", `/api/expeditions/${encodeURIComponent(expeditionId)}/survival/forced-travel/check`, request, "Resolve forced-travel check");
    }

    public resetForcedTravel(expeditionId: string, request: ResetForcedTravelRequest): Promise<SurvivalOperation> {
        return this.sendJson("POST", `/api/expeditions/${encodeURIComponent(expeditionId)}/survival/forced-travel/reset`, request, "Reset forced travel");
    }

    public resolveExposure(expeditionId: string, request: ResolveExposureRequest): Promise<SurvivalOperation> {
        return this.sendJson("POST", `/api/expeditions/${encodeURIComponent(expeditionId)}/survival/exposure`, request, "Resolve survival exposure");
    }

    public resolveCamp(expeditionId: string, request: ResolveCampRequest): Promise<SurvivalOperation> {
        return this.sendJson("POST", `/api/expeditions/${encodeURIComponent(expeditionId)}/survival/camp`, request, "Resolve camp");
    }

    public recoverFromRest(expeditionId: string, request: RecoverFromRestRequest): Promise<SurvivalOperation> {
        return this.sendJson("POST", `/api/expeditions/${encodeURIComponent(expeditionId)}/survival/rest-recovery`, request, "Resolve rest recovery");
    }

    private async getJson<T>(path: string, label: string): Promise<T> {
        const response = await fetch(`${this.backendBaseUrl}${path}`, { headers: { Accept: "application/json" } });
        return this.readJson<T>(response, label);
    }

    private async sendJson<T>(method: string, path: string, body: unknown, label: string): Promise<T> {
        const response = await fetch(`${this.backendBaseUrl}${path}`, {
            method,
            headers: { Accept: "application/json", "Content-Type": "application/json" },
            body: JSON.stringify(body)
        });
        return this.readJson<T>(response, label);
    }

    private async readJson<T>(response: Response, label: string): Promise<T> {
        if (!response.ok) {
            let detail = "";
            try {
                const payload = await response.clone().json() as { detail?: string; title?: string };
                detail = payload.detail ?? payload.title ?? "";
            } catch {
                detail = (await response.text()).trim();
            }
            const kind = response.status === 400 ? "validation"
                : response.status === 401 || response.status === 403 ? "auth"
                    : response.status === 404 ? "not-found"
                        : response.status === 409 ? "conflict"
                            : "server";
            throw new HexCrawlApiError(response.status, kind, `${label} failed${detail ? `: ${detail}` : "."}`);
        }
        return await response.json() as T;
    }
}
