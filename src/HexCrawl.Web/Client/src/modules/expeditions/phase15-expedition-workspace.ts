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
    let runtimeRefreshTimer: number | null = null;
    let survivalRefreshTimer: number | null = null;
    let journeyRefreshTimer: number | null = null;
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

    const render = (): void => {
        if (disposed) return;
        host.replaceChildren();
        host.append(renderCurrentAction());

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
            contextButton("History", () => focusHistory()));
        if (survival) context.append(contextButton("Resources & effects", () => openDetails("[data-survival-resources-panel]")));
        if (journey && journey.processPolicy.support !== "None") context.append(contextButton("Journey", () => openDetails("[data-journey-panel]")));
        host.append(context);

        if (selectedHex && world) host.append(renderSelectedHex(selectedHex.q, selectedHex.r));
    };

    const renderCurrentAction = (): HTMLElement => {
        const section = document.createElement("section");
        section.className = "hc-current-action";
        const header = document.createElement("header");
        header.append(textElement("h2", "Current action"));
        const action = currentAction();
        header.append(badge(action.status, action.tone));
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

    const currentAction = (): {
        status: string;
        tone: "neutral" | "good" | "warning" | "danger" | "info";
        title: string;
        detail: string;
        buttonLabel: string | null;
        activate: (() => void) | null;
    } => {
        if (runtime.pauseReason === "EncounterTriggered") {
            return {
                status: "Paused",
                tone: "warning",
                title: "An encounter interrupted the expedition.",
                detail: "Review the encounter context, hand off to Block Initiative when ready, and return to the same expedition state afterward.",
                buttonLabel: "Review encounter handoff",
                activate: () => focusEncounterHandoff()
            };
        }
        if (runtime.pauseReason === "LostRecognitionRequired") {
            return {
                status: "Decision required",
                tone: "warning",
                title: "The party may recognize that it is lost.",
                detail: "Resolve recognition and reorientation before continuing travel.",
                buttonLabel: "Resolve navigation decision",
                activate: () => openRunWatch("[data-boundary-resolution]")
            };
        }
        if (runtime.pauseReason === "ConditionsReviewRequired") {
            return {
                status: "Review required",
                tone: "warning",
                title: "Current conditions require DM review.",
                detail: "Review the relevant travel, environment, and consequence state before continuing.",
                buttonLabel: "Review travel controls",
                activate: () => openRunWatch()
            };
        }
        if (runtime.pauseReason === "BacktrackBoundaryReached") {
            return {
                status: "Boundary reached",
                tone: "warning",
                title: "Travel reached a boundary that needs a decision.",
                detail: "Resolve the boundary/backtrack decision before the expedition advances.",
                buttonLabel: "Resolve boundary",
                activate: () => openRunWatch("[data-boundary-resolution]")
            };
        }

        const pendingJourneyEvent = journey?.eventOccurrences.find(event => event.status === "ResolutionRequired") ?? null;
        if (pendingJourneyEvent) {
            return {
                status: "Journey event",
                tone: "warning",
                title: pendingJourneyEvent.eventType
                    ? `${pendingJourneyEvent.eventType} needs resolution.`
                    : "A journey event needs resolution.",
                detail: "Resolve the current event and its consequences from the journey workspace.",
                buttonLabel: "Resolve journey event",
                activate: () => openDetails("[data-journey-panel]")
            };
        }

        const activeJourney = journey?.activeProcesses[0] ?? null;
        if (activeJourney && (!runtime.expedition.isSpatial || activeJourney.status === "ResolutionRequired")) {
            const stage = activeJourney.definition.stages.find(value => value.stageKey === activeJourney.currentStageKey);
            const state = activeJourney.stageStates.find(value => value.stageKey === activeJourney.currentStageKey);
            return {
                status: activeJourney.status === "ResolutionRequired" ? "Resolution required" : "Journey active",
                tone: activeJourney.status === "ResolutionRequired" ? "warning" : "info",
                title: `${activeJourney.definition.displayName}: ${stage?.displayName ?? activeJourney.currentStageKey}`,
                detail: state ? journeyProgress(activeJourney, state.numericProgress, state.explicitState) : "Review the active journey stage.",
                buttonLabel: "Open journey workspace",
                activate: () => openDetails("[data-journey-panel]")
            };
        }

        const state = runtime.expedition;
        if (state.isSpatial) {
            if (state.activeWatchNumber !== null) {
                const remaining = state.activeWatchRemainingHours ?? runtime.remainingWatchHours;
                return {
                    status: "In progress",
                    tone: "info",
                    title: `Continue watch ${state.activeWatchNumber}.`,
                    detail: `${formatHours(remaining)} remain in the current watch. Review only the resolutions that apply to this procedure before advancing.`,
                    buttonLabel: "Continue current watch",
                    activate: () => openRunWatch()
                };
            }
            return {
                status: "Ready",
                tone: "good",
                title: `Plan travel from hex ${state.currentHex.q}, ${state.currentHex.r}.`,
                detail: runtime.procedure.runtime
                    ? "Set the intended course and applicable travel inputs, then run the next watch."
                    : "The stored procedure is structural; use the applicable assisted/manual procedure tools rather than inventing executable travel.",
                buttonLabel: runtime.procedure.runtime ? "Plan next travel watch" : null,
                activate: runtime.procedure.runtime ? () => openRunWatch("[data-plan-fields]") : null
            };
        }

        const intervalAvailable = runtime.procedure.focusedIntervalPolicy.support === "Supported";
        if (intervalAvailable) {
            return {
                status: state.activeWatchNumber === null ? "Ready" : "In progress",
                tone: "good",
                title: state.activeWatchNumber === null
                    ? `Ready for watch ${state.completedWatches + 1}.`
                    : `Continue watch ${state.activeWatchNumber}.`,
                detail: "This procedure provides a real focused interval, so watch/time bookkeeping is available without fabricating spatial travel.",
                buttonLabel: "Open watch / time",
                activate: () => clickIfPresent("[data-watch]")
            };
        }

        if (journey?.processPolicy.support === "Supported") {
            return {
                status: "Journey procedure",
                tone: "info",
                title: "No repeating watch is required by this procedure.",
                detail: activeJourney
                    ? "Continue the active multi-stage journey process."
                    : "Start or manage the journey process directly; no map or interval bookkeeping is fabricated.",
                buttonLabel: "Open journey workspace",
                activate: () => openDetails("[data-journey-panel]")
            };
        }

        return {
            status: "Procedure-led",
            tone: "neutral",
            title: "No repeating watch or spatial action is currently required.",
            detail: "Use only the party, resource, environment, or custom procedure surfaces that apply to this expedition.",
            buttonLabel: null,
            activate: null
        };
    };

    const runtimeStats = (): HTMLElement[] => {
        const state = runtime.expedition;
        const stats: HTMLElement[] = [
            statAction(
                "Time",
                `Day ${state.currentDay}`,
                `${formatHours(state.elapsedTravelHours)} elapsed`,
                focusHistory)
        ];

        if (state.isSpatial) {
            stats.push(
                statAction(
                    "Travel",
                    formatDistance(state.distanceTraveled),
                    state.activeWatchNumber === null ? "Ready for next watch" : `${formatHours(state.activeWatchRemainingHours ?? runtime.remainingWatchHours)} remaining`,
                    () => openRunWatch("[data-travel-resolution]"),
                    state.activeWatchNumber === null ? "neutral" : "info"),
                statAction(
                    "Navigation",
                    navigationValue(state),
                    state.intendedDirection === null ? "No current course" : `Intended ${directionLabel(state.intendedDirection)}`,
                    () => openRunWatch("[data-navigation-resolution]"),
                    state.isLost ? "warning" : "good"),
                statAction(
                    "Current hex",
                    `${state.currentHex.q}, ${state.currentHex.r}`,
                    state.actualDirection === null ? "No active course" : `Actual ${directionLabel(state.actualDirection)}`,
                    showCurrentContext)
            );
        } else if (runtime.procedure.focusedIntervalPolicy.support === "Supported") {
            stats.push(statAction(
                "Watch",
                state.activeWatchNumber === null ? `Ready for ${state.completedWatches + 1}` : String(state.activeWatchNumber),
                state.activeWatchNumber === null ? "No active watch" : `${formatHours(state.activeWatchRemainingHours ?? runtime.remainingWatchHours)} remaining`,
                () => clickIfPresent("[data-watch]")));
        }

        stats.push(
            statAction(
                "Party",
                `${runtime.party.members.length} member${runtime.party.members.length === 1 ? "" : "s"}`,
                activeAssignmentSummary(runtime),
                showPartyContext),
            statAction(
                "Movement",
                movementValue(runtime),
                movementDetail(runtime),
                () => openPartyEditor(),
                runtime.movementComposition.status === "Resolved" ? "good" : runtime.movementComposition.status === "InputRequired" ? "warning" : "neutral")
        );

        if (survival) {
            stats.push(
                statAction(
                    "Resources",
                    resourceValue(survival),
                    resourceDetail(survival),
                    () => openDetails("[data-survival-resources-panel]"),
                    survival.resources.some(resource => resource.isDepleted) || survival.pendingResourceConsequences.length > 0 ? "warning" : "neutral"),
                statAction(
                    "Forced travel",
                    forcedTravelValue(survival),
                    forcedTravelDetail(survival),
                    () => openDetails("[data-survival-resources-panel]"),
                    survival.forcedTravel.checkDue ? "warning" : "neutral")
            );
        }

        if (journey && journey.processPolicy.support !== "None") {
            const active = journey.activeProcesses[0] ?? null;
            stats.push(statAction(
                "Journey",
                active ? active.definition.displayName : journey.processPolicy.support === "Supported" ? "Ready" : "Unavailable",
                active ? journeyStageLabel(active) : "No active process",
                () => openDetails("[data-journey-panel]"),
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
        const spatial = runtime.expedition.isSpatial ? runtime.expedition : null;
        if (spatial && spatial.currentHex.q === q && spatial.currentHex.r === r) {
            card.append(textElement("p", "The party is currently in this hex."));
        }
        const locations = locationsInHex(world!, q, r);
        if (locations.length > 0) {
            const list = document.createElement("ul");
            for (const location of locations) {
                const item = document.createElement("li");
                item.textContent = `${location.name} · ${location.category}`;
                list.append(item);
            }
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
            const members = runtime.party.members;
            if (members.length === 0) body.append(textElement("p", "No party members are recorded.", "hc-phase15-empty"));
            else {
                const list = document.createElement("ul");
                for (const member of members) {
                    const assignments = runtime.party.activityAssignments.filter(value => value.participantId === member.id);
                    const item = document.createElement("li");
                    const details = assignments
                        .map(value => value.roleKey ?? value.activityKey)
                        .filter((value): value is string => Boolean(value));
                    item.textContent = `${member.name}${details.length > 0 ? ` · ${details.join(", ")}` : ""}`;
                    list.append(item);
                }
                body.append(list);
            }
            if (runtime.party.baseMovement) {
                body.append(textElement("p", `Movement reference: ${movementReference(runtime)}.`));
            }
            const edit = document.createElement("button");
            edit.type = "button";
            edit.textContent = "Manage party";
            edit.addEventListener("click", () => {
                drawer?.close();
                openPartyEditor();
            });
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
                const currentLocations = world ? locationsInHex(world, state.currentHex.q, state.currentHex.r) : [];
                if (currentLocations.length > 0) {
                    body.append(textElement("h3", "Locations here"));
                    const list = document.createElement("ul");
                    for (const location of currentLocations) list.append(textElement("li", `${location.name} · ${location.category}`));
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

    const refreshRuntime = (): void => {
        if (runtimeRefreshTimer !== null) window.clearTimeout(runtimeRefreshTimer);
        runtimeRefreshTimer = window.setTimeout(() => {
            runtimeRefreshTimer = null;
            void api.getExpedition(expeditionId).then(next => {
                if (disposed) return;
                runtime = next;
                if (runtime.overworldId && !world) {
                    void api.getOverworld(runtime.overworldId).then(value => {
                        if (!disposed) {
                            world = value;
                            render();
                        }
                    }).catch(() => {});
                }
                render();
            }).catch(() => {});
        }, 50);
    };

    const refreshSurvival = (): void => {
        if (survivalRefreshTimer !== null) window.clearTimeout(survivalRefreshTimer);
        survivalRefreshTimer = window.setTimeout(() => {
            survivalRefreshTimer = null;
            void survivalApi.get(expeditionId).then(next => {
                if (!disposed) {
                    survival = next;
                    render();
                }
            }).catch(() => {});
        }, 50);
    };

    const refreshJourney = (): void => {
        if (journeyRefreshTimer !== null) window.clearTimeout(journeyRefreshTimer);
        journeyRefreshTimer = window.setTimeout(() => {
            journeyRefreshTimer = null;
            void journeyApi.get(expeditionId).then(next => {
                if (!disposed) {
                    journey = next;
                    render();
                }
            }).catch(() => {});
        }, 50);
    };

    const runtimeObserver = observe(root.querySelector("[data-status]"), refreshRuntime);
    const survivalObserver = observe(root.querySelector("[data-survival-resources-panel]"), refreshSurvival);
    const journeyObserver = observe(root.querySelector("[data-journey-panel]"), refreshJourney);
    const mapStatus = root.querySelector<HTMLElement>("[data-map] [role='status']");
    const mapObserver = mapStatus ? new MutationObserver(() => {
        const match = mapStatus.textContent?.match(/Selected hex q (-?\d+), r (-?\d+)/i);
        if (match) {
            selectedHex = { q: Number(match[1]), r: Number(match[2]) };
            render();
        } else if (/cleared/i.test(mapStatus.textContent ?? "")) {
            selectedHex = null;
            render();
        }
    }) : null;
    mapObserver?.observe(mapStatus!, { childList: true, subtree: true, characterData: true });

    render();

    return () => {
        disposed = true;
        if (runtimeRefreshTimer !== null) window.clearTimeout(runtimeRefreshTimer);
        if (survivalRefreshTimer !== null) window.clearTimeout(survivalRefreshTimer);
        if (journeyRefreshTimer !== null) window.clearTimeout(journeyRefreshTimer);
        runtimeObserver?.disconnect();
        survivalObserver?.disconnect();
        journeyObserver?.disconnect();
        mapObserver?.disconnect();
        drawer?.close();
        host.remove();
        page.classList.remove("hc-phase15-expedition", "hc-phase15");
    };
}

function observe(target: Node | null, action: () => void): MutationObserver | null {
    if (!target) return null;
    const observer = new MutationObserver(action);
    observer.observe(target, { childList: true, subtree: true, characterData: true });
    return observer;
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
    if (state.activeWatchNumber !== null) return "Oriented · watch active";
    return "Oriented";
}

function movementValue(runtime: ExpeditionDetail): string {
    const movement = runtime.movementComposition;
    if (movement.effectiveValue !== null) {
        return `${movement.effectiveValue}${movement.effectiveUnit ? ` ${movement.effectiveUnit}` : ""}${movement.effectivePerUnit ? `/${movement.effectivePerUnit}` : ""}`;
    }
    if (runtime.party.baseMovement?.perWatch) return formatDistance(runtime.party.baseMovement.perWatch);
    if (runtime.party.baseMovement?.perHour) return `${formatDistance(runtime.party.baseMovement.perHour)}/hour`;
    return humanMovementStatus(movement.status);
}

function movementDetail(runtime: ExpeditionDetail): string {
    const movement = runtime.movementComposition;
    if (movement.limitingParticipantId) {
        const member = runtime.party.members.find(value => value.id === movement.limitingParticipantId);
        if (member) return `Limited by ${member.name}`;
    }
    if (movement.missingInputs.length > 0) return `${movement.missingInputs.length} input${movement.missingInputs.length === 1 ? "" : "s"} needed`;
    return movement.referenceUse === "Fallback" ? "Using party movement reference" : humanMovementStatus(movement.status);
}

function humanMovementStatus(value: string): string {
    return value.replace(/([a-z])([A-Z])/g, "$1 $2");
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
    if (assignments.length === 0) return runtime.party.activityAssignments.length > 0
        ? `${runtime.party.activityAssignments.length} standing assignment${runtime.party.activityAssignments.length === 1 ? "" : "s"}`
        : "No active role/activity assignments";
    return `${assignments.length} active assignment${assignments.length === 1 ? "" : "s"}`;
}

function resourceValue(state: SurvivalResources): string {
    if (state.resources.length === 0) return "None recorded";
    const depleted = state.resources.filter(value => value.isDepleted).length;
    return depleted > 0
        ? `${depleted} depleted`
        : `${state.resources.length} tracked`;
}

function resourceDetail(state: SurvivalResources): string {
    const counted = state.resources
        .filter(value => value.inventoryModel === "Counted" && value.quantity !== null)
        .slice(0, 2)
        .map(value => `${friendlyKey(value.resourceKey)} ${value.quantity}${value.unit ? ` ${value.unit}` : ""}`);
    if (state.pendingResourceConsequences.length > 0) {
        return `${state.pendingResourceConsequences.length} consequence${state.pendingResourceConsequences.length === 1 ? "" : "s"} pending`;
    }
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
    return value.replace(/[._-]+/g, " ").replace(/\b\w/g, match => match.toUpperCase());
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
    const target = focusSelector
        ? details.querySelector<HTMLElement>(focusSelector)
        : details.querySelector<HTMLElement>("input, select, button, textarea");
    const focus = target?.matches("input,select,button,textarea")
        ? target
        : target?.querySelector<HTMLElement>("input, select, button, textarea");
    focus?.focus();
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
    if (!host) return;
    host.scrollIntoView({ block: "center" });
    host.querySelector<HTMLButtonElement>("button")?.focus();
}

function clickIfPresent(selector: string): void {
    document.querySelector<HTMLButtonElement>(`.hex-crawl-app ${selector}`)?.click();
}
