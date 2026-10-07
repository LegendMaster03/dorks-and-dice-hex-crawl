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
        const page = root.matches(".hc-page")
            ? root
            : root.querySelector<HTMLElement>(".hc-page");
        page?.append(this.panel);
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
        const section = this.section("Journey rules");
        const process = state.processPolicy;
        const events = state.eventPolicy;
        section.append(this.muted(process.support === "None"
            ? "This procedure does not use a journey process."
            : process.support === "Unsupported"
                ? process.unsupportedReason ?? "The stored journey rules are not supported by this runtime."
                : `Journey stages track ${process.progressKind === "Numeric" ? "progress" : "table state"}${process.progressUnit ? ` in ${process.progressUnit}` : ""}. ${process.roleDriven ? "Assigned roles can be used for resolutions." : "No journey role is required by the procedure."}`));
        section.append(this.muted(events.support === "None"
            ? "This procedure does not create journey events."
            : events.support === "Unsupported"
                ? events.unsupportedReason ?? "The stored journey-event rules are not supported by this runtime."
                : "Journey events appear when their configured trigger occurs. The DM resolves the event content and any resulting expedition consequence."));

        const advanced = document.createElement("details");
        const summary = document.createElement("summary");
        summary.textContent = "Advanced technical details";
        advanced.append(
            summary,
            this.muted(`Process stage model: ${process.stageModel ?? "none"}; transition: ${process.stageTransitionModel ?? "none"}; progress model: ${process.progressModel ?? "none"}; role assignment: ${process.roleAssignmentModel ?? "none"}; interval integration: ${process.intervalIntegrationModel ?? "none"}.`),
            this.muted(`Event trigger model: ${events.triggerModel ?? "none"}; trigger sources: ${events.triggerSources.join(", ") || "none"}; link mode: ${events.linkMode ?? "none"}; targeting: ${events.targetingModel ?? "none"}; consequence model: ${events.consequenceModel ?? "none"}.`));
        section.append(advanced);
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
            item.textContent = `${humanize(assignment.roleKey!)}: ${assignment.participantId ? members.get(assignment.participantId) ?? "Unknown participant" : "Party"}`;
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
            const name = this.input("Journey name", "text", "Journey");
            const destination = this.input("Destination / target", "text");
            const route = this.input("Route", "text");
            const submit = this.button("Start journey");
            form.append(name.wrapper, destination.wrapper, route.wrapper, submit);
            form.addEventListener("submit", event => {
                event.preventDefault();
                void this.mutate(submit, async () => {
                    const result = await this.api.startProcess(this.expeditionId, {
                        expectedVersion: this.requireState().expeditionVersion,
                        processId: crypto.randomUUID(),
                        processKey: "journey",
                        displayName: required(name.control.value, "Journey name"),
                        destinationReference: nullable(destination.control.value),
                        routeReference: nullable(route.control.value),
                        provenance: dmJourneyProvenance("journey-process-start")
                    });
                    this.apply(result.state);
                });
            });
            section.append(this.muted(`Stages: ${state.processPolicy.stageKeys.map(humanize).join(" → ")}. No route length, check formula, or event count is inferred.`), form);
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
        const advanced = document.createElement("details");
        const advancedSummary = document.createElement("summary");
        advancedSummary.textContent = "Advanced custom process definition";
        advanced.append(
            advancedSummary,
            this.muted("Use exact process, stage, and approach keys only when the generic procedure intentionally leaves the journey structure unresolved."),
            custom);
        section.append(advanced);
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
        card.append(this.heading(`${process.definition.displayName} — ${humanize(process.status)}`));
        card.append(this.muted(`Stage: ${current.displayName}. ${describeProgress(process, stageState)} Successes ${stageState.successes}; failures ${stageState.failures}; complications ${stageState.complications}.`));
        if (current.approaches.length > 0) card.append(this.muted(`Approaches: ${current.approaches.map(value => value.displayName).join(" · ")}`));
        if (process.pendingActions.length > 0) card.append(this.muted(`Pending: ${process.pendingActions.map(value => `${humanize(value.kind)}${value.detail ? ` — ${value.detail}` : ""}`).join(" | ")}`));
        card.append(this.processStageSummary(process), this.resolveProcessForm(process), this.processCloseControls(process));
        const technical = document.createElement("details");
        const technicalSummary = document.createElement("summary");
        technicalSummary.textContent = "Advanced technical details";
        technical.append(
            technicalSummary,
            this.muted(`Process ID: ${process.id}`),
            this.muted(`Process key: ${process.processKey}; current stage key: ${process.currentStageKey}.`));
        card.append(technical);
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
        const pendingValues = process.pendingActions.length > 0
            ? process.pendingActions.map(value => `${value.id}|${humanize(value.kind)}${value.detail ? ` — ${value.detail}` : ""}`)
            : [""];
        const pending = this.select("Pending opportunity", pendingValues);
        const approach = this.select("Approach", ["", ...stage.approaches.map(value => `${value.approachKey}|${value.displayName}`)]);
        const runtime = this.getRuntime();
        const roles = [...new Set(runtime.party.activityAssignments.map(value => value.roleKey).filter((value): value is string => Boolean(value)))];
        const roleValues = process.execution.roleDriven && roles.length > 0
            ? roles.map(value => `${value}|${humanize(value)}`)
            : ["", ...roles.map(value => `${value}|${humanize(value)}`)];
        const role = this.select("Role", roleValues);
        const actor = this.select("Actor", ["", ...runtime.party.members.map(value => `${value.id}|${value.name}`)]);
        const progress = this.input(process.execution.progressKind === "Numeric" ? "Progress delta" : "Progress state", process.execution.progressKind === "Numeric" ? "number" : "text", process.execution.progressKind === "Numeric" ? "0" : "");
        if (progress.control instanceof HTMLInputElement && process.execution.progressKind === "Numeric") progress.control.step = "any";
        const successes = this.input("Success delta", "number", "0");
        const failures = this.input("Failure delta", "number", "0");
        const complications = this.input("Complication delta", "number", "0");
        const completeStage = this.checkbox("Complete current stage", false);
        const targetStage = this.select("Next stage", ["", ...process.definition.stageOrder
            .filter(value => value !== process.currentStageKey)
            .map(value => `${value}|${process.definition.stages.find(stage => stage.stageKey === value)?.displayName ?? humanize(value)}`)]);
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
            section.append(
                this.heading("Resolved / skipped events"),
                this.readableTable(
                    "Journey event record",
                    ["Event", "Status", "Target", "Environment", "Consequences"],
                    resolved.map(event => [
                        event.eventKey ? humanize(event.eventKey) : humanize(event.trigger),
                        humanize(event.status),
                        event.participantSnapshot?.participantName
                            ?? (event.targetRoleKey ? humanize(event.targetRoleKey) : humanize(event.targetKind)),
                        event.environment.length > 0
                            ? event.environment.map(value => `${humanize(value.dimension)} ${value.value}${value.unit ? ` ${value.unit}` : ""}`).join(" · ")
                            : "None recorded",
                        event.consequenceIds.length > 0 ? String(event.consequenceIds.length) : "None"
                    ])));
            for (const event of resolved.filter(value => value.status === "Resolved" && value.consequenceIds.length > 0)) {
                const actions = document.createElement("div");
                actions.className = "hc-card";
                actions.append(
                    this.heading(event.eventKey ? humanize(event.eventKey) : humanize(event.trigger)),
                    this.encounterHandoffControl(event));
                section.append(actions);
            }
            const technical = document.createElement("details");
            const technicalSummary = document.createElement("summary");
            technicalSummary.textContent = "Resolved event technical details";
            technical.append(technicalSummary);
            for (const event of resolved) {
                technical.append(this.muted(
                    `${event.id} · trigger ${event.trigger} · reference ${event.triggerReference} · process ${event.processId ?? "none"} · stage ${event.stageKey ?? "none"} · consequences ${event.consequenceIds.join(", ") || "none"}`));
            }
            section.append(technical);
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
        card.append(
            this.heading("Pending journey event"),
            this.muted(`Triggered by ${journeyTriggerLabel(event.trigger)}.`));
        if (event.environment.length > 0) card.append(this.muted(`Relevant environment: ${event.environment.map(value => `${humanize(value.dimension)} ${value.value}${value.unit ? ` ${value.unit}` : ""}`).join("; ")}`));
        const technical = document.createElement("details");
        const technicalSummary = document.createElement("summary");
        technicalSummary.textContent = "Advanced technical details";
        technical.append(
            technicalSummary,
            this.muted(`Trigger reference: ${event.triggerReference}.`),
            this.muted(`Occurrence ID: ${event.id}; process ID: ${event.processId ?? "none"}; stage key: ${event.stageKey ?? "none"}.`));
        card.append(technical);
        const form = document.createElement("form");
        form.className = "hc-form hc-form-grid";
        const state = this.requireState();
        const runtime = this.getRuntime();
        const status = this.select("Resolution", ["Resolved|Resolved", "Skipped|Skipped", "NotApplicable|Not applicable"]);
        const key = this.input("Event name", "text");
        const type = this.input("Event type", "text");
        const targetKinds = state.eventPolicy.targetingModel === "travel-role"
            ? ["Role"]
            : ["Party", "Expedition", "Role", "Participant", "Mount", "Vehicle"];
        const targetKind = this.select("Affects", targetKinds.map(value => `${value}|${humanize(value)}`));
        const role = this.select("Role", ["", ...runtime.participantActivityPolicy.roleKeys.map(value => `${value}|${humanize(value)}`)]);
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
                        eventKey: resolved ? required(key.control.value, "Event name") : null,
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
        const section = this.section("Journey history");
        if (state.history.length === 0) {
            section.append(this.muted("No journey history yet."));
            return section;
        }
        section.append(this.readableTable(
            "Journey history",
            ["Event", "Detail", "Completed watches", "Source"],
            state.history.map(record => [
                journeyHistoryLabel(record.kind),
                record.detail,
                String(record.completedWatches),
                humanize(record.provenance.sourceKey)
            ])));

        const advanced = document.createElement("details");
        const summary = document.createElement("summary");
        summary.textContent = "Advanced technical details";
        const technical = document.createElement("ol");
        for (const record of state.history) {
            const item = document.createElement("li");
            item.textContent = `${record.kind} · ${record.detail} · watch ${record.completedWatches} · source ${record.provenance.sourceKey} · process ${record.processId ?? "none"} · stage ${record.stageKey ?? "none"} · resolution ${record.resolutionId ?? "none"} · event ${record.eventOccurrenceId ?? "none"} · record ${record.id}`;
            technical.append(item);
        }
        advanced.append(summary, technical);
        section.append(advanced);
        return section;
    }

    private consequenceFields(): { container: HTMLElement; build: (source: string, sourceId: string, occurrenceId: string) => ExpeditionConsequenceInput[] } {
        const container = document.createElement("fieldset");
        const legend = document.createElement("legend");
        legend.textContent = "Optional journey result";

        const kind = this.select("Result", [
            "None|No consequence",
            "TimeDelay|Time delay",
            "PersistentEffectChange|Persistent effect",
            "ResourceChange|Resource change",
            "EncounterCircumstance|Encounter circumstance",
            "DamageEndurance|Damage / endurance"
        ]);
        const runtime = this.getRuntime();
        const effectKinds = runtime.procedure.modules
            .find(module => module.moduleKey === "effects.expedition")
            ?.parameters.effectKinds?.split(";").map(value => value.trim()).filter(Boolean) ?? [];
        const effect = effectKinds.length > 0
            ? this.select("Effect", effectKinds.map(value => `${value}|${humanize(value)}`))
            : this.input("Effect", "text");
        const name = this.input("Resource / circumstance / state", "text");
        const value = this.input("Amount / level change", "number", "1");
        value.control.step = "any";
        const unit = this.input("Unit / value", "text");
        const timeUnit = this.select("Time unit", ["Hours|Hours", "Minutes|Minutes", "Days|Days"]);

        const targetOptions = [
            "Party|Party",
            "Expedition|Expedition",
            ...runtime.party.members.map(member => `Participant:${member.id}|${member.name}`),
            ...(runtime.party.movementContributors ?? [])
                .filter(contributor => contributor.kind === "Mount" || contributor.kind === "Vehicle")
                .map(contributor => `${contributor.kind}:${contributor.id}|${humanize(contributor.kind)}: ${humanize(contributor.key)}`)
        ];
        const affected = this.select("Affects", targetOptions);

        const technical = document.createElement("details");
        const technicalSummary = document.createElement("summary");
        technicalSummary.textContent = "Advanced technical details";
        technical.append(
            technicalSummary,
            this.muted("The selected tabletop result is translated to the existing generic ExpeditionConsequence model. Exact category, target scope, generated identity, and provenance remain available in persisted audit state."));

        container.append(
            legend,
            kind.wrapper,
            effect.wrapper,
            name.wrapper,
            value.wrapper,
            unit.wrapper,
            timeUnit.wrapper,
            affected.wrapper,
            technical);

        const updateVisibility = (): void => {
            const selected = kind.control.value;
            effect.wrapper.hidden = selected !== "PersistentEffectChange";
            name.wrapper.hidden = !["ResourceChange", "EncounterCircumstance", "DamageEndurance"].includes(selected);
            value.wrapper.hidden = selected === "None" || selected === "EncounterCircumstance";
            unit.wrapper.hidden = !["ResourceChange", "EncounterCircumstance", "DamageEndurance"].includes(selected);
            timeUnit.wrapper.hidden = selected !== "TimeDelay";
            affected.wrapper.hidden = selected === "None";
        };
        kind.control.addEventListener("change", updateVisibility);
        updateVisibility();

        return {
            container,
            build: (source, sourceId, occurrenceId) => {
                if (kind.control.value === "None") return [];
                const consequenceId = crypto.randomUUID();
                const resolvedTarget = parseJourneyTarget(affected.control.value);
                const provenance = dmJourneyProvenance(`${source}-consequence`, `${sourceId}:${occurrenceId}`);
                switch (kind.control.value) {
                    case "TimeDelay":
                        return [{
                            id: consequenceId,
                            consequenceKey: "journey-time-delay",
                            category: "TimeDelay",
                            target: resolvedTarget,
                            components: [{ kind: "timeDelay", value: positiveNumber(value.control.value), unit: timeUnit.control.value as "Minutes" | "Hours" | "Days" }],
                            provenance
                        }];
                    case "PersistentEffectChange": {
                        const effectKey = required(effect.control.value, "Effect");
                        return [{
                            id: consequenceId,
                            consequenceKey: effectKey,
                            category: "PersistentEffectChange",
                            target: resolvedTarget,
                            components: [{ kind: "persistentEffectChange", effectKey, operation: "AdjustLevel", levelDelta: nonZeroInteger(value.control.value), explicitlyResolved: true, movementComponents: [] }],
                            provenance
                        }];
                    }
                    case "ResourceChange": {
                        const resourceKey = required(name.control.value, "Resource");
                        return [{
                            id: consequenceId,
                            consequenceKey: resourceKey,
                            category: "ResourceChange",
                            target: resolvedTarget,
                            components: [{ kind: "resourceChange", resourceKey, operation: "AdjustQuantity", quantity: nonZeroNumber(value.control.value), unit: required(unit.control.value, "Resource unit") }],
                            provenance
                        }];
                    }
                    case "EncounterCircumstance": {
                        const circumstanceKey = required(name.control.value, "Circumstance");
                        return [{
                            id: consequenceId,
                            consequenceKey: circumstanceKey,
                            category: "EncounterCircumstance",
                            target: resolvedTarget,
                            components: [{ kind: "encounterCircumstance", circumstanceKey, value: nullable(unit.control.value) }],
                            provenance
                        }];
                    }
                    case "DamageEndurance": {
                        const stateKey = required(name.control.value, "State");
                        return [{
                            id: consequenceId,
                            consequenceKey: stateKey,
                            category: "DamageEndurance",
                            target: resolvedTarget,
                            components: [{ kind: "externalState", stateKey, delta: nonZeroNumber(value.control.value), unit: nullable(unit.control.value) }],
                            provenance
                        }];
                    }
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

    private readableTable(captionText: string, headers: string[], rows: string[][]): HTMLTableElement {
        const table = document.createElement("table");
        table.className = "hc-readable-table";
        const caption = document.createElement("caption");
        caption.textContent = captionText;
        const head = document.createElement("thead");
        const header = document.createElement("tr");
        for (const label of headers) {
            const cell = document.createElement("th");
            cell.scope = "col";
            cell.textContent = label;
            header.append(cell);
        }
        head.append(header);
        const body = document.createElement("tbody");
        for (const values of rows) {
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

function journeyTriggerLabel(value: string): string {
    switch (value) {
        case "ProcessProgress": return "journey progress";
        case "StageTransition": return "a stage transition";
        case "WatchCompleted": return "a completed watch";
        case "Landmark": return "a landmark";
        case "External": return "an external event";
        case "Explicit": return "an explicit journey event";
        default: return humanize(value).toLowerCase();
    }
}

function journeyHistoryLabel(kind: string): string {
    switch (kind) {
        case "ProcessStarted": return "Journey started";
        case "ResolutionRecorded": return "Journey result recorded";
        case "ProgressChanged": return "Journey progress updated";
        case "ComplicationChanged": return "Journey complication recorded";
        case "FailureChanged": return "Journey failure recorded";
        case "StageTransitioned": return "Journey stage changed";
        case "ProcessCompleted": return "Journey completed";
        case "ProcessFailed": return "Journey failed";
        case "ProcessAbandoned": return "Journey abandoned";
        case "EventOpportunityCreated": return "Journey event became available";
        case "EventResolved": return "Journey event resolved";
        case "EventSkipped": return "Journey event skipped";
        case "WatchOpportunityCreated": return "Journey watch resolution became available";
        default: return humanize(kind);
    }
}

function humanize(value: string): string {
    const text = value
        .replace(/([a-z0-9])([A-Z])/g, "$1 $2")
        .replace(/[-_]+/g, " ")
        .trim();
    return text ? text[0].toUpperCase() + text.slice(1) : value;
}

function parseJourneyTarget(value: string): ReturnType<typeof target> {
    const separator = value.indexOf(":");
    if (separator < 0) return target(value as ExpeditionEffectScope, null);
    return target(
        value.slice(0, separator) as ExpeditionEffectScope,
        value.slice(separator + 1));
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
