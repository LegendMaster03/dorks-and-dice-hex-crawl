import type { HexCrawlApi } from "../../api";
import { blockInitiativeHandoffHref } from "../../encounter-handoff";
import { worldToHex } from "../../hex-math";
import { JourneyApi } from "../../journey-api";
import type { ExpeditionJourneyState } from "../../journey-types";
import { MapSurface } from "../../map-surface";
import { ensurePhase15Styles } from "../../phase15-styles";
import { discoveredSubjectIds, directionLabel, formatDistance, formatHours } from "../../runtime-view";
import { SurvivalResourcesApi } from "../../survival-api";
import type { SurvivalResources } from "../../survival-types";
import { canonicalExpeditionRoute } from "../../tool-route";
import type { ExpeditionDetail, HexCoordinate, Overworld, ToolHostContext } from "../../types";
import { clearUiError, showUiError } from "../../ui-error";
import { badge, disclosure, openWorkspaceDrawer, statAction, textElement, type WorkspaceDrawer } from "../../ui/workspace";
import { ExpeditionEnvironmentPanel } from "./environment-panel";
import { ExpeditionJourneyPanel } from "./journey-panel";
import { ExpeditionPartySheetController } from "./expedition-party-sheet";
import { publishExpeditionRuntimeChanged } from "./expedition-runtime-events";
import { ExpeditionSurvivalResourcesPanel } from "./survival-resources-panel";
import { ExpeditionWatchController } from "./expedition-watch-controller";
import { canUseFocusedNonSpatialWatch, focusedIntervalHours } from "./focused-interval-policy";
import { expeditionWorkspacePresentation } from "./expedition-workspace-model";

export type ExpeditionViewMode = "map" | "tracker";

type TravelPreferences = {
    direction: number | null;
    pace: string;
};

export async function renderExpedition(
    root: HTMLElement,
    api: HexCrawlApi,
    expeditionId: string,
    _mode: ExpeditionViewMode,
    navigate: (route: string, replace?: boolean) => void,
    routeWorldId?: string,
    toolContext: ToolHostContext | null = null): Promise<() => void> {
    ensurePhase15Styles();
    root.classList.add("hc-phase15");

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
    const journeyApi = new JourneyApi(toolContext);
    let survival = await safeLoad(() => survivalApi.get(expeditionId));
    let journey = await safeLoad(() => journeyApi.get(expeditionId));
    let map: MapSurface | null = null;
    let drawer: WorkspaceDrawer | null = null;
    let drawerCleanup: (() => void) | null = null;
    let disposed = false;
    let selectedHex: HexCoordinate | null = null;
    let preferences = loadTravelPreferences(runtime);

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
        [survival, journey] = await Promise.all([
            safeLoad(() => survivalApi.get(expeditionId)),
            safeLoad(() => journeyApi.get(expeditionId))
        ]);
    };

    const applyRuntime = (next: ExpeditionDetail): void => {
        runtime = next;
        preferences = mergeRuntimePreferences(runtime, preferences);
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
        cleanupDrawer();
        drawer = null;

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
        nav.append(button("DM tools", () => navigate("/")));
        if (runtime.overworldId) {
            nav.append(button("World authoring", () => navigate(`/worlds/${runtime.overworldId}/edit`)));
        }
        nav.append(button("Procedure reference", () =>
            navigate(`/procedures/${encodeURIComponent(runtime.procedure.procedureId)}/revisions/${runtime.procedure.revision}/reference`)));
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
        stats.append(
            statAction("Time", presentation.timeLabel, null, openHistory),
            statAction(
                runtime.expedition.isSpatial ? "Position / course" : "Context",
                presentation.routeLabel ?? runtime.context.name,
                runtime.expedition.isSpatial ? travelPreferenceDetail(runtime, preferences) : "Non-spatial expedition",
                runtime.expedition.isSpatial ? () => openTravelWorkspace() : openHistory,
                runtime.pauseReason ? "warning" : "neutral"),
            statAction(
                "Party",
                `${runtime.party.members.length} member${runtime.party.members.length === 1 ? "" : "s"}`,
                partyActivitySummary(runtime),
                openPartyWorkspace),
            statAction(
                "Movement",
                movementSummary(runtime),
                runtime.movementComposition.missingInputs.length > 0
                    ? `${runtime.movementComposition.missingInputs.length} unresolved input${runtime.movementComposition.missingInputs.length === 1 ? "" : "s"}`
                    : movementSuggestionDetail(runtime),
                runtime.expedition.isSpatial ? () => openTravelWorkspace() : openPartyWorkspace,
                runtime.movementComposition.missingInputs.length > 0 ? "warning" : "neutral"));
        if (presentation.capabilities.navigation && presentation.navigationLabel) {
            stats.append(statAction(
                "Navigation",
                presentation.navigationLabel,
                runtime.expedition.isSpatial && runtime.expedition.intendedDirection !== null
                    ? `Intended ${directionLabel(runtime.expedition.intendedDirection)}`
                    : null,
                () => openTravelWorkspace(),
                runtime.expedition.isSpatial && runtime.expedition.isLost ? "warning" : "neutral"));
        }
        if (presentation.capabilities.resources || presentation.capabilities.survival || presentation.capabilities.effects) {
            stats.append(statAction(
                "Survival / resources",
                presentation.resourceLabel ?? "Procedure active",
                survivalDetail(survival),
                openSurvivalWorkspace,
                survivalAttention(survival) ? "warning" : "neutral"));
        }
        if (presentation.capabilities.journey) {
            stats.append(statAction(
                "Journey",
                presentation.journeyLabel ?? "No active journey",
                journeyDetail(journey),
                openJourneyWorkspace,
                journeyAttention(journey) ? "warning" : "neutral"));
        }
        if (presentation.capabilities.encounters) {
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
    };

    const renderCurrentAction = (action: ReturnType<typeof expeditionWorkspacePresentation>["action"]): HTMLElement => {
        const section = document.createElement("section");
        section.className = "hc-current-action";
        const header = document.createElement("header");
        header.append(textElement("h2", "Next action"), badge(action.urgent ? "Needs resolution" : "Ready", action.urgent ? "warning" : "good"));
        section.append(header, textElement("p", action.label, "hc-current-action-primary"), textElement("p", action.detail, "hc-current-action-detail"));
        const row = document.createElement("div");
        row.className = "hc-button-row";
        const primary = button(primaryActionLabel(action.kind), () => activateAction(action.kind));
        primary.className = "hc-primary-action";
        row.append(primary);
        if (runtime.expedition.isSpatial && runtime.procedure.runtime !== null && action.kind !== "encounter") {
            row.append(button("Travel controls", () => openTravelWorkspace()));
        }
        section.append(row);
        return section;
    };

    const renderSpatialWorkspace = (): HTMLElement => {
        const section = document.createElement("section");
        section.className = world ? "hc-workspace-grid" : "hc-nonspatial-primary";

        const primary = document.createElement("div");
        primary.className = "hc-panel hc-map-panel";
        primary.append(textElement("h2", world ? "Expedition map" : "Spatial expedition"));
        if (world) {
            const host = document.createElement("div");
            host.className = "hc-map-host";
            host.dataset.map = "";
            primary.append(host);
            const context = document.createElement("div");
            context.dataset.mapContext = "";
            context.className = "hc-context-card";
            primary.append(context);
        } else {
            primary.append(textElement("p", "This crawl uses spatial hex state without a world map. Use the directional travel controls to set intent.", "hc-muted"));
        }
        primary.append(renderDirectionControls());

        const secondary = document.createElement("aside");
        secondary.className = "hc-panel hc-sidebar";
        secondary.append(
            textElement("h2", "At the table"),
            actionCard("Party & activities", partyCardDetail(runtime), openPartyWorkspace),
            actionCard("Current environment", environmentCardDetail(runtime), openEnvironmentWorkspace));
        if (expeditionWorkspacePresentation(runtime, journey, survival).capabilities.journey) {
            secondary.append(actionCard("Journey / challenge", journeyDetail(journey), openJourneyWorkspace));
        }
        if (expeditionWorkspacePresentation(runtime, journey, survival).capabilities.resources
            || expeditionWorkspacePresentation(runtime, journey, survival).capabilities.survival) {
            secondary.append(actionCard("Survival & resources", survivalDetail(survival), openSurvivalWorkspace));
        }
        section.append(primary, secondary);
        return section;
    };

    const renderNonSpatialWorkspace = (): HTMLElement => {
        const section = document.createElement("section");
        section.className = "hc-nonspatial-primary";
        const main = document.createElement("article");
        main.className = "hc-panel hc-journey-primary";
        main.append(textElement("h2", "Expedition procedure"));
        if (journey?.processPolicy.support === "Supported") {
            main.append(textElement("p", journeyDetail(journey)));
            const control = button("Open journey workspace", openJourneyWorkspace);
            control.className = "hc-primary-action";
            main.append(control);
        } else if (canUseFocusedNonSpatialWatch(runtime)) {
            main.append(textElement("p", "This procedure advances time without fabricating map or direction state."));
            const control = button("Run watch / time", openNonSpatialWatchWorkspace);
            control.className = "hc-primary-action";
            main.append(control);
        } else {
            main.append(textElement("p", "This structural expedition has no executable spatial or interval action. Use the procedure reference and focused GM tools for manual play."));
        }
        const side = document.createElement("aside");
        side.className = "hc-panel";
        side.append(
            textElement("h2", "Campaign state"),
            actionCard("Party & activities", partyCardDetail(runtime), openPartyWorkspace));
        if (expeditionWorkspacePresentation(runtime, journey, survival).capabilities.resources
            || expeditionWorkspacePresentation(runtime, journey, survival).capabilities.survival) {
            side.append(actionCard("Survival & resources", survivalDetail(survival), openSurvivalWorkspace));
        }
        section.append(main, side);
        return section;
    };

    const renderDirectionControls = (): HTMLElement => {
        const section = document.createElement("section");
        section.className = "hc-direction-control";
        section.append(
            textElement("h3", "Travel direction"),
            textElement("p", "Choose an adjacent direction to express travel intent. This does not move the party by itself; the existing procedure runtime still resolves distance, navigation, boundaries, encounters, terrain, and consequences.", "hc-muted"));
        const grid = document.createElement("div");
        grid.className = "hc-direction-grid";
        grid.setAttribute("role", "group");
        grid.setAttribute("aria-label", "Adjacent hex travel direction");
        for (let direction = 0; direction < 6; direction += 1) {
            const control = button(directionLabel(direction), () => {
                preferences.direction = direction;
                saveTravelPreferences(runtime.id, preferences);
                openTravelWorkspace(direction);
            });
            control.dataset.direction = String(direction);
            control.setAttribute("aria-pressed", preferences.direction === direction ? "true" : "false");
            if (preferences.direction === direction) control.classList.add("hc-active-view");
            grid.append(control);
        }
        section.append(grid, textElement(
            "p",
            `Reusable intent: ${preferences.direction === null ? "no course selected" : directionLabel(preferences.direction)} · pace ${preferences.pace}.`,
            "hc-muted"));
        return section;
    };

    const renderGmTools = (): HTMLElement => {
        const tools = disclosure("GM Tools", false);
        const row = document.createElement("div");
        row.className = "hc-button-row hc-gm-tools";
        row.append(
            button("Party & travel order", openPartyWorkspace),
            button("Environment", openEnvironmentWorkspace),
            button("Survival & resources", openSurvivalWorkspace),
            button("Journey", openJourneyWorkspace),
            button("History", openHistory),
            button("Procedure reference", () =>
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
            host.append(textElement("p", "Select a hex to inspect it. Selecting a hex does not mutate expedition state.", "hc-muted"));
            return;
        }

        host.append(textElement("h3", `Selected hex ${selectedHex.q}, ${selectedHex.r}`));
        const direction = adjacentDirection(runtime.expedition.currentHex, selectedHex);
        if (direction !== null) {
            const travel = button(`Travel ${directionLabel(direction)}`, () => {
                preferences.direction = direction;
                saveTravelPreferences(runtime.id, preferences);
                openTravelWorkspace(direction);
            });
            travel.className = "hc-primary-action";
            host.append(
                textElement("p", "This adjacent hex can be used as the intended course. The runtime will still resolve whether and how the party reaches it.", "hc-muted"),
                travel);
        } else if (!sameHex(runtime.expedition.currentHex, selectedHex)) {
            host.append(textElement("p", "Travel intent is expressed one adjacent direction at a time so procedure mechanics can resolve partial progress and interruptions.", "hc-muted"));
        }

        const subjects = [
            ...currentWorld.locations
                .filter(item => sameHex(worldToHex(currentWorld.grid, item.position), selectedHex!))
                .map(item => ({ id: item.id, name: item.name, type: "Location" as const })),
            ...currentWorld.features
                .filter(item => item.kind === "Point" && item.position && sameHex(worldToHex(currentWorld.grid, item.position), selectedHex!))
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
                selectedHex = hex;
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
        cleanupDrawer();
        drawer?.close();
        drawer = openWorkspaceDrawer(root, title, body => {
            const cleanup = build(body);
            drawerCleanup = cleanup ?? null;
        }, null, cleanupDrawer);
    };

    const openTravelWorkspace = (direction = preferences.direction): void => {
        if (!runtime.expedition.isSpatial || runtime.procedure.runtime === null) {
            openHistory();
            return;
        }
        if (direction !== null) {
            preferences.direction = direction;
            saveTravelPreferences(runtime.id, preferences);
        }
        openDrawer("Travel / watch", body => {
            body.innerHTML = watchWorkspaceMarkup();
            const controller = new ExpeditionWatchController(
                body,
                api,
                world,
                () => runtime,
                applyRuntime,
                async action => {
                    await mutate(async () => {
                        await action();
                        captureTravelPreferences(body);
                    });
                });
            controller.sync(runtime);
            applyTravelPreferences(body);
            const directionControl = body.querySelector<HTMLSelectElement>('select[name="direction"]');
            const paceControl = body.querySelector<HTMLInputElement>('input[name="pace"]');
            directionControl?.addEventListener("change", captureTravelPreferencesFromControls);
            paceControl?.addEventListener("change", captureTravelPreferencesFromControls);
            return () => controller.dispose();

            function captureTravelPreferencesFromControls(): void {
                captureTravelPreferences(body);
            }
        });
    };

    const applyTravelPreferences = (host: HTMLElement): void => {
        if (!runtime.expedition.isSpatial) return;
        const direction = host.querySelector<HTMLSelectElement>('select[name="direction"]');
        const pace = host.querySelector<HTMLInputElement>('input[name="pace"]');
        const active = runtime.expedition.activeWatchNumber !== null;
        if (!active && direction && preferences.direction !== null) direction.value = String(preferences.direction);
        if (!active && pace && preferences.pace) pace.value = preferences.pace;
    };

    const captureTravelPreferences = (host: HTMLElement): void => {
        const direction = host.querySelector<HTMLSelectElement>('select[name="direction"]');
        const pace = host.querySelector<HTMLInputElement>('input[name="pace"]');
        if (direction?.value !== undefined && direction.value !== "") {
            const parsed = Number(direction.value);
            if (Number.isInteger(parsed) && parsed >= 0 && parsed <= 5) preferences.direction = parsed;
        }
        if (pace?.value.trim()) preferences.pace = pace.value.trim();
        saveTravelPreferences(runtime.id, preferences);
    };

    const openPartyWorkspace = (): void => {
        openDrawer("Party & travel order", body => {
            body.classList.add("hc-page");
            const summary = document.createElement("div");
            summary.dataset.partySummary = "";
            const editor = document.createElement("div");
            editor.dataset.partyEditor = "";
            body.append(summary, editor);
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

    const openSurvivalWorkspace = (): void => {
        openDrawer("Survival & resources", body => {
            body.classList.add("hc-page");
            const panel = new ExpeditionSurvivalResourcesPanel(
                body,
                survivalApi,
                runtime.id,
                async (_control, action) => {
                    await runUiMutation(action);
                });
            void panel.sync();
            queueMicrotask(() => {
                const details = body.querySelector<HTMLDetailsElement>("[data-survival-resources-panel]");
                if (details) details.open = true;
            });
            return () => panel.dispose();
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
            const triggered = [...runtime.history].reverse().find(event => event.kind === "EncounterTriggered");
            if (!triggered) {
                body.append(textElement("p", "No encounter is currently interrupting the expedition."));
                return;
            }
            body.append(
                textElement("p", triggered.message),
                textElement("p", "Preparing a Block Initiative handoff does not consume or clear expedition consequences. After the encounter is resolved, return here and resume the current travel procedure.", "hc-muted"));
            const row = document.createElement("div");
            row.className = "hc-button-row";
            const handoff = button("Open in Block Initiative", () => void prepareEncounterHandoff(triggered.sequence, handoff));
            handoff.className = "hc-primary-action";
            const resume = button("Resume travel after encounter", () => openTravelWorkspace());
            row.append(handoff, resume);
            body.append(row);
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
        openDrawer("Watch / time", body => {
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
            if (runtime.history.length === 0) {
                body.append(textElement("p", "No expedition history has been recorded yet."));
                return;
            }
            const list = document.createElement("ol");
            list.className = "hc-history";
            for (const event of [...runtime.history].reverse().slice(0, 80)) {
                const item = document.createElement("li");
                item.textContent = `#${event.sequence} · ${formatHours(event.expeditionElapsedHours)} · ${event.message}`;
                list.append(item);
            }
            body.append(list);
        });
    };

    const activateAction = (kind: ReturnType<typeof expeditionWorkspacePresentation>["action"]["kind"]): void => {
        switch (kind) {
            case "encounter": openEncounterWorkspace(); break;
            case "navigation":
            case "travel": openTravelWorkspace(); break;
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

function primaryActionLabel(kind: ReturnType<typeof expeditionWorkspacePresentation>["action"]["kind"]): string {
    switch (kind) {
        case "encounter": return "Resolve encounter";
        case "navigation": return "Resolve navigation";
        case "journey": return "Open journey";
        case "travel": return "Continue travel";
        case "watch": return "Run watch";
        default: return "View procedure";
    }
}

function actionCard(title: string, detail: string, action: () => void): HTMLElement {
    const card = document.createElement("article");
    card.className = "hc-context-card";
    card.append(textElement("h3", title), textElement("p", detail, "hc-muted"), button("Open", action));
    return card;
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

function environmentCardDetail(runtime: ExpeditionDetail): string {
    if (!runtime.expedition.isSpatial) return "No spatial environment context.";
    return `Hex ${runtime.expedition.currentHex.q}, ${runtime.expedition.currentHex.r} · edit resolved environment only when it matters to procedure inputs.`;
}

function survivalDetail(state: SurvivalResources | null): string {
    if (!state) return "Survival state unavailable.";
    const parts: string[] = [];
    if (state.resources.length > 0) parts.push(`${state.resources.length} resource${state.resources.length === 1 ? "" : "s"}`);
    if (state.forcedTravel.checkDue) parts.push("forced-travel check due");
    if (state.pendingResourceConsequences.length > 0) parts.push(`${state.pendingResourceConsequences.length} pending consequence${state.pendingResourceConsequences.length === 1 ? "" : "s"}`);
    if (state.exposure.length > 0) parts.push(`${state.exposure.length} exposure track${state.exposure.length === 1 ? "" : "s"}`);
    return parts.join(" · ") || "No survival resolution currently needs attention.";
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
    return Boolean(state?.activeProcesses.some(process => process.status === "ResolutionRequired" || process.pendingActions.length > 0));
}

function encounterSummary(runtime: ExpeditionDetail): string {
    const latest = [...runtime.history].reverse().find(event => event.kind.includes("Encounter"));
    return latest ? latest.message : "No current encounter";
}

function travelPreferenceDetail(runtime: ExpeditionDetail, preferences: TravelPreferences): string {
    if (!runtime.expedition.isSpatial) return "";
    const course = preferences.direction === null ? "course not set" : directionLabel(preferences.direction);
    return `${course} · ${preferences.pace} pace`;
}

function loadTravelPreferences(runtime: ExpeditionDetail): TravelPreferences {
    const fallback: TravelPreferences = {
        direction: runtime.expedition.isSpatial ? runtime.expedition.intendedDirection : null,
        pace: runtime.expedition.isSpatial ? runtime.expedition.activePaceKey ?? "normal" : "normal"
    };
    try {
        const raw = localStorage.getItem(`hex-crawl.expedition.${runtime.id}.travel-intent`);
        if (!raw) return fallback;
        const parsed = JSON.parse(raw) as Partial<TravelPreferences>;
        return {
            direction: Number.isInteger(parsed.direction) && Number(parsed.direction) >= 0 && Number(parsed.direction) <= 5
                ? Number(parsed.direction)
                : fallback.direction,
            pace: typeof parsed.pace === "string" && parsed.pace.trim() ? parsed.pace.trim() : fallback.pace
        };
    } catch {
        return fallback;
    }
}

function mergeRuntimePreferences(runtime: ExpeditionDetail, current: TravelPreferences): TravelPreferences {
    if (!runtime.expedition.isSpatial) return current;
    return {
        direction: runtime.expedition.intendedDirection ?? current.direction,
        pace: runtime.expedition.activePaceKey ?? current.pace
    };
}

function saveTravelPreferences(expeditionId: string, preferences: TravelPreferences): void {
    try {
        localStorage.setItem(`hex-crawl.expedition.${expeditionId}.travel-intent`, JSON.stringify(preferences));
    } catch {
        // Persistent UI preferences are optional; runtime state remains authoritative.
    }
}

export function adjacentDirection(from: HexCoordinate, to: HexCoordinate): number | null {
    const dq = to.q - from.q;
    const dr = to.r - from.r;
    const directions = [
        [1, 0],
        [1, -1],
        [0, -1],
        [-1, 0],
        [-1, 1],
        [0, 1]
    ] as const;
    const index = directions.findIndex(([q, r]) => q === dq && r === dr);
    return index < 0 ? null : index;
}

function sameHex(left: HexCoordinate, right: HexCoordinate): boolean {
    return left.q === right.q && left.r === right.r;
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

function watchWorkspaceMarkup(): string {
    return `
        <section class="hc-running-sheet hc-focused-watch-workspace">
            <div class="hc-sheet-ledger-heading">
                <h3 data-watch-summary>Run watch</h3>
                <span>Reusable course and pace stay filled until changed.</span>
            </div>
            <div class="hc-form hc-watch-requirements" data-requirements></div>
            <form class="hc-form" data-advance>
                <fieldset data-plan-fields data-focus-group="travel navigation">
                    <legend>Travel intent</legend>
                    <label>Direction <select name="direction" required><option value="">Select direction</option>${directionOptions()}</select></label>
                    <label>Pace / travel mode <input name="pace" value="normal"></label>
                    <label>Navigation aid/context <input name="navigationAid" value="none"></label>
                    <label data-suppress-nav-row><input name="suppressNav" type="checkbox"> Navigation aid suppresses the check</label>
                    <label data-reset-veer-row><input name="resetVeer" type="checkbox"> Navigation aid resets veer at a boundary</label>
                    <label><input name="continueAcross" type="checkbox"> Continue automatically across unchanged boundaries</label>
                    <label data-double-back-row><input name="doubleBack" type="checkbox"> Deliberate single-hex double-back</label>
                    <p class="hc-hint" data-direction-hint></p>
                </fieldset>

                <fieldset data-resolution-helper hidden>
                    <legend>Automatic procedure resolution</legend>
                    <p class="hc-hint">Use procedure-defined helpers only for inputs that are still unresolved.</p>
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
                    <button type="button" data-resolution-helper-button>Resolve available procedure inputs</button>
                    <p class="hc-hint" data-resolution-helper-result aria-live="polite"></p>
                </fieldset>

                <fieldset data-travel-resolution data-focus-group="travel">
                    <legend>Movement result</legend>
                    <p class="hc-hint">Authoritative party movement is prefilled when available. Enter only movement information the runtime can not derive.</p>
                    <div data-fixed-distance><label>Effective distance <input name="effectiveDistance" type="number" min="0" step="any"></label></div>
                    <div data-variable-distance><label>Expected distance <input name="expectedDistance" type="number" min="0" step="any"></label><label>Actual resolved distance <input name="actualDistance" type="number" min="0" step="any"></label></div>
                    <div data-step-distance><label>Resolved hex steps <input name="hexSteps" type="number" min="0" step="1"></label></div>
                    <label>Travel result source <select name="travelSource"></select></label>
                    <label>Travel source note <input name="travelNote" placeholder="optional"></label>
                </fieldset>

                <fieldset data-navigation-resolution data-focus-group="navigation">
                    <legend>Navigation</legend>
                    <label>Result <select name="navigationOutcome"><option value="">Select resolved result</option><option value="Succeeded">Succeeded</option><option value="Failed">Failed / lost</option></select></label>
                    <label data-veer-row>Resolved veer steps <input name="veerSteps" type="number" step="1" placeholder="+1 or -1"></label>
                    <label>Navigation result source <select name="navigationSource"></select></label>
                    <label>Navigation source note <input name="navigationNote" placeholder="optional"></label>
                </fieldset>

                <fieldset data-encounter-resolution data-focus-group="encounters">
                    <legend>Encounter</legend>
                    <label>Resolved outcome <select name="encounterOutcome"><option value="">Select resolved outcome</option><option value="None">No encounter</option><option value="WanderingEncounter">Wandering encounter</option><option value="KeyedLocationDiscovery">Keyed location discovery</option><option value="ManualCustom">Manual / custom interruption</option></select></label>
                    <label data-encounter-hour>Occurs at hour within watch <input name="encounterHour" type="number" min="0" step="any"></label>
                    <label data-encounter-location>Keyed location <select name="locationId"><option value="">—</option></select></label>
                    <label>Encounter note <input name="encounterNote" placeholder="optional"></label>
                    <label>Encounter result source <select name="encounterSource"></select></label>
                    <label>Encounter source note <input name="encounterSourceNote" placeholder="optional"></label>
                </fieldset>

                <fieldset data-boundary-resolution data-focus-group="navigation">
                    <legend>Boundary decision</legend>
                    <label><input name="recognizedLost" type="checkbox"> The party recognizes that it is lost</label>
                    <label><input name="reorient" type="checkbox"> The party reorients</label>
                    <label>Decision source <select name="boundarySource"></select></label>
                    <label>Decision source note <input name="boundaryNote" placeholder="optional"></label>
                </fieldset>

                <details><summary>DM authority / override</summary><div class="hc-form">
                    <label>Override note <input name="dmOverrideNote" placeholder="record unusual ruling or authoritative override"></label>
                </div></details>
                <button type="submit" class="hc-primary-action" data-advance-button>Run watch</button>
            </form>
        </section>`;
}

function directionOptions(): string {
    return [0, 1, 2, 3, 4, 5]
        .map(value => `<option value="${value}">${directionLabel(value)}</option>`)
        .join("");
}