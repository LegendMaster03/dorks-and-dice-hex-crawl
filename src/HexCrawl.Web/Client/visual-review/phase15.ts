// @ts-nocheck
import { ensurePartyResponsiveStyles } from "../src/party-responsive-styles";
import { ensureStyles } from "../src/styles";
import { renderExpedition } from "../src/modules/expeditions/expedition-view";

const params = new URLSearchParams(window.location.search);
const stateName = params.get("state") || "no-course";
const theme = params.get("theme") === "dark" ? "dark" : "light";
document.documentElement.dataset.bsTheme = theme;

const root = document.getElementById("tool-root");
if (!(root instanceof HTMLElement)) throw new Error("Missing visual-review root.");
ensureStyles();
ensurePartyResponsiveStyles();

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
        coordinateConvention: "Axial",
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

function survivalFixture(forced) {
    return {
        expeditionVersion: 42,
        resourcePolicy: { support: "Supported" },
        resources: [
            { id: "food", resourceKey: "food", isDepleted: false },
            { id: "water", resourceKey: "water", isDepleted: false }
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
        exposure: [{ id: "cold", exposureKey: "cold", amount: 1, unit: "step" }],
        pendingResourceConsequences: []
    };
}

function journeyFixture() {
    return {
        expeditionVersion: 42,
        processPolicy: { support: "Supported" },
        eventPolicy: { support: "Supported" },
        activeProcesses: [{
            id: "journey-1",
            status: "Active",
            definition: {
                displayName: "Cross the Ashen Pass",
                stages: [{ stageKey: "pass", displayName: "Cross the pass" }]
            },
            currentStageKey: "pass",
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
        runtime.expedition.activeEncounterHandled = false;
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
    case "no-course":
        break;
    default:
        throw new Error("Unknown visual state: " + stateName);
}

localStorage.removeItem("hex-crawl:travel-intent:" + runtime.id);

const jsonResponse = value => new Response(JSON.stringify(value), {
    status: 200,
    headers: { "Content-Type": "application/json" }
});
const originalFetch = window.fetch.bind(window);
window.fetch = async input => {
    const url = typeof input === "string" ? input : input instanceof Request ? input.url : String(input);
    if (url.includes("/api/expeditions/" + runtime.id + "/survival")) return jsonResponse(survival);
    if (url.includes("/api/expeditions/" + runtime.id + "/journeys")) return jsonResponse(journey);
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

await renderExpedition(root, api, runtime.id, "map", () => {}, world.id, null);

function findButton(label) {
    return Array.from(root.querySelectorAll("button")).find(button => button.textContent?.trim() === label) || null;
}

await new Promise(resolve => requestAnimationFrame(() => requestAnimationFrame(resolve)));

if (stateName === "map-selected") {
    const canvas = root.querySelector("canvas");
    if (canvas) {
        const rect = canvas.getBoundingClientRect();
        canvas.dispatchEvent(new MouseEvent("click", {
            bubbles: true,
            button: 0,
            clientX: rect.left + rect.width / 2 + 90,
            clientY: rect.top + rect.height / 2
        }));
    }
} else if (stateName === "navigation-pending") {
    findButton("Resolve navigation")?.click();
} else if (stateName === "movement-input-pending") {
    findButton("Continue travel")?.click();
} else if (stateName === "encounter-pending") {
    findButton("Resolve encounter")?.click();
} else if (stateName === "more-options-open") {
    findButton("More options")?.click();
}

await new Promise(resolve => setTimeout(resolve, 120));

const isVisible = element => Boolean(element && !element.hidden && element.getClientRects().length);
const buttons = Array.from(root.querySelectorAll("button"));
const mapHost = root.querySelector(".hc-map-host");
const mapContext = root.querySelector("[data-map-context]");
const rail = root.querySelector(".hc-table-rail");
const metrics = {
    state: stateName,
    theme,
    viewportWidth: window.innerWidth,
    viewportHeight: window.innerHeight,
    scrollWidth: document.documentElement.scrollWidth,
    scrollHeight: document.documentElement.scrollHeight,
    navigatorButtons: root.querySelectorAll("[data-adjacency-edge]").length,
    selectedEdges: root.querySelectorAll('[data-adjacency-edge][aria-pressed="true"]').length,
    continueButtons: buttons.filter(button => isVisible(button) && button.textContent?.trim() === "Continue travel").length,
    travelControlsButtons: buttons.filter(button => isVisible(button) && button.textContent?.trim() === "Travel controls").length,
    drawerCount: root.querySelectorAll("[data-phase15-drawer]").length,
    mapHeight: mapHost ? Math.round(mapHost.getBoundingClientRect().height) : 0,
    mapContextVisible: isVisible(mapContext),
    railVisible: isVisible(rail),
    primaryAction: root.querySelector(".hc-current-action-primary")?.textContent?.trim() || null,
    focusedTitle: root.querySelector("[data-phase15-drawer] h2")?.textContent?.trim() || null
};
document.getElementById("review-metrics").textContent = JSON.stringify(metrics);
document.documentElement.dataset.visualReviewReady = "true";
window.fetch = originalFetch;
