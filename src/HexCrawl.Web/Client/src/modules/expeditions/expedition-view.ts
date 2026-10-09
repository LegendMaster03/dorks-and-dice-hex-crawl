import type { HexCrawlApi } from "../../api";
import { blockInitiativeHandoffHref } from "../../encounter-handoff";
import { ExpeditionEffectsApi } from "../../effect-api";
import type { ExpeditionEffectState } from "../../effect-types";
import { worldToHex } from "../../hex-math";
import { JourneyApi } from "../../journey-api";
import type { ExpeditionJourneyState } from "../../journey-types";
import { MapSurface } from "../../map-surface";
import { ensurePhase15Styles } from "../../phase15-styles";
import { discoveredSubjectIds, directionLabel, formatDistance, formatHours } from "../../runtime-view";
import { SurvivalResourcesApi } from "../../survival-api";
import type { SurvivalResources } from "../../survival-types";
import { canonicalExpeditionRoute } from "../../tool-route";
import type { ExpeditionDetail, HexCoordinate, Overworld, ResolutionSource, ToolHostContext } from "../../types";
import { clearUiError, showUiError } from "../../ui-error";
import { badge, disclosure, openWorkspaceDrawer, statAction, textElement, type WorkspaceDrawer } from "../../ui/workspace";
import { applyGuidedExperience, guidedDisclosure, guidancePreferenceButton } from "../../ui/guidance";
import { ExpeditionEffectsPanel } from "./effects-panel";
import { ExpeditionEnvironmentPanel } from "./environment-panel";
import { ExpeditionJourneyPanel } from "./journey-panel";
import { ExpeditionPartySheetController } from "./expedition-party-sheet";
import { adjacencyForCell, adjacencyForIntent } from "./spatial-adjacency";
import { renderCurrentCellNavigator } from "./cell-navigator";
import { currentRuntimeCellAdjacency, sameHexCell } from "./current-cell-topology";
import { publishExpeditionRuntimeChanged } from "./expedition-runtime-events";
import { ExpeditionSurvivalResourcesPanel } from "./survival-resources-panel";
import { ExpeditionWatchController } from "./expedition-watch-controller";
import { authoritativeFixedWatchDistance } from "./expedition-party-movement";
import {
    loadTravelPreferences,
    mergeRuntimeTravelPreferences,
    normalizeTravelDirectionPreference,
    normalizeTravelModePreference,
    saveTravelPreferences,
    type TravelPreferences
} from "./expedition-travel-intent";
import { canUseFocusedNonSpatialWatch, focusedIntervalHours } from "./focused-interval-policy";
import { navigationResolutionDue, pauseInstruction, spatialTravelContinuationTarget } from "./expedition-workflow";
import { movementCompositionLedger } from "./expedition-movement-composition-view";
import { encounterScheduleConfiguration, hasEncounterSchedule } from "./encounter-schedule-presentation";
import { orderedJourneyStageDefinitions } from "./journey-stage-presentation";
import { expeditionWorkspacePresentation, spatialPositionPresentation } from "./expedition-workspace-model";

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
    const survivalApi = new SurvivalResourcesApi(toolContext);
    const effectsApi = new ExpeditionEffectsApi(toolContext);
    const journeyApi = new JourneyApi(toolContext);
    let survival = await safeLoad(() => survivalApi.get(expeditionId));
    let effects = await safeLoad(() => effectsApi.get(expeditionId));
    let journey = await safeLoad(() => journeyApi.get(expeditionId));
    let map: MapSurface | null = null;
    let drawer: WorkspaceDrawer | null = null;
    let drawerCleanup: (() => void) | null = null;
    let disposed = false;
    let selectedHex: HexCoordinate | null = null;
    let selectedHexTracksTravelIntent = false;
    let courseIntentMutationPending = false;
    let preferences = loadTravelPreferences(runtime);
    const availableTravelModes = (): string[] => runtime.movementComposition.policy.travelModeKeys ?? [];
    const normalizeTravelModeSelection = (): void => {
        preferences.pace = normalizeTravelModePreference(
            preferences.pace,
            availableTravelModes(),
            runtime.expedition.isSpatial ? runtime.expedition.activePaceKey : null);
    };
    // Normal runtime pace editing exists only for an authoritative finite choice with alternatives.
    // Zero/one-mode procedures are fixed here; arbitrary identifiers require an explicit procedure contract.
    const createPaceControl = (name?: string): HTMLSelectElement | null => {
        const choices = availableTravelModes();
        if (choices.length <= 1) return null;
        const control = document.createElement("select");
        if (name) control.name = name;
        for (const choice of choices) {
            const item = document.createElement("option");
            item.value = choice;
            item.textContent = humanize(choice);
            control.append(item);
        }
        control.value = choices.includes(preferences.pace) ? preferences.pace : choices[0];
        control.setAttribute("aria-label", "Pace or travel mode");
        return control;
    };
    const movementDistanceUnit = (): string | null =>
        runtime.movementComposition.suggestedExpectedDistance?.unit.symbol
        ?? runtime.movementComposition.effectiveDistanceUnit?.symbol
        ?? runtime.context.hexCenterDistance?.unit.symbol
        ?? null;
    normalizeTravelModeSelection();
    let adjacencyCache: {
        key: string;
        value: ReturnType<typeof currentRuntimeCellAdjacency>;
    } | null = null;

    const currentAdjacency = () => {
        if (!runtime.expedition.isSpatial) return null;
        const cell = runtime.expedition.currentHex;
        const grid = world?.grid ?? null;
        const key = [
            cell.q,
            cell.r,
            runtime.procedure.tilingGjhNotation,
            world?.version ?? "abstract",
            grid?.orientation ?? runtime.context.orientation ?? "PointyTop",
            grid?.rotationDegrees ?? 0,
            grid?.hexRadiusWorldUnits ?? 1
        ].join(":");
        if (adjacencyCache?.key !== key) {
            adjacencyCache = {
                key,
                value: currentRuntimeCellAdjacency({
                    currentCell: cell,
                    tilingGjhNotation: runtime.procedure.tilingGjhNotation,
                    selectedDirection: null,
                    worldGrid: grid,
                    abstractOrientation: runtime.context.orientation
                })
            };
        }

        const base = adjacencyCache.value;
        if (preferences.direction === null) return base;
        const selected = adjacencyForIntent(base, preferences.direction);
        return {
            ...base,
            selectedAdjacencyId: selected?.id ?? null
        };
    };

    const normalizeTravelDirectionSelection = (): void => {
        if (!runtime.expedition.isSpatial) {
            preferences.direction = null;
            return;
        }
        const adjacency = currentAdjacency();
        preferences.direction = normalizeTravelDirectionPreference(
            preferences.direction,
            adjacency?.adjacencies.map(edge => edge.intentValue) ?? []);
    };
    normalizeTravelDirectionSelection();

    const synchronizeTravelTargetProjection = (): void => {
        if (!runtime.expedition.isSpatial || preferences.direction === null) {
            if (selectedHexTracksTravelIntent) {
                selectedHex = null;
                selectedHexTracksTravelIntent = false;
            }
            return;
        }
        // An arbitrary inspected cell remains independent from the intended travel target.
        if (selectedHex !== null && !selectedHexTracksTravelIntent) return;
        const adjacency = currentAdjacency();
        selectedHex = adjacency
            ? adjacencyForIntent(adjacency, preferences.direction)?.targetCell ?? null
            : null;
        selectedHexTracksTravelIntent = selectedHex !== null;
    };
    synchronizeTravelTargetProjection();

    const courseLabel = (direction: number | null): string => {
        const adjacency = currentAdjacency();
        if (direction === null || !adjacency) return directionLabel(direction);
        const edge = adjacencyForIntent(adjacency, direction);
        return edge ? edgeCourseLabel(edge) : directionLabel(direction);
    };

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
        preferences = mergeRuntimeTravelPreferences(runtime, preferences);
        normalizeTravelModeSelection();
        normalizeTravelDirectionSelection();
        synchronizeTravelTargetProjection();
        saveTravelPreferences(runtime.id, preferences);
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

        const presentation = expeditionWorkspacePresentation(runtime, journey, survival);
        const page = document.createElement("section");
        page.className = "hc-page hc-phase15-expedition";

        const header = document.createElement("header");
        header.className = "hc-page-header";
        const heading = document.createElement("div");
        heading.append(
            textElement("h1", runtime.name),
            textElement("p", `${runtime.context.name} · ${runtime.procedure.name}`));
        const nav = document.createElement("nav");
        nav.className = "hc-button-row";
        nav.append(button("Hex Crawl home", () => navigate("/")));
        if (runtime.overworldId) {
            nav.append(button("World authoring", () => navigate(`/worlds/${runtime.overworldId}/edit`)));
        }
        nav.append(button("Ruleset reference", () =>
            navigate(`/procedures/${encodeURIComponent(runtime.procedure.procedureId)}/revisions/${runtime.procedure.revision}/reference`)));
        nav.append(guidancePreferenceButton(root));
        header.append(heading, nav);
        page.append(header);

        const error = document.createElement("div");
        error.className = "hc-error";
        error.dataset.error = "";
        error.hidden = true;
        error.setAttribute("role", "alert");
        page.append(error);

        page.append(renderCurrentAction(presentation.action));

        const status = document.createElement("section");
        status.className = "hc-phase15-runtime-summary";
        const stats = document.createElement("div");
        stats.className = "hc-stat-action-grid";
        const position = spatialPositionPresentation(runtime);
        stats.append(
            statAction("Time", presentation.timeLabel, travelPeriodDetail(runtime), openHistory),
            statAction(
                runtime.expedition.isSpatial ? "Position" : "Context",
                position?.value ?? presentation.routeLabel ?? runtime.context.name,
                runtime.expedition.isSpatial ? position?.detail ?? null : "Journey / no grid",
                runtime.expedition.isSpatial ? focusTravelCourse : openHistory,
                runtime.pauseReason ? "warning" : "neutral"));
        // Party management remains in the GM tools; an empty, unused party is not a status condition.
        if (runtime.party.members.length > 0 || runtime.party.activityAssignments.length > 0) {
            stats.append(statAction(
                "Party",
                `${runtime.party.members.length} member${runtime.party.members.length === 1 ? "" : "s"}`,
                partyActivitySummary(runtime),
                openPartyWorkspace));
        }
        if (presentation.capabilities.travel) {
            stats.append(statAction(
                "Movement",
                movementSummary(runtime),
                movementStatusDetail(runtime),
                runtime.expedition.isSpatial ? () => openTravelWorkspace("movement") : openPartyWorkspace,
                runtime.movementComposition.missingInputs.length > 0 ? "warning" : "neutral"));
        }
        const navigationConfigured = runtime.procedure.modules.some(module =>
            module.moduleKey.includes("navigation")
            && module.parameters.usesNavigationChecks !== "false"
            && module.parameters.usesPersistentVeer !== "false");
        if (presentation.capabilities.navigation && presentation.navigationLabel
            && (navigationConfigured || runtime.expedition.isLost || navigationResolutionDue(runtime, false, false))) {
            stats.append(statAction(
                "Navigation",
                presentation.navigationLabel,
                runtime.expedition.isSpatial && preferences.direction !== null
                    ? `Intended ${courseLabel(preferences.direction)}`
                    : null,
                openNavigationWorkspace,
                runtime.expedition.isSpatial && runtime.expedition.isLost ? "warning" : "neutral"));
        }
        const encounterConfigured = runtime.procedure.modules.some(module =>
            (module.moduleKey.includes("encounter") && module.parameters.cadence !== undefined
                && module.parameters.cadence !== "None")
            || module.moduleKey === "encounters.schedule");
        if ((presentation.capabilities.encounters && encounterConfigured)
            || runtime.pauseReason === "EncounterTriggered" || runtime.expedition.pendingEncounter) {
            stats.append(statAction(
                "Encounters",
                runtime.pauseReason === "EncounterTriggered" ? "Encounter active" : encounterSummary(runtime),
                runtime.pauseReason === "EncounterTriggered" ? "Travel is paused for resolution" : null,
                openEncounterWorkspace,
                runtime.pauseReason === "EncounterTriggered" ? "danger" : "neutral"));
        }
        status.append(stats);
        page.append(status);

        if (runtime.expedition.isSpatial) {
            page.append(renderSpatialWorkspace());
        } else {
            page.append(renderNonSpatialWorkspace());
        }

        page.append(renderGmTools());
        root.replaceChildren(page);

        bindMapIfPresent();
        if (restoreFocusAfterRender) {
            queueMicrotask(() => {
                if (disposed) return;
                root.querySelector<HTMLButtonElement>("[data-current-action-button]")?.focus();
            });
        }
    };

    const currentActionCopy = (
        action: ReturnType<typeof expeditionWorkspacePresentation>["action"]): { label: string; detail: string } => {
        const routineSpatialTravel = runtime.expedition.isSpatial
            && action.kind === "travel"
            && !action.urgent;
        const courseRequired = routineSpatialTravel && preferences.direction === null;
        return {
            label: courseRequired
                ? "Choose travel direction"
                : routineSpatialTravel
                    ? "Continue travel"
                    : action.label,
            detail: courseRequired
                ? "Choose an adjacent destination from the navigator or map. Your pace stays reusable; selecting a direction does not move the party."
                : routineSpatialTravel
                    ? "Use the selected direction and pace. Only unresolved ruleset inputs will be requested."
                    : action.detail
        };
    };

    const guidedActionExplanation = (
        kind: ReturnType<typeof expeditionWorkspacePresentation>["action"]["kind"]): string => {
        switch (kind) {
            case "travel": return "Travel is the routine progression step. The saved ruleset resolves movement, partial progress, navigation, encounters, and consequences from the inputs you provide.";
            case "navigation": return "Navigation is due before more travel can resolve. It compares intended travel with the party's actual route; resolving it does not move the party by itself.";
            case "encounter": return "An encounter interrupted travel. Resolve or hand off that encounter before the expedition can continue.";
            case "survival": return "A forced-travel, resource, or survival consequence is already pending and must be resolved before later travel.";
            case "journey": return "The active journey has a stored stage, event, approach, or resolution that needs table input before it can continue.";
            case "watch": return "This ruleset uses a repeating travel period. Running or resuming it advances configured time without inventing spatial movement.";
            case "boundary": return "The party reached a boundary where its position or direction needs a decision before more travel changes expedition state.";
            case "procedure": return "There is no automatic travel or journey step available. Use the saved ruleset as the table authority and enter manual results where required.";
        }
        return "Follow the current saved action before advancing the expedition.";
    };

    const guidedActionInput = (
        kind: ReturnType<typeof expeditionWorkspacePresentation>["action"]["kind"]): string => {
        switch (kind) {
            case "travel":
                if (!runtime.expedition.isSpatial) return "Use the current interval rules and any table or DM values requested by the travel/time workspace.";
                if (preferences.direction === null) return "Choose an adjacent destination from the map or navigator. That choice records intended travel only; it does not move the party.";
                if (runtime.movementComposition.missingInputs.length > 0) {
                    const period = travelPeriodDetail(runtime);
                    const unit = movementDistanceUnit();
                    const suggested = runtime.movementComposition.suggestedExpectedDistance;
                    const basis = suggested
                        ? `The saved movement calculation suggests ${formatNumber(suggested.value)} ${suggested.unit.symbol}; confirm it against the table's actual result.`
                        : "No authoritative movement formula supplied a distance. Use the distance resolved by your ruleset, dice, another system, or the DM for this period; do not assume zero.";
                    return `Enter the travel distance${unit ? ` in ${unit}` : ""} for ${period || "this travel period"}, then choose its source (calculated, rolled, or DM decision). ${basis}`;
                }
                return "The saved travel direction, pace, and movement state are reused. The travel workspace asks only for any remaining table or DM decisions.";
            case "navigation": return "Enter the configured navigation result or DM decision in the navigation workspace. The intended direction remains the reference point.";
            case "encounter": return "Use the encounter already recorded by the expedition. Hand it off if useful, then mark it resolved only after the table has finished it.";
            case "survival": return "Use the pending check or consequence already shown in Resources & effects. Enter the table result or DM decision where the ruleset requires one.";
            case "journey": return "Use the current journey stage and pending action shown in the journey workspace. Any roll or decision comes from that saved action and the table.";
            case "watch": return "Use the saved interval settings and enter only the values the time workspace requests.";
            case "boundary": return "Choose the boundary, recognition, or reorientation decision requested by the saved travel state.";
            case "procedure": return "Consult this saved ruleset. Enter manual or unsupported results explicitly instead of assuming automatic resolution.";
        }
        return "Use the inputs shown by the current action.";
    };

    const guidedActionResult = (
        kind: ReturnType<typeof expeditionWorkspacePresentation>["action"]["kind"]): string => {
        switch (kind) {
            case "travel": return "When travel resolves, saved time, position, cell progress, and any new navigation, encounter, or survival consequence are updated from the actual result. The Next action card then recalculates.";
            case "navigation": return "The navigation result updates route or lost-state information. It does not advance position by itself; the next travel action uses the resulting state.";
            case "encounter": return "Resolving the encounter clears that interruption. Travel resumes only after the saved expedition state confirms there is no remaining encounter pause.";
            case "survival": return "The resolved consequence updates the applicable resource, effect, or forced-travel state. Any remaining consequence becomes the next required action.";
            case "journey": return "The saved journey process updates its stage, progress, event, or completion state according to the resolved action, then exposes the next pending step.";
            case "watch": return "The configured interval advances time and records the result without creating spatial movement.";
            case "boundary": return "The decision updates travel intent or position handling at the boundary, after which the Next action is recalculated from saved state.";
            case "procedure": return "Manual entries remain explicit history/state; Hex Crawl does not invent an outcome for rules it can not execute.";
        }
        return "After resolution, the Next action is recalculated from saved expedition state.";
    };

    const renderGuidedActionGuide = (
        action: ReturnType<typeof expeditionWorkspacePresentation>["action"]): HTMLElement => {
        const guide = document.createElement("details");
        guide.className = "hc-guided-action-guide hc-guided-only";
        const summary = document.createElement("summary");
        summary.textContent = "What this action needs and changes";
        guide.append(summary);
        const row = (label: string, copy: string): HTMLElement => {
            const paragraph = document.createElement("p");
            paragraph.append(textElement("strong", label), document.createTextNode(` ${copy}`));
            return paragraph;
        };
        guide.append(
            row("Input:", guidedActionInput(action.kind)),
            row("After resolution:", guidedActionResult(action.kind)));
        // Present the current authoritative state, not the final provenance/diagnostic history row.
        const latestOutcome = [...runtime.history].reverse().find(event =>
            !/provenance|diagnostic|resolved input source/i.test(event.message));
        if (latestOutcome) {
            guide.append(row("Recorded event:", latestOutcome.message));
        }
        if (runtime.expedition.isSpatial) {
            const position = spatialPositionPresentation(runtime);
            if (position) guide.append(row("Saved position and progress:", [position.value, position.detail].filter(Boolean).join(" · ")));
        }
        guide.append(row("Current time:", expeditionWorkspacePresentation(runtime, journey, survival).timeLabel));
        if (runtime.pauseReason) guide.append(row("Current interruption:", pauseInstruction(runtime) ?? runtime.pauseReason));
        guide.append(row("Next required action:", currentActionCopy(action).label));
        if (runtime.history.length > 0) {
            const technical = document.createElement("details");
            technical.className = "hc-optional-reference";
            const caption = document.createElement("summary");
            caption.textContent = "Technical history and result sources";
            technical.append(caption);
            for (const event of runtime.history.slice(-5)) technical.append(textElement("p", event.message));
            guide.append(technical);
        }
        return guide;
    };

    const renderCurrentAction = (action: ReturnType<typeof expeditionWorkspacePresentation>["action"]): HTMLElement => {
        const section = document.createElement("section");
        section.className = "hc-current-action";
        const header = document.createElement("header");
        header.append(textElement("h2", "Next action"), badge(action.urgent ? "Needs resolution" : "Ready", action.urgent ? "warning" : "good"));
        const copy = currentActionCopy(action);
        const actionLabel = textElement("p", copy.label, "hc-current-action-primary");
        actionLabel.dataset.currentActionLabel = "";
        const actionDetail = textElement("p", copy.detail, "hc-current-action-detail");
        actionDetail.dataset.currentActionDetail = "";
        section.append(header, actionLabel, actionDetail);
        const row = document.createElement("div");
        row.className = "hc-button-row";
        const primary = button(copy.label, () => activateAction(action.kind));
        primary.className = "hc-primary-action";
        primary.dataset.currentActionButton = "";
        row.append(primary);
        section.append(row, renderGuidedActionGuide(action));
        section.append(guidedDisclosure("Why is this next?", copy.label, guidedActionExplanation(action.kind)));
        return section;
    };

    const renderSpatialWorkspace = (): HTMLElement => {
        const section = document.createElement("section");
        section.className = world ? "hc-workspace-grid" : "hc-nonspatial-primary";

        const primary = document.createElement("div");
        primary.className = "hc-panel hc-map-panel";
        primary.append(textElement("h2", world ? "Expedition map" : "Mapless exploration"));
        if (!world) primary.append(renderCurrentTravel());
        if (world) {
            const frame = document.createElement("div");
            frame.className = "hc-map-frame";
            const host = document.createElement("div");
            host.className = "hc-map-host";
            host.dataset.map = "";
            const context = document.createElement("div");
            context.dataset.mapContext = "";
            context.className = "hc-map-context-overlay";
            context.hidden = true;
            frame.append(host, renderAdjacencyNavigator(), context);
            primary.append(frame);
        } else {
            primary.append(textElement("p", "There is no authored map. Spatial positions are still tracked; choose an adjacent destination with the travel-direction control below.", "hc-muted"));
        }

        const secondary = document.createElement("aside");
        secondary.className = "hc-panel hc-sidebar hc-table-rail";
        secondary.append(textElement("h2", "At the table"));
        if (world) secondary.append(renderCurrentTravel());

        const partyContext = partyRailContext(runtime);
        if (partyContext) {
            secondary.append(railAction(partyContext.title, partyContext.detail, openPartyWorkspace));
        }
        const environmentContext = environmentRailDetail(survival);
        if (environmentContext) {
            secondary.append(railAction("Current cell", environmentContext, openEnvironmentWorkspace));
        }
        if (journeyRailUseful(journey)) {
            secondary.append(railAction("Journey / challenge", journeyDetail(journey), openJourneyWorkspace));
        }
        if (survivalRailUseful(survival) || effectsUseful(effects)) {
            secondary.append(railAction("Resources & effects", resourcesEffectsDetail(survival, effects), openSurvivalWorkspace));
        }
        if (encounterScheduleAvailable(runtime)) {
            secondary.append(railAction("Encounter schedule", encounterScheduleSummary(runtime), openEncounterWorkspace));
        }
        section.append(primary, secondary);
        return section;
    };

    const renderNonSpatialWorkspace = (): HTMLElement => {
        const section = document.createElement("section");
        section.className = "hc-nonspatial-primary";
        const main = document.createElement("article");
        main.className = "hc-panel hc-journey-primary";
        const presentation = expeditionWorkspacePresentation(runtime, journey, survival);
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

    const setCourseIntentMutationPending = (pending: boolean): void => {
        courseIntentMutationPending = pending;
        for (const control of root.querySelectorAll<HTMLButtonElement | HTMLSelectElement>(
            '[data-adjacency-interface-id], [data-adjacency-select], select[name="direction"]')) {
            control.disabled = pending;
        }
    };

    const commitTravelIntent = async (direction: number | null): Promise<void> => {
        if (!runtime.expedition.isSpatial || courseIntentMutationPending) return;
        setCourseIntentMutationPending(true);
        const errorBefore = root.querySelector<HTMLElement>("[data-error]");
        if (errorBefore) clearUiError(errorBefore);
        try {
            const next = await api.setExpeditionCourseIntent(runtime.id, {
                expectedVersion: runtime.version,
                intendedDirection: direction
            });
            if (direction === null) {
                if (selectedHexTracksTravelIntent) {
                    selectedHex = null;
                    selectedHexTracksTravelIntent = false;
                }
            } else {
                const adjacency = currentAdjacency();
                const edge = adjacency ? adjacencyForIntent(adjacency, direction) : null;
                selectedHex = edge?.targetCell ?? null;
                selectedHexTracksTravelIntent = selectedHex !== null;
            }
            applyRuntime(next);
            rebaseAuxiliaryVersions(next.version);
            if (!disposed) render();
        } catch (value) {
            try {
                const latest = await api.getExpedition(expeditionId);
                applyRuntime(latest);
                await refreshAuxiliary();
                if (!disposed) render();
            } catch {
                // Preserve the original mutation failure if authoritative reload also fails.
            }
            const errorAfter = root.querySelector<HTMLElement>("[data-error]");
            if (!disposed && errorAfter) showUiError(errorAfter, value);
        } finally {
            if (!disposed) setCourseIntentMutationPending(false);
        }
    };

    const selectTravelIntent = (direction: number): void => {
        if (!runtime.expedition.isSpatial) return;
        void commitTravelIntent(direction);
    };

    const clearTravelIntent = (): void => {
        if (!runtime.expedition.isSpatial) return;
        void commitTravelIntent(null);
    };

    const toggleTravelIntent = (direction: number): void => {
        if (!runtime.expedition.isSpatial) return;
        if (preferences.direction !== direction) {
            selectTravelIntent(direction);
            return;
        }
        if (runtime.expedition.activeWatchNumber !== null) {
            const error = root.querySelector<HTMLElement>("[data-error]");
            if (error) {
                showUiError(error, new Error(
                    "The active travel watch requires an intended course. Choose another adjacent course instead of clearing it."));
            }
            return;
        }
        clearTravelIntent();
    };

    const syncTravelIntentControls = (): void => {
        if (!runtime.expedition.isSpatial) return;
        const adjacency = currentAdjacency();
        if (!adjacency) return;

        for (const control of root.querySelectorAll<HTMLButtonElement>("[data-adjacency-interface-id]")) {
            const selected = adjacency.selectedAdjacencyId === control.dataset.adjacencyInterfaceId;
            control.setAttribute("aria-pressed", String(selected));
            control.classList.toggle("is-selected", selected);
        }
        const selector = root.querySelector<HTMLSelectElement>("[data-adjacency-select]");
        if (selector) selector.value = preferences.direction === null ? "" : String(preferences.direction);

        const summary = root.querySelector<HTMLElement>("[data-travel-intent-summary]");
        if (summary) summary.textContent = travelIntentSummary(runtime, preferences, adjacency);
        const course = root.querySelector<HTMLElement>("[data-current-travel-course]");
        if (course) course.textContent = currentCourseLabel(preferences, adjacency);
        const pace = root.querySelector<HTMLElement>("[data-current-travel-pace]");
        if (pace) pace.textContent = preferences.pace;
        const actual = root.querySelector<HTMLElement>("[data-current-travel-actual]");
        if (actual) actual.textContent = actualCourseLabel(runtime, adjacency);
        const progress = root.querySelector<HTMLElement>("[data-current-travel-progress]");
        if (progress) progress.textContent = travelProgressDetail(runtime);

        const action = expeditionWorkspacePresentation(runtime, journey, survival).action;
        const copy = currentActionCopy(action);
        const nextLabel = root.querySelector<HTMLElement>("[data-current-action-label]");
        if (nextLabel) nextLabel.textContent = copy.label;
        const nextDetail = root.querySelector<HTMLElement>("[data-current-action-detail]");
        if (nextDetail) nextDetail.textContent = copy.detail;
        const nextButton = root.querySelector<HTMLButtonElement>("[data-current-action-button]");
        if (nextButton) nextButton.textContent = copy.label;
    };

    const renderAdjacencyNavigator = (): HTMLElement => {
        const adjacency = currentAdjacency();
        const action = expeditionWorkspacePresentation(runtime, journey, survival).action;
        return renderCurrentCellNavigator(adjacency, {
            selectable: action.kind === "travel" || action.kind === "navigation",
            actualIntent: runtime.expedition.actualDirection,
            onToggle: selected => toggleTravelIntent(selected.intentValue)
        });
    };

    const renderCurrentTravel = (): HTMLElement => {
        const adjacency = currentAdjacency();
        const section = document.createElement("section");
        section.className = "hc-current-travel";
        section.dataset.currentTravel = "";
        section.append(textElement("h3", "Current travel"));

        if (!adjacency) {
            section.append(textElement("p", "Current-cell adjacency is unavailable.", "hc-muted"));
            return section;
        }

        const facts = document.createElement("dl");
        facts.className = "hc-current-travel-facts";
        facts.append(
            travelFact("Travel direction", currentCourseLabel(preferences, adjacency), "currentTravelCourse"),
            travelFact("Pace", humanize(preferences.pace), "currentTravelPace"),
            travelFact("Actual", actualCourseLabel(runtime, adjacency), "currentTravelActual"),
            travelFact("Progress", travelProgressDetail(runtime), "currentTravelProgress"));
        section.append(facts);
        const cellProgress = cellProgressIndicator(runtime);
        if (cellProgress) section.append(cellProgress);

        if (!world) {
            const course = document.createElement("select");
            course.dataset.adjacencySelect = "";
            course.setAttribute("aria-label", "Intended adjacent cell");
            const empty = document.createElement("option");
            empty.value = "";
            empty.textContent = "Choose adjacent cell";
            course.append(empty);
            for (const edge of adjacency.adjacencies) {
                const option = document.createElement("option");
                option.value = String(edge.intentValue);
                option.textContent = edgeCourseLabel(edge);
                course.append(option);
            }
            course.value = preferences.direction === null ? "" : String(preferences.direction);
            course.addEventListener("change", () => {
                if (!course.value) {
                    clearTravelIntent();
                    return;
                }
                const edge = adjacencyForIntent(adjacency, Number(course.value));
                if (edge) selectTravelIntent(edge.intentValue);
            });
            section.append(labelled("Course", course));
        }

        const summary = textElement("p", travelIntentSummary(runtime, preferences, adjacency), "hc-muted");
        summary.dataset.travelIntentSummary = "";
        section.append(summary);

        const pace = createPaceControl();
        let paceEditor: HTMLDivElement | null = null;
        if (pace) {
            const editablePace = pace;
            paceEditor = document.createElement("div");
            paceEditor.className = "hc-current-travel-pace-editor";
            paceEditor.hidden = true;
            const savePace = button("Save pace", () => {
                preferences.pace = editablePace.value;
                saveTravelPreferences(runtime.id, preferences);
                paceEditor!.hidden = true;
                syncTravelIntentControls();
            });
            paceEditor.append(labelled("Pace / travel mode", editablePace), savePace);
            section.append(paceEditor);
        }

        if (runtime.procedure.runtime !== null) {
            const actions = document.createElement("div");
            actions.className = "hc-button-row hc-current-travel-actions";
            if (paceEditor && pace) {
                const editablePace = pace;
                const changePace = button("Change pace", () => {
                    paceEditor!.hidden = !paceEditor!.hidden;
                    if (!paceEditor!.hidden) editablePace.focus();
                });
                actions.append(changePace);
            }
            const more = button("More options", () => openTravelWorkspace("advanced"));
            more.className = "hc-secondary-action";
            actions.append(more);
            section.append(actions);
        }
        return section;
    };

    const renderGmTools = (): HTMLElement => {
        const presentation = expeditionWorkspacePresentation(runtime, journey, survival);
        const tools = disclosure("GM Tools", false);
        const row = document.createElement("div");
        row.className = "hc-button-row hc-gm-tools";
        row.append(button(
            runtime.expedition.isSpatial ? "Party & travel order" : "Party & roles",
            openPartyWorkspace));
        if (runtime.expedition.isSpatial) {
            row.append(button("Teleport party", () => openRepositionWorkspace(selectedHex)));
        }
        row.append(button("Environment", openEnvironmentWorkspace));
        if (presentation.capabilities.resources || presentation.capabilities.survival || presentation.capabilities.effects) {
            row.append(button(
                presentation.capabilities.effects
                    ? presentation.capabilities.resources || presentation.capabilities.survival
                        ? "Survival, resources & effects"
                        : "Effects"
                    : "Survival & resources",
                openSurvivalWorkspace));
        }
        if (presentation.capabilities.journey) {
            row.append(button("Journey", openJourneyWorkspace));
        }
        row.append(
            button("History", openHistory),
            button("Ruleset reference", () =>
                navigate(`/procedures/${encodeURIComponent(runtime.procedure.procedureId)}/revisions/${runtime.procedure.revision}/reference`)));
        tools.append(row);
        return tools;
    };

    const renderMapContext = (): void => {
        const host = root.querySelector<HTMLElement>("[data-map-context]");
        const currentWorld = world;
        if (!host || !runtime.expedition.isSpatial || !currentWorld) return;
        host.replaceChildren();
        if (!selectedHex) {
            host.hidden = true;
            return;
        }

        host.hidden = false;
        host.append(textElement("h3", "Selected map cell"));
        const adjacency = currentAdjacency();
        const edge = adjacency
            ? adjacencyForCell(adjacency, selectedHex, sameHexCell)
            : null;
        if (edge) {
            host.append(textElement(
                "p",
                `Intended next cell · ${edgeCourseLabel(edge)}. Selecting an adjacent cell expresses travel intent only; it does not move the party.`,
                "hc-muted"));
        } else if (sameHexCell(runtime.expedition.currentHex, selectedHex)) {
            host.append(textElement("p", "The party is currently in this cell.", "hc-muted"));
        } else {
            host.append(textElement("p", "Inspecting a non-adjacent cell does not change travel intent or expedition position.", "hc-muted"));
        }

        if (!edge && !sameHexCell(runtime.expedition.currentHex, selectedHex)) {
            const move = button("Teleport party here", () => openRepositionWorkspace(selectedHex));
            move.className = "hc-secondary-action";
            host.append(move);
        }

        const subjects = [
            ...currentWorld.locations
                .filter(item => sameHexCell(worldToHex(currentWorld.grid, item.position), selectedHex!))
                .map(item => ({ id: item.id, name: item.name, type: "Location" as const })),
            ...currentWorld.features
                .filter(item => item.kind === "Point" && item.position && sameHexCell(worldToHex(currentWorld.grid, item.position), selectedHex!))
                .map(item => ({ id: item.id, name: item.name, type: "Feature" as const }))
        ];
        if (subjects.length > 0) {
            const discovered = discoveredSubjectIds(runtime);
            const list = document.createElement("div");
            list.className = "hc-stack";
            for (const subject of subjects) {
                const item = document.createElement("div");
                item.className = "hc-discovery-row";
                item.append(textElement("span", `${subject.name} · ${subject.type}`));
                if (!discovered.has(subject.id)) {
                    item.append(button("Reveal / discover", () => void runUiMutation(async () => {
                        applyRuntime(await api.discover(runtime.id, runtime.version, subject.id, subject.type));
                    })));
                } else {
                    item.append(badge("Known to players", "good"));
                }
                list.append(item);
            }
            host.append(list);
        }
    };

    const bindMapIfPresent = (): void => {
        const host = root.querySelector<HTMLElement>("[data-map]");
        if (!host || !world || !runtime.expedition.isSpatial) {
            map?.dispose();
            map = null;
            return;
        }
        if (map) {
            map.attach(host);
        } else {
            map = new MapSurface(host, () => world);
            map.setHexSelectionHandler(hex => {
                if (hex) {
                    const adjacency = currentAdjacency();
                    const edge = adjacency
                        ? adjacencyForCell(adjacency, hex, sameHexCell)
                        : null;
                    if (edge) {
                        selectTravelIntent(edge.intentValue);
                        return;
                    }
                }
                selectedHex = hex;
                selectedHexTracksTravelIntent = false;
                syncTravelIntentControls();
                renderMapContext();
            });
        }
        map.renderer.expeditionHex = runtime.expedition.currentHex;
        map.renderer.discoveredSubjectIds = discoveredSubjectIds(runtime);
        map.renderer.selectedHex = selectedHex;
        map.requestRender();
        renderMapContext();
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

    const openTravelWorkspace = (
        focus: "advanced" | "movement" | "encounter" = "advanced"): void => {
        if (!runtime.expedition.isSpatial || runtime.procedure.runtime === null) {
            openHistory();
            return;
        }
        const title = focus === "movement"
            ? "Movement resolution"
            : focus === "encounter"
                ? "Encounter check"
                : "Advanced travel controls";
        openDrawer(title, body => {
            body.innerHTML = watchWorkspaceMarkup(currentAdjacency(), availableTravelModes(), movementDistanceUnit());
            const controller = new ExpeditionWatchController(
                body,
                api,
                world,
                () => runtime,
                applyRuntime,
                async action => {
                    await mutate(async () => {
                        await action();
                        captureTravelPacePreference(body);
                    });
                },
                focus === "encounter" ? "encounterCadence" : "advance");
            controller.sync(runtime);
            applyTravelPreferences(body);
            focusWatchWorkspace(body, focus);
            const directionControl = body.querySelector<HTMLSelectElement>('select[name="direction"]');
            const paceControl = body.querySelector<HTMLInputElement | HTMLSelectElement>('[name="pace"]');
            directionControl?.addEventListener("change", captureTravelDirectionFromControls);
            paceControl?.addEventListener("change", captureTravelPaceFromControls);
            return () => {
                directionControl?.removeEventListener("change", captureTravelDirectionFromControls);
                paceControl?.removeEventListener("change", captureTravelPaceFromControls);
                controller.dispose();
            };

            function captureTravelDirectionFromControls(): void {
                if (!directionControl) return;
                if (!directionControl.value) {
                    clearTravelIntent();
                    return;
                }
                const parsed = Number(directionControl.value);
                const adjacency = currentAdjacency();
                const edge = Number.isInteger(parsed) && adjacency
                    ? adjacencyForIntent(adjacency, parsed)
                    : null;
                if (edge) selectTravelIntent(edge.intentValue);
            }

            function captureTravelPaceFromControls(): void {
                captureTravelPacePreference(body);
            }
        });
    };

    const focusWatchWorkspace = (
        body: HTMLElement,
        focus: "advanced" | "movement" | "encounter"): void => {
        if (focus === "advanced") return;
        const focusGroups = [...body.querySelectorAll<HTMLElement>("[data-focus-group]")];
        for (const group of focusGroups) {
            const keys = (group.dataset.focusGroup ?? "").split(/\s+/).filter(Boolean);
            group.hidden = !keys.includes(focus);
        }
        const requirements = body.querySelector<HTMLElement>("[data-requirements]");
        if (requirements) requirements.hidden = true;
        const genericHelper = body.querySelector<HTMLElement>("[data-resolution-helper]");
        if (genericHelper) {
            genericHelper.hidden = true;
            genericHelper.classList.add("hc-focused-hidden");
        }

        const intro = document.createElement("section");
        intro.className = "hc-focused-resolution-summary";
        const heading = focus === "movement"
            ? "Movement resolution"
            : "Encounter check";
        const watchSummary = body.querySelector<HTMLElement>("[data-watch-summary]");
        if (watchSummary) {
            watchSummary.textContent = focus === "movement"
                ? "Travel segment"
                : "Encounter resolution";
        }
        intro.append(
            textElement("h3", heading),
            textElement("p", travelIntentSummary(runtime, preferences, currentAdjacency()), "hc-muted"));
        if (focus === "movement") {
            intro.append(
                textElement(
                    "p",
                    movementSuggestionDetail(runtime) ?? movementSummary(runtime),
                    "hc-muted"),
                movementCompositionLedger(runtime));
        } else if (focus === "encounter") {
            intro.append(textElement("p", "Resolve the due encounter check, then continue the same travel intent.", "hc-muted"));
        } else {
            intro.append(textElement("p", "Resolve the due encounter check, then continue the same travel intent.", "hc-muted"));
        }
        body.prepend(intro);

        const advance = body.querySelector<HTMLButtonElement>("[data-advance-button]");
        if (advance) {
            advance.textContent = focus === "movement"
                ? "Resolve movement and continue"
                : focus === "encounter"
                    ? "Record encounter check"
                    : "Resolve encounter check and continue";
        }
    };

    const openBoundaryWorkspace = (): void => {
        if (!runtime.expedition.isSpatial
            || runtime.pauseReason !== "LostRecognitionRequired") {
            openHistory();
            return;
        }

        openDrawer("Boundary crossing", body => {
            body.append(textElement(
                "p",
                pauseInstruction(runtime)
                    ?? "Resolve the pending lost-party boundary decision before travel continues.",
                "hc-muted"));

            const form = document.createElement("form");
            form.className = "hc-form";
            const recognized = document.createElement("input");
            recognized.type = "checkbox";
            recognized.name = "recognizedLost";
            const reorient = document.createElement("input");
            reorient.type = "checkbox";
            reorient.name = "reorient";
            const source = document.createElement("select");
            for (const [value, label] of [
                ["ManualRoll", "Manual roll"],
                ["ExternalSystem", "External system"],
                ["DmOverride", "DM ruling"],
                ["ProcedureDefault", "Procedure default"]
            ] as const) {
                const option = document.createElement("option");
                option.value = value;
                option.textContent = label;
                source.append(option);
            }
            const resolutionNote = document.createElement("input");
            resolutionNote.placeholder = "optional source note";

            const syncReorient = (): void => {
                reorient.disabled = !recognized.checked;
                if (!recognized.checked) reorient.checked = false;
            };
            recognized.addEventListener("change", syncReorient);
            syncReorient();

            const submit = document.createElement("button");
            submit.type = "submit";
            submit.className = "hc-primary-action";
            submit.textContent = "Resolve boundary";
            form.append(
                labelled("Party recognizes it is lost", recognized),
                labelled("Party reorients", reorient),
                labelled("Resolution source", source),
                labelled("Source note", resolutionNote),
                submit);
            form.addEventListener("submit", event => {
                event.preventDefault();
                void runUiMutation(async () => {
                    applyRuntime(await api.resolveBoundaryDecision(runtime.id, {
                        expectedVersion: runtime.version,
                        recognizedLost: recognized.checked,
                        reorient: recognized.checked && reorient.checked,
                        resolutionSource: source.value as ResolutionSource,
                        resolutionNote: resolutionNote.value.trim() || undefined
                    }));
                });
            });

            const more = button("More options", () => openTravelWorkspace("advanced"));
            more.className = "hc-secondary-action";
            body.append(form, more);
        });
    };

    const openTravelReviewWorkspace = (): void => {
        if (!runtime.expedition.isSpatial) {
            openHistory();
            return;
        }
        const adjacency = currentAdjacency();
        if (!adjacency) return;
        const title = runtime.pauseReason === "BacktrackBoundaryReached"
            ? "Backtrack boundary"
            : "Changed travel conditions";
        openDrawer(title, body => {
            body.append(textElement(
                "p",
                pauseInstruction(runtime) ?? "Review the current travel conditions before travel continues.",
                "hc-muted"));

            const intendedEdge = preferences.direction === null
                ? null
                : adjacencyForIntent(adjacency, preferences.direction);
            if (!intendedEdge) {
                body.append(textElement(
                    "p",
                    "The previous course is not available from the current cell. Choose an adjacent course before resuming travel.",
                    "hc-muted"));
                const choose = button("Choose travel direction", () => {
                    closeDrawer();
                    queueMicrotask(focusTravelCourse);
                });
                choose.className = "hc-primary-action";
                const more = button("More options", () => openTravelWorkspace("advanced"));
                more.className = "hc-secondary-action";
                const row = document.createElement("div");
                row.className = "hc-button-row";
                row.append(choose, more);
                body.append(row);
                return;
            }

            body.append(contextLine(
                "Course",
                `${edgeCourseLabel(intendedEdge)} → cell ${intendedEdge.targetCell.q}, ${intendedEdge.targetCell.r}`));

            const form = document.createElement("form");
            form.className = "hc-form";
            const pace = createPaceControl();
            if (pace) {
                pace.required = true;
                form.append(labelled("Pace / travel mode", pace));
            } else {
                form.append(contextLine("Pace", humanize(preferences.pace)));
            }

            const submit = document.createElement("button");
            submit.type = "submit";
            submit.className = "hc-primary-action";
            submit.textContent = "Continue travel";
            form.append(submit);
            form.addEventListener("submit", event => {
                event.preventDefault();
                preferences.pace = pace?.value.trim() || preferences.pace;
                saveTravelPreferences(runtime.id, preferences);
                continueTravel(true);
            });

            const changeCourse = button("Change travel direction", () => {
                closeDrawer();
                queueMicrotask(focusTravelCourse);
            });
            changeCourse.className = "hc-secondary-action";
            const more = button("More options", () => openTravelWorkspace("advanced"));
            more.className = "hc-secondary-action";
            const row = document.createElement("div");
            row.className = "hc-button-row";
            row.append(changeCourse, more);
            body.append(form, row);
        });
    };

    const openNavigationWorkspace = (): void => {
        if (!runtime.expedition.isSpatial) {
            openHistory();
            return;
        }
        const adjacency = currentAdjacency();
        if (!adjacency) return;
        openDrawer("Navigation", body => {
            const state = runtime.expedition;
            const assigned = runtime.party.activityAssignments
                .filter(assignment => (assignment.roleKey ?? "").toLowerCase().includes("navig"))
                .map(assignment => runtime.party.members.find(member => member.id === assignment.participantId)?.name)
                .filter((name): name is string => Boolean(name));
            const navigationDue = navigationResolutionDue(runtime, false, false);
            const intendedEdge = preferences.direction === null
                ? null
                : adjacencyForIntent(adjacency, preferences.direction);
            body.append(
                textElement("h3", navigationDue ? "Navigation required" : "Navigation status"),
                contextLine("Navigator", assigned.join(", ") || "No navigator role assigned"),
                contextLine("Intended travel direction", intendedEdge
                    ? `${edgeCourseLabel(intendedEdge)} → cell ${intendedEdge.targetCell.q}, ${intendedEdge.targetCell.r}`
                    : "Not selected"),
                contextLine("Current navigation state", state.isLost
                    ? `Lost / off course · actual ${courseLabel(state.actualDirection)}`
                    : "On course"));

            if (!navigationDue) {
                body.append(textElement(
                    "p",
                    "No navigation resolution is due for the current watch state. Navigation changes remain tied to the procedure workflow.",
                    "hc-muted"));
                return;
            }

            if (!intendedEdge) {
                body.append(textElement(
                    "p",
                    "Choose an adjacent course before resolving navigation. The selected navigator edge or adjacent map cell will be reused by later travel stages.",
                    "hc-muted"));
                const choose = button("Choose travel direction", () => {
                    closeDrawer();
                    queueMicrotask(focusTravelCourse);
                });
                choose.className = "hc-primary-action";
                const more = button("Advanced travel controls", () => openTravelWorkspace("advanced"));
                more.className = "hc-secondary-action";
                const row = document.createElement("div");
                row.className = "hc-button-row";
                row.append(choose, more);
                body.append(row);
                return;
            }

            const form = document.createElement("form");
            form.className = "hc-form";

            const outcome = document.createElement("select");
            outcome.name = "navigationOutcome";
            outcome.innerHTML = '<option value="oriented">On course</option><option value="lost">Lost / off course</option>';
            outcome.value = state.isLost ? "lost" : "oriented";

            const veer = document.createElement("input");
            veer.name = "veerSteps";
            veer.type = "number";
            veer.step = "1";
            veer.value = state.isLost ? String(state.veerSteps) : "1";

            const source = document.createElement("select");
            source.name = "source";
            for (const [value, label] of [
                ["ManualRoll", "Manual roll"],
                ["ExternalSystem", "External system"],
                ["DmOverride", "DM ruling"],
                ["ProcedureDefault", "Procedure default"]
            ] as const) {
                const option = document.createElement("option");
                option.value = value;
                option.textContent = label;
                source.append(option);
            }

            const resolutionNote = document.createElement("input");
            resolutionNote.name = "resolutionNote";
            resolutionNote.placeholder = "optional source note";
            const note = document.createElement("input");
            note.name = "note";
            note.placeholder = "optional table note";

            const veerField = labelled("Resolved veer steps", veer);
            const syncOutcome = (): void => {
                const lost = outcome.value === "lost";
                veerField.hidden = !lost;
                if (!lost) veer.value = "0";
                else if (Number(veer.value) === 0) veer.value = "1";
            };
            outcome.addEventListener("change", syncOutcome);
            syncOutcome();

            const submit = button("Resolve navigation", () => {});
            submit.type = "submit";
            submit.className = "hc-primary-action";
            form.append(
                labelled("Resolution", outcome),
                veerField,
                labelled("Resolution source", source),
                labelled("Source note", resolutionNote),
                labelled("Table note", note),
                submit);
            form.addEventListener("submit", event => {
                event.preventDefault();
                const direction = intendedEdge.intentValue;
                const lost = outcome.value === "lost";
                const resolvedVeer = lost ? Number(veer.value) : 0;
                if (!Number.isInteger(resolvedVeer) || (lost && resolvedVeer === 0)) {
                    throw new Error("A lost navigation result requires a non-zero whole-step veer.");
                }
                void runUiMutation(async () => {
                    applyRuntime(await api.recordNavigationAssistant(runtime.id, {
                        expectedVersion: runtime.version,
                        isLost: lost,
                        veerSteps: resolvedVeer,
                        intendedDirection: direction,
                        resolutionSource: source.value as ResolutionSource,
                        resolutionNote: resolutionNote.value.trim() || undefined,
                        note: note.value.trim() || undefined
                    }));
                });
            });

            const changeCourse = button("Change travel direction", () => {
                closeDrawer();
                queueMicrotask(focusTravelCourse);
            });
            changeCourse.className = "hc-secondary-action";
            const more = button("Advanced travel controls", () => openTravelWorkspace("advanced"));
            more.className = "hc-secondary-action";
            const row = document.createElement("div");
            row.className = "hc-button-row";
            row.append(changeCourse, more);
            body.append(form, row);
        });
    };

    const applyTravelPreferences = (host: HTMLElement): void => {
        if (!runtime.expedition.isSpatial) return;
        const direction = host.querySelector<HTMLSelectElement>('select[name="direction"]');
        const pace = host.querySelector<HTMLInputElement | HTMLSelectElement>('[name="pace"]');
        const active = runtime.expedition.activeWatchNumber !== null;
        if (!active && direction && preferences.direction !== null) direction.value = String(preferences.direction);
        if (!active && pace && preferences.pace) pace.value = preferences.pace;
    };

    const captureTravelPacePreference = (host: HTMLElement): void => {
        const pace = host.querySelector<HTMLInputElement | HTMLSelectElement>('[name="pace"]');
        if (pace?.value.trim()) preferences.pace = pace.value.trim();
        saveTravelPreferences(runtime.id, preferences);
    };

    const openRepositionWorkspace = (target: HexCoordinate | null = selectedHex): void => {
        if (!runtime.expedition.isSpatial) {
            openHistory();
            return;
        }

        const initial = target ?? runtime.expedition.currentHex;
        openDrawer("Teleport party", body => {
            body.append(
                textElement(
                    "p",
                    "Teleport party directly repositions the party without resolving travel, navigation, encounters, survival, or journey progress. Use it for initial placement, teleportation, scene transitions, or corrections. It resets in-cell progress and navigation drift; an active travel watch is ended.",
                    "hc-muted"));

            const form = document.createElement("form");
            form.className = "hc-form";
            const q = document.createElement("input");
            q.type = "number";
            q.step = "1";
            q.required = true;
            q.value = String(initial.q);
            const r = document.createElement("input");
            r.type = "number";
            r.step = "1";
            r.required = true;
            r.value = String(initial.r);
            const note = document.createElement("input");
            note.placeholder = "optional reason, such as setup, teleportation, or correction";
            const submit = document.createElement("button");
            submit.type = "submit";
            submit.className = "hc-primary-action";
            submit.textContent = "Teleport party";
            form.append(
                labelled("Target q", q),
                labelled("Target r", r),
                labelled("Reason / note", note),
                submit);

            form.addEventListener("submit", event => {
                event.preventDefault();
                void runUiMutation(async () => {
                    if (!q.value.trim() || !r.value.trim()) {
                        throw new Error("Party position requires both axial cell coordinates.");
                    }
                    const targetQ = Number(q.value);
                    const targetR = Number(r.value);
                    if (!Number.isInteger(targetQ) || !Number.isInteger(targetR)) {
                        throw new Error("Party position requires whole axial cell coordinates.");
                    }
                    const destination = { q: targetQ, r: targetR };
                    const next = await api.repositionExpedition(runtime.id, {
                        expectedVersion: runtime.version,
                        targetHex: destination,
                        note: note.value.trim() || undefined
                    });
                    preferences.direction = null;
                    selectedHex = null;
                    selectedHexTracksTravelIntent = false;
                    saveTravelPreferences(runtime.id, preferences);
                    applyRuntime(next);
                });
            });
            body.append(form);
        });
    };

    const openPartyWorkspace = (): void => {
        openDrawer(runtime.expedition.isSpatial ? "Party & travel order" : "Party & activities", body => {
            body.classList.add("hc-page");
            const summary = document.createElement("div");
            summary.dataset.partySummary = "";
            const editor = document.createElement("div");
            editor.dataset.partyEditor = "";
            body.append(summary);
            if (!runtime.expedition.isSpatial
                && (runtime.movementComposition.contributors.length > 0
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
        focus: "all" | "attention" = "all"): void => {
        const capabilities = expeditionWorkspacePresentation(runtime, journey, survival).capabilities;
        const panelFocus = focus === "attention"
            ? survival?.forcedTravel.checkDue
                ? "forcedTravel"
                : "pendingResourceConsequences"
            : "all";
        const title = panelFocus === "forcedTravel"
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
                void effectPanel.sync();
                cleanups.push(() => effectPanel.dispose());
            }
            if (capabilities.resources || capabilities.survival) {
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

    const focusTravelCourse = (): void => {
        const selected = root.querySelector<HTMLButtonElement>(".hc-adjacency-interface.is-selected:not(:disabled)");
        const first = root.querySelector<HTMLButtonElement>(".hc-adjacency-interface:not(:disabled)");
        const fallback = root.querySelector<HTMLSelectElement>("[data-adjacency-select]");
        const target = selected ?? first ?? fallback;
        target?.scrollIntoView({ block: "nearest", inline: "nearest" });
        target?.focus();
    };

    const continueTravel = (resumeTravelReview = false): void => {
        if (!runtime.expedition.isSpatial || runtime.procedure.runtime === null) {
            openHistory();
            return;
        }
        const adjacency = currentAdjacency();
        const edge = preferences.direction === null || !adjacency
            ? null
            : adjacencyForIntent(adjacency, preferences.direction);
        const state = runtime.expedition;
        const active = state.activeWatchNumber !== null;
        const suppressesNavigation = active ? state.activeSuppressesNavigationCheck : false;
        const deliberateDoubleBack = active ? state.activeDeliberateDoubleBack : false;
        const effectiveDistance = authoritativeFixedWatchDistance(runtime);
        const target = spatialTravelContinuationTarget(
            runtime,
            edge !== null,
            effectiveDistance !== null,
            suppressesNavigation,
            deliberateDoubleBack,
            survivalAttention(survival),
            resumeTravelReview);
        switch (target) {
            case "course":
                focusTravelCourse();
                return;
            case "navigation":
                openNavigationWorkspace();
                return;
            case "encounter":
                if (runtime.pauseReason === "EncounterTriggered") openEncounterWorkspace();
                else openTravelWorkspace("encounter");
                return;
            case "movement":
                openTravelWorkspace("movement");
                return;
            case "boundary":
                openBoundaryWorkspace();
                return;
            case "review":
                openTravelReviewWorkspace();
                return;
            case "survival":
                openSurvivalWorkspace();
                return;
            case "unavailable":
                openHistory();
                return;
        }
        if (!edge || effectiveDistance === null) return;

        void runUiMutation(async () => {
            applyRuntime(await ExpeditionWatchController.continueResolvedTravel(
                api,
                runtime,
                edge.intentValue,
                preferences.pace));
        });
    };

    const activateAction = (kind: ReturnType<typeof expeditionWorkspacePresentation>["action"]["kind"]): void => {
        switch (kind) {
            case "encounter": openEncounterWorkspace(); break;
            case "navigation": openNavigationWorkspace(); break;
            case "boundary": openBoundaryWorkspace(); break;
            case "survival": openSurvivalWorkspace("attention"); break;
            case "travel": continueTravel(); break;
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
        map?.dispose();
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

function cellProgressIndicator(runtime: ExpeditionDetail): HTMLElement | null {
    if (!runtime.expedition.isSpatial || runtime.procedure.runtime?.tracksIntraHexProgress !== true) return null;

    const progress = runtime.expedition.hexProgress;
    const requirement = runtime.expedition.exitRequirement;
    const section = document.createElement("section");
    section.className = "hc-cell-progress";
    section.dataset.cellProgress = "";

    const heading = document.createElement("div");
    heading.className = "hc-progress-heading";
    heading.append(
        textElement("span", "Cell progress", "hc-ledger-kicker"),
        textElement(
            "strong",
            requirement && requirement.unit.symbol === progress.unit.symbol
                ? `${formatNumber(progress.value)} / ${formatNumber(requirement.value)} ${progress.unit.symbol}`
                : `${formatNumber(progress.value)} ${progress.unit.symbol}`));
    section.append(heading);

    if (requirement && requirement.unit.symbol === progress.unit.symbol && requirement.value > 0) {
        const meter = document.createElement("progress");
        meter.max = requirement.value;
        meter.value = Math.max(0, Math.min(progress.value, requirement.value));
        meter.setAttribute("aria-label", "Progress through the current cell");
        section.append(meter);
    } else {
        section.classList.add("is-unbounded");
        section.append(textElement(
            "span",
            requirement
                ? `Exit requirement uses ${requirement.unit.symbol}; no percentage is derived across unlike units.`
                : "No authoritative exit requirement is available, so no percentage is shown.",
            "hc-muted"));
    }
    return section;
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

function currentCourseLabel(
    preferences: TravelPreferences,
    adjacency: ReturnType<typeof currentRuntimeCellAdjacency> | null): string {
    return preferences.direction === null
        ? "No direction selected"
        : adjacencyCourseLabel(adjacency, preferences.direction);
}

function actualCourseLabel(
    runtime: ExpeditionDetail,
    adjacency: ReturnType<typeof currentRuntimeCellAdjacency> | null): string {
    if (!runtime.expedition.isSpatial || runtime.expedition.actualDirection === null) {
        return "Not yet resolved";
    }
    return adjacencyCourseLabel(adjacency, runtime.expedition.actualDirection);
}

function travelProgressDetail(runtime: ExpeditionDetail): string {
    if (!runtime.expedition.isSpatial) return "";
    if (runtime.expedition.activeWatchNumber !== null) {
        const remaining = runtime.expedition.activeWatchRemainingHours;
        return remaining === null
            ? `Watch ${runtime.expedition.activeWatchNumber} active`
            : `Watch ${runtime.expedition.activeWatchNumber} · ${formatHours(remaining)} remaining`;
    }
    return movementSuggestionDetail(runtime) ?? "Ready for next segment";
}

function travelIntentSummary(
    runtime: ExpeditionDetail,
    preferences: TravelPreferences,
    adjacency: ReturnType<typeof currentRuntimeCellAdjacency> | null): string {
    if (!runtime.expedition.isSpatial) return "";
    const intended = preferences.direction === null
        ? "No intended direction"
        : `Intended ${adjacencyCourseLabel(adjacency, preferences.direction)}`;
    const actual = runtime.expedition.actualDirection === null
        ? "actual direction not yet resolved"
        : `actual direction ${adjacencyCourseLabel(adjacency, runtime.expedition.actualDirection)}`;
    return `${intended} · ${preferences.pace} pace · ${actual}`;
}

function adjacencyCourseLabel(
    adjacency: ReturnType<typeof currentRuntimeCellAdjacency> | null,
    direction: number): string {
    const edge = adjacency ? adjacencyForIntent(adjacency, direction) : null;
    return edge ? edgeCourseLabel(edge) : directionLabel(direction);
}

function edgeCourseLabel(
    edge: ReturnType<typeof currentRuntimeCellAdjacency>["adjacencies"][number]): string {
    return edge.label;
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

function escapeHtml(value: string): string {
    return value
        .replaceAll("&", "&amp;")
        .replaceAll("<", "&lt;")
        .replaceAll(">", "&gt;")
        .replaceAll('"', "&quot;")
        .replaceAll("'", "&#39;");
}

function watchWorkspaceMarkup(adjacency: ReturnType<typeof currentRuntimeCellAdjacency> | null, travelModes: readonly string[], distanceUnit: string | null): string {
    return `
        <section class="hc-running-sheet hc-focused-watch-workspace">
            <div class="hc-sheet-ledger-heading">
                <h3 data-watch-summary>Run watch</h3>
                <span>Reusable travel direction and pace stay filled until changed.</span>
            </div>
            <div class="hc-form hc-watch-requirements" data-requirements></div>
            <form class="hc-form" data-advance>
                <fieldset data-plan-fields data-focus-group="advanced">
                    <legend>Travel intent</legend>
                    <label>Adjacent cell <select name="direction" required><option value="">Select adjacent cell</option>${directionOptions(adjacency)}</select></label>
                    <label>Pace / travel mode ${travelModeControlMarkup(travelModes)}</label>
                    <label>Navigation aid/context <input name="navigationAid" value="none"></label>
                    <label data-suppress-nav-row><input name="suppressNav" type="checkbox"> Navigation aid suppresses the check</label>
                    <label data-reset-veer-row><input name="resetVeer" type="checkbox"> Navigation aid resets veer at a boundary</label>
                    <label><input name="continueAcross" type="checkbox"> Continue automatically across unchanged boundaries</label>
                    <label data-double-back-row><input name="doubleBack" type="checkbox"> Deliberate single-hex double-back</label>
                    <p class="hc-hint" data-direction-hint></p>
                </fieldset>

                <fieldset data-resolution-helper hidden>
                    <legend>Automatic ruleset resolution</legend>
                    <p class="hc-hint">Use ruleset-defined helpers only for inputs that are still unresolved.</p>
                    <div data-helper-travel>
                        <p class="hc-hint" data-helper-travel-mechanic></p>
                        <p class="hc-hint">Travel uses the expected distance below as the situational input.</p>
                    </div>
                    <div data-helper-navigation class="hc-form">
                        <p class="hc-hint" data-helper-navigation-mechanic></p>
                        <label>Navigation DC <input name="helperNavigationDc" type="number" step="1" placeholder="DM-confirmed DC"></label>
                        <label>Navigation modifier <input name="helperNavigationModifier" type="number" step="1" placeholder="+0 if none"></label>
                        <label>Failure veer <input name="helperFailureVeer" type="number" step="1" placeholder="+1 or -1"></label>
                    </div>
                    <div data-helper-encounter><p class="hc-hint" data-helper-encounter-mechanic></p></div>
                    <button type="button" data-resolution-helper-button>Resolve available ruleset inputs</button>
                    <p class="hc-hint" data-resolution-helper-result aria-live="polite"></p>
                </fieldset>

                <fieldset data-travel-resolution data-focus-group="advanced movement">
                    <legend>Movement result</legend>
                    <p class="hc-hint">Enter the distance actually resolved for this travel period, using the unit shown below. Use a ruleset calculation or table roll when one is available; otherwise enter the distance decided at the table. Choose its source below. A blank value is not an automatic zero.</p>
                    <div data-fixed-distance><label>${distanceInputLabel("Effective distance", distanceUnit)} <input name="effectiveDistance" type="number" min="0" step="any"></label></div>
                    <div data-variable-distance><label>${distanceInputLabel("Expected distance", distanceUnit)} <input name="expectedDistance" type="number" min="0" step="any"></label><label>${distanceInputLabel("Actual resolved distance", distanceUnit)} <input name="actualDistance" type="number" min="0" step="any"></label></div>
                    <div data-step-distance><label>Resolved hex steps <input name="hexSteps" type="number" min="0" step="1"></label></div>
                    <label>Travel result source <select name="travelSource"></select></label>
                    <label>Travel source note <input name="travelNote" placeholder="optional"></label>
                </fieldset>

                <fieldset data-navigation-resolution data-focus-group="advanced">
                    <legend>Navigation</legend>
                    <label>Result <select name="navigationOutcome"><option value="">Select resolved result</option><option value="Succeeded">Succeeded</option><option value="Failed">Failed / lost</option></select></label>
                    <label data-veer-row>Resolved veer steps <input name="veerSteps" type="number" step="1" placeholder="+1 or -1"></label>
                    <label>Navigation result source <select name="navigationSource"></select></label>
                    <label>Navigation source note <input name="navigationNote" placeholder="optional"></label>
                </fieldset>

                <fieldset data-encounter-resolution data-focus-group="advanced encounter">
                    <legend>Encounter</legend>
                    <label>Resolved outcome <select name="encounterOutcome"><option value="">Select resolved outcome</option><option value="None">No encounter</option><option value="WanderingEncounter">Wandering encounter</option><option value="KeyedLocationDiscovery">Keyed location discovery</option><option value="ManualCustom">Manual / custom interruption</option></select></label>
                    <label data-encounter-hour>Occurs at hour within watch <input name="encounterHour" type="number" min="0" step="any"></label>
                    <label data-encounter-location>Keyed location <select name="locationId"><option value="">—</option></select></label>
                    <label>Encounter note <input name="encounterNote" placeholder="optional"></label>
                    <label>Encounter result source <select name="encounterSource"></select></label>
                    <label>Encounter source note <input name="encounterSourceNote" placeholder="optional"></label>
                </fieldset>

                <fieldset data-boundary-resolution data-focus-group="advanced">
                    <legend>Boundary decision</legend>
                    <label><input name="recognizedLost" type="checkbox"> The party recognizes that it is lost</label>
                    <label><input name="reorient" type="checkbox"> The party reorients</label>
                    <label>Decision source <select name="boundarySource"></select></label>
                    <label>Decision source note <input name="boundaryNote" placeholder="optional"></label>
                </fieldset>

                <details data-focus-group="advanced"><summary>DM authority / override</summary><div class="hc-form">
                    <label>Override note <input name="dmOverrideNote" placeholder="record unusual ruling or authoritative override"></label>
                </div></details>
                <button type="submit" class="hc-primary-action" data-advance-button>Run watch</button>
            </form>
        </section>`;
}

function travelModeControlMarkup(travelModes: readonly string[]): string {
    if (travelModes.length <= 1) {
        const fixed = travelModes[0] ?? "normal";
        return `<span class="hc-fixed-travel-mode">${escapeHtml(humanize(fixed))}</span><input name="pace" type="hidden" value="${escapeHtml(fixed)}">`;
    }
    return `<select name="pace">${travelModes
        .map(mode => `<option value="${escapeHtml(mode)}">${escapeHtml(humanize(mode))}</option>`)
        .join("")}</select>`;
}

function distanceInputLabel(label: string, unit: string | null): string {
    return unit ? `${escapeHtml(label)} (${escapeHtml(unit)})` : escapeHtml(label);
}

function directionOptions(adjacency: ReturnType<typeof currentRuntimeCellAdjacency> | null): string {
    if (!adjacency) return "";
    return adjacency.adjacencies
        .map(edge => `<option value="${edge.intentValue}">${edgeCourseLabel(edge)}</option>`)
        .join("");
}