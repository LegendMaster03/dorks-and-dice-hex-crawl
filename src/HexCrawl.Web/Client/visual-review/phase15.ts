// @ts-nocheck
import { ensurePartyResponsiveStyles } from "../src/party-responsive-styles";
import { ensureStyles } from "../src/styles";
import { renderExpedition } from "../src/modules/expeditions/expedition-view";
import { renderToolHome } from "../src/modules/home/tool-home-view";
import { renderProcedureAuthoringWorkspace } from "../src/modules/procedures/procedure-authoring-view";

const params = new URLSearchParams(window.location.search);
const stateName = params.get("state") || "no-course";
const theme = params.get("theme") === "dark" ? "dark" : "light";
const containerWidth = Number(params.get("containerWidth") || "0");
document.documentElement.dataset.bsTheme = theme;

const root = document.getElementById("tool-root");
if (!(root instanceof HTMLElement)) throw new Error("Missing visual-review root.");
if (Number.isFinite(containerWidth) && containerWidth > 0) {
    root.style.width = containerWidth + "px";
    root.style.maxWidth = containerWidth + "px";
}
ensureStyles();
ensurePartyResponsiveStyles();

const responseJson = value => new Response(JSON.stringify(value), {
    status: 200,
    headers: { "Content-Type": "application/json" }
});

function composerFixture() {
    const duration = {
        type: "duration",
        required: true,
        description: "Length of the repeating expedition interval.",
        defaultValue: "4h"
    };
    const mechanic = {
        key: "time.interval.standard",
        displayName: "Repeating expedition interval",
        description: "Advance expedition procedure state in a repeating interval.",
        version: 1,
        executionHandler: "visual-review",
        automationLevel: "Assisted",
        executionSupport: "Native",
        inputs: [],
        outputs: ["time.interval"],
        parameterSchema: { durationTicks: duration },
        compatibilityTags: []
    };
    return {
        procedureId: "visual-procedure",
        revision: 3,
        key: "visual-procedure",
        name: "Shattered Marches Procedure",
        isExecutable: true,
        modificationCount: 0,
        modifiedModuleCount: 0,
        origin: null,
        modules: [{
            moduleKey: "time.interval",
            category: "Time",
            displayName: "Travel period",
            purpose: "Set the repeating expedition interval.",
            executionStage: "Time",
            reads: [],
            produces: ["time.interval"],
            requiredDependencies: [],
            optionalDependencies: [],
            presentationMetadata: {},
            mechanic,
            alternatives: [],
            configurationSchema: { durationTicks: duration },
            parameters: { durationTicks: "4h" },
            requiredInputs: [],
            outputs: ["time.interval"],
            dependencyIssues: [],
            isModified: false,
            modificationCount: 0,
            validationIssues: []
        }],
        dependencies: { hasErrors: false, issues: [] },
        dependencyFixes: [],
        overrides: []
    };
}

const savedProcedure = {
    procedureId: "visual-procedure",
    revision: 3,
    key: "visual-procedure",
    name: "Shattered Marches Procedure",
    campaignId: null,
    originPresetKey: null,
    originPresetDisplayName: null,
    isExecutable: true,
    moduleCount: 1,
    createdAt: "2026-10-06T04:00:00Z"
};

function installProcedureFetch() {
    const original = window.fetch.bind(window);
    window.fetch = async input => {
        const url = typeof input === "string" ? input : input instanceof Request ? input.url : String(input);
        if (url.endsWith("/api/procedures")) return responseJson([savedProcedure]);
        if (url.endsWith("/api/procedures/visual-procedure/revisions")) {
            return responseJson([{
                procedureId: "visual-procedure",
                revision: 3,
                name: "Shattered Marches Procedure",
                modificationCount: 0,
                createdAt: "2026-10-06T04:00:00Z"
            }]);
        }
        if (url.includes("/api/procedures/composer/canonical/draft")) {
            return responseJson({
                procedureId: "visual-procedure",
                revision: 3,
                canonicalJson: JSON.stringify({
                    procedureId: "visual-procedure",
                    revision: 3,
                    name: "Shattered Marches Procedure",
                    modules: [{ moduleKey: "time.interval", parameters: { durationTicks: "4h" } }]
                }, null, 2)
            });
        }
        if (url.includes("/api/procedures/composer/draft") || url.includes("/api/procedures/visual-procedure")) {
            return responseJson(composerFixture());
        }
        return new Response(JSON.stringify({ detail: "visual-review procedure stub" }), {
            status: 404,
            headers: { "Content-Type": "application/json" }
        });
    };
    return () => { window.fetch = original; };
}

function emitSpecialMetrics(surface) {
    const buttons = Array.from(root.querySelectorAll("button"));
    const text = root.textContent || "";
    const metrics = {
        state: stateName,
        surface,
        theme,
        viewportWidth: window.innerWidth,
        viewportHeight: window.innerHeight,
        reviewWidth: Math.round(root.getBoundingClientRect().width),
        reviewScrollWidth: root.scrollWidth,
        scrollWidth: document.documentElement.scrollWidth,
        scrollHeight: document.documentElement.scrollHeight,
        pageTitle: root.querySelector("h1")?.textContent?.trim() || null,
        openExpeditionButtons: buttons.filter(button => button.textContent?.trim() === "Open expedition").length,
        startExpeditionVisible: text.includes("Start expedition"),
        manageProceduresVisible: text.includes("Manage procedures"),
        manageWorldsVisible: text.includes("Manage worlds"),
        gmUtilitiesVisible: text.includes("GM utilities"),
        compactVisible: Boolean(root.querySelector(".hc-compact-procedure")),
        advancedVisible: Boolean(root.querySelector(".hc-advanced-layout")),
        jsonVisible: Boolean(root.querySelector(".hc-json-editor")),
        procedureHomeVisible: text.includes("Saved procedures") && text.includes("Build my own"),
        technicalModeLabels: ["Compact", "Advanced", "JSON"].filter(label => text.includes(label)).length
    };
    document.getElementById("review-metrics").textContent = JSON.stringify(metrics);
    document.documentElement.dataset.visualReviewReady = "true";
}

if (stateName === "home") {
    const restoreFetch = installProcedureFetch();
    const api = {
        listExpeditions: async () => [{
            id: "visual-expedition",
            context: { kind: "WorldBound", name: "The Shattered Marches", overworldId: "visual-world" },
            name: "Crossing the Shattered Marches",
            procedureName: "Shattered Marches Procedure",
            version: 42,
            createdAt: "2026-10-05T12:00:00Z",
            updatedAt: "2026-10-06T04:00:00Z"
        }],
        listOverworlds: async () => [{
            id: "visual-world",
            name: "The Shattered Marches",
            version: 8,
            createdAt: "2026-10-01T12:00:00Z",
            updatedAt: "2026-10-06T04:00:00Z"
        }],
        getProcedurePresets: async () => []
    };
    await renderToolHome(root, api, () => {});
    emitSpecialMetrics("home");
    restoreFetch();
} else if (stateName.startsWith("procedure-")) {
    const restoreFetch = installProcedureFetch();
    const api = { getProcedurePresets: async () => [] };
    if (stateName === "procedure-home") {
        localStorage.removeItem("hex-crawl.procedure-authoring.mode");
        await renderProcedureAuthoringWorkspace(root, api, null, () => {});
    } else {
        const mode = stateName.replace("procedure-", "");
        localStorage.setItem("hex-crawl.procedure-authoring.mode", mode);
        await renderProcedureAuthoringWorkspace(root, api, "visual-procedure", () => {});
    }
    emitSpecialMetrics("procedure");
    restoreFetch();
} else {
const mile = { kind: "Mile", symbol: "mi", metersPerUnit: 1609.344 };
const distance = value => ({ value, unit: mile });
const world = {
    id: "visual-world",
    name: "The Shattered Marches",
    version: 8,
    createdAt: "2026-10-01T12:00:00Z",
    updatedAt: "2026-10-06T04:00:00Z",
    grid: {
        id: "visual-grid",
        orientation: "PointyTop",
        coordinateConvention: "AxialQr",
        origin: { x: 0, y: 0 },
        rotationDegrees: 0,
        hexRadiusWorldUnits: 1,
        neighborCenterDistance: distance(12)
    },
    features: [{
        id: "feature-ridge",
        name: "Ashen Ridge",
        category: "ridge",
        kind: "Point",
        position: { x: 1.5, y: 0.866 },
        path: null,
        boundary: null
    }],
    locations: [{
        id: "location-tower",
        name: "Old Signal Tower",
        category: "landmark",
        position: { x: -1.5, y: 0.866 },
        discoverability: "Obvious"
    }],
    sourceMaps: []
};

function moduleDef(moduleKey) {
    return {
        moduleKey,
        moduleName: moduleKey,
        mechanicKey: moduleKey,
        mechanicVersion: 1,
        executionHandler: "visual-review",
        automationLevel: "Assisted",
        parameters: {}
    };
}

function procedure() {
    return {
        procedureId: "visual-procedure",
        revision: 15,
        key: "phase15-visual",
        name: "Expedition Procedure",
        isExecutable: true,
        runtime: {
            intervalHours: 4,
            travelResolution: "ContinuousDistance",
            actualDistanceResolution: "Fixed",
            encounterCadence: "None",
            usesNavigationChecks: false,
            usesPersistentVeer: true,
            tracksIntraHexProgress: true,
            directionChangesCostProgress: true,
            supportsDeliberateDoubleBack: true,
            startingExitProgressFactor: 0,
            nearExitProgressFactor: 0.5,
            farExitProgressFactor: 1,
            backExitProgressFactor: 1,
            directionChangeProgressCostFactor: 0.5,
            resolutionHelpers: null
        },
        focusedIntervalPolicy: {
            support: "Supported",
            intervalHours: 4,
            mechanicKey: "time.interval",
            mechanicVersion: 1,
            executionHandler: "visual-review",
            unsupportedReason: null
        },
        modules: [
            "time.interval",
            "movement.resolution",
            "movement.hex-progress",
            "navigation.check",
            "encounters.cadence",
            "party.activities",
            "survival.resources",
            "time.forced-travel",
            "effects.expedition",
            "journey.process"
        ].map(moduleDef)
    };
}

function party() {
    const members = ["Ari", "Brann", "Cora", "Dain", "Eli"].map((name, index) => ({
        id: "member-" + (index + 1),
        name,
        externalCharacterId: null,
        countsTowardPartyMovement: true
    }));
    return {
        members,
        marchingOrder: members.map((member, index) => ({ memberId: member.id, rank: index + 1, file: 1 })),
        watchList: [{ slot: 1, memberIds: ["member-2", "member-4"], label: "First watch" }],
        standingOrders: [{ id: "order-1", text: "Keep the lantern hooded near settlements.", enabled: true }],
        activityAssignments: [
            { id: "assignment-1", scope: "Participant", participantId: "member-1", activityKey: "navigate", roleKey: "navigator", note: null },
            { id: "assignment-2", scope: "Participant", participantId: "member-3", activityKey: "scout", roleKey: "scout", note: null }
        ],
        movementContributors: [],
        baseMovement: {
            perHour: distance(1.5),
            perWatch: distance(6),
            perMarch: distance(24),
            limitingMemberId: null,
            note: "Party overland pace"
        }
    };
}

function movement(resolved) {
    return {
        policy: {
            support: "Supported",
            budgetModel: "watch-distance",
            baseBudget: 6,
            budgetUnit: "mi",
            limitingScope: "Party",
            travelModeKeys: ["normal", "fast", "slow"],
            terrainSupport: "Supported",
            terrainAdjustmentModel: "multiplier",
            terrainAdjustments: {},
            routeAdjustmentModel: "symbolic",
            weatherAdjustmentModel: "symbolic",
            mechanicKey: "movement.resolution",
            mechanicVersion: 1,
            executionHandler: "visual-review",
            unsupportedReason: null
        },
        status: resolved ? "Resolved" : "InputRequired",
        effectiveValue: resolved ? 6 : null,
        effectiveUnit: resolved ? "mi" : null,
        effectivePerUnit: resolved ? "watch" : null,
        effectiveDistanceUnit: resolved ? mile : null,
        limitingContributorKey: null,
        limitingParticipantId: null,
        preOverrideValue: resolved ? 6 : null,
        contributors: [],
        provenance: resolved ? ["party:base"] : [],
        missingInputs: resolved ? [] : ["party base movement"],
        diagnostics: [],
        referenceUse: resolved ? "AuthoritativeBase" : "None",
        suggestedExpectedDistance: resolved ? distance(6) : null
    };
}

function runtimeFixture() {
    return {
        id: "visual-" + stateName,
        overworldId: world.id,
        context: {
            kind: "WorldBound",
            name: "The Shattered Marches",
            overworldId: world.id,
            orientation: null,
            hexCenterDistance: distance(12)
        },
        name: "Crossing the Shattered Marches",
        version: 42,
        createdAt: "2026-10-05T12:00:00Z",
        updatedAt: "2026-10-06T04:00:00Z",
        procedure: procedure(),
        presentation: null,
        pauseReason: null,
        remainingWatchHours: 0,
        expedition: {
            id: "visual-" + stateName,
            isSpatial: true,
            currentHex: { q: 0, r: 0 },
            position: { x: 0, y: 0 },
            positionPrecision: "HexAnchor",
            entryDirection: 3,
            lastTravelDirection: 0,
            intendedDirection: null,
            actualDirection: null,
            isLost: false,
            veerSteps: 0,
            veerDegrees: 0,
            distanceTraveled: distance(18),
            hexProgress: distance(0),
            exitRequirement: distance(12),
            elapsedTravelHours: 12,
            currentDay: 1,
            completedWatches: 3,
            activeWatchNumber: null,
            activeWatchTotalHours: null,
            activeWatchElapsedHours: null,
            activeWatchRemainingHours: null,
            activeWatchPendingDecision: null,
            activePaceKey: null,
            activeActivityAssignments: [],
            activeNavigationAidKey: null,
            activeSuppressesNavigationCheck: false,
            activeResetsVeerAtBoundary: false,
            activeDeliberateDoubleBack: false,
            activeContinueAcrossBoundaries: false,
            activeEncounterKind: null,
            activeEncounterHour: null,
            activeEncounterHandled: null
        },
        party: party(),
        participantActivityPolicy: {
            support: "Supported",
            assignmentScope: "Participant",
            activityBudgetModel: "one-primary",
            activityKeys: ["navigate", "scout", "forage"],
            roleKeys: ["navigator", "scout", "lookout"],
            mechanicKey: "party.activities",
            mechanicVersion: 1,
            executionHandler: "visual-review",
            unsupportedReason: null
        },
        movementComposition: movement(true),
        knownHexes: [{ q: 0, r: 0 }],
        knowledge: [],
        history: [{
            sequence: 1,
            watchNumber: 1,
            kind: "WatchCompleted",
            expeditionElapsedHours: 4,
            hex: { q: -1, r: 0 },
            message: "First travel watch completed.",
            distanceValue: 6,
            distanceUnit: "mi",
            subjectId: null,
            subjectType: null
        }]
    };
}

function nonSpatialRuntimeFixture() {
    const value = runtimeFixture();
    value.overworldId = null;
    value.context = {
        kind: "NonSpatial",
        name: "The Shattered Marches journey"
    };
    value.procedure = {
        ...procedure(),
        name: "Journey Procedure",
        runtime: null,
        focusedIntervalPolicy: {
            support: "None",
            intervalHours: null,
            mechanicKey: null,
            mechanicVersion: null,
            executionHandler: null,
            unsupportedReason: null
        },
        modules: [
            "party.activities",
            "journey.process",
            "journey.events",
            "effects.expedition"
        ].map(moduleDef)
    };
    value.expedition = {
        id: "visual-" + stateName,
        isSpatial: false,
        elapsedTravelHours: 18,
        currentDay: 3,
        completedWatches: 0,
        activeWatchNumber: null,
        activeWatchTotalHours: null,
        activeWatchElapsedHours: null,
        activeWatchRemainingHours: null,
        activeWatchPendingDecision: null,
        activePaceKey: null,
        activeActivityAssignments: []
    };
    value.movementComposition = {
        policy: {
            support: "None",
            budgetModel: null,
            baseBudget: null,
            budgetUnit: null,
            limitingScope: null,
            travelModeKeys: [],
            terrainSupport: "None",
            terrainAdjustmentModel: null,
            terrainAdjustments: {},
            routeAdjustmentModel: null,
            weatherAdjustmentModel: null,
            mechanicKey: null,
            mechanicVersion: null,
            executionHandler: null,
            unsupportedReason: null
        },
        status: "NotApplicable",
        effectiveValue: null,
        effectiveUnit: null,
        effectivePerUnit: null,
        effectiveDistanceUnit: null,
        limitingContributorKey: null,
        limitingParticipantId: null,
        preOverrideValue: null,
        contributors: [],
        provenance: [],
        missingInputs: [],
        diagnostics: [],
        referenceUse: "None",
        suggestedExpectedDistance: null
    };
    value.knownHexes = [];
    value.knowledge = [];
    return value;
}

function survivalFixture(forced) {
    const unsupported = {
        support: "None",
        mechanicKey: null,
        mechanicVersion: null,
        executionHandler: null,
        unsupportedReason: null
    };
    return {
        expeditionVersion: 42,
        resourcePolicy: {
            support: "Supported",
            resourceKinds: ["food", "water"],
            inventoryModel: "Counted",
            consumptionModel: "resolved-quantity",
            consumptionInterval: "travel-day",
            mechanicKey: "survival.resources",
            mechanicVersion: 1,
            executionHandler: "visual-review",
            unsupportedReason: null
        },
        foragingPolicy: {
            ...unsupported,
            resolutionModel: null,
            timeCost: null,
            timeUnit: null,
            movementTradeoff: null,
            activityBacked: false
        },
        campingPolicy: {
            ...unsupported,
            resolutionModel: null,
            timeCost: null,
            timeUnit: null,
            watchModel: null,
            activityBacked: false
        },
        forcedTravelPolicy: {
            support: "Supported",
            normalTravelLimit: 8,
            limitUnit: "Hours",
            checkModel: "resolved-check",
            failureConsequence: "fatigue",
            mechanicKey: "time.forced-travel",
            mechanicVersion: 1,
            executionHandler: "visual-review",
            unsupportedReason: null
        },
        exposurePolicy: {
            ...unsupported,
            dimensions: [],
            evaluationModel: null,
            evaluationInterval: null,
            targetScope: null,
            consequenceModel: null
        },
        resources: [
            {
                id: "food",
                resourceKey: "food",
                target: { scope: "Party", targetId: null },
                inventoryModel: "Counted",
                quantity: 8,
                unit: "ration",
                symbolicState: null,
                supplyDieSides: null,
                isDepleted: false,
                note: null
            },
            {
                id: "water",
                resourceKey: "water",
                target: { scope: "Party", targetId: null },
                inventoryModel: "Counted",
                quantity: 10,
                unit: "waterskin",
                symbolicState: null,
                supplyDieSides: null,
                isDepleted: false,
                note: null
            }
        ],
        forcedTravel: {
            amountSinceReset: forced ? 10 : 4,
            unit: "Hours",
            normalLimit: 8,
            thresholdReached: forced,
            forcedTravelBegun: forced,
            checkDue: forced,
            pendingCheckId: forced ? "forced-check" : null,
            pendingConsequenceId: null,
            lastResolution: null
        },
        exposure: [{
            id: "cold",
            exposureKey: "cold",
            target: { scope: "Party", targetId: null },
            amount: 1,
            unit: "step",
            sourceOccurrenceIds: ["visual-exposure"]
        }],
        camp: null,
        environmentFacts: [],
        pendingResourceConsequences: []
    };
}

function journeySurvivalFixture() {
    const state = survivalFixture(false);
    state.resourcePolicy = {
        ...state.resourcePolicy,
        support: "None",
        resourceKinds: [],
        mechanicKey: null,
        mechanicVersion: null,
        executionHandler: null
    };
    state.forcedTravelPolicy = {
        ...state.forcedTravelPolicy,
        support: "None",
        normalTravelLimit: null,
        limitUnit: null,
        checkModel: null,
        failureConsequence: null,
        mechanicKey: null,
        mechanicVersion: null,
        executionHandler: null
    };
    state.resources = [];
    state.exposure = [];
    state.pendingResourceConsequences = [];
    state.forcedTravel = {
        amountSinceReset: 0,
        unit: null,
        normalLimit: null,
        thresholdReached: false,
        forcedTravelBegun: false,
        checkDue: false,
        pendingCheckId: null,
        pendingConsequenceId: null,
        lastResolution: null
    };
    return state;
}

function journeyFixture() {
    return {
        expeditionVersion: 42,
        processPolicy: { support: "Supported" },
        eventPolicy: { support: "Supported" },
        activeProcesses: [{
            id: "journey-1",
            processKey: "ashen-pass",
            status: "Active",
            definition: {
                processKey: "ashen-pass",
                displayName: "Cross the Ashen Pass",
                description: "Guide the company through the flooded pass and into the high country.",
                stages: [{
                    stageKey: "pass",
                    displayName: "Cross the pass",
                    description: "Make progress through the broken highland route.",
                    progressTarget: 6,
                    roleKeys: ["navigator", "scout"]
                }]
            },
            execution: {
                progressKind: "Numeric",
                progressUnit: "legs",
                roleDriven: true
            },
            currentStageKey: "pass",
            stageStates: [{
                stageKey: "pass",
                numericProgress: 3,
                explicitState: null,
                successes: 2,
                failures: 0,
                complications: 0,
                completed: false
            }],
            pendingActions: [],
            isTerminal: false
        }],
        closedProcesses: [],
        eventOccurrences: [],
        resolutions: [],
        history: []
    };
}

let runtime = runtimeFixture();
let survival = survivalFixture(false);
let journey = journeyFixture();

switch (stateName) {
    case "selected-edge":
        runtime.expedition.intendedDirection = 1;
        runtime.expedition.actualDirection = 1;
        break;
    case "persisted-course":
        break;
    case "partial-progress":
        runtime.expedition.intendedDirection = 2;
        runtime.expedition.actualDirection = 2;
        runtime.expedition.hexProgress = distance(4.5);
        runtime.expedition.activeWatchNumber = 4;
        runtime.expedition.activeWatchTotalHours = 4;
        runtime.expedition.activeWatchElapsedHours = 1.5;
        runtime.expedition.activeWatchRemainingHours = 2.5;
        runtime.expedition.activePaceKey = "careful";
        runtime.remainingWatchHours = 2.5;
        break;
    case "navigation-pending":
        runtime.expedition.intendedDirection = 1;
        runtime.procedure.runtime.usesNavigationChecks = true;
        break;
    case "boundary-pending":
        runtime.expedition.intendedDirection = 1;
        runtime.expedition.actualDirection = 2;
        runtime.expedition.isLost = true;
        runtime.pauseReason = "LostRecognitionRequired";
        break;
    case "movement-input-pending":
        runtime.expedition.intendedDirection = 3;
        runtime.movementComposition = movement(false);
        break;
    case "encounter-pending":
        runtime.pauseReason = "EncounterTriggered";
        runtime.remainingWatchHours = 2.5;
        runtime.expedition.intendedDirection = 0;
        runtime.expedition.actualDirection = 0;
        runtime.expedition.activeWatchNumber = 4;
        runtime.expedition.activeWatchTotalHours = 4;
        runtime.expedition.activeWatchElapsedHours = 1.5;
        runtime.expedition.activeWatchRemainingHours = 2.5;
        runtime.expedition.activePaceKey = "normal";
        runtime.expedition.activeEncounterKind = "WanderingEncounter";
        runtime.expedition.activeEncounterHour = 1.5;
        runtime.expedition.activeEncounterHandled = true;
        runtime.expedition.activeWatchPendingDecision = "EncounterTriggered";
        runtime.history.push({
            sequence: 2,
            watchNumber: 4,
            kind: "EncounterTriggered",
            expeditionElapsedHours: 13.5,
            hex: { q: 0, r: 0 },
            message: "A wandering encounter interrupts the expedition.",
            distanceValue: null,
            distanceUnit: null,
            subjectId: null,
            subjectType: null
        });
        break;
    case "forced-travel-pending":
        runtime.expedition.intendedDirection = 4;
        survival = survivalFixture(true);
        break;
    case "more-options-open":
        runtime.expedition.intendedDirection = 5;
        runtime.expedition.actualDirection = 5;
        break;
    case "rail-realistic":
        runtime.expedition.intendedDirection = 0;
        runtime.expedition.actualDirection = 0;
        break;
    case "map-selected":
    case "map-nonadjacent":
    case "teleport-workspace":
    case "no-course":
        break;
    case "journey-normal":
        runtime = nonSpatialRuntimeFixture();
        survival = journeySurvivalFixture();
        journey = journeyFixture();
        break;
    case "journey-pending":
        runtime = nonSpatialRuntimeFixture();
        survival = journeySurvivalFixture();
        journey = journeyFixture();
        journey.activeProcesses[0].status = "ResolutionRequired";
        journey.eventOccurrences = [{
            occurrenceId: "event-1",
            processId: "journey-1",
            status: "ResolutionRequired",
            eventType: "Journey hazard",
            eventKey: "hazard",
            stageKey: "pass"
        }];
        break;
    case "journey-consequence":
        runtime = nonSpatialRuntimeFixture();
        survival = journeySurvivalFixture();
        journey = journeyFixture();
        journey.activeProcesses[0].stageStates[0].failures = 1;
        journey.activeProcesses[0].stageStates[0].complications = 2;
        break;
    default:
        throw new Error("Unknown visual state: " + stateName);
}

localStorage.removeItem("hex-crawl.expedition." + runtime.id + ".travel-intent");
if (stateName === "persisted-course") {
    localStorage.setItem(
        "hex-crawl.expedition." + runtime.id + ".travel-intent",
        JSON.stringify({ direction: 2, pace: "fast" }));
}

const originalFetch = window.fetch.bind(window);
window.fetch = async input => {
    const url = typeof input === "string" ? input : input instanceof Request ? input.url : String(input);
    if (url.includes("/api/expeditions/" + runtime.id + "/survival")) return responseJson(survival);
    if (url.includes("/api/expeditions/" + runtime.id + "/journeys")) return responseJson(journey);
    return new Response(JSON.stringify({ detail: "visual-review stub" }), {
        status: 404,
        headers: { "Content-Type": "application/json" }
    });
};

const api = new Proxy({
    getExpedition: async () => runtime,
    getOverworld: async () => world,
    getTravelEnvironmentCatalog: async () => ({
        availability: "unavailable",
        provider: null,
        catalog: null,
        detail: "Visual review fixture intentionally uses explicit procedure inputs."
    })
}, {
    get(target, property) {
        if (property in target) return target[property];
        return async () => runtime;
    }
});

await renderExpedition(root, api, runtime.id, () => {}, undefined, null);

function findButton(label) {
    return Array.from(root.querySelectorAll("button")).find(button => button.textContent?.trim() === label) || null;
}

async function waitForRootText(text, attempts = 20) {
    for (let index = 0; index < attempts; index += 1) {
        if ((root.textContent || "").includes(text)) return;
        await new Promise(resolve => setTimeout(resolve, 0));
    }
    throw new Error(`Timed out waiting for rendered review text: ${text}`);
}

await new Promise(resolve => setTimeout(resolve, 0));

if (stateName === "map-selected" || stateName === "map-nonadjacent") {
    const canvas = root.querySelector("canvas");
    if (canvas) {
        const rect = canvas.getBoundingClientRect();
        canvas.dispatchEvent(new MouseEvent("click", {
            bubbles: true,
            button: 0,
            clientX: rect.left + rect.width / 2 + (stateName === "map-nonadjacent" ? 280 : 90),
            clientY: rect.top + rect.height / 2
        }));
    }
} else if (stateName === "navigation-pending") {
    findButton("Resolve navigation")?.click();
} else if (stateName === "movement-input-pending") {
    findButton("Continue travel")?.click();
} else if (stateName === "encounter-pending") {
    findButton("Resolve encounter")?.click();
} else if (stateName === "forced-travel-pending") {
    findButton("Resolve forced travel")?.click();
    await waitForRootText("Current requirement");
} else if (stateName === "boundary-pending") {
    findButton("Resolve lost-party boundary decision")?.click();
} else if (stateName === "more-options-open") {
    findButton("More options")?.click();
} else if (stateName === "teleport-workspace") {
    findButton("Teleport party")?.click();
}

if (stateName === "selected-edge") {
    root.querySelector('[data-adjacency-edge][aria-pressed="true"]')?.focus({ preventScroll: true });
}

// Synchronous interactions settle immediately. Async focused panels wait for a
// concrete rendered condition above rather than using an arbitrary sleep.
await Promise.resolve();

const isVisible = element => Boolean(element && !element.hidden && element.getClientRects().length);
const buttons = Array.from(root.querySelectorAll("button"));
const mapHost = root.querySelector(".hc-map-host");
const mapContext = root.querySelector("[data-map-context]");
const rail = root.querySelector(".hc-table-rail");
const currentTravel = root.querySelector("[data-current-travel]");
const rootText = root.textContent || "";
const metrics = {
    state: stateName,
    surface: "expedition",
    theme,
    viewportWidth: window.innerWidth,
    viewportHeight: window.innerHeight,
    reviewWidth: Math.round(root.getBoundingClientRect().width),
    reviewScrollWidth: root.scrollWidth,
    scrollWidth: document.documentElement.scrollWidth,
    scrollHeight: document.documentElement.scrollHeight,
    navigatorButtons: root.querySelectorAll("[data-adjacency-edge]").length,
    selectedEdges: root.querySelectorAll('[data-adjacency-edge][aria-pressed="true"]').length,
    continueButtons: buttons.filter(button => isVisible(button) && button.textContent?.trim() === "Continue travel").length,
    travelControlsButtons: buttons.filter(button => isVisible(button) && button.textContent?.trim() === "Travel controls").length,
    drawerCount: root.querySelectorAll("[data-phase15-drawer]").length,
    mapHeight: mapHost ? Math.round(mapHost.getBoundingClientRect().height) : 0,
    mapTop: mapHost ? Math.round(mapHost.getBoundingClientRect().top) : null,
    mapContextVisible: isVisible(mapContext),
    railVisible: isVisible(rail),
    currentTravelVisible: isVisible(currentTravel),
    currentTravelTop: currentTravel ? Math.round(currentTravel.getBoundingClientRect().top) : null,
    primaryAction: root.querySelector(".hc-current-action-primary")?.textContent?.trim() || null,
    focusedTitle: root.querySelector("[data-phase15-drawer] h2")?.textContent?.trim() || null,
    focusedEdge: document.activeElement?.matches?.("[data-adjacency-edge]") ?? false,
    journeyVisible: rootText.includes("Current stage") && rootText.includes("Progress") && rootText.includes("Roles") && rootText.includes("Pending"),
    movementStatusVisible: Array.from(root.querySelectorAll(".hc-ux-stat")).some(item => item.textContent?.includes("Movement")),
    fakeSpatialStateVisible: !runtime.expedition.isSpatial && (
        rootText.includes("Current travel")
        || rootText.includes("Pace / travel mode")
        || rootText.includes("Current cell")
        || rootText.includes("Hex progress")
        || rootText.includes("Teleport party")
        || rootText.includes("Party & travel order")),
    teleportContextVisible: Array.from(root.querySelectorAll("button")).some(button => isVisible(button) && button.textContent?.trim() === "Teleport party here"),
    journeyConsequenceVisible: rootText.includes("1 failure") && rootText.includes("2 complications"),
    movementUnitVisible: rootText.includes("Effective distance (mi)"),
    forcedTravelPrimaryDomainFacing: rootText.includes("Current requirement") && rootText.includes("Failure consequence:"),
    forcedTravelTechnicalExpanded: Array.from(root.querySelectorAll("details[open] > summary")).some(summary => summary.textContent?.trim() === "Advanced consequence details")
};
document.getElementById("review-metrics").textContent = JSON.stringify(metrics);
document.documentElement.dataset.visualReviewReady = "true";
window.fetch = originalFetch;

}
