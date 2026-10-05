import type { HexCrawlApi } from "../../api";
import { worldToHex } from "../../hex-math";
import type { JourneyApi } from "../../journey-api";
import type { ExpeditionJourneyState, JourneyProcessInstance } from "../../journey-types";
import { directionLabel, formatDistance, formatHours } from "../../runtime-view";
import type { SurvivalResourcesApi } from "../../survival-api";
import type { SurvivalResources } from "../../survival-types";
import type { ExpeditionDetail, Overworld, SpatialRuntimeExpedition } from "../../types";
import { ensurePhase15Styles } from "../../phase15-styles";
import { badge, openWorkspaceDrawer, statAction, textElement } from "../../ui/workspace";

export async function enhanceExpeditionWorkspace(
    root: HTMLElement,
    api: HexCrawlApi,
    survivalApi: SurvivalResourcesApi,
    journeyApi: JourneyApi,
    expeditionId: string): Promise<() => void> {
    ensurePhase15Styles();
    let disposed = false;
    let runtime = await api.getExpedition(expeditionId);
    const [initialSurvival, initialJourney, initialWorld] = await Promise.allSettled([
        survivalApi.get(expeditionId),
        journeyApi.get(expeditionId),
        runtime.overworldId ? api.getOverworld(runtime.overworldId) : Promise.resolve(null)
    ]);
    let survival = initialSurvival.status === "fulfilled" ? initialSurvival.value : null;
    let journey = initialJourney.status === "fulfilled" ? initialJourney.value : null;
    let world: Overworld | null = initialWorld.status === "fulfilled" ? initialWorld.value : null;
    let selectedHex: { q: number; r: number } | null = null;
    let supportingRefresh = 0;
    let drawer: { close: () => void } | null = null;

    const page = root.querySelector<HTMLElement>(".hc-page");
    if (!page) return () => {};
    page.classList.add("hc-phase15-expedition", "hc-phase15");

    const host = document.createElement("section");
    host.className = "hc-panel hc-phase15-runtime-summary";
    host.dataset.phase15Runtime = "";
    const grid = root.querySelector<HTMLElement>(".hc-workspace-grid, .hc-tracker-grid, .hc-columns");
    if (grid) page.insertBefore(host, grid);
    else page.append(host);

    const refreshSupportingState = async (): Promise<void> => {
        const token = ++supportingRefresh;
        const [nextSurvival, nextJourney] = await Promise.allSettled([
            survivalApi.get(expeditionId),
            journeyApi.get(expeditionId)
        ]);
        if (disposed || token !== supportingRefresh) return;
        if (nextSurvival.status === "fulfilled") survival = nextSurvival.value;
        if (nextJourney.status === "fulfilled") journey = nextJourney.value;
        render();
    };

    const acceptRuntime = (next: ExpeditionDetail): void => {
        if (disposed || next.id !== expeditionId) return;
        const changed = next.version !== runtime.version;
        runtime = next;
        render();
        if (changed) void refreshSupportingState();
    };

    const restoreApiHooks = installRuntimeHooks(api, expeditionId, acceptRuntime);

    const render = (): void => {
        if (disposed) return;
        host.replaceChildren(renderCurrentAction());
        const stats = document.createElement("div");
        stats.className = "hc-stat-action-grid";
        stats.setAttribute("aria-label", "Expedition current state");
        stats.append(...runtimeStats());
        host.append(stats);

        const context = document.createElement("div");
        context.className = "hc-phase15-context-strip";
        context.append(
            contextButton("Party", showPartyContext),
            contextButton(runtime.expedition.isSpatial ? "Current hex" : "Current context", showCurrentContext),
            contextButton("History", focusHistory));
        if (survival) context.append(contextButton("Resources & effects", () => openDetails("[data-survival-resources-panel]")));
        if (journey && journey.processPolicy.support !== "None") context.append(contextButton("Journey", () => openDetails("[data-journey-panel]")));
        host.append(context);

        if (selectedHex && world) host.append(renderSelectedHex(selectedHex.q, selectedHex.r));
    };

    const renderCurrentAction = (): HTMLElement => {
        const section = document.createElement("section");
        section.className = "hc-current-action";
        const action = currentAction();
        const header = document.createElement("header");
        header.append(textElement("h2", "Current action"), badge(action.status, action.tone));
        section.append(
            header,
            textElement("p", action.title, "hc-current-action-primary"),
            textElement("p", action.detail, "hc-current-action-detail"));
        if (action.buttonLabel && action.activate) {
            const button = document.createElement("button");
            button.type = "button";
            button.className = "hc-primary-action";
            button.textContent = action.buttonLabel;
            button.addEventListener("click", action.activate);
            section.append(button);
        }
        return section;
    };

    const currentAction = (): CurrentAction => {
        if (runtime.pauseReason === "EncounterTriggered") {
            return action("Paused", "warning", "An encounter interrupted the expedition.",
                "Review the encounter context, hand off to Block Initiative when ready, and return to the same expedition state afterward.",
                "Review encounter handoff", focusEncounterHandoff);
        }
        if (runtime.pauseReason === "LostRecognitionRequired") {
            return action("Decision required", "warning", "The party may recognize that it is lost.",
                "Resolve recognition and reorientation before continuing travel.",
                "Resolve navigation decision", () => openRunWatch("[data-boundary-resolution]"));
        }
        if (runtime.pauseReason === "ConditionsReviewRequired") {
            return action("Review required", "warning", "Current conditions require DM review.",
                "Review the relevant travel, environment, and consequence state before continuing.",
                "Review travel controls", () => openRunWatch());
        }
        if (runtime.pauseReason === "BacktrackBoundaryReached") {
            return action("Boundary reached", "warning", "Travel reached a boundary that needs a decision.",
                "Resolve the boundary/backtrack decision before the expedition advances.",
                "Resolve boundary", () => openRunWatch("[data-boundary-resolution]"));
        }

        const pendingEvent = journey?.eventOccurrences.find(event => event.status === "ResolutionRequired") ?? null;
        if (pendingEvent) {
            return action("Journey event", "warning",
                pendingEvent.eventType ? `${pendingEvent.eventType} needs resolution.` : "A journey event needs resolution.",
                "Resolve the current event and its consequences from the journey workspace.",
                "Resolve journey event", () => openDetails("[data-journey-panel]"));
        }

        const activeJourney = journey?.activeProcesses[0] ?? null;
        if (activeJourney && (!runtime.expedition.isSpatial || activeJourney.status === "ResolutionRequired")) {
            const stage = activeJourney.definition.stages.find(value => value.stageKey === activeJourney.currentStageKey);
            const state = activeJourney.stageStates.find(value => value.stageKey === activeJourney.currentStageKey);
            return action(
                activeJourney.status === "ResolutionRequired" ? "Resolution required" : "Journey active",
                activeJourney.status === "ResolutionRequired" ? "warning" : "info",
                `${activeJourney.definition.displayName}: ${stage?.displayName ?? activeJourney.currentStageKey}`,
                state ? journeyProgress(activeJourney, state.numericProgress, state.explicitState) : "Review the active journey stage.",
                "Open journey workspace", () => openDetails("[data-journey-panel]"));
        }

        const state = runtime.expedition;
        if (state.isSpatial) {
            if (state.activeWatchNumber !== null) {
                return action("In progress", "info", `Continue watch ${state.activeWatchNumber}.`,
                    `${formatHours(state.activeWatchRemainingHours ?? runtime.remainingWatchHours)} remain in the current watch. Review only the resolutions that apply to this procedure before advancing.`,
                    "Continue current watch", () => openRunWatch());
            }
            return action("Ready", "good", `Plan travel from hex ${state.currentHex.q}, ${state.currentHex.r}.`,
                runtime.procedure.runtime
                    ? "Set the intended course and applicable travel inputs, then run the next watch."
                    : "The stored procedure is structural; use the applicable assisted/manual tools rather than inventing executable travel.",
                runtime.procedure.runtime ? "Plan next travel watch" : null,
                runtime.procedure.runtime ? () => openRunWatch("[data-plan-fields]") : null);
        }

        if (runtime.procedure.focusedIntervalPolicy.support === "Supported") {
            return action(state.activeWatchNumber === null ? "Ready" : "In progress", "good",
                state.activeWatchNumber === null ? `Ready for watch ${state.completedWatches + 1}.` : `Continue watch ${state.activeWatchNumber}.`,
                "This procedure provides a real focused interval, so watch/time bookkeeping is available without fabricating spatial travel.",
                "Open watch / time", () => clickIfPresent("[data-watch]"));
        }

        if (journey?.processPolicy.support === "Supported") {
            return action("Journey procedure", "info", "No repeating watch is required by this procedure.",
                activeJourney ? "Continue the active multi-stage journey process." : "Start or manage the journey process directly; no map or interval bookkeeping is fabricated.",
                "Open journey workspace", () => openDetails("[data-journey-panel]"));
        }

        return action("Procedure-led", "neutral", "No repeating watch or spatial action is currently required.",
            "Use only the party, resource, environment, or custom procedure surfaces that apply to this expedition.", null, null);
    };

    const runtimeStats = (): HTMLElement[] => {
        const state = runtime.expedition;
        const stats: HTMLElement[] = [
            statAction("Time", `Day ${state.currentDay}`, `${formatHours(state.elapsedTravelHours)} elapsed`, focusHistory)
        ];
        if (state.isSpatial) {
            stats.push(
                statAction("Travel", formatDistance(state.distanceTraveled),
                    state.activeWatchNumber === null ? "Ready for next watch" : `${formatHours(state.activeWatchRemainingHours ?? runtime.remainingWatchHours)} remaining`,
                    () => openRunWatch("[data-travel-resolution]"), state.activeWatchNumber === null ? "neutral" : "info"),
                statAction("Navigation", navigationValue(state),
                    state.intendedDirection === null ? "No current course" : `Intended ${directionLabel(state.intendedDirection)}`,
                    () => openRunWatch("[data-navigation-resolution]"), state.isLost ? "warning" : "good"),
                statAction("Current hex", `${state.currentHex.q}, ${state.currentHex.r}`,
                    state.actualDirection === null ? "No active course" : `Actual ${directionLabel(state.actualDirection)}`, showCurrentContext));
        } else if (runtime.procedure.focusedIntervalPolicy.support === "Supported") {
            stats.push(statAction("Watch",
                state.activeWatchNumber === null ? `Ready for ${state.completedWatches + 1}` : String(state.activeWatchNumber),
                state.activeWatchNumber === null ? "No active watch" : `${formatHours(state.activeWatchRemainingHours ?? runtime.remainingWatchHours)} remaining`,
                () => clickIfPresent("[data-watch]")));
        }

        stats.push(
            statAction("Party", `${runtime.party.members.length} member${runtime.party.members.length === 1 ? "" : "s"}`,
                activeAssignmentSummary(runtime), showPartyContext),
            statAction("Movement", movementValue(runtime), movementDetail(runtime), openPartyEditor,
                runtime.movementComposition.status === "Resolved" ? "good" : runtime.movementComposition.status === "InputRequired" ? "warning" : "neutral"));

        if (survival) {
            stats.push(
                statAction("Resources", resourceValue(survival), resourceDetail(survival), () => openDetails("[data-survival-resources-panel]"),
                    survival.resources.some(value => value.isDepleted) || survival.pendingResourceConsequences.length > 0 ? "warning" : "neutral"),
                statAction("Forced travel", forcedTravelValue(survival), forcedTravelDetail(survival), () => openDetails("[data-survival-resources-panel]"),
                    survival.forcedTravel.checkDue ? "warning" : "neutral"));
        }

        if (journey && journey.processPolicy.support !== "None") {
            const active = journey.activeProcesses[0] ?? null;
            stats.push(statAction("Journey",
                active ? active.definition.displayName : journey.processPolicy.support === "Supported" ? "Ready" : "Unavailable",
                active ? journeyStageLabel(active) : "No active process", () => openDetails("[data-journey-panel]"),
                active?.status === "ResolutionRequired" ? "warning" : "neutral"));
        }
        return stats;
    };

    const renderSelectedHex = (q: number, r: number): HTMLElement => {
        const card = document.createElement("section");
        card.className = "hc-context-card";
        const heading = document.createElement("div");
        heading.className = "hc-area-card-heading";
        heading.append(textElement("h3", `Selected hex ${q}, ${r}`), badge("Map context", "info"));
        card.append(heading);
        const state = runtime.expedition;
        if (state.isSpatial && state.currentHex.q === q && state.currentHex.r === r) card.append(textElement("p", "The party is currently in this hex."));
        const locations = locationsInHex(world!, q, r);
        if (locations.length > 0) {
            const list = document.createElement("ul");
            for (const location of locations) list.append(textElement("li", `${location.name} · ${location.category}`));
            card.append(textElement("strong", "Locations"), list);
        } else {
            card.append(textElement("p", "No authored locations are recorded in this selected hex.", "hc-muted"));
        }
        card.append(textElement("p", "Selecting map context does not mutate expedition state. Durable travel changes still require an explicit action.", "hc-muted"));
        return card;
    };

    const showPartyContext = (): void => {
        drawer?.close();
        drawer = openWorkspaceDrawer(root, "Party", body => {
            if (runtime.party.members.length === 0) body.append(textElement("p", "No party members are recorded.", "hc-phase15-empty"));
            else {
                const list = document.createElement("ul");
                for (const member of runtime.party.members) {
                    const roles = runtime.party.activityAssignments
                        .filter(value => value.participantId === member.id)
                        .map(value => value.roleKey ?? value.activityKey)
                        .filter((value): value is string => Boolean(value));
                    list.append(textElement("li", `${member.name}${roles.length > 0 ? ` · ${roles.join(", ")}` : ""}`));
                }
                body.append(list);
            }
            if (runtime.party.baseMovement) body.append(textElement("p", `Movement reference: ${movementReference(runtime)}.`));
            const edit = document.createElement("button");
            edit.type = "button";
            edit.textContent = "Manage party";
            edit.addEventListener("click", () => { drawer?.close(); openPartyEditor(); });
            body.append(edit);
        });
    };

    const showCurrentContext = (): void => {
        drawer?.close();
        drawer = openWorkspaceDrawer(root, runtime.expedition.isSpatial ? "Current travel context" : "Current expedition context", body => {
            const state = runtime.expedition;
            if (state.isSpatial) {
                body.append(
                    contextLine("Current hex", `${state.currentHex.q}, ${state.currentHex.r}`),
                    contextLine("Entry", directionLabel(state.entryDirection)),
                    contextLine("Intended course", directionLabel(state.intendedDirection)),
                    contextLine("Actual course", directionLabel(state.actualDirection)),
                    contextLine("Distance traveled", formatDistance(state.distanceTraveled)),
                    contextLine("Navigation", navigationValue(state)));
                const locations = world ? locationsInHex(world, state.currentHex.q, state.currentHex.r) : [];
                if (locations.length > 0) {
                    body.append(textElement("h3", "Locations here"));
                    const list = document.createElement("ul");
                    for (const location of locations) list.append(textElement("li", `${location.name} · ${location.category}`));
                    body.append(list);
                }
            } else {
                body.append(
                    contextLine("Context", runtime.context.name),
                    contextLine("Day", String(state.currentDay)),
                    contextLine("Elapsed", formatHours(state.elapsedTravelHours)));
            }
        });
    };

    const mapCanvas = root.querySelector<HTMLCanvasElement>("[data-map] canvas");
    const readMapSelection = (): void => {
        if (!mapCanvas || disposed) return;
        const label = mapCanvas.getAttribute("aria-label") ?? "";
        const match = label.match(/Selected hex q (-?\d+), r (-?\d+)/i);
        selectedHex = match ? { q: Number(match[1]), r: Number(match[2]) } : null;
        render();
    };
    const scheduleMapSelectionRead = (): void => {
        requestAnimationFrame(() => requestAnimationFrame(readMapSelection));
    };
    mapCanvas?.addEventListener("click", scheduleMapSelectionRead);
    mapCanvas?.addEventListener("keydown", scheduleMapSelectionRead);

    render();

    return () => {
        disposed = true;
        supportingRefresh += 1;
        mapCanvas?.removeEventListener("click", scheduleMapSelectionRead);
        mapCanvas?.removeEventListener("keydown", scheduleMapSelectionRead);
        restoreApiHooks();
        drawer?.close();
        host.remove();
        page.classList.remove("hc-phase15-expedition", "hc-phase15");
    };
}

type CurrentAction = {
    status: string;
    tone: "neutral" | "good" | "warning" | "danger" | "info";
    title: string;
    detail: string;
    buttonLabel: string | null;
    activate: (() => void) | null;
};

function action(
    status: string,
    tone: CurrentAction["tone"],
    title: string,
    detail: string,
    buttonLabel: string | null,
    activate: (() => void) | null): CurrentAction {
    return { status, tone, title, detail, buttonLabel, activate };
}

function installRuntimeHooks(
    api: HexCrawlApi,
    expeditionId: string,
    accept: (runtime: ExpeditionDetail) => void): () => void {
    const getExpedition = api.getExpedition.bind(api);
    const advanceExpedition = api.advanceExpedition.bind(api);
    const updateExpeditionParty = api.updateExpeditionParty.bind(api);
    const discover = api.discover.bind(api);
    const travelAssistant = api.recordTravelAssistant.bind(api);
    const watchAssistant = api.recordWatchAssistant.bind(api);
    const navigationAssistant = api.recordNavigationAssistant.bind(api);
    const encounterAssistant = api.recordEncounterAssistant.bind(api);

    api.getExpedition = async id => {
        const result = await getExpedition(id);
        if (id === expeditionId) accept(result);
        return result;
    };
    api.advanceExpedition = async (id, input) => {
        const result = await advanceExpedition(id, input);
        if (id === expeditionId) accept(result);
        return result;
    };
    api.updateExpeditionParty = async (id, input) => {
        const result = await updateExpeditionParty(id, input);
        if (id === expeditionId) accept(result);
        return result;
    };
    api.discover = async (id, expectedVersion, subjectId, subjectType) => {
        const result = await discover(id, expectedVersion, subjectId, subjectType);
        if (id === expeditionId) accept(result);
        return result;
    };
    api.recordTravelAssistant = async (id, input) => {
        const result = await travelAssistant(id, input);
        if (id === expeditionId) accept(result);
        return result;
    };
    api.recordWatchAssistant = async (id, input) => {
        const result = await watchAssistant(id, input);
        if (id === expeditionId) accept(result);
        return result;
    };
    api.recordNavigationAssistant = async (id, input) => {
        const result = await navigationAssistant(id, input);
        if (id === expeditionId) accept(result);
        return result;
    };
    api.recordEncounterAssistant = async (id, input) => {
        const result = await encounterAssistant(id, input);
        if (id === expeditionId) accept(result);
        return result;
    };

    return () => {
        api.getExpedition = getExpedition;
        api.advanceExpedition = advanceExpedition;
        api.updateExpeditionParty = updateExpeditionParty;
        api.discover = discover;
        api.recordTravelAssistant = travelAssistant;
        api.recordWatchAssistant = watchAssistant;
        api.recordNavigationAssistant = navigationAssistant;
        api.recordEncounterAssistant = encounterAssistant;
    };
}

function contextButton(label: string, action: () => void): HTMLButtonElement {
    const button = document.createElement("button");
    button.type = "button";
    button.className = "hc-context-button";
    button.textContent = label;
    button.addEventListener("click", action);
    return button;
}

function contextLine(label: string, value: string): HTMLElement {
    const row = document.createElement("p");
    const strong = document.createElement("strong");
    strong.textContent = `${label}: `;
    row.append(strong, document.createTextNode(value));
    return row;
}

function navigationValue(state: SpatialRuntimeExpedition): string {
    if (state.isLost) return `Lost · ${state.veerDegrees}° off course`;
    return state.activeWatchNumber !== null ? "Oriented · watch active" : "Oriented";
}

function movementValue(runtime: ExpeditionDetail): string {
    const movement = runtime.movementComposition;
    if (movement.effectiveValue !== null) {
        return `${movement.effectiveValue}${movement.effectiveUnit ? ` ${movement.effectiveUnit}` : ""}${movement.effectivePerUnit ? `/${movement.effectivePerUnit}` : ""}`;
    }
    if (runtime.party.baseMovement?.perWatch) return formatDistance(runtime.party.baseMovement.perWatch);
    if (runtime.party.baseMovement?.perHour) return `${formatDistance(runtime.party.baseMovement.perHour)}/hour`;
    return friendlyKey(movement.status);
}

function movementDetail(runtime: ExpeditionDetail): string {
    const movement = runtime.movementComposition;
    if (movement.limitingParticipantId) {
        const member = runtime.party.members.find(value => value.id === movement.limitingParticipantId);
        if (member) return `Limited by ${member.name}`;
    }
    if (movement.missingInputs.length > 0) return `${movement.missingInputs.length} input${movement.missingInputs.length === 1 ? "" : "s"} needed`;
    return movement.referenceUse === "Fallback" ? "Using party movement reference" : friendlyKey(movement.status);
}

function movementReference(runtime: ExpeditionDetail): string {
    const reference = runtime.party.baseMovement;
    if (!reference) return "none";
    if (reference.perWatch) return `${formatDistance(reference.perWatch)} per watch`;
    if (reference.perHour) return `${formatDistance(reference.perHour)} per hour`;
    if (reference.perMarch) return `${formatDistance(reference.perMarch)} per march`;
    return reference.note ?? "recorded";
}

function activeAssignmentSummary(runtime: ExpeditionDetail): string {
    const assignments = runtime.expedition.activeActivityAssignments;
    if (assignments.length > 0) return `${assignments.length} active assignment${assignments.length === 1 ? "" : "s"}`;
    return runtime.party.activityAssignments.length > 0
        ? `${runtime.party.activityAssignments.length} standing assignment${runtime.party.activityAssignments.length === 1 ? "" : "s"}`
        : "No active role/activity assignments";
}

function resourceValue(state: SurvivalResources): string {
    if (state.resources.length === 0) return "None recorded";
    const depleted = state.resources.filter(value => value.isDepleted).length;
    return depleted > 0 ? `${depleted} depleted` : `${state.resources.length} tracked`;
}

function resourceDetail(state: SurvivalResources): string {
    if (state.pendingResourceConsequences.length > 0) {
        return `${state.pendingResourceConsequences.length} consequence${state.pendingResourceConsequences.length === 1 ? "" : "s"} pending`;
    }
    const counted = state.resources
        .filter(value => value.inventoryModel === "Counted" && value.quantity !== null)
        .slice(0, 2)
        .map(value => `${friendlyKey(value.resourceKey)} ${value.quantity}${value.unit ? ` ${value.unit}` : ""}`);
    return counted.length > 0 ? counted.join(" · ") : "Open for current inventory";
}

function forcedTravelValue(state: SurvivalResources): string {
    if (state.forcedTravelPolicy.support === "None") return "Not used";
    if (state.forcedTravel.checkDue) return "Check due";
    if (state.forcedTravel.normalLimit !== null) {
        return `${state.forcedTravel.amountSinceReset} / ${state.forcedTravel.normalLimit}${state.forcedTravel.unit ? ` ${state.forcedTravel.unit}` : ""}`;
    }
    return state.forcedTravel.forcedTravelBegun ? "Forced travel active" : "Within normal travel";
}

function forcedTravelDetail(state: SurvivalResources): string {
    if (state.forcedTravel.pendingConsequenceId) return "Consequence pending";
    if (state.forcedTravelPolicy.support === "Unsupported") return state.forcedTravelPolicy.unsupportedReason ?? "Manual adjudication required";
    return state.forcedTravel.thresholdReached ? "Normal travel limit reached" : "No forced-travel action due";
}

function journeyStageLabel(process: JourneyProcessInstance): string {
    const stage = process.definition.stages.find(value => value.stageKey === process.currentStageKey);
    return `${stage?.displayName ?? process.currentStageKey} · ${process.status}`;
}

function journeyProgress(process: JourneyProcessInstance, numeric: number | null, explicit: string | null): string {
    if (process.execution.progressKind === "Numeric") {
        const stage = process.definition.stages.find(value => value.stageKey === process.currentStageKey);
        return stage?.progressTarget !== null && stage?.progressTarget !== undefined
            ? `Progress ${numeric ?? 0} / ${stage.progressTarget}${process.execution.progressUnit ? ` ${process.execution.progressUnit}` : ""}`
            : `Progress ${numeric ?? 0}${process.execution.progressUnit ? ` ${process.execution.progressUnit}` : ""}`;
    }
    return explicit ? `State: ${explicit}` : "Review the current stage and available approaches.";
}

function friendlyKey(value: string): string {
    return value.replace(/[._-]+/g, " ").replace(/([a-z])([A-Z])/g, "$1 $2").replace(/\b\w/g, match => match.toUpperCase());
}

function locationsInHex(world: Overworld, q: number, r: number) {
    return world.locations.filter(location => {
        const hex = worldToHex(world.grid, location.position);
        return hex.q === q && hex.r === r;
    });
}

function openRunWatch(focusSelector?: string): void {
    const details = document.querySelector<HTMLDetailsElement>(".hex-crawl-app .hc-sheet-controls");
    if (!details) return;
    details.open = true;
    details.scrollIntoView({ block: "nearest" });
    const target = focusSelector ? details.querySelector<HTMLElement>(focusSelector) : details;
    (target?.matches("input,select,button,textarea") ? target : target?.querySelector<HTMLElement>("input, select, button, textarea"))?.focus();
}

function openPartyEditor(): void {
    const details = document.querySelector<HTMLDetailsElement>(".hex-crawl-app .hc-party-editor-panel");
    if (!details) return;
    details.open = true;
    details.scrollIntoView({ block: "nearest" });
    details.querySelector<HTMLElement>("input, select, button, textarea, summary")?.focus();
}

function openDetails(selector: string): void {
    const details = document.querySelector<HTMLDetailsElement>(`.hex-crawl-app ${selector}`);
    if (!details) return;
    details.open = true;
    details.scrollIntoView({ block: "start" });
    details.querySelector<HTMLElement>("summary")?.focus();
}

function focusHistory(): void {
    const target = document.querySelector<HTMLElement>(".hex-crawl-app [data-history]");
    target?.scrollIntoView({ block: "start" });
    target?.querySelector<HTMLElement>("summary, button, a")?.focus();
}

function focusEncounterHandoff(): void {
    const host = document.querySelector<HTMLElement>(".hex-crawl-app [data-encounter-handoff]");
    host?.scrollIntoView({ block: "center" });
    host?.querySelector<HTMLButtonElement>("button")?.focus();
}

function clickIfPresent(selector: string): void {
    document.querySelector<HTMLButtonElement>(`.hex-crawl-app ${selector}`)?.click();
}
