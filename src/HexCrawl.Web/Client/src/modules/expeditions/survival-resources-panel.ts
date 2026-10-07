import { SurvivalResourcesApi } from "../../survival-api";
import type { ExpeditionDetail } from "../../types";
import type {
    CampingPolicy,
    ConsequenceProvenanceRequest,
    ExpeditionEffectScope,
    ExpeditionResource,
    ExpeditionResourceInventoryModel,
    ExpeditionTarget,
    ExposurePolicy,
    ForagingPolicy,
    ForcedTravelPolicy,
    ResolvedSurvivalComponentRequest,
    ResourceChangeOperation,
    ResourceConsumptionPolicy,
    ResourceChangeRequest,
    SurvivalResources
} from "../../survival-types";

export type SurvivalResourcesPanelFocus =
    | "all"
    | "forcedTravel"
    | "pendingResourceConsequences";

export class ExpeditionSurvivalResourcesPanel {
    private readonly panel: HTMLDetailsElement;
    private readonly body: HTMLElement;
    private state: SurvivalResources | null = null;
    private disposed = false;

    public constructor(
        root: HTMLElement,
        private readonly api: SurvivalResourcesApi,
        private readonly expeditionId: string,
        private readonly getRuntime: () => ExpeditionDetail,
        private readonly mutate: (control: HTMLButtonElement | null, action: () => Promise<void>) => Promise<void>,
        private readonly focus: SurvivalResourcesPanelFocus = "all") {
        this.panel = document.createElement("details");
        this.panel.className = "hc-panel";
        this.panel.dataset.survivalResourcesPanel = "";
        const summary = document.createElement("summary");
        summary.textContent = "Survival and resources";
        this.body = document.createElement("div");
        this.body.className = "hc-form";
        this.panel.append(summary, this.body);
        const page = root.matches(".hc-page")
            ? root
            : root.querySelector<HTMLElement>(".hc-page");
        if (page) page.append(this.panel);
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
        const state = this.state;
        if (!state) return;
        if (this.focus === "forcedTravel") {
            this.body.replaceChildren(
                this.section("Forced travel", this.forcedTravelView(state)));
            return;
        }
        if (this.focus === "pendingResourceConsequences") {
            this.body.replaceChildren(
                this.section(
                    "Pending travel consequences",
                    this.pendingResourceConsequencesView(state, true)));
            return;
        }
        this.body.replaceChildren(
            this.section("Resources", this.resourcesView(state)),
            this.section("Forced travel", this.forcedTravelView(state)),
            this.section("Survival exposure", this.exposureView(state)),
            this.section("Foraging", this.foragingView(state)),
            this.section("Camping and rest", this.campingView(state)));
    }

    private resourcesView(state: SurvivalResources): HTMLElement {
        const container = document.createElement("div");
        container.append(this.policySummary("Resource policy", state.resourcePolicy));
        if (state.resources.length === 0) {
            container.append(this.muted("No expedition-owned resources are recorded."));
        } else {
            const list = document.createElement("div");
            list.className = "hc-stack";
            for (const resource of state.resources) list.append(this.resourceCard(resource));
            container.append(list);
        }

        const add = document.createElement("form");
        add.className = "hc-form hc-form-grid";
        const key = this.input("Resource key", "text");
        const model = this.select("Inventory model", ["Counted", "Abstract", "SupplyDie", "ExternalManual"]);
        const value = this.input("Quantity, state, or die sides", "text");
        const unit = this.input("Unit for counted resources", "text");
        const scope = this.select("Scope", ["Party", "Expedition", "Participant", "Mount", "Vehicle"]);
        const target = this.input("Target ID when scope requires it", "text");
        const note = this.input("Note", "text");
        const submit = this.button("Add resource");
        add.append(key.wrapper, model.wrapper, value.wrapper, unit.wrapper, scope.wrapper, target.wrapper, note.wrapper, submit);
        add.addEventListener("submit", event => {
            event.preventDefault();
            void this.mutate(submit, async () => {
                const resourceId = crypto.randomUUID();
                const inventoryModel = model.control.value as ExpeditionResourceInventoryModel;
                const parsed = parseResourceValue(inventoryModel, value.control.value);
                const operation = await this.api.upsertResource(this.expeditionId, resourceId, {
                    expectedVersion: this.requireState().expeditionVersion,
                    resourceKey: required(key.control.value, "Resource key"),
                    target: targetValue(scope.control.value as ExpeditionEffectScope, target.control.value),
                    inventoryModel,
                    quantity: parsed.quantity,
                    unit: inventoryModel === "Counted" ? required(unit.control.value, "Counted resource unit") : null,
                    symbolicState: parsed.symbolicState,
                    supplyDieSides: parsed.supplyDieSides,
                    note: nullable(note.control.value),
                    provenance: dmProvenance("manual-resource-add")
                });
                this.apply(operation.state);
                add.reset();
                model.control.value = "Counted";
                scope.control.value = "Party";
            });
        });
        container.append(this.heading("Add expedition resource"), add);

        if (state.pendingResourceConsequences.length > 0) {
            container.append(
                this.heading("Pending resource consequences"),
                this.pendingResourceConsequencesView(state));
        }

        container.append(this.resourceConsumptionForm(state.resourcePolicy));
        return container;
    }

    private pendingResourceConsequencesView(
        state: SurvivalResources,
        showEmpty = false): HTMLElement {
        const container = document.createElement("div");
        container.className = "hc-stack";
        if (state.pendingResourceConsequences.length === 0) {
            if (showEmpty) container.append(this.muted("No travel consequence is pending."));
            return container;
        }

        for (const pending of state.pendingResourceConsequences) {
            const card = document.createElement("div");
            card.className = "hc-card";
            card.append(
                this.heading(pending.consequenceKey),
                this.muted(`${pending.resourceKeys.join(", ") || "Resource state"} — ${pending.reason}`));
            const apply = this.button("Apply resolved resource change");
            apply.type = "button";
            apply.addEventListener("click", () => {
                void this.mutate(apply, async () => {
                    const result = await this.api.applyPendingResource(
                        this.expeditionId,
                        pending.consequenceId,
                        {
                            expectedVersion: this.requireState().expeditionVersion,
                            provenance: dmProvenance("pending-resource-apply")
                        });
                    this.apply(result.state);
                });
            });
            card.append(apply);
            container.append(card);
        }
        return container;
    }

    private resourceCard(resource: ExpeditionResource): HTMLElement {
        const card = document.createElement("div");
        card.className = "hc-card";
        const title = this.heading(resource.resourceKey);
        const detail = document.createElement("p");
        detail.textContent = `${describeTarget(resource.target)} · ${resource.inventoryModel} · ${describeResourceValue(resource)}${resource.isDepleted ? " · depleted" : ""}`;
        const row = document.createElement("div");
        row.className = "hc-button-row";
        const correct = this.button("Correct state");
        correct.type = "button";
        correct.addEventListener("click", () => {
            const promptLabel = resource.inventoryModel === "Counted" ? "New quantity"
                : resource.inventoryModel === "Abstract" ? "New symbolic state"
                    : resource.inventoryModel === "SupplyDie" ? "New die sides; blank means depleted"
                        : "External/manual resources have no authoritative Hex Crawl value";
            if (resource.inventoryModel === "ExternalManual") return;
            const current = resource.quantity?.toString() ?? resource.symbolicState ?? resource.supplyDieSides?.toString() ?? "";
            const next = window.prompt(promptLabel, current);
            if (next === null) return;
            void this.mutate(correct, async () => {
                const parsed = parseResourceValue(resource.inventoryModel, next);
                const result = await this.api.upsertResource(this.expeditionId, resource.id, {
                    expectedVersion: this.requireState().expeditionVersion,
                    resourceKey: resource.resourceKey,
                    target: resource.target,
                    inventoryModel: resource.inventoryModel,
                    quantity: parsed.quantity,
                    unit: resource.unit,
                    symbolicState: parsed.symbolicState,
                    supplyDieSides: parsed.supplyDieSides,
                    note: resource.note,
                    provenance: dmProvenance("manual-resource-correction")
                });
                this.apply(result.state);
            });
        });
        const remove = this.button("Remove");
        remove.type = "button";
        remove.addEventListener("click", () => {
            void this.mutate(remove, async () => {
                const result = await this.api.removeResource(this.expeditionId, resource.id, {
                    expectedVersion: this.requireState().expeditionVersion,
                    provenance: dmProvenance("manual-resource-remove")
                });
                this.apply(result.state);
            });
        });
        row.append(correct, remove);
        card.append(title, detail, row);
        return card;
    }

    private resourceConsumptionForm(policy: ResourceConsumptionPolicy): HTMLElement {
        const wrapper = document.createElement("div");
        wrapper.append(this.heading("Resolve resource consumption"));
        if (policy.support !== "Supported") {
            wrapper.append(this.muted(policy.support === "None" ? "No pinned resource-consumption policy." : policy.unsupportedReason ?? "Resource policy is unsupported."));
            return wrapper;
        }
        const form = document.createElement("form");
        form.className = "hc-form hc-form-grid";
        const due = this.checkbox("Consumption is due", true);
        const resourceKey = this.input("Resource key", "text");
        resourceKey.control.value = policy.resourceKinds[0] ?? "";
        const resourceId = this.input("Resource ID when needed", "text");
        const operation = this.select("Resolved operation", ["AdjustQuantity", "SetQuantity", "SetState", "SetSupplyDie", "Deplete"]);
        operation.control.value = defaultResourceOperation(policy.inventoryModel);
        const value = this.input("Resolved value", "text");
        const unit = this.input("Unit for quantity operations", "text");
        const scope = this.select("Target scope", ["Party", "Expedition", "Participant", "Mount", "Vehicle"]);
        const target = this.input("Target ID when scope requires it", "text");
        const submit = this.button("Resolve consumption");
        form.append(due.wrapper, resourceKey.wrapper, resourceId.wrapper, operation.wrapper, value.wrapper, unit.wrapper,
            scope.wrapper, target.wrapper, submit);
        form.addEventListener("submit", event => {
            event.preventDefault();
            void this.mutate(submit, async () => {
                const changes: ResourceChangeRequest[] = due.control.checked
                    ? [buildResourceChange(
                        required(resourceKey.control.value, "Resource key"),
                        nullable(resourceId.control.value),
                        operation.control.value as ResourceChangeOperation,
                        value.control.value,
                        unit.control.value)]
                    : [];
                const result = await this.api.resolveConsumption(this.expeditionId, {
                    expectedVersion: this.requireState().expeditionVersion,
                    occurrenceId: crypto.randomUUID(),
                    due: due.control.checked,
                    target: targetValue(scope.control.value as ExpeditionEffectScope, target.control.value),
                    changes,
                    provenance: dmProvenance("resource-consumption-resolution")
                });
                this.apply(result.state);
            });
        });
        wrapper.append(this.muted(`Model ${policy.consumptionModel ?? "unspecified"}; cadence ${policy.consumptionInterval ?? "unspecified"}. The exact resolved operation is entered explicitly when the pinned contract does not define it.`), form);
        return wrapper;
    }

    private forcedTravelView(state: SurvivalResources): HTMLElement {
        const container = document.createElement("div");
        container.className = "hc-stack";
        const policy = state.forcedTravelPolicy;

        if (policy.support === "None") {
            container.append(this.muted("This procedure does not define forced travel."));
            return container;
        }
        if (policy.support === "Unsupported") {
            container.append(this.muted(policy.unsupportedReason ?? "The stored forced-travel procedure is not supported by this runtime."));
            container.append(this.forcedTravelTechnicalDetails(policy));
            return container;
        }

        const unit = state.forcedTravel.unit ?? policy.limitUnit ?? "units";
        const limit = state.forcedTravel.normalLimit ?? policy.normalTravelLimit;
        const status = state.forcedTravel.checkDue
            ? "A forced-travel check is required before routine travel continues."
            : state.forcedTravel.forcedTravelBegun
                ? "The party is in forced travel."
                : state.forcedTravel.thresholdReached
                    ? "The normal-travel threshold has been reached."
                    : "The party remains within normal travel.";

        container.append(
            this.heading("Current requirement"),
            this.muted(status),
            this.muted(`Travel recorded: ${formatNumber(state.forcedTravel.amountSinceReset)} ${unit}${limit === null ? "" : `; normal limit ${formatNumber(limit)} ${policy.limitUnit ?? unit}`}.`),
            this.muted(`Check: ${humanize(policy.checkModel ?? "DM-resolved check")}. Failure consequence: ${humanize(policy.failureConsequence ?? "procedure-defined consequence")}.`));

        if (state.forcedTravel.lastResolution) {
            container.append(this.muted(
                `Latest check: ${state.forcedTravel.lastResolution.success ? "succeeded" : "failed"} at ${formatNumber(state.forcedTravel.lastResolution.amountAtResolution)} ${state.forcedTravel.lastResolution.unit}.`));
        }

        container.append(this.forcedTravelControls(policy), this.forcedTravelTechnicalDetails(policy));
        return container;
    }

    private forcedTravelControls(policy: ForcedTravelPolicy): HTMLElement {
        const container = document.createElement("div");
        container.className = "hc-stack";

        const usage = document.createElement("form");
        usage.className = "hc-form hc-form-grid";
        const unitLabel = policy.limitUnit ? `Resolved travel usage (${policy.limitUnit})` : "Resolved travel usage";
        const amount = this.input(unitLabel, "number");
        amount.control.step = "any";
        const unit = policy.limitUnit ? null : this.input("Travel unit", "text");
        const add = this.button("Record resolved usage");
        usage.append(amount.wrapper);
        if (unit) usage.append(unit.wrapper);
        usage.append(add);
        usage.addEventListener("submit", event => {
            event.preventDefault();
            void this.mutate(add, async () => {
                const resolvedUnit = policy.limitUnit ?? required(unit!.control.value, "Forced-travel unit");
                const result = await this.api.recordForcedTravelUsage(this.expeditionId, {
                    expectedVersion: this.requireState().expeditionVersion,
                    occurrenceId: crypto.randomUUID(),
                    amount: finiteNumber(amount.control.value, "Travel usage"),
                    unit: resolvedUnit,
                    provenance: dmProvenance("forced-travel-manual-accounting")
                });
                this.apply(result.state);
            });
        });
        container.append(usage);

        const current = this.requireState().forcedTravel;
        if (current.checkDue && current.pendingCheckId) {
            const form = document.createElement("form");
            form.className = "hc-form hc-form-grid";
            form.append(
                this.muted("Record the resolved check result. If it failed, identify who or what is affected; the configured consequence is shown above."));

            const success = this.checkbox("Check succeeded", true);
            const runtime = this.getRuntime();
            const movementContributors = runtime.party.movementContributors ?? [];
            const constrainedScope = policy.failureTargetScope;
            const availableScopes: ExpeditionEffectScope[] = constrainedScope
                ? [constrainedScope]
                : [
                    "Party",
                    "Expedition",
                    ...(runtime.party.members.length > 0 ? ["Participant" as const] : []),
                    ...(movementContributors.some(value => value.kind === "Mount") ? ["Mount" as const] : []),
                    ...(movementContributors.some(value => value.kind === "Vehicle") ? ["Vehicle" as const] : [])
                ];
            const scope = constrainedScope
                ? null
                : this.select("Affected target", availableScopes);
            const target = document.createElement("select");
            target.dataset.forcedTravelTarget = "";
            const targetWrapper = document.createElement("label");
            const targetStatus = this.muted("");
            targetWrapper.append(document.createTextNode("Affected target"), target);
            if (constrainedScope) {
                form.append(this.muted(`Affected scope: ${humanize(constrainedScope)}.`));
            }

            const effectKey = this.input("Failure effect key", "text");
            effectKey.control.value = policy.failureConsequence ?? "";
            const levelDelta = this.input("Resolved effect level delta", "number");
            levelDelta.control.value = "1";
            const externalKey = this.input("External state key, optional", "text");
            const externalDelta = this.input("External state delta, optional", "number");

            const advanced = document.createElement("details");
            advanced.className = "hc-ux-disclosure";
            const advancedSummary = document.createElement("summary");
            advancedSummary.textContent = "Advanced consequence details";
            const advancedBody = document.createElement("div");
            advancedBody.className = "hc-form hc-form-grid";
            advancedBody.append(effectKey.wrapper, levelDelta.wrapper, externalKey.wrapper, externalDelta.wrapper);
            advanced.append(advancedSummary, advancedBody);

            const resolve = this.button("Record forced-travel result");

            const selectedScope = (): ExpeditionEffectScope =>
                constrainedScope ?? scope?.control.value as ExpeditionEffectScope ?? "Party";
            const choicesForScope = (value: ExpeditionEffectScope): Array<{ id: string; label: string }> => {
                if (value === "Participant") {
                    return runtime.party.members.map(member => ({ id: member.id, label: member.name }));
                }
                return movementContributors
                    .filter(contributor => contributor.kind === value)
                    .map(contributor => ({ id: contributor.id, label: humanize(contributor.key) }));
            };
            const syncTarget = (): void => {
                const targetScope = selectedScope();
                const requiresEntity = targetScope === "Participant" || targetScope === "Mount" || targetScope === "Vehicle";
                const choices = requiresEntity ? choicesForScope(targetScope) : [];
                target.replaceChildren();
                if (choices.length !== 1) {
                    const empty = document.createElement("option");
                    empty.value = "";
                    empty.textContent = targetScope === "Participant"
                        ? "Choose affected character"
                        : targetScope === "Mount"
                            ? "Choose affected mount"
                            : "Choose affected vehicle";
                    target.append(empty);
                }
                for (const choice of choices) {
                    const option = document.createElement("option");
                    option.value = choice.id;
                    option.textContent = choice.label;
                    target.append(option);
                }
                targetWrapper.firstChild!.textContent = targetScope === "Participant"
                    ? "Affected character"
                    : targetScope === "Mount"
                        ? "Affected mount"
                        : targetScope === "Vehicle"
                            ? "Affected vehicle"
                            : "Affected target";
                const resolvingFailure = !success.control.checked;
                target.required = resolvingFailure && requiresEntity;
                targetWrapper.hidden = !resolvingFailure || !requiresEntity;
                targetStatus.hidden = !resolvingFailure || !requiresEntity || choices.length > 0;
                targetStatus.textContent = targetScope === "Participant"
                    ? "This forced-travel consequence affects a character, but this expedition has no party members configured. Configure the party before recording a failed check."
                    : `This forced-travel consequence affects a ${targetScope.toLowerCase()}, but no selectable ${targetScope.toLowerCase()} is configured for this expedition.`;
                resolve.disabled = resolvingFailure && requiresEntity && choices.length === 0;
            };
            success.control.addEventListener("change", syncTarget);
            scope?.control.addEventListener("change", syncTarget);
            syncTarget();

            form.append(success.wrapper);
            if (scope) form.append(scope.wrapper);
            form.append(targetWrapper, targetStatus, advanced, resolve);
            form.addEventListener("submit", event => {
                event.preventDefault();
                void this.mutate(resolve, async () => {
                    const failureComponents: ResolvedSurvivalComponentRequest[] = [];
                    if (!success.control.checked) {
                        failureComponents.push({
                            kind: "PersistentEffect",
                            key: required(effectKey.control.value, "Failure effect key"),
                            effectOperation: "AdjustLevel",
                            levelDelta: finiteNumber(levelDelta.control.value, "Effect level delta")
                        });
                        if (nullable(externalKey.control.value)) {
                            failureComponents.push({
                                kind: "ExternalState",
                                key: required(externalKey.control.value, "External state key"),
                                delta: finiteNumber(externalDelta.control.value, "External state delta")
                            });
                        }
                    }
                    const result = await this.api.resolveForcedTravelCheck(this.expeditionId, {
                        expectedVersion: this.requireState().expeditionVersion,
                        checkId: current.pendingCheckId!,
                        success: success.control.checked,
                        target: success.control.checked
                            ? { scope: "Party", targetId: null }
                            : targetValue(selectedScope(), target.value),
                        failureComponents,
                        provenance: dmProvenance("forced-travel-check-resolution")
                    });
                    this.apply(result.state);
                });
            });
            container.append(form);
        }

        const reset = this.button("Reset forced-travel progress");
        reset.type = "button";
        reset.addEventListener("click", () => {
            void this.mutate(reset, async () => {
                const result = await this.api.resetForcedTravel(this.expeditionId, {
                    expectedVersion: this.requireState().expeditionVersion,
                    provenance: dmProvenance("forced-travel-reset")
                });
                this.apply(result.state);
            });
        });
        container.append(reset);
        return container;
    }

    private forcedTravelTechnicalDetails(policy: ForcedTravelPolicy): HTMLElement {
        const details = document.createElement("details");
        details.className = "hc-ux-disclosure";
        const summary = document.createElement("summary");
        summary.textContent = "Advanced policy details";
        const content = document.createElement("div");
        content.className = "hc-stack";
        content.append(this.muted(
            `Support: ${policy.support}; mechanic: ${policy.mechanicKey ?? "none"}${policy.mechanicVersion === null ? "" : ` v${policy.mechanicVersion}`}; execution: ${policy.executionHandler ?? "none"}.`));
        details.append(summary, content);
        return details;
    }

    private exposureView(state: SurvivalResources): HTMLElement {
        const container = document.createElement("div");
        container.append(this.policySummary("Exposure policy", state.exposurePolicy));
        if (state.environmentFacts.length > 0) {
            const facts = document.createElement("p");
            facts.textContent = `Relevant effective environment: ${state.environmentFacts.map(fact => `${fact.dimension}=${fact.value ?? fact.valueKind}`).join("; ")}.`;
            container.append(facts);
        } else {
            container.append(this.muted("No effective environment facts are automatically interpreted as fatigue, exhaustion, or damage."));
        }
        for (const progress of state.exposure) {
            container.append(this.muted(`${progress.exposureKey}: ${progress.amount} ${progress.unit} for ${describeTarget(progress.target)}.`));
        }
        if (state.exposurePolicy.support === "Supported") container.append(this.exposureForm(state.exposurePolicy));
        return container;
    }

    private exposureForm(policy: ExposurePolicy): HTMLElement {
        const form = document.createElement("form");
        form.className = "hc-form hc-form-grid";
        const key = this.input("Exposure key", "text");
        const delta = this.input("Resolved progress delta", "number");
        delta.control.step = "any";
        const unit = this.input("Progress unit", "text");
        const target = this.input(`${policy.targetScope ?? "Party"} target ID when required`, "text");
        const effect = this.input("Resolved persistent effect key, optional", "text");
        const level = this.input("Resolved effect level delta", "number");
        level.control.value = "1";
        const submit = this.button("Resolve exposure");
        form.append(key.wrapper, delta.wrapper, unit.wrapper, target.wrapper, effect.wrapper, level.wrapper, submit);
        form.addEventListener("submit", event => {
            event.preventDefault();
            void this.mutate(submit, async () => {
                const effectKey = nullable(effect.control.value);
                const targetScope = policy.targetScope ?? "Party";
                const result = await this.api.resolveExposure(this.expeditionId, {
                    expectedVersion: this.requireState().expeditionVersion,
                    occurrenceId: crypto.randomUUID(),
                    exposureKey: required(key.control.value, "Exposure key"),
                    target: targetValue(targetScope, target.control.value),
                    progressDelta: nullable(delta.control.value) === null ? null : finiteNumber(delta.control.value, "Exposure progress"),
                    progressUnit: nullable(unit.control.value),
                    consequenceComponents: effectKey ? [{
                        kind: "PersistentEffect",
                        key: effectKey,
                        effectOperation: "AdjustLevel",
                        levelDelta: finiteNumber(level.control.value, "Effect level delta")
                    }] : [],
                    provenance: dmProvenance("exposure-resolution")
                });
                this.apply(result.state);
            });
        });
        const wrapper = document.createElement("div");
        wrapper.append(this.muted(`Dimensions: ${policy.dimensions.join(", ")}; evaluation ${policy.evaluationModel ?? "resolved"}. Effects require explicit resolved data.`), form);
        return wrapper;
    }

    private foragingView(state: SurvivalResources): HTMLElement {
        const container = document.createElement("div");
        const policy = state.foragingPolicy;
        container.append(this.policySummary("Foraging policy", policy));
        if (policy.support !== "Supported") return container;
        const form = document.createElement("form");
        form.className = "hc-form hc-form-grid";
        const resource = this.input("Resolved resource key", "text");
        const resourceId = this.input("Resource ID when needed", "text");
        const operation = this.select("Resolved operation", ["AdjustQuantity", "SetQuantity", "SetState", "SetSupplyDie", "Deplete"]);
        const value = this.input("Resolved value", "text");
        const unit = this.input("Unit for quantity operations", "text");
        const scope = this.select("Target scope", ["Party", "Expedition", "Participant", "Mount", "Vehicle"]);
        const target = this.input("Target ID when scope requires it", "text");
        const assignments = this.input("Activity assignment IDs, comma-separated", "text");
        const submit = this.button("Resolve foraging yield");
        form.append(resource.wrapper, resourceId.wrapper, operation.wrapper, value.wrapper, unit.wrapper,
            scope.wrapper, target.wrapper, assignments.wrapper, submit);
        form.addEventListener("submit", event => {
            event.preventDefault();
            void this.mutate(submit, async () => {
                const change = buildResourceChange(
                    required(resource.control.value, "Resource key"),
                    nullable(resourceId.control.value),
                    operation.control.value as ResourceChangeOperation,
                    value.control.value,
                    unit.control.value);
                if (change.operation === "AdjustQuantity" && (change.quantity ?? 0) <= 0) {
                    throw new Error("Foraging adjust-quantity gains must be positive; negative input is not silently reinterpreted.");
                }
                const result = await this.api.resolveForaging(this.expeditionId, {
                    expectedVersion: this.requireState().expeditionVersion,
                    occurrenceId: crypto.randomUUID(),
                    target: targetValue(scope.control.value as ExpeditionEffectScope, target.control.value),
                    activityAssignmentIds: csv(assignments.control.value),
                    resourceGains: [change],
                    provenance: dmProvenance("foraging-resolution")
                });
                this.apply(result.state);
            });
        });
        container.append(this.muted(`Resolution ${policy.resolutionModel ?? "manual"}; cost ${policy.timeCost ?? "—"} ${policy.timeUnit ?? "units"}; travel tradeoff ${policy.movementTradeoff ?? "—"}. No yield table or check formula is inferred.`), form);
        return container;
    }

    private campingView(state: SurvivalResources): HTMLElement {
        const container = document.createElement("div");
        const policy = state.campingPolicy;
        container.append(this.policySummary("Camping policy", policy));
        if (state.camp) {
            container.append(this.muted(`Latest camp: ${state.camp.established ? "established" : "not established"}; rest trigger ${state.camp.restTriggerKey ?? "none"}; safe ${state.camp.restSafe === null ? "unresolved" : String(state.camp.restSafe)}; prolonged ${state.camp.restProlonged === null ? "unresolved" : String(state.camp.restProlonged)}.`));
        }
        if (policy.support === "Supported") container.append(this.campForm(policy));
        if (state.camp?.restTriggerKey) container.append(this.recoveryForm(state.camp.restTriggerKey));
        return container;
    }

    private campForm(policy: CampingPolicy): HTMLElement {
        const form = document.createElement("form");
        form.className = "hc-form hc-form-grid";
        const established = this.checkbox("Camp established", true);
        const assignments = this.input("Activity assignment IDs, comma-separated", "text");
        const trigger = this.input("Explicit rest trigger, optional", "text");
        const safe = this.select("Safe-rest resolution", ["unresolved", "true", "false"]);
        const prolonged = this.select("Prolonged-rest resolution", ["unresolved", "true", "false"]);
        const submit = this.button("Record camp resolution");
        form.append(established.wrapper, assignments.wrapper, trigger.wrapper, safe.wrapper, prolonged.wrapper, submit);
        form.addEventListener("submit", event => {
            event.preventDefault();
            void this.mutate(submit, async () => {
                const result = await this.api.resolveCamp(this.expeditionId, {
                    expectedVersion: this.requireState().expeditionVersion,
                    resolutionId: crypto.randomUUID(),
                    established: established.control.checked,
                    activityAssignmentIds: csv(assignments.control.value),
                    restTriggerKey: nullable(trigger.control.value),
                    restSafe: triState(safe.control.value),
                    restProlonged: triState(prolonged.control.value),
                    provenance: dmProvenance("camp-resolution")
                });
                this.apply(result.state);
            });
        });
        const wrapper = document.createElement("div");
        wrapper.append(this.muted(`Resolution ${policy.resolutionModel ?? "manual"}; cost ${policy.timeCost ?? "—"} ${policy.timeUnit ?? "units"}; watch model ${policy.watchModel ?? "—"}. Establishing camp does not imply safe rest.`), form);
        return wrapper;
    }

    private recoveryForm(triggerKey: string): HTMLElement {
        const wrapper = document.createElement("div");
        wrapper.append(this.heading("Explicit Phase 10 recovery bridge"));
        const form = document.createElement("form");
        form.className = "hc-form hc-form-grid";
        const effectId = this.input("Persistent effect ID", "text");
        const reduction = this.input("Resolved level reduction", "number");
        const clear = this.checkbox("Clear effect", false);
        const submit = this.button("Apply resolved recovery");
        form.append(effectId.wrapper, reduction.wrapper, clear.wrapper, submit);
        form.addEventListener("submit", event => {
            event.preventDefault();
            void this.mutate(submit, async () => {
                const result = await this.api.recoverFromRest(this.expeditionId, {
                    expectedVersion: this.requireState().expeditionVersion,
                    triggerKey,
                    effectId: required(effectId.control.value, "Effect ID"),
                    levelReduction: clear.control.checked || !nullable(reduction.control.value)
                        ? null
                        : finiteNumber(reduction.control.value, "Level reduction"),
                    clear: clear.control.checked,
                    provenance: dmProvenance("rest-recovery-resolution")
                });
                this.apply(result.state);
            });
        });
        wrapper.append(form);
        return wrapper;
    }

    private policySummary(label: string, policy: ResourceConsumptionPolicy | ForagingPolicy | CampingPolicy | ForcedTravelPolicy | ExposurePolicy): HTMLElement {
        const paragraph = document.createElement("p");
        paragraph.textContent = `${label}: ${policy.support}${policy.mechanicKey ? ` · ${policy.mechanicKey} v${policy.mechanicVersion ?? "?"}` : ""}${policy.unsupportedReason ? ` · ${policy.unsupportedReason}` : ""}.`;
        return paragraph;
    }

    private section(title: string, content: HTMLElement): HTMLElement {
        const section = document.createElement("section");
        section.className = "hc-stack";
        section.append(this.heading(title), content);
        return section;
    }

    private heading(text: string): HTMLElement {
        const heading = document.createElement("h3");
        heading.textContent = text;
        return heading;
    }

    private muted(text: string): HTMLElement {
        const paragraph = document.createElement("p");
        paragraph.className = "hc-muted";
        paragraph.textContent = text;
        return paragraph;
    }

    private button(text: string): HTMLButtonElement {
        const button = document.createElement("button");
        button.type = "submit";
        button.textContent = text;
        return button;
    }

    private input(label: string, type: string): { wrapper: HTMLLabelElement; control: HTMLInputElement } {
        const wrapper = document.createElement("label");
        wrapper.textContent = label;
        const control = document.createElement("input");
        control.type = type;
        wrapper.append(control);
        return { wrapper, control };
    }

    private select(label: string, values: string[]): { wrapper: HTMLLabelElement; control: HTMLSelectElement } {
        const wrapper = document.createElement("label");
        wrapper.textContent = label;
        const control = document.createElement("select");
        for (const value of values) {
            const option = document.createElement("option");
            option.value = value;
            option.textContent = value;
            control.append(option);
        }
        wrapper.append(control);
        return { wrapper, control };
    }

    private checkbox(label: string, checked: boolean): { wrapper: HTMLLabelElement; control: HTMLInputElement } {
        const wrapper = document.createElement("label");
        const control = document.createElement("input");
        control.type = "checkbox";
        control.checked = checked;
        wrapper.append(control, document.createTextNode(label));
        return { wrapper, control };
    }

    private requireState(): SurvivalResources {
        return this.state ?? (() => { throw new Error("Survival resources are not loaded."); })();
    }

    private apply(state: SurvivalResources): void {
        this.state = state;
        if (!this.disposed) this.render();
    }
}

function dmProvenance(sourceKey: string): ConsequenceProvenanceRequest {
    return { sourceKind: "Dm", sourceKey };
}

function defaultResourceOperation(model: ExpeditionResourceInventoryModel | null): ResourceChangeOperation {
    if (model === "Abstract") return "SetState";
    if (model === "SupplyDie") return "SetSupplyDie";
    return "AdjustQuantity";
}

function buildResourceChange(
    resourceKey: string,
    resourceId: string | null,
    operation: ResourceChangeOperation,
    rawValue: string,
    rawUnit: string): ResourceChangeRequest {
    const base = { resourceKey, resourceId, operation };
    switch (operation) {
        case "AdjustQuantity":
            return { ...base, quantity: finiteNumber(rawValue, "Resolved quantity change"), unit: required(rawUnit, "Resource unit") };
        case "SetQuantity":
            return { ...base, quantity: nonNegativeNumber(rawValue, "Resolved quantity"), unit: required(rawUnit, "Resource unit") };
        case "SetState":
            return { ...base, state: required(rawValue, "Resolved resource state") };
        case "SetSupplyDie":
            return { ...base, supplyDieSides: positiveInteger(rawValue, "Resolved supply die sides") };
        case "Deplete":
            return base;
        default:
            throw new Error("Unsupported resource operation.");
    }
}

function targetValue(scope: ExpeditionEffectScope, rawTarget: string): ExpeditionTarget {
    const requiresTarget = scope === "Participant" || scope === "Mount" || scope === "Vehicle";
    return { scope, targetId: requiresTarget ? required(rawTarget, `${scope} target ID`) : null };
}

function describeTarget(target: ExpeditionTarget): string {
    return target.targetId ? `${target.scope} ${target.targetId}` : target.scope;
}

function describeResourceValue(resource: ExpeditionResource): string {
    if (resource.inventoryModel === "Counted") return `${resource.quantity ?? 0} ${resource.unit ?? ""}`.trim();
    if (resource.inventoryModel === "Abstract") return resource.symbolicState ?? "unresolved";
    if (resource.inventoryModel === "SupplyDie") return resource.supplyDieSides ? `d${resource.supplyDieSides}` : "depleted";
    return "external/manual";
}

function parseResourceValue(model: ExpeditionResourceInventoryModel, raw: string): {
    quantity: number | null;
    symbolicState: string | null;
    supplyDieSides: number | null;
} {
    if (model === "Counted") return { quantity: nonNegativeNumber(raw, "Resource quantity"), symbolicState: null, supplyDieSides: null };
    if (model === "Abstract") return { quantity: null, symbolicState: required(raw, "Resource state"), supplyDieSides: null };
    if (model === "SupplyDie") {
        const text = nullable(raw);
        return { quantity: null, symbolicState: null, supplyDieSides: text === null ? null : positiveInteger(text, "Supply die sides") };
    }
    return { quantity: null, symbolicState: null, supplyDieSides: null };
}

function triState(value: string): boolean | null {
    return value === "true" ? true : value === "false" ? false : null;
}

function csv(value: string): string[] {
    return value.split(",").map(item => item.trim()).filter(Boolean);
}

function humanize(value: string): string {
    return value
        .replace(/[_\.\-]+/g, " ")
        .replace(/([a-z0-9])([A-Z])/g, "$1 $2")
        .replace(/\b\w/g, match => match.toUpperCase());
}

function formatNumber(value: number): string {
    return Number.isInteger(value) ? String(value) : value.toFixed(2).replace(/0+$/, "").replace(/\.$/, "");
}

function required(value: string, label: string): string {
    const trimmed = value.trim();
    if (!trimmed) throw new Error(`${label} is required.`);
    return trimmed;
}

function nullable(value: string): string | null {
    const trimmed = value.trim();
    return trimmed ? trimmed : null;
}

function finiteNumber(value: string, label: string): number {
    const parsed = Number(value);
    if (!Number.isFinite(parsed)) throw new Error(`${label} must be a finite number.`);
    return parsed;
}

function nonNegativeNumber(value: string, label: string): number {
    const parsed = finiteNumber(value, label);
    if (parsed < 0) throw new Error(`${label} can not be negative.`);
    return parsed;
}

function positiveInteger(value: string, label: string): number {
    const parsed = finiteNumber(value, label);
    if (!Number.isInteger(parsed) || parsed < 2) throw new Error(`${label} must be an integer of at least 2.`);
    return parsed;
}
