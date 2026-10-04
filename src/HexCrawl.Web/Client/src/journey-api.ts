import { backendBaseFromContext, HexCrawlApiError } from "./api";
import type { ToolHostContext } from "./types";
import type {
    CloseJourneyProcessRequest,
    CreateJourneyEventOpportunityRequest,
    ExpeditionJourneyState,
    JourneyOperation,
    ResolveJourneyEventRequest,
    ResolveJourneyProcessRequest,
    StartJourneyProcessRequest
} from "./journey-types";

export class JourneyApi {
    private readonly backendBaseUrl: string;

    public constructor(context: ToolHostContext | null) {
        this.backendBaseUrl = backendBaseFromContext(context?.apiBaseUrl);
    }

    public get(expeditionId: string): Promise<ExpeditionJourneyState> {
        return this.getJson(`/api/expeditions/${encodeURIComponent(expeditionId)}/journeys`, "Journey state");
    }

    public startProcess(expeditionId: string, request: StartJourneyProcessRequest): Promise<JourneyOperation> {
        return this.sendJson("POST", `/api/expeditions/${encodeURIComponent(expeditionId)}/journeys/processes`, request, "Start journey process");
    }

    public resolveProcess(expeditionId: string, processId: string, request: ResolveJourneyProcessRequest): Promise<JourneyOperation> {
        return this.sendJson("POST", `/api/expeditions/${encodeURIComponent(expeditionId)}/journeys/processes/${encodeURIComponent(processId)}/resolve`, request, "Resolve journey process");
    }

    public completeProcess(expeditionId: string, processId: string, request: CloseJourneyProcessRequest): Promise<JourneyOperation> {
        return this.closeProcess("complete", expeditionId, processId, request);
    }

    public failProcess(expeditionId: string, processId: string, request: CloseJourneyProcessRequest): Promise<JourneyOperation> {
        return this.closeProcess("fail", expeditionId, processId, request);
    }

    public abandonProcess(expeditionId: string, processId: string, request: CloseJourneyProcessRequest): Promise<JourneyOperation> {
        return this.closeProcess("abandon", expeditionId, processId, request);
    }

    public createEvent(expeditionId: string, request: CreateJourneyEventOpportunityRequest): Promise<JourneyOperation> {
        return this.sendJson("POST", `/api/expeditions/${encodeURIComponent(expeditionId)}/journeys/events`, request, "Create journey event opportunity");
    }

    public resolveEvent(expeditionId: string, occurrenceId: string, request: ResolveJourneyEventRequest): Promise<JourneyOperation> {
        return this.sendJson("POST", `/api/expeditions/${encodeURIComponent(expeditionId)}/journeys/events/${encodeURIComponent(occurrenceId)}/resolve`, request, "Resolve journey event");
    }

    private closeProcess(action: string, expeditionId: string, processId: string, request: CloseJourneyProcessRequest): Promise<JourneyOperation> {
        return this.sendJson("POST", `/api/expeditions/${encodeURIComponent(expeditionId)}/journeys/processes/${encodeURIComponent(processId)}/${action}`, request, `${action} journey process`);
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