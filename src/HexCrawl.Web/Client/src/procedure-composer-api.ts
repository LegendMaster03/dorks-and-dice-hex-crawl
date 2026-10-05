import { apiError, backendBaseFromContext } from "./api";
import type { ToolHostContext } from "./types";
import type {
    ProcedureCanonicalCreateInput,
    ProcedureCanonicalJson,
    ProcedureCanonicalRevisionInput,
    ProcedureCanonicalValidation,
    ProcedureComposer,
    ProcedureComposerCreateInput,
    ProcedureComposerDraftInput,
    ProcedureComposerRevisionInput,
    ProcedureRevisionSummary,
    SavedProcedureSummary
} from "./procedure-composer-types";
import type { ProcedureReference } from "./procedure-reference-types";

export class ProcedureComposerApi {
    private constructor(private readonly backendBaseUrl: string) {}

    public static async create(root: HTMLElement): Promise<ProcedureComposerApi> {
        const contextUrl = root.dataset.toolContextUrl;
        if (!contextUrl) return new ProcedureComposerApi("");

        const response = await fetch(contextUrl, { headers: { Accept: "application/json" } });
        if (!response.ok) throw await apiError(response, "Tool Host context");
        const context = await response.json() as ToolHostContext;
        return new ProcedureComposerApi(backendBaseFromContext(context.apiBaseUrl));
    }

    public listProcedures(): Promise<SavedProcedureSummary[]> {
        return this.getJson("/api/procedures", "Saved procedures");
    }

    public composeDraft(input: ProcedureComposerDraftInput): Promise<ProcedureComposer> {
        return this.sendJson("POST", "/api/procedures/composer/draft", input, "Procedure draft");
    }

    public composeCanonicalDraft(input: ProcedureComposerDraftInput): Promise<ProcedureCanonicalJson> {
        return this.sendJson(
            "POST",
            "/api/procedures/composer/canonical/draft",
            input,
            "Canonical procedure draft");
    }

    public validateCanonical(canonicalJson: string): Promise<ProcedureCanonicalValidation> {
        return this.sendJson(
            "POST",
            "/api/procedures/composer/canonical/validate",
            { canonicalJson },
            "Canonical procedure validation");
    }

    public createProcedure(input: ProcedureComposerCreateInput): Promise<ProcedureComposer> {
        return this.sendJson("POST", "/api/procedures", input, "Save procedure");
    }

    public createCanonicalProcedure(input: ProcedureCanonicalCreateInput): Promise<ProcedureComposer> {
        return this.sendJson("POST", "/api/procedures/canonical", input, "Save canonical procedure");
    }

    public getProcedure(procedureId: string, revision?: number | null): Promise<ProcedureComposer> {
        const suffix = revision == null
            ? ""
            : `/revisions/${encodeURIComponent(String(revision))}`;
        return this.getJson(
            `/api/procedures/${encodeURIComponent(procedureId)}${suffix}`,
            "Campaign procedure");
    }

    public getReference(procedureId: string, revision?: number | null): Promise<ProcedureReference> {
        const encodedId = encodeURIComponent(procedureId);
        const path = revision == null
            ? `/api/procedures/${encodedId}/reference`
            : `/api/procedures/${encodedId}/revisions/${encodeURIComponent(String(revision))}/reference`;
        return this.getJson(path, "Procedure reference");
    }

    public listRevisions(procedureId: string): Promise<ProcedureRevisionSummary[]> {
        return this.getJson(
            `/api/procedures/${encodeURIComponent(procedureId)}/revisions`,
            "Procedure revisions");
    }

    public createRevision(
        procedureId: string,
        input: ProcedureComposerRevisionInput): Promise<ProcedureComposer> {
        return this.sendJson(
            "POST",
            `/api/procedures/${encodeURIComponent(procedureId)}/revisions`,
            input,
            "Save procedure revision");
    }

    public createCanonicalRevision(
        procedureId: string,
        input: ProcedureCanonicalRevisionInput): Promise<ProcedureComposer> {
        return this.sendJson(
            "POST",
            `/api/procedures/${encodeURIComponent(procedureId)}/canonical/revisions`,
            input,
            "Save canonical procedure revision");
    }

    private async getJson<T>(path: string, label: string): Promise<T> {
        const response = await fetch(`${this.backendBaseUrl}${path}`, {
            headers: { Accept: "application/json" }
        });
        if (!response.ok) throw await apiError(response, label);
        return await response.json() as T;
    }

    private async sendJson<T>(
        method: "POST",
        path: string,
        body: unknown,
        label: string): Promise<T> {
        const response = await fetch(`${this.backendBaseUrl}${path}`, {
            method,
            headers: {
                Accept: "application/json",
                "Content-Type": "application/json"
            },
            body: JSON.stringify(body)
        });
        if (!response.ok) throw await apiError(response, label);
        return await response.json() as T;
    }
}
