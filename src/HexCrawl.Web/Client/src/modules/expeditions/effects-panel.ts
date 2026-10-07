import { ExpeditionEffectsApi } from "../../effect-api";
import type { ExpeditionEffect, ExpeditionEffectState, PendingConsequence } from "../../effect-types";
import type { ConsequenceProvenanceRequest } from "../../survival-types";
import type { ExpeditionDetail } from "../../types";
import { disclosure, humanizeIdentifier, textElement } from "../../ui/workspace";

export class ExpeditionEffectsPanel {
    private readonly panel: HTMLDetailsElement;
    private readonly body: HTMLElement;
    private state: ExpeditionEffectState | null = null;
    private disposed = false;

    public constructor(
        root: HTMLElement,
        private readonly api: ExpeditionEffectsApi,
        private readonly expeditionId: string,
        private readonly getRuntime: () => ExpeditionDetail,
        private readonly mutate: (control: HTMLButtonElement | null, action: () => Promise<void>) => Promise<void>) {
        this.panel = document.createElement("details");
        this.panel.className = "hc-panel";
        this.panel.dataset.effectsPanel = "";
        this.panel.open = true;
        const summary = document.createElement("summary");
        summary.textContent = "Effects";
        this.body = document.createElement("div");
        this.body.className = "hc-form";
        this.panel.append(summary, this.body);
        root.append(this.panel);
    }

    public async sync(): Promise<void> {
        this.state = await this.api.get(this.expeditionId);
        if (!this.disposed) this.render();
    }

    public dispose(): void {
        this.disposed = true;
        this.panel.remove();
    }

    private render(): void {
        const state = this.requireState();
        this.body.replaceChildren();

        const active = this.section("Active effects");
        if (state.activeEffects.length === 0) {
            active.append(this.muted("No active persistent effects."));
        } else {
            active.append(this.activeEffectsLedger(state));
            const manage = disclosure("Manage active effects");
            for (const effect of state.activeEffects) manage.append(this.effectCard(effect, state));
            active.append(manage);
        }
        this.body.append(active);

        const pending = this.section("Pending consequences");
        if (state.pendingConsequences.length === 0) {
            pending.append(this.muted("No unresolved consequences."));
        } else {
            for (const consequence of state.pendingConsequences) pending.append(this.pendingCard(consequence));
        }
        this.body.append(pending);

        const applied = this.section("Applied / resolved consequences");
        if (state.appliedConsequences.length === 0) {
            applied.append(this.muted("No applied consequences recorded."));
        } else {
            const list = document.createElement("ul");
            for (const record of state.appliedConsequences.slice(-12).reverse()) {
                const item = document.createElement("li");
                item.textContent = `${humanizeIdentifier(record.consequenceKey)} — ${humanizeIdentifier(record.status)}`;
                list.append(item);
            }
            applied.append(list);
        }
        this.body.append(applied);

        const advanced = disclosure("Advanced effect details");
        advanced.append(textElement(
            "p",
            `Policy: ${humanizeIdentifier(state.policy.support)}${state.policy.accumulationModel ? ` · accumulation ${state.policy.accumulationModel}` : ""}${state.policy.recoveryModel ? ` · recovery ${state.policy.recoveryModel}` : ""}.`,
            "hc-muted"));
        if (state.appliedConsequences.length > 0) {
            const consequenceAudit = document.createElement("ol");
            consequenceAudit.className = "hc-history";
            for (const record of state.appliedConsequences.slice(-20).reverse()) {
                const item = document.createElement("li");
                item.textContent = `${record.consequenceKey} · ${record.status} · consequence ${record.consequenceId} · source ${record.provenance.sourceKey}${record.detail ? ` · ${record.detail}` : ""}`;
                consequenceAudit.append(item);
            }
            advanced.append(textElement("h4", "Consequence audit"), consequenceAudit);
        }
        const audit = document.createElement("ol");
        audit.className = "hc-history";
        for (const record of state.history.slice(-20).reverse()) {
            const item = document.createElement("li");
            item.textContent = `${record.effectKey} · ${record.operation} · ${record.effectId}`;
            audit.append(item);
        }
        if (state.history.length === 0) advanced.append(this.muted("No effect audit records."));
        else advanced.append(audit);
        this.body.append(advanced);
    }

    private activeEffectsLedger(state: ExpeditionEffectState): HTMLElement {
        const table = document.createElement("table");
        table.className = "hc-readable-table";
        table.dataset.effectLedger = "";
        const caption = document.createElement("caption");
        caption.textContent = "Condition / effect ledger";
        const head = document.createElement("thead");
        const header = document.createElement("tr");
        for (const label of ["Effect", "Target", "Current value", "Recovery", "Source"]) {
            const cell = document.createElement("th");
            cell.scope = "col";
            cell.textContent = label;
            header.append(cell);
        }
        head.append(header);
        const body = document.createElement("tbody");
        for (const effect of state.activeEffects) {
            const source = effect.provenance.at(-1);
            const values = [
                humanizeIdentifier(effect.effectKey),
                this.targetLabel(effect),
                this.effectValue(effect),
                humanizeIdentifier(effect.recoveryModel ?? state.policy.recoveryModel ?? "DM adjudication"),
                source ? normalProvenanceLabel(source) : "Unknown source"
            ];
            const row = document.createElement("tr");
            values.forEach((value, index) => {
                const cell = document.createElement(index === 0 ? "th" : "td");
                if (index === 0) cell.scope = "row";
                cell.textContent = value;
                row.append(cell);
            });
            body.append(row);
        }
        table.append(caption, head, body);
        return table;
    }

    private effectCard(effect: ExpeditionEffect, state: ExpeditionEffectState): HTMLElement {
        const card = document.createElement("article");
        card.className = "hc-card";
        card.dataset.effectId = effect.id;

        const title = document.createElement("h4");
        title.textContent = humanizeIdentifier(effect.effectKey);
        card.append(
            title,
            this.muted(`Affects ${this.targetLabel(effect)}`),
            this.muted(this.effectValue(effect)));

        const source = effect.provenance.at(-1);
        if (source) {
            const note = normalProvenanceNote(source);
            card.append(this.muted(
                `Source: ${normalProvenanceLabel(source)}${note ? ` · ${note}` : ""}`));
        }

        const recoveryModel = effect.recoveryModel ?? state.policy.recoveryModel;
        card.append(this.muted(
            recoveryModel
                ? `Recovery: ${humanizeIdentifier(recoveryModel)}. DM adjudication can record an explicit reduction or clear.`
                : "Recovery amount is not generically defined; the DM can record an explicit reduction or clear."));

        const actions = document.createElement("div");
        actions.className = "hc-button-row";
        if ((effect.level ?? 0) > 0) {
            const reduce = this.button("Reduce 1 level");
            reduce.addEventListener("click", () => void this.mutate(reduce, async () => {
                await this.api.recover(this.expeditionId, effect.id, {
                    expectedVersion: this.getRuntime().version,
                    triggerKey: "manual",
                    levelReduction: 1,
                    clear: false,
                    provenance: dmEffectProvenance("effect-recovery", "DM-recorded explicit level recovery")
                });
            }));
            actions.append(reduce);
        }

        const clear = this.button("Clear effect");
        clear.addEventListener("click", () => void this.mutate(clear, async () => {
            await this.api.recover(this.expeditionId, effect.id, {
                expectedVersion: this.getRuntime().version,
                triggerKey: "manual",
                levelReduction: null,
                clear: true,
                provenance: dmEffectProvenance("effect-recovery", "DM-recorded explicit effect recovery")
            });
        }));
        actions.append(clear);
        card.append(actions);

        const technical = disclosure("Technical details");
        technical.append(
            this.muted(`Effect ID: ${effect.id}`),
            this.muted(`Scope: ${effect.target.scope}${effect.target.targetId ? ` · target ${effect.target.targetId}` : ""}`),
            this.muted(`Source consequences: ${effect.sourceConsequenceIds.length ? effect.sourceConsequenceIds.join(", ") : "none"}`),
            ...effect.provenance.map(value => this.muted(
                `Provenance: ${value.sourceKind} · source ${value.sourceKey} · reference ${value.sourceReference ?? "none"} · provider ${value.providerName ?? "none"} · note ${value.note ?? "none"}`)));
        card.append(technical);
        return card;
    }

    private pendingCard(pending: PendingConsequence): HTMLElement {
        const card = document.createElement("article");
        card.className = "hc-card";
        const title = document.createElement("h4");
        title.textContent = humanizeIdentifier(pending.consequence.consequenceKey);
        card.append(
            title,
            this.muted(`${humanizeIdentifier(pending.consequence.category)} · affects ${this.targetLabelFromTarget(pending.consequence.target)}`),
            this.muted(pending.reason),
            this.muted(`Next: ${pending.requiredAction}`));

        const note = document.createElement("input");
        note.type = "text";
        note.placeholder = "DM resolution or adjudication note";
        const resolve = this.button("Record resolution");
        resolve.addEventListener("click", () => {
            const resolutionNote = note.value.trim();
            if (!resolutionNote) {
                note.focus();
                return;
            }
            void this.mutate(resolve, async () => {
                await this.api.resolvePending(this.expeditionId, pending.consequence.id, {
                    expectedVersion: this.getRuntime().version,
                    resolutionNote,
                    provenance: dmEffectProvenance("pending-consequence-resolution", resolutionNote)
                });
            });
        });
        const label = document.createElement("label");
        label.append(document.createTextNode("Resolution note"), note);
        card.append(label, resolve);

        const technical = disclosure("Technical details");
        technical.append(
            this.muted(`Consequence ID: ${pending.consequence.id}`),
            this.muted(`Status: ${pending.status}`),
            this.muted(`Source: ${pending.consequence.provenance.sourceKey}`));
        card.append(technical);
        return card;
    }

    private effectValue(effect: ExpeditionEffect): string {
        const values: string[] = [];
        if (effect.level !== null) values.push(`Level ${effect.level}`);
        if (effect.magnitude !== null) values.push(`${formatNumber(effect.magnitude)}${effect.unit ? ` ${effect.unit}` : ""}`);
        if (effect.state) values.push(humanizeIdentifier(effect.state));
        return values.join(" · ") || "Active";
    }

    private targetLabel(effect: ExpeditionEffect): string {
        return this.targetLabelFromTarget(effect.target);
    }

    private targetLabelFromTarget(target: ExpeditionEffect["target"]): string {
        if (!target.targetId) return humanizeIdentifier(target.scope);
        const runtime = this.getRuntime();
        if (target.scope === "Participant") {
            return runtime.party.members.find(member => member.id === target.targetId)?.name
                ?? "Unknown participant";
        }
        const contributor = (runtime.party.movementContributors ?? []).find(value => value.id === target.targetId);
        return contributor ? `${humanizeIdentifier(target.scope)}: ${humanizeIdentifier(contributor.key)}` : humanizeIdentifier(target.scope);
    }

    private section(title: string): HTMLElement {
        const section = document.createElement("section");
        section.className = "hc-stack";
        section.append(textElement("h3", title));
        return section;
    }

    private muted(text: string): HTMLElement {
        return textElement("p", text, "hc-muted");
    }

    private button(label: string): HTMLButtonElement {
        const button = document.createElement("button");
        button.type = "button";
        button.textContent = label;
        return button;
    }

    private requireState(): ExpeditionEffectState {
        if (!this.state) throw new Error("Effect state has not loaded.");
        return this.state;
    }
}

function normalProvenanceLabel(value: ExpeditionEffect["provenance"][number]): string {
    if (value.sourceKind === "JourneyEvent" || value.sourceKey.startsWith("journey-event")) return "Journey event";
    if (value.sourceKey.startsWith("journey-process")) return "Journey result";
    return value.providerName?.trim() || humanizeIdentifier(value.sourceKind);
}

function normalProvenanceNote(value: ExpeditionEffect["provenance"][number]): string | null {
    if (value.sourceKind === "JourneyEvent" || value.sourceKey.startsWith("journey-")) return null;
    return value.note;
}

function dmEffectProvenance(sourceKey: string, note: string): ConsequenceProvenanceRequest {
    return {
        sourceKind: "Dm",
        sourceKey,
        sourceReference: null,
        providerName: null,
        note
    };
}

function formatNumber(value: number): string {
    return Number.isInteger(value) ? String(value) : value.toFixed(2).replace(/0+$/, "").replace(/\.$/, "");
}
