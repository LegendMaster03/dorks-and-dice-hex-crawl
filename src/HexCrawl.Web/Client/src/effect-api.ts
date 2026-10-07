import { backendBaseFromContext, HexCrawlApiError } from "./api";
import type {
    ClearEffectRequest,
    EffectOperation,
    ExpeditionEffectState,
    RecoverEffectRequest,
    ResolvePendingConsequenceRequest
} from "./effect-types";
import type { ToolHostContext } from "./types";

export class ExpeditionEffectsApi {
    private readonly backendBaseUrl: string;

    public constructor(context: ToolHostContext | null) {
        this.backendBaseUrl = backendBaseFromContext(context?.apiBaseUrl);
    }

    public get(expeditionId: string): Promise<ExpeditionEffectState> {
        return this.getJson(
            `/api/expeditions/${encodeURIComponent(expeditionId)}/effects`,
            "Expedition effects");
    }

    public recover(
        expeditionId: string,
        effectId: string,
        request: RecoverEffectRequest): Promise<EffectOperation> {
        return this.sendJson(
            "POST",
            `/api/expeditions/${encodeURIComponent(expeditionId)}/effects/${encodeURIComponent(effectId)}/recover`,
            request,
            "Recover expedition effect");
    }

    public clear(
        expeditionId: string,
        effectId: string,
        request: ClearEffectRequest): Promise<EffectOperation> {
        return this.sendJson(
            "DELETE",
            `/api/expeditions/${encodeURIComponent(expeditionId)}/effects/${encodeURIComponent(effectId)}`,
            request,
            "Clear expedition effect");
    }

    public resolvePending(
        expeditionId: string,
        consequenceId: string,
        request: ResolvePendingConsequenceRequest): Promise<EffectOperation> {
        return this.sendJson(
            "POST",
            `/api/expeditions/${encodeURIComponent(expeditionId)}/consequences/${encodeURIComponent(consequenceId)}/resolve`,
            request,
            "Resolve pending consequence");
    }

    private async getJson<T>(path: string, label: string): Promise<T> {
        const response = await fetch(`${this.backendBaseUrl}${path}`, { headers: { Accept: "application/json" } });
        return this.readJson<T>(response, label);
    }

    private async sendJson<T>(method: "POST" | "DELETE", path: string, body: unknown, label: string): Promise<T> {
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
