import { blockInitiativeHandoffHref } from "../../encounter-handoff";
import { JourneyApi } from "../../journey-api";
import type {
    ExpeditionConsequenceInput,
    ExpeditionJourneyState,
    JourneyApproachDefinition,
    JourneyEventOccurrence,
    JourneyEventTargetKind,
    JourneyProcessDefinition,
    JourneyProcessInstance,
    JourneyStageDefinition
} from "../../journey-types";
import { dmJourneyProvenance, target } from "../../journey-types";
import type { ExpeditionDetail } from "../../types";
import type { ExpeditionEffectScope } from "../../survival-types";

export class ExpeditionJourneyPanel {
    private readonly panel: HTMLDetailsElement;
    private readonly body: HTMLElement;
    private state: ExpeditionJourneyState | null = null;
    private disposed = false;

    public constructor(
        root: HTMLElement,
        private readonly api: JourneyApi,
        private readonly expeditionId: string,
        private readonly getRuntime: () => ExpeditionDetail,
        private readonly mutate: (control: HTMLButtonElement | null, action: () => Promise<void>) => Promise<void>) {
        this.panel = document.createElement("details");
        this.panel.className = "hc-panel";
        this.panel.dataset.journeyPanel = "";
        const summary = document.createElement("summary");
        summary.textContent = "Journey / challenge";
        this.body = document.createElement("div");
        this.body.className = "hc-form";
        this.panel.append(summary, this.body);
        root.querySelector<HTMLElement>(".hc-page")?.append(this.panel);
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
        this.body.replaceChildren(
            this.policySummary(state),
            this.rolesView(),
            this.startView(state),
            this.processesView(state),
            this.eventsView(state),
            this.historyView(state));
    }

    private policySummary(state: ExpeditionJourneyState): HTMLElement {
        const section = this.section("Pinned journey policy");
        const process = state.processPolicy;
        const events = state.eventPolicy;
        section.append(this.muted(process.support === "None"
            ? "No journey.process capability is stored on this campaign procedure."
            : process.support === "Unsupported"
                ? process.unsupportedReason ?? "The stored journey.process policy is unsupported."
                : `Process: ${process.stageModel}; transition ${process.stageTransitionModel}; progress ${process.progressModel} (${process.progressKind}${process.progressUnit ? `, ${process.progressUnit}` : ""}); completion ${process.completionModel}; roles ${process.roleDriven ? process.roleAssignmentModel : "not required"}; interval integration ${process.intervalIntegrationModel}.`));
        section.append(this.muted(events.support === "None"
            ? "No journey.events capability is stored on this campaign procedure."
            : events.support === "Unsupported"
                ? events.unsupportedReason ?? "The stored journey.events policy is unsupported."
                : `Events: ${events.triggerModel}; explicit sources ${events.triggerSources.join(", ")}; ${events.linkMode}; target ${events.targetingModel}; environment ${events.terrainInfluence}; consequence ${events.consequenceModel}. Trigger semantics create opportunities only; event content and quantities are resolved inputs.`));
        return section;
    }

    private rolesView(): HTMLElement {
        const section = this.section("Current roles / participants");
        const runtime = this.getRuntime();
        const members = new Map(runtime.party.members.map(member => [member.id, member.name]));
        const roles = runtime.party.activityAssignments.filter(value => value.roleKey);
        if (roles.length === 0) {
            section.append(this.muted("No current typed role assignments."));
            return section;
        }
        const list = document.createElement("ul");
        for (const assignment of roles) {
            const item = document.createElement("li");
            item.textContent = `${assignment.roleKey}: ${assignment.participantId ? members.get(assignment.participantId) ?? assignment.participantId : "party"}`;
            list.append(item);
        }
        section.append(list, this.muted("Each resolution snapshots the participant/role used; later role edits do not rewrite history."));
        return section;
    }

    private startView(state: ExpeditionJourneyState): HTMLElement {
        const section = this.section("Start a process / challenge");
        if (state.processPolicy.support !== "Supported") {
            section.append(this.muted("Starting a process requires a supported exact-pinned journey.process policy."));
            return section;
        }

        if (state.processPolicy.stageKeys.length > 0) {
            const form = document.createElement("form");
            form.className = "hc-form hc-form-grid";
            const key = this.input("Process key", "text", "journey");
            const name = this.input("Display name", "text", "Journey");
            const destination = this.input("Destination / target reference", "text");
            const route = this.input("Route reference", "text");
            const submit = this.button("Start pinned process");
            form.append(key.wrapper, name.wrapper, destination.wrapper, route.wrapper, submit);
            form.addEventListener("submit", event => {
                event.preventDefault();
                void this.mutate(submit, async () => {
                    const result = await this.api.startProcess(this.expeditionId, {
                        expectedVersion: this.requireState().expeditionVersion,
                        processId: crypto.randomUUID(),
                        processKey: required(key.control.value, "Process key"),
                        displayName: required(name.control.value, "Display name"),
                        destinationReference: nullable(destination.control.value),
                        routeReference: nullable(route.control.value),
                        provenance: dmJourneyProvenance("journey-process-start")
                    });
                    this.apply(result.state);
                });
            });
            section.append(this.muted(`The pinned procedure supplies stages: ${state.processPolicy.stageKeys.join(" → ")}. No route length, check formula, or event count is inferred.`), form);
            return section;
        }

        const custom = document.createElement("form");
        custom.className = "hc-form hc-form-grid";
        const key = this.input("Custom process key", "text", "difficult-crossing");
        const name = this.input("Custom process name", "text", "Difficult crossing");
        const stages = this.input("Stage keys (semicolon separated)", "text", "approach;cross;secure");
        const approaches = this.input("Approaches per stage", "text", "approach=climb,find-crossing,construct-bridge;cross=climb,magic;secure=climb");
        const submit = this.button("Start custom challenge");
        custom.append(key.wrapper, name.wrapper, stages.wrapper, approaches.wrapper, submit);
        custom.addEventListener("submit", event => {
            event.preventDefault();
            void this.mutate(submit, async () => {
                const definition = customDefinition(
                    required(key.control.value, "Custom process key"),
                    required(name.control.value, "Custom process name"),
                    splitKeys(stages.control.value),
                    parseApproaches(approaches.control.value),
                    this.requireState());
                const result = await this.api.startProcess(this.expeditionId, {
                    expectedVersion: this.requireState().expeditionVersion,
                    processId: crypto.randomUUID(),
                    definition,
                    provenance: dmJourneyProvenance("custom-journey-process-start")
                });
                this.apply(result.state);
            });
        });
        section.append(this.muted("Custom definitions provide concrete stage/approach structure only. They do not replace the campaign's pinned execution policy."), custom);
        return section;
    }

    private processesView(state: ExpeditionJourneyState): HTMLElement {
        const section = this.section("Active processes");
        if (state.activeProcesses.length === 0) section.append(this.muted("No active journey processes or challenges."));
        for (const process of state.activeProcesses) section.append(this.processCard(process));

        if (state.closedProcesses.length > 0) {
            section.append(this.heading("Completed / failed / abandoned"));
            for (const process of state.closedProcesses) {
                const card = document.createElement("div");
                card.className = "hc-card";
                card.append(this.heading(`${process.definition.displayName} — ${process.status}`));
                card.append(this.muted(`Ended at ${process.endedAtExpeditionTime ?? "—"}; ${process.endReason ?? "no reason recorded"}.`));
                card.append(this.processStageSummary(process));
                section.append(card);
            }
        }
        return section;
    }

    private processCard(process: JourneyProcessInstance): HTMLElement {
        const card = document.createElement("div");
        card.className = "hc-card";
        const current = process.definition.stages.find(value => value.stageKey === process.currentStageKey)!;
        const stageState = process.stageStates.find(value => value.stageKey === process.currentStageKey)!;
        card.append(this.heading(`${process.definition.displayName} — ${process.status}`));
        card.append(this.muted(`Stage: ${current.displayName} (${current.stageKey}). ${describeProgress(process, stageState)} Successes ${stageState.successes}; failures ${stageState.failures}; complications ${stageState.complications}.`));
        if (current.approaches.length > 0) card.append(this.muted(`Approaches: ${current.approaches.map(value => capabilityLabel(value)).join(" · ")}`));
        if (process.pendingActions.length > 0) card.append(this.muted(`Pending: ${process.pendingActions.map(value => `${value.kind}${value.detail ? ` — ${value.detail}` : ""}`).join(" | ")}`));
        card.append(this.processStageSummary(process), this.resolveProcessForm(process), this.processCloseControls(process));
        return card;
    }

    private processStageSummary(process: JourneyProcessInstance): HTMLElement {
        const list = document.createElement("ol");
        for (const stageKey of process.definition.stageOrder) {
            const definition = process.definition.stages.find(value => value.stageKey === stageKey)!;
            const state = process.stageStates.find(value => value.stageKey === stageKey)!;
            const item = document.createElement("li");
            item.textContent = `${definition.displayName}: ${state.completed ? "completed" : stageKey === process.currentStageKey ? "current" : "pending"}; ${describeProgress(process, state)}; failures ${state.failures}; complications ${state.complications}`;
            list.append(item);
        }
        return list;
    }

    private resolveProcessForm(process: JourneyProcessInstance): HTMLElement {
        const form = document.createElement("form");
        form.className = "hc-form hc-form-grid";
        const stage = process.definition.stages.find(value => value.stageKey === process.currentStageKey)!;
        const pendingValues = process.pendingActions.length > 0 ? process.pendingActions.map(value => value.id) : [""];
        const pending = this.select("Pending opportunity", pendingValues);
        const approach = this.select("Approach", ["", ...stage.approaches.map(value => value.approachKey)]);
        const runtime = this.getRuntime();
        const roles = [...new Set(runtime.party.activityAssignments.map(value => value.roleKey).filter((value): value is string => Boolean(value)))];
        const roleValues = process.execution.roleDriven && roles.length > 0 ? roles : ["", ...roles];
        const role = this.select("Role", roleValues);
        const actor = this.select("Actor", ["", ...runtime.party.members.map(value => `${value.id}|${value.name}`)]);
        const progress = this.input(process.execution.progressKind === "Numeric" ? "Progress delta" : "Progress state", process.execution.progressKind === "Numeric" ? "number" : "text", process.execution.progressKind === "Numeric" ? "0" : "");
        if (progress.control instanceof HTMLInputElement && process.execution.progressKind === "Numeric") progress.control.step = "any";
        const successes = this.input("Success delta", "number", "0");
        const failures = this.input("Failure delta", "number", "0");
        const complications = this.input("Complication delta", "number", "0");
        const completeStage = this.checkbox("Complete current stage", false);
        const targetStage = this.select("Explicit next stage", ["", ...process.definition.stageOrder.filter(value => value !== process.currentStageKey)]);
        const completeProcess = this.checkbox("Complete process", false);
        const failProcess = this.checkbox("Fail process", false);
        const consequence = this.consequenceFields();
        const submit = this.button("Resolve attempt");
        form.append(pending.wrapper, approach.wrapper, role.wrapper, actor.wrapper, progress.wrapper, successes.wrapper,
            failures.wrapper, complications.wrapper, completeStage.wrapper, targetStage.wrapper, completeProcess.wrapper,
            failProcess.wrapper, consequence.container, submit);
        form.addEventListener("submit", event => {
            event.preventDefault();
            void this.mutate(submit, async () => {
                const resolutionId = crypto.randomUUID();
                const actorId = actor.control.value ? actor.control.value.split("|", 1)[0] : null;
                const result = await this.api.resolveProcess(this.expeditionId, process.id, {
                    expectedVersion: this.requireState().expeditionVersion,
                    captureCurrentEnvironment: true,
                    resolution: {
                        resolutionId,
                        processId: process.id,
                        stageKey: process.currentStageKey,
                        pendingActionId: nullable(pending.control.value),
                        approachKey: nullable(approach.control.value),
                        actorParticipantId: actorId,
                        roleKey: nullable(role.control.value),
                        outcomeKey: null,
                        progressDelta: process.execution.progressKind === "Numeric" ? optionalNumber(progress.control.value) : null,
                        progressState: process.execution.progressKind === "ExplicitState" ? nullable(progress.control.value) : null,
                        successDelta: integer(successes.control.value),
                        failureDelta: integer(failures.control.value),
                        complicationDelta: integer(complications.control.value),
                        completeStage: completeStage.control.checked,
                        targetStageKey: nullable(targetStage.control.value),
                        completeProcess: completeProcess.control.checked,
                        failProcess: failProcess.control.checked,
                        consequences: consequence.build("journey-process", process.id, resolutionId),
                        provenance: dmJourneyProvenance("journey-process-resolution")
                    }
                });
                this.apply(result.state);
            });
        });
        return form;
    }

    private processCloseControls(process: JourneyProcessInstance): HTMLElement {
        const row = document.createElement("div");
        row.className = "hc-button-row";
        const finalStageKey = process.definition.stageOrder.at(-1);
        const finalStage = process.stageStates.find(value => value.stageKey === finalStageKey);
        const canComplete = process.execution.completionModel === "explicit-completion"
            || process.execution.completionModel === "reach-destination"
            || process.execution.completionModel === "final-stage-completion"
                && process.currentStageKey === finalStageKey
                && finalStage?.completed === true;
        const actions: Array<[string, (request: { expectedVersion: number; reason: string; provenance: ReturnType<typeof dmJourneyProvenance> }) => Promise<{ state: ExpeditionJourneyState }>]> = [
            ["Complete", request => this.api.completeProcess(this.expeditionId, process.id, request)],
            ["Fail", request => this.api.failProcess(this.expeditionId, process.id, request)],
            ["Abandon", request => this.api.abandonProcess(this.expeditionId, process.id, request)]
        ];
        for (const [label, action] of actions) {
            const button = this.button(label);
            button.type = "button";
            if (label === "Complete") button.disabled = !canComplete;
            button.addEventListener("click", () => {
                const reason = window.prompt(`${label} process: reason`, label === "Complete" ? "Resolved by DM" : "DM resolution");
                if (!reason) return;
                void this.mutate(button, async () => {
                    const result = await action({
                        expectedVersion: this.requireState().expeditionVersion,
                        reason,
                        provenance: dmJourneyProvenance(`journey-process-${label.toLowerCase()}`)
                    });
                    this.apply(result.state);
                });
            });
            row.append(button);
        }
        return row;
    }

    private eventsView(state: ExpeditionJourneyState): HTMLElement {
        const section = this.section("Journey events");
        if (state.eventPolicy.support === "Supported" && state.eventPolicy.triggerSources.includes("Landmark")) {
            const form = document.createElement("form");
            form.className = "hc-form hc-form-grid";
            const landmark = this.input("Landmark reference", "text");
            const submit = this.button("Create landmark opportunity");
            form.append(landmark.wrapper, submit);
            form.addEventListener("submit", event => {
                event.preventDefault();
                void this.mutate(submit, async () => {
                    const result = await this.api.createEvent(this.expeditionId, {
                        expectedVersion: this.requireState().expeditionVersion,
                        captureCurrentEnvironment: true,
                        opportunity: {
                            occurrenceId: crypto.randomUUID(),
                            trigger: "Landmark",
                            triggerReference: required(landmark.control.value, "Landmark reference"),
                            targetKind: "Unresolved",
                            environment: [],
                            provenance: dmJourneyProvenance("journey-landmark-event-opportunity")
                        }
                    });
                    this.apply(result.state);
                });
            });
            section.append(form);
        }
        const pending = state.eventOccurrences.filter(value => value.status === "ResolutionRequired");
        if (pending.length === 0) section.append(this.muted("No pending journey-event opportunities."));
        for (const event of pending) section.append(this.eventCard(event));
        const resolved = state.eventOccurrences.filter(value => value.status !== "ResolutionRequired");
        if (resolved.length > 0) {
            section.append(this.heading("Resolved / skipped events"));
            for (const event of resolved) {
                const card = document.createElement("div");
                card.className = "hc-card";
                card.append(this.heading(`${event.eventKey ?? event.trigger} — ${event.status}`));
                card.append(this.muted(`Trigger ${event.trigger}: ${event.triggerReference}. Target ${event.participantSnapshot?.participantName ?? event.targetRoleKey ?? event.targetKind}. Consequences: ${event.consequenceIds.length ? event.consequenceIds.join(", ") : "none"}.`));
                if (event.environment.length > 0) card.append(this.muted(`Environment snapshot: ${event.environment.map(value => `${value.dimension}=${value.value}${value.unit ? ` ${value.unit}` : ""}`).join("; ")}`));
                if (event.status === "Resolved" && event.consequenceIds.length > 0) card.append(this.encounterHandoffControl(event));
                section.append(card);
            }
        }
        return section;
    }

    private encounterHandoffControl(event: JourneyEventOccurrence): HTMLElement {
        const row = document.createElement("div");
        row.className = "hc-button-row";
        const text = this.muted("If this event retained an encounter circumstance, Hex Crawl can prepare its exact occurrence context for Block Initiative. Preparing it does not consume the consequence.");
        const button = this.button("Open encounter in Block Initiative");
        button.type = "button";
        const handoffId = crypto.randomUUID();
        button.addEventListener("click", () => void this.mutate(button, async () => {
            button.textContent = "Preparing encounter…";
            const handoff = await this.api.createEncounterHandoff(this.expeditionId, {
                expectedVersion: this.requireState().expeditionVersion,
                handoffId,
                returnPath: safeCurrentReturnPath(),
                runtimeEncounterSequence: null,
                journeyEventOccurrenceId: event.id
            });
            window.location.assign(blockInitiativeHandoffHref(handoff));
        }));
        row.append(text, button);
        return row;
    }

    private eventCard(event: JourneyEventOccurrence): HTMLElement {
        const card = document.createElement("div");
        card.className = "hc-card";
        card.append(this.heading(`Pending ${event.trigger} event`));
        card.append(this.muted(`Source: ${event.triggerReference}${event.processId ? `; process ${event.processId}; stage ${event.stageKey ?? "—"}` : ""}.`));
        if (event.environment.length > 0) card.append(this.muted(`Relevant environment: ${event.environment.map(value => `${value.dimension}=${value.value}${value.unit ? ` ${value.unit}` : ""}`).join("; ")}`));
        const form = document.createElement("form");
        form.className = "hc-form hc-form-grid";
        const state = this.requireState();
        const runtime = this.getRuntime();
        const status = this.select("Resolution", ["Resolved", "Skipped", "NotApplicable"]);
        const key = this.input("Event key", "text");
        const type = this.input("Event type/category", "text");
        const targetKinds = state.eventPolicy.targetingModel === "travel-role"
            ? ["Role"]
            : ["Party", "Expedition", "Role", "Participant", "Mount", "Vehicle"];
        const targetKind = this.select("Target kind", targetKinds);
        const role = this.select("Target role", ["", ...runtime.participantActivityPolicy.roleKeys]);
        const participant = this.select("Target participant", ["", ...runtime.party.members.map(value => `${value.id}|${value.name}`)]);
        const contributors = (runtime.party.movementContributors ?? [])
            .filter(value => value.kind === "Mount" || value.kind === "Vehicle")
            .map(value => `${value.id}|${value.kind}: ${value.key}`);
        const movementTarget = this.select("Target mount / vehicle", ["", ...contributors]);
        const consequence = this.consequenceFields();
        const note = this.input("Resolution note", "text");
        const submit = this.button("Resolve event");
        form.append(status.wrapper, key.wrapper, type.wrapper, targetKind.wrapper, role.wrapper, participant.wrapper,
            movementTarget.wrapper, consequence.container, note.wrapper, submit);
        form.addEventListener("submit", domEvent => {
            domEvent.preventDefault();
            void this.mutate(submit, async () => {
                const resolved = status.control.value === "Resolved";
                const selectedKind = targetKind.control.value as JourneyEventTargetKind;
                const selectedTargetId = !resolved ? null
                    : selectedKind === "Participant" || selectedKind === "Role" ? nullable(participant.control.value)
                    : selectedKind === "Mount" || selectedKind === "Vehicle" ? nullable(movementTarget.control.value)
                    : null;
                const result = await this.api.resolveEvent(this.expeditionId, event.id, {
                    expectedVersion: this.requireState().expeditionVersion,
                    captureCurrentEnvironment: true,
                    resolution: {
                        occurrenceId: event.id,
                        status: status.control.value as "Resolved" | "Skipped" | "NotApplicable",
                        eventKey: resolved ? required(key.control.value, "Event key") : null,
                        eventType: resolved ? nullable(type.control.value) : null,
                        targetKind: resolved ? selectedKind : "Unresolved",
                        targetRoleKey: resolved && selectedKind === "Role" ? required(role.control.value, "Target role") : null,
                        targetId: selectedTargetId,
                        consequences: resolved ? consequence.build("journey-event", event.id, event.id) : [],
                        provenance: dmJourneyProvenance("journey-event-resolution"),
                        note: nullable(note.control.value)
                    }
                });
                this.apply(result.state);
            });
        });
        card.append(form);
        return card;
    }

    private historyView(state: ExpeditionJourneyState): HTMLElement {
        const section = this.section("Process history");
        if (state.history.length === 0) {
            section.append(this.muted("No journey history yet."));
            return section;
        }
        const list = document.createElement("ol");
        for (const record of state.history) {
            const item = document.createElement("li");
            item.textContent = `${record.kind}: ${record.detail} [watch ${record.completedWatches}; source ${record.provenance.sourceKey}]`;
            list.append(item);
        }
        section.append(list);
        return section;
    }

    private consequenceFields(): { container: HTMLElement; build: (source: string, sourceId: string, occurrenceId: string) => ExpeditionConsequenceInput[] } {
        const container = document.createElement("fieldset");
        const legend = document.createElement("legend");
        legend.textContent = "Optional structured consequence";
        const kind = this.select("Consequence", ["None", "TimeDelay", "PersistentEffectChange", "ResourceChange", "EncounterCircumstance", "DamageEndurance"]);
        const key = this.input("Consequence/effect/resource key", "text");
        const value = this.input("Resolved value / delta", "number", "1");
        value.control.step = "any";
        const unit = this.input("Unit / encounter value", "text");
        const scope = this.select("Scope", ["Party", "Expedition", "Participant", "Mount", "Vehicle"]);
        const targetId = this.input("Target ID when required", "text");
        container.append(legend, kind.wrapper, key.wrapper, value.wrapper, unit.wrapper, scope.wrapper, targetId.wrapper);
        return {
            container,
            build: (source, sourceId, occurrenceId) => {
                if (kind.control.value === "None") return [];
                const consequenceId = crypto.randomUUID();
                const consequenceKey = required(key.control.value, "Consequence key");
                const resolvedTarget = target(scope.control.value as ExpeditionEffectScope, nullable(targetId.control.value));
                const provenance = dmJourneyProvenance(`${source}-consequence`, `${sourceId}:${occurrenceId}`);
                switch (kind.control.value) {
                    case "TimeDelay":
                        return [{ id: consequenceId, consequenceKey, category: "TimeDelay", target: resolvedTarget,
                            components: [{ kind: "timeDelay", value: positiveNumber(value.control.value), unit: unit.control.value === "Days" ? "Days" : unit.control.value === "Minutes" ? "Minutes" : "Hours" }], provenance }];
                    case "PersistentEffectChange":
                        return [{ id: consequenceId, consequenceKey, category: "PersistentEffectChange", target: resolvedTarget,
                            components: [{ kind: "persistentEffectChange", effectKey: consequenceKey, operation: "AdjustLevel", levelDelta: nonZeroInteger(value.control.value), explicitlyResolved: true, movementComponents: [] }], provenance }];
                    case "ResourceChange":
                        return [{ id: consequenceId, consequenceKey, category: "ResourceChange", target: resolvedTarget,
                            components: [{ kind: "resourceChange", resourceKey: consequenceKey, operation: "AdjustQuantity", quantity: nonZeroNumber(value.control.value), unit: required(unit.control.value, "Resource unit") }], provenance }];
                    case "EncounterCircumstance":
                        return [{ id: consequenceId, consequenceKey, category: "EncounterCircumstance", target: resolvedTarget,
                            components: [{ kind: "encounterCircumstance", circumstanceKey: consequenceKey, value: nullable(unit.control.value) }], provenance }];
                    case "DamageEndurance":
                        return [{ id: consequenceId, consequenceKey, category: "DamageEndurance", target: resolvedTarget,
                            components: [{ kind: "externalState", stateKey: consequenceKey, delta: nonZeroNumber(value.control.value), unit: nullable(unit.control.value) }], provenance }];
                    default:
                        return [];
                }
            }
        };
    }

    private apply(next: ExpeditionJourneyState): void {
        this.state = next;
        if (!this.disposed) this.render();
    }

    private requireState(): ExpeditionJourneyState {
        if (!this.state) throw new Error("Journey state has not loaded.");
        return this.state;
    }

    private section(title: string): HTMLElement {
        const section = document.createElement("section");
        section.className = "hc-stack";
        section.append(this.heading(title));
        return section;
    }

    private heading(text: string): HTMLElement {
        const heading = document.createElement("h4");
        heading.textContent = text;
        return heading;
    }

    private muted(text: string): HTMLElement {
        const value = document.createElement("p");
        value.className = "hc-muted";
        value.textContent = text;
        return value;
    }

    private input(label: string, type: string, initial = ""): { wrapper: HTMLElement; control: HTMLInputElement } {
        const wrapper = document.createElement("label");
        wrapper.textContent = label;
        const control = document.createElement("input");
        control.type = type;
        control.value = initial;
        wrapper.append(control);
        return { wrapper, control };
    }

    private select(label: string, values: string[]): { wrapper: HTMLElement; control: HTMLSelectElement } {
        const wrapper = document.createElement("label");
        wrapper.textContent = label;
        const control = document.createElement("select");
        for (const raw of values) {
            const option = document.createElement("option");
            const separator = raw.indexOf("|");
            option.value = separator >= 0 ? raw.slice(0, separator) : raw;
            option.textContent = separator >= 0 ? raw.slice(separator + 1) : raw || "—";
            control.append(option);
        }
        wrapper.append(control);
        return { wrapper, control };
    }

    private checkbox(label: string, checked: boolean): { wrapper: HTMLElement; control: HTMLInputElement } {
        const wrapper = document.createElement("label");
        const control = document.createElement("input");
        control.type = "checkbox";
        control.checked = checked;
        wrapper.append(control, document.createTextNode(label));
        return { wrapper, control };
    }

    private button(text: string): HTMLButtonElement {
        const button = document.createElement("button");
        button.type = "submit";
        button.textContent = text;
        return button;
    }
}

function customDefinition(
    processKey: string,
    displayName: string,
    stageKeys: string[],
    approaches: Map<string, string[]>,
    state: ExpeditionJourneyState): JourneyProcessDefinition {
    if (stageKeys.length === 0) throw new Error("At least one stage key is required.");
    const definitions: JourneyStageDefinition[] = stageKeys.map((stageKey, index) => ({
        stageKey,
        displayName: humanize(stageKey),
        description: null,
        completionModel: "Explicit",
        progressTarget: null,
        successTarget: null,
        failureLimit: null,
        complicationLimit: null,
        failProcessAtFailureLimit: false,
        failProcessAtComplicationLimit: false,
        initialProgressState: state.processPolicy.progressKind === "ExplicitState" ? "initial" : null,
        explicitNextStageKey: index + 1 < stageKeys.length ? stageKeys[index + 1] : null,
        outcomeTransitions: [],
        approaches: (approaches.get(stageKey) ?? []).map((approachKey): JourneyApproachDefinition => ({
            approachKey,
            displayName: humanize(approachKey),
            capabilityReference: null,
            note: null
        })),
        roleKeys: []
    }));
    return {
        processKey,
        displayName,
        description: null,
        initialStageKey: stageKeys[0],
        stageOrder: stageKeys,
        stages: definitions,
        destinationReference: null,
        routeReference: null,
        locationReference: null,
        note: null
    };
}

function parseApproaches(value: string): Map<string, string[]> {
    const result = new Map<string, string[]>();
    for (const item of value.split(";").map(value => value.trim()).filter(Boolean)) {
        const separator = item.indexOf("=");
        if (separator < 0) continue;
        result.set(item.slice(0, separator).trim(), item.slice(separator + 1).split(",").map(value => value.trim()).filter(Boolean));
    }
    return result;
}

function capabilityLabel(value: JourneyApproachDefinition): string {
    return value.capabilityReference
        ? `${value.displayName} [${value.capabilityReference.capabilityKey}]`
        : value.displayName;
}

function describeProgress(process: JourneyProcessInstance, state: JourneyProcessInstance["stageStates"][number]): string {
    return process.execution.progressKind === "Numeric"
        ? `Progress ${state.numericProgress ?? 0} ${process.execution.progressUnit ?? "units"}.`
        : `State ${state.explicitState ?? "—"}.`;
}

function safeCurrentReturnPath(): string | null {
    const path = window.location.pathname;
    if (path !== "/tools/hex-crawl" && !path.startsWith("/tools/hex-crawl/")) return null;
    return `${path}${window.location.search}${window.location.hash}`;
}

function splitKeys(value: string): string[] {
    return value.split(";").map(item => item.trim()).filter(Boolean);
}

function humanize(value: string): string {
    const text = value.replace(/[-_]+/g, " ").trim();
    return text ? text[0].toUpperCase() + text.slice(1) : value;
}

function nullable(value: string): string | null {
    const trimmed = value.trim();
    return trimmed ? trimmed : null;
}

function required(value: string, label: string): string {
    const trimmed = value.trim();
    if (!trimmed) throw new Error(`${label} is required.`);
    return trimmed;
}

function optionalNumber(value: string): number | null {
    if (!value.trim()) return null;
    const parsed = Number(value);
    if (!Number.isFinite(parsed)) throw new Error("Progress must be finite.");
    return parsed;
}

function integer(value: string): number {
    const parsed = Number(value);
    if (!Number.isInteger(parsed)) throw new Error("Counter deltas must be integers.");
    return parsed;
}

function positiveNumber(value: string): number {
    const parsed = Number(value);
    if (!Number.isFinite(parsed) || parsed <= 0) throw new Error("Value must be positive and finite.");
    return parsed;
}

function nonZeroNumber(value: string): number {
    const parsed = Number(value);
    if (!Number.isFinite(parsed) || parsed === 0) throw new Error("Value must be finite and non-zero.");
    return parsed;
}

function nonZeroInteger(value: string): number {
    const parsed = Number(value);
    if (!Number.isInteger(parsed) || parsed === 0) throw new Error("Value must be a non-zero integer.");
    return parsed;
}
