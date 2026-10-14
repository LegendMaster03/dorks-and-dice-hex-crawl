import type { HexCrawlApi } from "../../api";
import { blockInitiativeHandoffHref } from "../../encounter-handoff";
import { ExpeditionEffectsApi } from "../../effect-api";
import type { ExpeditionEffectState } from "../../effect-types";
import { JourneyApi } from "../../journey-api";
import type { ExpeditionJourneyState } from "../../journey-types";
import { ensurePhase15Styles } from "../../phase15-styles";
import { formatDistance, formatHours } from "../../runtime-view";
import { SurvivalResourcesApi } from "../../survival-api";
import type { SurvivalResources } from "../../survival-types";
import { canonicalExpeditionRoute } from "../../tool-route";
import type { ExpeditionDetail, Overworld, ToolHostContext } from "../../types";
import { clearUiError, showUiError } from "../../ui-error";
import { badge, disclosure, openWorkspaceDrawer, statAction, textElement, type WorkspaceDrawer } from "../../ui/workspace";
import { applyGuidedExperience, guidancePreferenceButton } from "../../ui/guidance";
import { ExpeditionEffectsPanel } from "./effects-panel";
import { ExpeditionEnvironmentPanel } from "./environment-panel";
import { ExpeditionJourneyPanel } from "./journey-panel";
import { ExpeditionPartySheetController } from "./expedition-party-sheet";
import { renderPeriodicExpeditionWorkbench } from "./periodic-expedition-workbench";
import { publishExpeditionRuntimeChanged } from "./expedition-runtime-events";
import { ExpeditionSurvivalResourcesPanel } from "./survival-resources-panel";
import { renderPhase15NextAction } from "./phase15-next-action";
import { canUseFocusedNonSpatialWatch, focusedIntervalHours } from "./focused-interval-policy";
import { movementCompositionLedger } from "./expedition-movement-composition-view";
import { encounterScheduleConfiguration, hasEncounterSchedule } from "./encounter-schedule-presentation";
import { orderedJourneyStageDefinitions } from "./journey-stage-presentation";
import { expeditionWorkspacePresentation } from "./expedition-workspace-model";

export async function renderExpedition(
    root: HTMLElement,
    api: HexCrawlApi,
    expeditionId: string,
    navigate: (route: string, replace?: boolean) => void,
    routeWorldId?: string,
    toolContext: ToolHostContext | null = null): Promise<() => void> {
    ensurePhase15Styles();
    root.classList.add("hc-phase15");
    applyGuidedExperience(root);

    let runtime = await api.getExpedition(expeditionId);
    if (routeWorldId) {
        const canonical = canonicalExpeditionRoute(routeWorldId, runtime);
        if (canonical) {
            navigate(canonical, true);
            return () => { root.classList.remove("hc-phase15"); };
        }
    }

    let world: Overworld | null = runtime.overworldId ? await api.getOverworld(runtime.overworldId) : null;
    // Every spatial expedition must carry an authoritative qualified cell.
    // A malformed or historical spatial snapshot must fail closed; it must
    // never fall through to a superseded axial movement implementation.
    if (runtime.expedition.isSpatial) {
        if (!runtime.expedition.currentCell)
            throw new Error("Spatial expedition is missing its required authoritative cell.");
        return renderPeriodicExpeditionWorkbench(root, api, runtime, world, navigate, toolContext);
    }

    const survivalApi = new SurvivalResourcesApi(toolContext);
    const effectsApi = new ExpeditionEffectsApi(toolContext);
    const journeyApi = new JourneyApi(toolContext);
    let survival = await safeLoad(() => survivalApi.get(expeditionId));
    let effects = await safeLoad(() => effectsApi.get(expeditionId));
    let journey = await safeLoad(() => journeyApi.get(expeditionId));
    let drawer: WorkspaceDrawer | null = null;
    let drawerCleanup: (() => void) | null = null;
    let disposed = false;

    const closeDrawer = (): void => {
        drawer?.close();
        drawer = null;
    };

    const cleanupDrawer = (): void => {
        const cleanup = drawerCleanup;
        drawerCleanup = null;
        cleanup?.();
    };

    const refreshAuxiliary = async (): Promise<void> => {
        [survival, effects, journey] = await Promise.all([
            safeLoad(() => survivalApi.get(expeditionId)),
            safeLoad(() => effectsApi.get(expeditionId)),
            safeLoad(() => journeyApi.get(expeditionId))
        ]);
    };

    const rebaseAuxiliaryVersions = (version: number): void => {
        if (survival) survival = { ...survival, expeditionVersion: version };
        if (journey) journey = { ...journey, expeditionVersion: version };
    };

    const applyRuntime = (next: ExpeditionDetail): void => {
        runtime = next;
        publishExpeditionRuntimeChanged(root, runtime);
    };

    const mutate = async (action: () => Promise<void>): Promise<void> => {
        const error = root.querySelector<HTMLElement>("[data-error]");
        if (error) clearUiError(error);
        try {
            await action();
            const latest = await api.getExpedition(expeditionId);
            applyRuntime(latest);
            if (latest.overworldId !== world?.id) {
                world = latest.overworldId ? await api.getOverworld(latest.overworldId) : null;
            }
            await refreshAuxiliary();
            if (!disposed) render();
        } catch (value) {
            if (!disposed && error) showUiError(error, value);
            throw value;
        }
    };

    const runUiMutation = async (action: () => Promise<void>): Promise<void> => {
        try {
            await mutate(action);
        } catch {
            // mutate already surfaced the failure in the workspace error region.
        }
    };

    const render = (): void => {
        const restoreFocusAfterRender = drawer !== null;
        if (drawer) {
            const activeDrawer = drawer;
            drawer = null;
            activeDrawer.close();
        } else {
            cleanupDrawer();
        }
        const presentation = expeditionWorkspacePresentation(runtime, journey, survival, effects);
        const page = document.createElement("section");
        page.className = "hc-page hc-phase15-expedition";
        const header = document.createElement("header");
        header.className = "hc-page-header";
        const heading = document.createElement("div");
        heading.append(textElement("h1", runtime.name),
            textElement("p", `${runtime.context.name} · ${runtime.procedure.name}`));
        const nav = document.createElement("nav");
        nav.className = "hc-button-row";
        nav.append(button("Hex Crawl home", () => navigate("/")),
            button("Ruleset reference", () =>
                navigate(`/procedures/${encodeURIComponent(runtime.procedure.procedureId)}/revisions/${runtime.procedure.revision}/reference`)),
            guidancePreferenceButton(root));
        header.append(heading, nav);
        page.append(header);
        const error = document.createElement("div");
        error.className = "hc-error";
        error.dataset.error = "";
        error.hidden = true;
        error.setAttribute("role", "alert");
        page.append(error, renderCurrentAction(presentation.action));
        const status = document.createElement("section");
        status.className = "hc-phase15-runtime-summary";
        const stats = document.createElement("div");
        stats.className = "hc-stat-action-grid";
        stats.append(statAction("Time", presentation.timeLabel, travelPeriodDetail(runtime), openHistory),
            statAction("Context", presentation.routeLabel ?? runtime.context.name, "Journey / no grid",
                openHistory, runtime.pauseReason ? "warning" : "neutral"));
        if (runtime.party.members.length > 0 || runtime.party.activityAssignments.length > 0)
            stats.append(statAction("Party",
                `${runtime.party.members.length} member${runtime.party.members.length === 1 ? "" : "s"}`,
                partyActivitySummary(runtime), openPartyWorkspace));
        if (presentation.capabilities.travel)
            stats.append(statAction("Movement", movementSummary(runtime),
                movementStatusDetail(runtime), openPartyWorkspace,
                runtime.movementComposition.missingInputs.length > 0 ? "warning" : "neutral"));
        if (encounterScheduleAvailable(runtime))
            stats.append(statAction("Encounters", encounterSummary(runtime),
                runtime.expedition.pendingEncounter ? "Encounter awaits resolution" : null,
                openEncounterWorkspace, runtime.expedition.pendingEncounter ? "warning" : "neutral"));
        status.append(stats);
        page.append(status, renderNonSpatialWorkspace(), renderGmTools());
        root.replaceChildren(page);
        if (restoreFocusAfterRender) {
            queueMicrotask(() => {
                if (!disposed)
                    root.querySelector<HTMLButtonElement>("[data-current-action-button]")?.focus();
            });
        }
    };

    const currentActionCopy = (
        action: ReturnType<typeof expeditionWorkspacePresentation>["action"]): { label: string; detail: string } => ({
        label: action.label, detail: action.detail
    });

    const guidedActionExplanation = (
        kind: ReturnType<typeof expeditionWorkspacePresentation>["action"]["kind"]): string => {
        switch (kind) {
            case "encounter": return "Resolve or hand off the encounter before continuing.";
            case "survival": return "Resolve pending resource or forced-travel consequences.";
            case "effects": return "Review and record the table adjudication for a pending effect consequence.";
            case "journey": return "Resolve the saved journey stage, event, or required table action.";
            case "watch": return "Record a configured interval without inventing spatial movement.";
            default: return "Consult the saved ruleset and supply required table decisions.";
        }
    };

    const renderGuidedActionGuide = (
        action: ReturnType<typeof expeditionWorkspacePresentation>["action"]): HTMLElement => {
        const guide = document.createElement("details");
        guide.className = "hc-guided-action-guide hc-guided-only";
        const summary = document.createElement("summary");
        summary.textContent = "What this action needs and changes";
        guide.append(summary,
            textElement("p", guidedActionExplanation(action.kind)),
            textElement("p", `Current time: ${expeditionWorkspacePresentation(runtime, journey, survival, effects).timeLabel}`));
        const latest = [...runtime.history].reverse().find(event =>
            !/provenance|diagnostic|resolved input source/i.test(event.message));
        if (latest) guide.append(textElement("p", `Recorded event: ${latest.message}`));
        if (runtime.pauseReason) guide.append(textElement("p", `Current interruption: ${runtime.pauseReason}`));
        return guide;
    };

    const renderCurrentAction = (action: ReturnType<typeof expeditionWorkspacePresentation>["action"]): HTMLElement =>
        renderPhase15NextAction(action, currentActionCopy(action),
            () => activateAction(action.kind), renderGuidedActionGuide(action),
            guidedActionExplanation(action.kind));

    const renderNonSpatialWorkspace = (): HTMLElement => {
        const section = document.createElement("section");
        section.className = "hc-nonspatial-primary";
        const main = document.createElement("article");
        main.className = "hc-panel hc-journey-primary";
        const presentation = expeditionWorkspacePresentation(runtime, journey, survival, effects);
        const activeJourney = journey?.activeProcesses.find(process => !process.isTerminal) ?? null;

        if (journey?.processPolicy.support === "Supported") {
            main.append(textElement("h2", "Journey"));
            if (activeJourney) {
                const stage = activeJourney.definition.stages.find(value => value.stageKey === activeJourney.currentStageKey) ?? null;
                const stageState = activeJourney.stageStates.find(value => value.stageKey === activeJourney.currentStageKey) ?? null;
                const heading = document.createElement("header");
                heading.className = "hc-journey-primary-head";
                heading.append(
                    textElement("h3", activeJourney.definition.displayName),
                    badge(humanize(activeJourney.status), journeyAttention(journey) ? "warning" : "good"));
                if (activeJourney.definition.description) {
                    heading.append(textElement("p", activeJourney.definition.description, "hc-muted"));
                }
                main.append(heading);

                const facts = document.createElement("dl");
                facts.className = "hc-journey-state-grid";
                facts.append(
                    journeyFact("Current stage", stage?.displayName ?? humanize(activeJourney.currentStageKey)),
                    journeyFact("Progress", journeyProgressSummary(activeJourney, stage, stageState)),
                    journeyFact("Roles", journeyRoleSummary(runtime)),
                    journeyFact("Pending", journeyPendingSummary(journey, activeJourney)),
                    journeyFact("Consequences / state", journeyStateSummary(stageState, survival, effects)));
                main.append(facts, journeyStageSequence(activeJourney));

                if (presentation.action.kind !== "journey") {
                    main.append(button("Open journey workspace", openJourneyWorkspace));
                }
            } else {
                main.append(
                    textElement("p", "No journey process is currently active.", "hc-muted"),
                    textElement("p", journeyPendingEventSummary(journey), "hc-muted"));
                if (presentation.action.kind !== "journey") {
                    main.append(button("Start or manage journey", openJourneyWorkspace));
                }
            }
        } else if (canUseFocusedNonSpatialWatch(runtime)) {
            main.append(
                textElement("h2", "Procedure interval"),
                textElement("p", "This procedure advances its configured interval without spatial position, course, pace, hex progress, or map state.", "hc-muted"));
            const control = button(currentActionCopy(presentation.action).label, openNonSpatialWatchWorkspace);
            control.className = "hc-primary-action";
            main.append(control);
        } else {
            main.append(
                textElement("h2", "Procedure state"),
                textElement("p", "This expedition has no executable spatial, journey, or interval action. Use the procedure reference and focused GM tools for manual play.", "hc-muted"));
        }

        const side = document.createElement("aside");
        side.className = "hc-panel hc-sidebar hc-table-rail";
        side.append(
            textElement("h2", "At the table"),
            railAction("Party & roles", journeyRoleSummary(runtime), openPartyWorkspace));
        if (presentation.capabilities.resources || presentation.capabilities.survival || presentation.capabilities.effects) {
            side.append(railAction("Resources & effects", resourcesEffectsDetail(survival, effects), openSurvivalWorkspace));
        }
        if (journey?.eventPolicy.support === "Supported") {
            side.append(railAction("Journey events", journeyEventSummary(journey), openJourneyWorkspace));
        }
        if (encounterScheduleAvailable(runtime)) {
            side.append(railAction("Encounter schedule", encounterScheduleSummary(runtime), openEncounterWorkspace));
        }
        side.append(railAction("Expedition history", presentation.timeLabel, openHistory));
        section.append(main, side);
        return section;
    };

    const journeyFact = (label: string, value: string): HTMLElement => {
        const fragment = document.createDocumentFragment();
        fragment.append(textElement("dt", label), textElement("dd", value));
        const wrapper = document.createElement("div");
        wrapper.className = "hc-journey-fact";
        wrapper.append(fragment);
        return wrapper;
    };

    const journeyProgressSummary = (
        process: ExpeditionJourneyState["activeProcesses"][number],
        stage: ExpeditionJourneyState["activeProcesses"][number]["definition"]["stages"][number] | null,
        state: ExpeditionJourneyState["activeProcesses"][number]["stageStates"][number] | null): string => {
        if (!state) return "Not recorded";
        if (process.execution.progressKind === "Numeric") {
            const current = state.numericProgress ?? 0;
            if (stage?.progressTarget !== null && stage?.progressTarget !== undefined) {
                return `${formatNumber(current)} / ${formatNumber(stage.progressTarget)}${process.execution.progressUnit ? ` ${process.execution.progressUnit}` : ""}`;
            }
            return `${formatNumber(current)}${process.execution.progressUnit ? ` ${process.execution.progressUnit}` : ""}`;
        }
        return state.explicitState ? humanize(state.explicitState) : "Not recorded";
    };

    const journeyRoleSummary = (state: ExpeditionDetail): string => {
        const memberNames = new Map(state.party.members.map(member => [member.id, member.name]));
        const roles = state.party.activityAssignments.filter(assignment => assignment.roleKey);
        if (roles.length === 0) return "No journey roles assigned";
        return roles
            .map(assignment => `${humanize(assignment.roleKey!)} — ${assignment.participantId ? memberNames.get(assignment.participantId) ?? "Unknown participant" : "Party"}`)
            .join(" · ");
    };

    const journeyPendingSummary = (
        state: ExpeditionJourneyState,
        process: ExpeditionJourneyState["activeProcesses"][number]): string => {
        const parts = process.pendingActions.map(action =>
            action.detail?.trim() || humanize(action.kind));
        const events = state.eventOccurrences.filter(event =>
            event.status === "ResolutionRequired"
            && (event.processId === null || event.processId === process.id));
        parts.push(...events.map(event =>
            event.eventType ?? event.eventKey ?? "Journey event requires resolution"));
        return parts.length > 0 ? parts.join(" · ") : "No unresolved journey action";
    };

    const journeyPendingEventSummary = (state: ExpeditionJourneyState): string => {
        const pending = state.eventOccurrences.filter(event => event.status === "ResolutionRequired");
        if (pending.length === 0) return "No unresolved journey event.";
        return pending
            .map(event => event.eventType ?? event.eventKey ?? "Journey event requires resolution")
            .join(" · ");
    };

    const journeyEventSummary = (state: ExpeditionJourneyState): string => {
        const pending = state.eventOccurrences.filter(event => event.status === "ResolutionRequired");
        if (pending.length > 0) {
            return `${pending.length} event${pending.length === 1 ? "" : "s"} require resolution`;
        }
        const resolved = state.eventOccurrences.filter(event => event.status === "Resolved").length;
        return resolved > 0
            ? `${resolved} resolved event${resolved === 1 ? "" : "s"}`
            : "No recorded journey events";
    };

    const journeyStateSummary = (
        state: ExpeditionJourneyState["activeProcesses"][number]["stageStates"][number] | null,
        resources: SurvivalResources | null,
        effectState: ExpeditionEffectState | null): string => {
        const parts: string[] = [];
        if (effectState === null) {
            parts.push("Consequence state unavailable");
        } else if (effectState.pendingConsequences.length > 0) {
            const count = effectState.pendingConsequences.length;
            parts.push(`${count} consequence${count === 1 ? "" : "s"} need${count === 1 ? "s" : ""} resolution`);
        }
        if (state && (state.failures > 0 || state.complications > 0)) {
            const stage: string[] = [];
            if (state.failures > 0) stage.push(`${state.failures} failure${state.failures === 1 ? "" : "s"}`);
            if (state.complications > 0) stage.push(`${state.complications} complication${state.complications === 1 ? "" : "s"}`);
            parts.push(`Stage: ${stage.join(", ")}`);
        }
        if (resources && survivalAttention(resources)) parts.push(survivalDetail(resources));
        return parts.length > 0 ? parts.join(" · ") : "No pending consequences";
    };

    const renderGmTools = (): HTMLElement => {
        const presentation = expeditionWorkspacePresentation(runtime, journey, survival, effects);
        const tools = disclosure("GM Tools", false);
        const row = document.createElement("div");
        row.className = "hc-button-row hc-gm-tools";
        row.append(button("Party & roles", openPartyWorkspace),
            button("Environment", openEnvironmentWorkspace));
        if (presentation.capabilities.resources || presentation.capabilities.survival || presentation.capabilities.effects)
            row.append(button("Survival, resources & effects", openSurvivalWorkspace));
        if (presentation.capabilities.journey)
            row.append(button("Journey", openJourneyWorkspace));
        row.append(button("History", openHistory),
            button("Ruleset reference", () =>
                navigate(`/procedures/${encodeURIComponent(runtime.procedure.procedureId)}/revisions/${runtime.procedure.revision}/reference`)));
        tools.append(row);
        return tools;
    };

    const openDrawer = (
        title: string,
        build: (body: HTMLElement) => (() => void) | void): void => {
        const returnFocus = document.activeElement instanceof HTMLElement
            ? document.activeElement
            : root.querySelector<HTMLElement>("[data-current-action-button]");
        cleanupDrawer();
        drawer?.close();
        drawer = openWorkspaceDrawer(root, title, body => {
            const cleanup = build(body);
            drawerCleanup = cleanup ?? null;
        }, returnFocus, cleanupDrawer);
    };

    const openPartyWorkspace = (): void => {
        openDrawer("Party & activities", body => {
            body.classList.add("hc-page");
            const summary = document.createElement("div");
            summary.dataset.partySummary = "";
            const editor = document.createElement("div");
            editor.dataset.partyEditor = "";
            body.append(summary);
            if ((runtime.movementComposition.contributors.length > 0
                    || runtime.movementComposition.missingInputs.length > 0
                    || runtime.movementComposition.effectiveValue !== null)) {
                const movement = document.createElement("section");
                movement.className = "hc-stack";
                movement.append(
                    textElement("h3", "Movement composition"),
                    movementCompositionLedger(runtime));
                body.append(movement);
            }
            body.append(editor);
            const controller = new ExpeditionPartySheetController(
                body,
                api,
                () => runtime,
                applyRuntime,
                async (_control, action) => {
                    await runUiMutation(action);
                });
            controller.sync(runtime);
        });
    };

    const openEnvironmentWorkspace = (): void => {
        openDrawer("Current environment", body => {
            body.classList.add("hc-page");
            const panel = new ExpeditionEnvironmentPanel(
                body,
                api,
                () => runtime,
                applyRuntime,
                async (_control, action) => {
                    await runUiMutation(action);
                });
            panel.sync();
            queueMicrotask(() => {
                const details = body.querySelector<HTMLDetailsElement>(".hc-environment-panel");
                if (details) details.open = true;
            });
            return () => panel.dispose();
        });
    };

    const openSurvivalWorkspace = (
        focus: "all" | "attention" | "effects" = "all"): void => {
        const capabilities = expeditionWorkspacePresentation(runtime, journey, survival, effects).capabilities;
        const panelFocus = focus === "effects" ? "effects"
            : focus === "attention"
                ? survival?.forcedTravel.checkDue
                    ? "forcedTravel"
                    : "pendingResourceConsequences"
                : "all";
        const title = panelFocus === "effects" ? "Pending effect consequence"
            : panelFocus === "forcedTravel"
                ? "Forced travel"
            : panelFocus === "pendingResourceConsequences"
                ? "Travel consequence"
                : capabilities.effects ? "Resources & effects" : "Survival & resources";
        openDrawer(title, body => {
            body.classList.add("hc-page");
            const cleanups: Array<() => void> = [];
            if (capabilities.effects) {
                const effectPanel = new ExpeditionEffectsPanel(
                    body,
                    effectsApi,
                    runtime.id,
                    () => runtime,
                    async (_control, action) => {
                        await runUiMutation(action);
                    });
                void effectPanel.sync().then(() => {
                    if (focus === "effects") {
                        const action = [...body.querySelectorAll<HTMLButtonElement>("[data-effects-panel] button")]
                            .find(button => button.textContent?.trim() === "Record resolution");
                        action?.focus();
                    }
                });
                cleanups.push(() => effectPanel.dispose());
            }
            if (focus !== "effects" && (capabilities.resources || capabilities.survival)) {
                const panel = new ExpeditionSurvivalResourcesPanel(
                    body,
                    survivalApi,
                    runtime.id,
                    () => runtime,
                    async (_control, action) => {
                        await runUiMutation(action);
                    },
                    panelFocus);
                void panel.sync();
                queueMicrotask(() => {
                    const details = body.querySelector<HTMLDetailsElement>("[data-survival-resources-panel]");
                    if (details) details.open = true;
                });
                cleanups.push(() => panel.dispose());
            }
            return () => cleanups.forEach(cleanup => cleanup());
        });
    };

    const openJourneyWorkspace = (): void => {
        openDrawer("Journey / challenge", body => {
            body.classList.add("hc-page");
            const panel = new ExpeditionJourneyPanel(
                body,
                journeyApi,
                runtime.id,
                () => runtime,
                async (_control, action) => {
                    await runUiMutation(action);
                });
            void panel.sync();
            queueMicrotask(() => {
                const details = body.querySelector<HTMLDetailsElement>("[data-journey-panel]");
                if (details) details.open = true;
            });
            return () => panel.dispose();
        });
    };

    const openEncounterWorkspace = (): void => {
        openDrawer("Encounter", body => {
            body.append(encounterSchedulePanel(runtime));
            const pending = runtime.expedition.pendingEncounter;
            if (!pending) {
                body.append(textElement("p", "No encounter is currently interrupting the expedition.", "hc-muted"));
                return;
            }

            const triggered = runtime.history.find(event =>
                event.kind === "EncounterTriggered"
                && event.sequence === pending.triggerSequence
                && event.encounterOccurrenceId === pending.id);

            body.append(
                textElement("h3", humanize(pending.outcome)),
                textElement("p", triggered?.message ?? pending.note ?? "Encounter resolution is pending."),
                contextLine("Watch", String(pending.watchNumber)),
                textElement(
                    "p",
                    "Preparing a Block Initiative handoff does not resolve the expedition interruption. Mark the encounter resolved here only after its table or tactical handling is complete.",
                    "hc-muted"));

            const resultNote = document.createElement("input");
            resultNote.name = "encounterResultNote";
            resultNote.placeholder = "Optional result or table note";

            const row = document.createElement("div");
            row.className = "hc-button-row";
            const handoff = button("Open in Block Initiative", () =>
                void prepareEncounterHandoff(pending.triggerSequence, handoff));
            handoff.className = "hc-primary-action";
            const resolve = button("Mark encounter resolved", () => {
                void runUiMutation(async () => {
                    const next = await api.resolveEncounter(runtime.id, pending.id, {
                        expectedVersion: runtime.version,
                        resolutionSource: "DmOverride",
                        resolutionNote: "Encounter resolution recorded from expedition workspace.",
                        resultNote: resultNote.value.trim() || undefined
                    });
                    applyRuntime(next);
                });
            });
            row.append(handoff, resolve);
            body.append(labelled("Result note", resultNote), row);
        });
    };

    const prepareEncounterHandoff = async (sequence: number, control: HTMLButtonElement): Promise<void> => {
        control.disabled = true;
        const previous = control.textContent;
        control.textContent = "Preparing encounter…";
        try {
            const handoff = await api.createEncounterHandoff(runtime.id, {
                expectedVersion: runtime.version,
                handoffId: crypto.randomUUID(),
                returnPath: safeCurrentReturnPath(),
                runtimeEncounterSequence: sequence,
                journeyEventOccurrenceId: null
            });
            window.location.assign(blockInitiativeHandoffHref(handoff));
        } catch (value) {
            const error = root.querySelector<HTMLElement>("[data-error]");
            if (error) showUiError(error, value);
            control.disabled = false;
            control.textContent = previous;
        }
    };

    const openNonSpatialWatchWorkspace = (): void => {
        const hours = focusedIntervalHours(runtime);
        if (hours === null) {
            openHistory();
            return;
        }
        openDrawer("Procedure interval", body => {
            body.append(
                textElement("p", `Advance the configured ${formatHours(hours)} interval without adding spatial state.`, "hc-muted"));
            const form = document.createElement("form");
            form.className = "hc-form";
            const note = document.createElement("input");
            note.name = "note";
            note.placeholder = "optional table note";
            const submit = document.createElement("button");
            submit.type = "submit";
            submit.className = "hc-primary-action";
            submit.textContent = `Record ${formatHours(hours)} watch`;
            form.append(labelled("Note", note), submit);
            form.addEventListener("submit", event => {
                event.preventDefault();
                void runUiMutation(async () => {
                    const next = await api.recordWatchAssistant(runtime.id, {
                        expectedVersion: runtime.version,
                        elapsedHours: hours,
                        resolutionSource: "ProcedureDefault",
                        note: note.value.trim() || undefined
                    });
                    applyRuntime(next);
                });
            });
            body.append(form);
        });
    };

    const openHistory = (): void => {
        openDrawer("Expedition history", body => {
            const hasRuntime = runtime.history.length > 0;
            const hasJourney = (journey?.history.length ?? 0) > 0;
            const hasEffects = (effects?.history.length ?? 0) > 0;
            if (!hasRuntime && !hasJourney && !hasEffects) {
                body.append(textElement("p", "No expedition history has been recorded yet."));
                return;
            }

            if (hasJourney && journey) {
                body.append(textElement("h3", "Journey"));
                const list = document.createElement("ol");
                list.className = "hc-history";
                for (const record of journey.history.slice(-80).reverse()) {
                    const item = document.createElement("li");
                    item.textContent = journeyHistoryLabel(record.kind);
                    list.append(item);
                }
                body.append(list);
            }

            if (hasRuntime) {
                body.append(textElement("h3", "Travel / runtime"));
                const list = document.createElement("ol");
                list.className = "hc-history";
                for (const event of [...runtime.history].reverse().slice(0, 80)) {
                    const item = document.createElement("li");
                    item.textContent = `${formatHours(event.expeditionElapsedHours)} · ${event.message}`;
                    list.append(item);
                }
                body.append(list);
            }

            if (hasEffects && effects) {
                body.append(textElement("h3", "Effects / consequences"));
                const list = document.createElement("ol");
                list.className = "hc-history";
                for (const record of effects.history.slice(-80).reverse()) {
                    const item = document.createElement("li");
                    const before = record.beforeLevel !== null ? ` level ${record.beforeLevel}` : "";
                    const after = record.afterLevel !== null
                        ? ` → ${record.afterLevel}`
                        : record.beforeLevel !== null ? " → cleared" : "";
                    item.textContent = `${humanize(record.effectKey)} — ${effectOperationLabel(record.operation)}${before}${after}`;
                    list.append(item);
                }
                body.append(list);
            }

            const advanced = disclosure("Advanced history details");
            if (journey?.history.length) {
                for (const record of journey.history.slice(-40).reverse()) {
                    advanced.append(textElement(
                        "p",
                        `Journey ${record.kind} · ${record.detail} · watch ${record.completedWatches} · source ${record.provenance.sourceKey} · record ${record.id}`,
                        "hc-muted"));
                }
            }
            for (const event of runtime.history.slice(-40).reverse()) {
                advanced.append(textElement(
                    "p",
                    `Runtime #${event.sequence} · watch ${event.watchNumber} · ${event.kind}`,
                    "hc-muted"));
            }
            if (effects?.history.length) {
                for (const record of effects.history.slice(-40).reverse()) {
                    advanced.append(textElement(
                        "p",
                        `Effect ${record.effectId} · ${record.operation} · audit ${record.id}`,
                        "hc-muted"));
                }
            }
            body.append(advanced);
        });
    };

    const activateAction = (kind: ReturnType<typeof expeditionWorkspacePresentation>["action"]["kind"]): void => {
        switch (kind) {
            case "encounter": openEncounterWorkspace(); break;
            case "survival": openSurvivalWorkspace("attention"); break;
            case "effects": openSurvivalWorkspace("effects"); break;
            case "journey": openJourneyWorkspace(); break;
            case "watch": openNonSpatialWatchWorkspace(); break;
            default:
                navigate(`/procedures/${encodeURIComponent(runtime.procedure.procedureId)}/revisions/${runtime.procedure.revision}/reference`);
                break;
        }
    };
    render();
    return () => {
        disposed = true;
        cleanupDrawer();
        closeDrawer();
        root.classList.remove("hc-phase15");
    };
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

function effectOperationLabel(operation: string): string {
    const suffix = operation.includes(":") ? operation.slice(operation.lastIndexOf(":") + 1) : operation;
    return humanize(suffix);
}

function actionCard(title: string, detail: string, action: () => void): HTMLElement {
    const card = document.createElement("article");
    card.className = "hc-context-card";
    card.append(textElement("h3", title), textElement("p", detail, "hc-muted"), button("Open", action));
    return card;
}

function railAction(title: string, detail: string, action: () => void): HTMLButtonElement {
    const control = document.createElement("button");
    control.type = "button";
    control.className = "hc-rail-action";
    control.append(
        textElement("strong", title),
        textElement("span", detail, "hc-muted"));
    control.addEventListener("click", action);
    return control;
}

function travelFact(label: string, value: string, dataKey: string): DocumentFragment {
    const fragment = document.createDocumentFragment();
    const term = document.createElement("dt");
    term.textContent = label;
    const detail = document.createElement("dd");
    detail.textContent = value;
    detail.dataset[dataKey] = "";
    fragment.append(term, detail);
    return fragment;
}

function button(label: string, action: () => void): HTMLButtonElement {
    const control = document.createElement("button");
    control.type = "button";
    control.textContent = label;
    control.addEventListener("click", action);
    return control;
}

function labelled(text: string, control: HTMLElement): HTMLLabelElement {
    const label = document.createElement("label");
    label.append(document.createTextNode(text), control);
    return label;
}

function contextLine(label: string, value: string): HTMLParagraphElement {
    const row = document.createElement("p");
    const strong = document.createElement("strong");
    strong.textContent = `${label}: `;
    row.append(strong, document.createTextNode(value));
    return row;
}

function partyActivitySummary(runtime: ExpeditionDetail): string {
    const assignments = runtime.party.activityAssignments.length;
    if (assignments > 0) return `${assignments} active assignment${assignments === 1 ? "" : "s"}`;
    if (runtime.participantActivityPolicy.support === "Supported") return "No current activity assignments";
    return "Activity procedure not used";
}

function partyCardDetail(runtime: ExpeditionDetail): string {
    const names = runtime.party.members.map(member => member.name);
    if (names.length === 0) return "Party sheet is not configured yet.";
    const summary = names.slice(0, 4).join(", ");
    return names.length > 4 ? `${summary} · ${names.length - 4} more` : summary;
}

function partyRailContext(runtime: ExpeditionDetail): { title: string; detail: string } | null {
    if (runtime.party.members.length === 0) {
        return { title: "Party setup", detail: "Party not configured · configure party" };
    }

    const assignments = runtime.party.activityAssignments;
    if (assignments.length === 0) {
        return runtime.participantActivityPolicy.support === "Supported"
            ? { title: "Travel assignments", detail: "No active assignments · configure roles" }
            : null;
    }

    const memberNames = new Map(runtime.party.members.map(member => [member.id, member.name]));
    const detail = assignments.slice(0, 3).map(assignment => {
        const activity = humanize(assignment.roleKey ?? assignment.activityKey ?? "activity");
        const target = assignment.participantId
            ? memberNames.get(assignment.participantId) ?? "Unknown participant"
            : assignment.scope === "Party" ? "Party" : humanize(assignment.scope);
        return `${activity}: ${target}`;
    });
    if (assignments.length > 3) detail.push(`+${assignments.length - 3} more`);
    return { title: "Travel assignments", detail: detail.join(" · ") };
}

function travelPeriodDetail(runtime: ExpeditionDetail): string {
    const completed = runtime.expedition.completedWatches;
    const completedLabel = `${completed} completed watch${completed === 1 ? "" : "es"}`;
    const intervalHours = runtime.procedure.runtime?.intervalHours ?? null;
    return intervalHours === null
        ? completedLabel
        : `Configured period ${formatHours(intervalHours)} · ${completedLabel}`;
}

function movementStatusDetail(runtime: ExpeditionDetail): string | null {
    const composition = runtime.movementComposition;
    const parts: string[] = [];
    if (composition.missingInputs.length > 0) {
        parts.push(`${composition.missingInputs.length} unresolved input${composition.missingInputs.length === 1 ? "" : "s"}`);
    }
    const limiter = composition.limitingParticipantId
        ? runtime.party.members.find(member => member.id === composition.limitingParticipantId)?.name
        : composition.limitingContributorKey;
    if (limiter) parts.push(`Limited by ${limiter}`);
    const suggestion = movementSuggestionDetail(runtime);
    if (suggestion) parts.push(suggestion);
    if (composition.referenceUse === "Fallback") parts.push("Reference fallback; not a fully resolved composition");
    else if (composition.referenceUse === "InformationalOnly") parts.push("Reference value is informational only");
    return parts.length > 0 ? parts.join(" · ") : null;
}

function movementSummary(runtime: ExpeditionDetail): string {
    const composition = runtime.movementComposition;
    if (composition.effectiveValue !== null && composition.effectiveUnit) {
        const suffix = composition.effectivePerUnit ? `/${composition.effectivePerUnit}` : "";
        return `${formatNumber(composition.effectiveValue)} ${composition.effectiveUnit}${suffix}`;
    }
    return humanize(composition.status);
}

function movementSuggestionDetail(runtime: ExpeditionDetail): string | null {
    const suggestion = runtime.movementComposition.suggestedExpectedDistance;
    return suggestion ? `Suggested next watch: ${formatDistance(suggestion)}` : null;
}

function journeyStageSequence(
    process: ExpeditionJourneyState["activeProcesses"][number]): HTMLElement {
    const section = document.createElement("section");
    section.className = "hc-journey-stage-sequence";
    section.append(textElement("h4", "Journey stages"));
    const list = document.createElement("ol");
    list.className = "hc-stage-sequence";

    for (const [index, stage] of orderedJourneyStageDefinitions(
        process.definition.stageOrder,
        process.definition.stages).entries()) {
        const state = process.stageStates.find(value => value.stageKey === stage.stageKey) ?? null;
        const item = document.createElement("li");
        item.className = "hc-stage-step";
        if (stage.stageKey === process.currentStageKey && !process.isTerminal) item.classList.add("is-current");
        if (state?.completed) item.classList.add("is-complete");

        const marker = textElement("span", state?.completed ? "✓" : String(index + 1), "hc-stage-marker");
        marker.setAttribute("aria-hidden", "true");
        const content = document.createElement("div");
        content.className = "hc-stage-content";
        content.append(textElement("strong", stage.displayName));
        if (stage.description) content.append(textElement("span", stage.description, "hc-muted"));

        if (process.execution.progressKind === "Numeric" && state && stage.stageKey === process.currentStageKey) {
            const current = state.numericProgress ?? 0;
            const unit = process.execution.progressUnit ? ` ${process.execution.progressUnit}` : "";
            const target = stage.progressTarget;
            const progressText = target === null
                ? `${formatNumber(current)}${unit}`
                : `${formatNumber(current)} / ${formatNumber(target)}${unit}`;
            content.append(textElement("span", `Progress: ${progressText}`, "hc-stage-progress-label"));

            const lowerBound = process.execution.progressFloor
                ?? (process.execution.allowNegativeProgress ? null : 0);
            if (target !== null && lowerBound !== null && target > lowerBound) {
                const meter = document.createElement("progress");
                meter.max = target - lowerBound;
                meter.value = Math.max(0, Math.min(current - lowerBound, target - lowerBound));
                meter.setAttribute("aria-label", `${stage.displayName} progress`);
                content.append(meter);
            }
            const bounds: string[] = [];
            if (process.execution.progressFloor !== null) bounds.push(`minimum ${formatNumber(process.execution.progressFloor)}`);
            if (process.execution.progressCeiling !== null) bounds.push(`maximum ${formatNumber(process.execution.progressCeiling)}`);
            if (bounds.length > 0) content.append(textElement("span", bounds.join(" · "), "hc-muted"));
        }

        item.append(marker, content);
        list.append(item);
    }
    section.append(list);
    return section;
}

function environmentRailDetail(state: SurvivalResources | null): string | null {
    const facts = state?.environmentFacts.filter(fact => fact.effective) ?? [];
    if (facts.length === 0) return null;
    const detail = facts.slice(0, 3).map(fact =>
        `${humanize(fact.dimension)}: ${fact.value ?? humanize(fact.valueKind)}`);
    if (facts.length > 3) detail.push(`+${facts.length - 3} more`);
    return detail.join(" · ");
}

function encounterScheduleAvailable(runtime: ExpeditionDetail): boolean {
    return Boolean(
        runtime.expedition.pendingEncounter
        || (runtime.procedure.runtime?.encounterCadence ?? "None") !== "None"
        || hasEncounterSchedule(runtime.procedure));
}

function encounterScheduleSummary(runtime: ExpeditionDetail): string {
    const cadence = runtime.procedure.runtime?.encounterCadence ?? "None";
    const schedule = encounterScheduleConfiguration(runtime.procedure);
    const parts = [
        cadence === "PerWatch"
            ? "Automatic: every watch"
            : cadence === "PerDay"
                ? "Automatic: every day"
                : "Automatic: none"
    ];
    if (schedule) {
        parts.push(`Contextual: ${schedule.scheduleModel ? humanize(schedule.scheduleModel) : "configured"}`);
        if (schedule.travelChecksPerInterval) {
            parts.push(`${schedule.travelChecksPerInterval} travel check${schedule.travelChecksPerInterval === "1" ? "" : "s"} / interval`);
        }
        if (schedule.campCheck !== null) parts.push(`Camp check: ${schedule.campCheck ? "yes" : "no"}`);
        if (schedule.terrainProbabilityModel) parts.push(`Terrain: ${humanize(schedule.terrainProbabilityModel)}`);
    }
    const state = runtime.expedition;
    const watchContext = state.activeWatchNumber !== null
        ? `watch ${state.activeWatchNumber}`
        : `${state.completedWatches} completed watch${state.completedWatches === 1 ? "" : "es"}`;
    parts.push(`day ${state.currentDay}`, watchContext, `${formatHours(state.elapsedTravelHours)} elapsed`);
    if (state.pendingEncounter) parts.push("encounter pending");
    return parts.join(" · ");
}

function encounterSchedulePanel(runtime: ExpeditionDetail): HTMLElement {
    const section = document.createElement("section");
    section.className = "hc-focused-resolution-summary";
    section.dataset.encounterSchedule = "";
    section.append(textElement("h3", "Encounter check schedule"));

    const cadence = runtime.procedure.runtime?.encounterCadence ?? "None";
    const schedule = encounterScheduleConfiguration(runtime.procedure);
    const facts = document.createElement("dl");
    facts.className = "hc-current-travel-facts";
    facts.append(
        travelFact("Automatic cadence", cadence === "PerWatch" ? "Every watch" : cadence === "PerDay" ? "Every day" : "None", "encounterCadence"));
    if (schedule) {
        facts.append(
            travelFact("Contextual schedule", schedule.scheduleModel ? humanize(schedule.scheduleModel) : "Configured", "encounterContextualSchedule"),
            travelFact(
                "Travel checks",
                schedule.travelChecksPerInterval
                    ? `${schedule.travelChecksPerInterval} per interval`
                    : "Not specified",
                "encounterTravelChecks"),
            travelFact(
                "Camp check",
                schedule.campCheck === null ? "Not specified" : schedule.campCheck ? "Yes" : "No",
                "encounterCampCheck"),
            travelFact(
                "Terrain influence",
                schedule.terrainProbabilityModel ? humanize(schedule.terrainProbabilityModel) : "Not specified",
                "encounterTerrainInfluence"));
    }
    facts.append(
        travelFact("Current day", String(runtime.expedition.currentDay), "encounterDay"),
        travelFact(
            "Watch tracking",
            runtime.expedition.activeWatchNumber !== null
                ? `Watch ${runtime.expedition.activeWatchNumber} · ${formatHours(runtime.expedition.activeWatchElapsedHours ?? 0)} elapsed`
                : `${runtime.expedition.completedWatches} completed watch${runtime.expedition.completedWatches === 1 ? "" : "es"}`,
            "encounterWatch"),
        travelFact(
            "Encounter state",
            runtime.expedition.pendingEncounter
                ? `${humanize(runtime.expedition.pendingEncounter.outcome)} pending`
                : "No encounter pending",
            "encounterState"));
    section.append(facts);

    if (schedule && cadence === "None") {
        section.append(textElement(
            "p",
            "This contextual schedule is configured by the procedure for table use; it does not imply an automatic encounter cadence.",
            "hc-muted"));
    }

    const helper = runtime.procedure.runtime?.resolutionHelpers?.encounter;
    if (helper) {
        const modifier = helper.checkRoll.modifier === 0
            ? ""
            : helper.checkRoll.modifier > 0
                ? ` + ${helper.checkRoll.modifier}`
                : ` - ${Math.abs(helper.checkRoll.modifier)}`;
        section.append(textElement(
            "p",
            `Automatic helper: ${helper.checkRoll.diceCount}d${helper.checkRoll.dieSides}${modifier}. Wandering results: ${helper.wanderingResults.join(", ") || "none"}; keyed-location results: ${helper.keyedLocationResults.join(", ") || "none"}.`,
            "hc-muted"));
    }
    return section;
}

function effectsSummary(state: ExpeditionEffectState | null): string | null {
    if (!state) return null;
    if (state.activeEffects.length > 0) {
        return `${state.activeEffects.length} active effect${state.activeEffects.length === 1 ? "" : "s"}`;
    }
    if (state.pendingConsequences.length > 0) {
        return `${state.pendingConsequences.length} pending consequence${state.pendingConsequences.length === 1 ? "" : "s"}`;
    }
    return state.policy.support === "Supported" ? "No active effects" : null;
}

function effectsDetail(state: ExpeditionEffectState | null): string[] {
    if (!state) return [];
    const parts: string[] = state.activeEffects.slice(0, 2).map(effect => {
        const value = effect.level !== null
            ? `level ${effect.level}`
            : effect.magnitude !== null
                ? `${formatNumber(effect.magnitude)}${effect.unit ? ` ${effect.unit}` : ""}`
                : effect.state ? humanize(effect.state) : "active";
        return `${humanize(effect.effectKey)}: ${value}`;
    });
    if (state.activeEffects.length > 2) parts.push(`+${state.activeEffects.length - 2} effects`);
    if (state.pendingConsequences.length > 0) parts.push(`${state.pendingConsequences.length} pending consequence${state.pendingConsequences.length === 1 ? "" : "s"}`);
    return parts;
}

function resourcesEffectsDetail(survival: SurvivalResources | null, effects: ExpeditionEffectState | null): string {
    const parts: string[] = [];
    const effectParts = effectsDetail(effects);
    parts.push(...effectParts);
    const survivalText = survivalDetail(survival);
    if (survivalText !== "No survival resolution currently needs attention." && survivalText !== "Survival state unavailable.") {
        parts.push(survivalText);
    }
    return parts.join(" · ") || effectsSummary(effects) || survivalText;
}

function effectsUseful(state: ExpeditionEffectState | null): boolean {
    return Boolean(state && (state.activeEffects.length > 0 || state.pendingConsequences.length > 0));
}

function survivalDetail(state: SurvivalResources | null): string {
    if (!state) return "Survival state unavailable.";
    const parts: string[] = state.resources.slice(0, 3).map(resource => {
        const value = resource.quantity !== null
            ? `${formatNumber(resource.quantity)}${resource.unit ? ` ${resource.unit}` : ""}`
            : resource.symbolicState ?? (resource.isDepleted ? "depleted" : humanize(resource.inventoryModel));
        return `${humanize(resource.resourceKey)}: ${value}`;
    });
    if (state.resources.length > 3) parts.push(`+${state.resources.length - 3} resources`);
    if (state.forcedTravel.checkDue) parts.push("forced-travel check due");
    if (state.pendingResourceConsequences.length > 0) parts.push(`${state.pendingResourceConsequences.length} pending consequence${state.pendingResourceConsequences.length === 1 ? "" : "s"}`);
    if (state.exposure.length > 0) parts.push(`${state.exposure.length} exposure track${state.exposure.length === 1 ? "" : "s"}`);
    return parts.join(" · ") || "No survival resolution currently needs attention.";
}

function survivalRailUseful(state: SurvivalResources | null): boolean {
    return Boolean(state && (
        state.resources.length > 0
        || state.forcedTravel.checkDue
        || state.pendingResourceConsequences.length > 0
        || state.exposure.length > 0));
}

function survivalAttention(state: SurvivalResources | null): boolean {
    return Boolean(state && (state.forcedTravel.checkDue || state.pendingResourceConsequences.length > 0));
}

function journeyDetail(state: ExpeditionJourneyState | null): string {
    if (!state) return "Journey state unavailable.";
    const active = state.activeProcesses.find(process => !process.isTerminal);
    if (!active) return state.processPolicy.support === "Supported" ? "No active journey." : "Journey procedure not used.";
    const stage = active.definition.stages.find(candidate => candidate.stageKey === active.currentStageKey)?.displayName ?? active.currentStageKey;
    return `${active.definition.displayName} · ${stage}${active.pendingActions.length > 0 ? ` · ${active.pendingActions.length} pending` : ""}`;
}

function journeyAttention(state: ExpeditionJourneyState | null): boolean {
    return Boolean(state && (
        state.activeProcesses.some(process => process.status === "ResolutionRequired" || process.pendingActions.length > 0)
        || state.eventOccurrences.some(event => event.status === "ResolutionRequired")));
}

function journeyRailUseful(state: ExpeditionJourneyState | null): boolean {
    return Boolean(state && (
        state.activeProcesses.some(process => !process.isTerminal)
        || journeyAttention(state)));
}

function encounterSummary(runtime: ExpeditionDetail): string {
    const latest = [...runtime.history].reverse().find(event => event.kind.includes("Encounter"));
    return latest ? latest.message : "No current encounter";
}

function safeCurrentReturnPath(): string | null {
    const path = window.location.pathname;
    if (path !== "/tools/hex-crawl" && !path.startsWith("/tools/hex-crawl/")) return null;
    return `${path}${window.location.search}${window.location.hash}`;
}

async function safeLoad<T>(action: () => Promise<T>): Promise<T | null> {
    try {
        return await action();
    } catch {
        return null;
    }
}

function formatNumber(value: number): string {
    return Number.isInteger(value) ? String(value) : value.toFixed(2).replace(/0+$/, "").replace(/\.$/, "");
}

function humanize(value: string): string {
    return value
        .replace(/[_\.\-]+/g, " ")
        .replace(/([a-z0-9])([A-Z])/g, "$1 $2")
        .replace(/\b\w/g, match => match.toUpperCase());
}

