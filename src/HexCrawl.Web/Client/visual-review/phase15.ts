// @ts-nocheck
import { ensurePartyResponsiveStyles } from "../src/party-responsive-styles";
import { ensureStyles } from "../src/styles";
import { renderExpedition } from "../src/modules/expeditions/expedition-view";
import { renderToolHome } from "../src/modules/home/tool-home-view";
import { renderProcedureAuthoringWorkspace } from "../src/modules/procedures/procedure-authoring-view";
import { renderWorldEditor } from "../src/modules/worlds/world-editor-view";

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
        defaultValue: "144000000000"
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
            parameters: { durationTicks: "144000000000" },
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
                    modules: [{ moduleKey: "time.interval", parameters: { durationTicks: "144000000000" } }]
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
        technicalModeLabels: ["Compact", "Advanced", "JSON"].filter(label => text.includes(label)).length,
        selectedCellTitle: root.querySelector("[data-selected-cell-title]")?.textContent?.trim() || null,
        selectedCellTerrain: root.querySelector("[data-cell-terrain]")?.value || null,
        selectedCellHiddenPoiVisible: text.includes("Ruined Watchtower · Ruin · Hidden"),
        selectedCellRoadVisible: text.includes("Old King's Road · road · Line"),
        selectedCellRouteBehaviorVisible: text.includes("Old King's Road · Route: good-road"),
        selectedCellAdvancedOpen: Boolean(root.querySelector("[data-cell-advanced][open]")),
        mapHeight: Math.round(root.querySelector("[data-map]")?.getBoundingClientRect().height || 0)
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
} else if (stateName === "world-selected-cell") {
    const worldMile = { kind: "Mile", symbol: "mi", metersPerUnit: 1609.344 };
    const worldFixture = {
        id: "visual-world",
        name: "The Shattered Marches",
        version: 12,
        createdAt: "2026-10-01T12:00:00Z",
        updatedAt: "2026-10-07T05:00:00Z",
        grid: {
            id: "visual-grid",
            orientation: "PointyTop",
            coordinateConvention: "AxialQr",
            origin: { x: 0, y: 0 },
            rotationDegrees: 18,
            hexRadiusWorldUnits: 1,
            neighborCenterDistance: { value: 12, unit: worldMile }
        },
        features: [{
            id: "road-1",
            name: "Old King's Road",
            category: "road",
            kind: "Line",
            position: null,
            path: [{ x: -2.5, y: -0.8 }, { x: 0, y: 0 }, { x: 2.5, y: 0.8 }],
            boundary: null
        }, {
            id: "forest-region",
            name: "Blackwood Forest",
            category: "forest",
            kind: "Region",
            position: null,
            path: null,
            boundary: [{ x: -0.8, y: -0.8 }, { x: 1.1, y: -0.6 }, { x: 0.9, y: 1.0 }, { x: -0.9, y: 0.8 }]
        }],
        locations: [{
            id: "tower-1",
            name: "Ruined Watchtower",
            category: "Ruin",
            position: { x: 0, y: 0 },
            discoverability: "Hidden"
        }],
        sourceMaps: []
    };
    const environmentFixture = {
        overworldId: worldFixture.id,
        version: worldFixture.version,
        annotations: [{
            id: "hex-environment",
            scope: { kind: "Hex", hex: { q: 0, r: 0 }, featureId: null },
            facts: [{
                id: "terrain-1",
                dimension: "terrain",
                valueKind: "Tag",
                tag: "forest",
                measurement: null,
                provenance: "visual-review",
                note: null
            }, {
                id: "visibility-1",
                dimension: "visibility",
                valueKind: "Tag",
                tag: "dense",
                measurement: null,
                provenance: "visual-review",
                note: null
            }]
        }, {
            id: "road-environment",
            scope: { kind: "SpatialFeature", hex: null, featureId: "road-1" },
            facts: [{
                id: "route-1",
                dimension: "route",
                valueKind: "Tag",
                tag: "good-road",
                measurement: null,
                provenance: "visual-review",
                note: null
            }]
        }]
    };
    sessionStorage.setItem(
        `hex-crawl.world-editor.selected-cell.${worldFixture.id}`,
        JSON.stringify({ q: 0, r: 0 }));
    const api = {
        getOverworld: async () => worldFixture,
        getWorldEnvironment: async () => environmentFixture,
        getProcedurePresets: async () => [],
        listExpeditions: async () => [],
        listSourceMaps: async () => ({ overworldVersion: worldFixture.version, sourceMaps: [] })
    };
    await renderWorldEditor(root, api, worldFixture.id, () => {});
    emitSpecialMetrics("world");
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
        parameters: moduleKey === "effects.expedition"
            ? { effectKinds: "fatigue;exhaustion" }
            : {}
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

function movement(resolved, travelModeKeys = ["normal", "fast", "slow"]) {
    return {
        policy: {
            support: "Supported",
            budgetModel: "watch-distance",
            baseBudget: 6,
            budgetUnit: "mi",
            limitingScope: "Party",
            travelModeKeys,
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
            activeEncounterHandled: null,
            pendingEncounter: null
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
        activeActivityAssignments: [],
        pendingEncounter: null
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

function abstractHexRuntimeFixture() {
    const value = runtimeFixture();
    value.overworldId = null;
    value.context = {
        kind: "AbstractHex",
        name: "Abstract route",
        orientation: "PointyTop",
        hexCenterDistance: distance(12)
    };
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
            failureTargetScope: "Participant",
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
        failureTargetScope: null,
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

function effectsFixture(active = false) {
    return {
        policy: {
            support: "Supported",
            effectKinds: ["fatigue", "exhaustion"],
            accumulationModel: "levels",
            recoveryModel: "rest",
            scope: "Participant",
            mechanicKey: "effects.expedition",
            mechanicVersion: 1,
            executionHandler: "visual-review",
            unsupportedReason: null
        },
        activeEffects: active ? [{
            id: "effect-fatigue",
            effectKey: "fatigue",
            target: { scope: "Participant", targetId: "member-1" },
            mergeKey: "fatigue:member-1",
            level: 2,
            magnitude: null,
            unit: null,
            state: null,
            movementComponents: [],
            sourceConsequenceIds: ["consequence-fatigue"],
            provenance: [{
                sourceKind: "ForcedTravelResult",
                sourceKey: "forced-travel-check",
                sourceReference: null,
                providerName: null,
                note: "Failed forced-travel check"
            }],
            recoveryModel: "rest"
        }] : [],
        appliedConsequences: [],
        pendingConsequences: [],
        history: active ? [{
            id: "effect-audit-1",
            effectId: "effect-fatigue",
            effectKey: "fatigue",
            operation: "adjust-level",
            beforeLevel: 1,
            afterLevel: 2,
            beforeMagnitude: null,
            afterMagnitude: null,
            consequenceId: "consequence-fatigue",
            provenance: {
                sourceKind: "ForcedTravelResult",
                sourceKey: "forced-travel-check",
                sourceReference: null,
                providerName: null,
                note: "Failed forced-travel check"
            }
        }] : []
    };
}

function journeyFixture() {
    return {
        expeditionVersion: 42,
        processPolicy: {
            support: "Supported",
            stageModel: "Ordered",
            stageKeys: ["approach", "pass", "arrival"],
            stageTransitionModel: "Sequential",
            progressModel: "Accumulated",
            progressKind: "Numeric",
            progressUnit: "legs",
            allowNegativeProgress: false,
            progressFloor: 0,
            progressCeiling: 6,
            completionModel: "StageCompletion",
            roleDriven: true,
            roleAssignmentModel: "CurrentAtResolution",
            intervalIntegrationModel: "None",
            blocksRelevantTravelWhileResolutionRequired: true,
            mechanicKey: "journey.process",
            mechanicVersion: 1,
            executionHandler: "visual-review",
            unsupportedReason: null
        },
        eventPolicy: {
            support: "Supported",
            triggerModel: "Explicit",
            triggerSources: ["ProcessProgress", "StageTransition"],
            linkMode: "ProcessLinked",
            targetingModel: "RoleOrParty",
            terrainInfluence: "Snapshot",
            consequenceModel: "ExpeditionConsequence",
            requiresResolvedTrigger: false,
            blocksRelevantTravelWhileResolutionRequired: true,
            mechanicKey: "journey.events",
            mechanicVersion: 1,
            executionHandler: "visual-review",
            unsupportedReason: null
        },
        activeProcesses: [{
            id: "journey-1",
            processKey: "ashen-pass",
            status: "Active",
            definition: {
                processKey: "ashen-pass",
                displayName: "Cross the Ashen Pass",
                description: "Guide the company through the flooded pass and into the high country.",
                initialStageKey: "approach",
                stageOrder: ["approach", "pass", "arrival"],
                destinationReference: "High country",
                routeReference: "Ashen Pass",
                locationReference: null,
                note: null,
                stages: [{
                    stageKey: "approach",
                    displayName: "Reach the pass",
                    description: "Find the safe approach and establish the route.",
                    completionModel: "ProgressThreshold",
                    progressTarget: 2,
                    successTarget: null,
                    failureLimit: null,
                    complicationLimit: null,
                    failProcessAtFailureLimit: false,
                    failProcessAtComplicationLimit: false,
                    initialProgressState: null,
                    explicitNextStageKey: null,
                    outcomeTransitions: [],
                    approaches: [{
                        approachKey: "scout",
                        displayName: "Scout the approach",
                        capabilityReference: null,
                        note: null
                    }],
                    roleKeys: ["navigator", "scout"]
                }, {
                    stageKey: "pass",
                    displayName: "Cross the pass",
                    description: "Make progress through the broken highland route.",
                    completionModel: "ProgressThreshold",
                    progressTarget: 6,
                    successTarget: null,
                    failureLimit: null,
                    complicationLimit: null,
                    failProcessAtFailureLimit: false,
                    failProcessAtComplicationLimit: false,
                    initialProgressState: null,
                    explicitNextStageKey: null,
                    outcomeTransitions: [],
                    approaches: [{
                        approachKey: "steady",
                        displayName: "Steady progress",
                        capabilityReference: null,
                        note: null
                    }],
                    roleKeys: ["navigator", "scout"]
                }, {
                    stageKey: "arrival",
                    displayName: "Reach the high country",
                    description: "Finish the crossing and secure the destination.",
                    completionModel: "ProgressThreshold",
                    progressTarget: 1,
                    successTarget: null,
                    failureLimit: null,
                    complicationLimit: null,
                    failProcessAtFailureLimit: false,
                    failProcessAtComplicationLimit: false,
                    initialProgressState: null,
                    explicitNextStageKey: null,
                    outcomeTransitions: [],
                    approaches: [{
                        approachKey: "finish",
                        displayName: "Complete the crossing",
                        capabilityReference: null,
                        note: null
                    }],
                    roleKeys: ["navigator", "scout"]
                }]
            },
            execution: {
                stageModel: "Ordered",
                stageTransitionModel: "Sequential",
                progressModel: "Accumulated",
                progressKind: "Numeric",
                progressUnit: "legs",
                allowNegativeProgress: false,
                progressFloor: 0,
                progressCeiling: 6,
                completionModel: "StageCompletion",
                roleDriven: true,
                roleAssignmentModel: "CurrentAtResolution",
                intervalIntegrationModel: "None",
                blocksRelevantTravelWhileResolutionRequired: true,
                mechanicKey: "journey.process",
                mechanicVersion: 1,
                executionHandler: "visual-review"
            },
            currentStageKey: "pass",
            stageStates: [{
                stageKey: "approach",
                numericProgress: 2,
                explicitState: null,
                successes: 1,
                failures: 0,
                complications: 0,
                completed: true
            }, {
                stageKey: "pass",
                numericProgress: 3,
                explicitState: null,
                successes: 2,
                failures: 0,
                complications: 0,
                completed: false
            }, {
                stageKey: "arrival",
                numericProgress: 0,
                explicitState: null,
                successes: 0,
                failures: 0,
                complications: 0,
                completed: false
            }],
            pendingActions: [],
            startedAtExpeditionTime: "PT0H",
            startedAfterCompletedWatches: 0,
            endedAtExpeditionTime: null,
            endedAfterCompletedWatches: null,
            endReason: null,
            provenance: {
                sourceKind: "Dm",
                sourceKey: "visual-journey-start",
                sourceReference: null,
                providerName: null,
                note: null
            },
            isTerminal: false
        }],
        closedProcesses: [],
        eventOccurrences: [],
        resolutions: [],
        history: []
    };
}

function pendingJourneyConsequence(id, suffix = "") {
    return {
        consequence: {
            id,
            consequenceKey: `journey-fatigue${suffix}`,
            category: "PersistentEffectChange",
            target: { scope: "Participant", targetId: "member-1" },
            components: [],
            provenance: {
                sourceKind: "JourneyEvent",
                sourceKey: "journey-event-consequence",
                sourceReference: "event-1",
                providerName: null,
                note: `event-1:8ac00000-0000-0000-0000-00000000000${suffix || "1"}`
            },
            sourceReference: "event-1",
            note: null
        },
        status: "RequiresAdjudication",
        reason: "Journey consequence requires resolution.",
        requiredAction: "Resolve consequence.",
        unresolvedComponents: []
    };
}

let runtime = runtimeFixture();
let survival = survivalFixture(false);
let effects = effectsFixture(false);
let journey = journeyFixture();

switch (stateName) {
    case "selected-edge":
    case "focus-return":
        runtime.expedition.intendedDirection = 1;
        runtime.expedition.actualDirection = 1;
        break;
    case "fixed-pace":
        runtime.expedition.intendedDirection = 1;
        runtime.expedition.actualDirection = 1;
        runtime.movementComposition = movement(true, []);
        break;
    case "empty-party":
        runtime.expedition.intendedDirection = 1;
        runtime.expedition.actualDirection = 1;
        runtime.party = {
            ...runtime.party,
            members: [],
            marchingOrder: [],
            watchList: [],
            activityAssignments: [],
            baseMovement: null
        };
        break;
    case "persisted-course":
        runtime.expedition.intendedDirection = 2;
        runtime.expedition.actualDirection = 2;
        break;
    case "course-change-reload":
        runtime.expedition.currentHex = { q: 1, r: -1 };
        runtime.expedition.intendedDirection = 1;
        runtime.expedition.actualDirection = 1;
        runtime.expedition.hexProgress = distance(2);
        runtime.expedition.exitRequirement = distance(6);
        runtime.expedition.completedWatches = 2;
        runtime.procedure.runtime.usesNavigationChecks = true;
        break;
    case "course-clear-reload":
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
    case "movement-composition":
        runtime.expedition.intendedDirection = 3;
        runtime.expedition.actualDirection = 3;
        runtime.movementComposition = movement(true);
        runtime.movementComposition.effectiveValue = 4.5;
        runtime.movementComposition.preOverrideValue = 4;
        runtime.movementComposition.limitingParticipantId = "member-2";
        runtime.movementComposition.referenceUse = "AuthoritativeBase";
        runtime.movementComposition.contributors = [
            { id: "participant", kind: "Participant", key: "Brann speed", operation: "Base", applied: true, value: 6, symbolicValue: null, unit: "mi", perUnit: "watch", participantId: "member-2", movementUnitKey: null, provenance: "party sheet", detail: "Slowest traveler" },
            { id: "mount", kind: "Mount", key: "Pack mule", operation: "Replace", applied: false, value: 7, symbolicValue: null, unit: "mi", perUnit: "watch", participantId: null, movementUnitKey: "pack-mule", provenance: "party sheet", detail: "Retained mount option" },
            { id: "vehicle", kind: "Vehicle", key: "River skiff", operation: "Replace", applied: false, value: 8, symbolicValue: null, unit: "mi", perUnit: "watch", participantId: null, movementUnitKey: "skiff", provenance: "party sheet", detail: null },
            { id: "load", kind: "Load", key: "Heavy cargo", operation: "Multiply", applied: true, value: 0.8, symbolicValue: null, unit: null, perUnit: null, participantId: null, movementUnitKey: null, provenance: "load", detail: null },
            { id: "mode", kind: "TravelMode", key: "careful", operation: "Multiply", applied: true, value: 0.9, symbolicValue: null, unit: null, perUnit: null, participantId: null, movementUnitKey: null, provenance: "travel mode", detail: null },
            { id: "terrain", kind: "TerrainRoute", key: "broken ground", operation: "Multiply", applied: true, value: 0.75, symbolicValue: null, unit: null, perUnit: null, participantId: null, movementUnitKey: null, provenance: "current cell", detail: null },
            { id: "environment", kind: "Environment", key: "driving rain", operation: "SymbolicLimit", applied: false, value: null, symbolicValue: "manual review", unit: null, perUnit: null, participantId: null, movementUnitKey: null, provenance: "environment", detail: "Retained pending adjudication" },
            { id: "effect", kind: "PersistentEffect", key: "fatigue", operation: "Cap", applied: true, value: 4, symbolicValue: null, unit: "mi", perUnit: "watch", participantId: "member-2", movementUnitKey: null, provenance: "effect engine", detail: null },
            { id: "override", kind: "DmOverride", key: "clear route ruling", operation: "Replace", applied: true, value: 4.5, symbolicValue: null, unit: "mi", perUnit: "watch", participantId: null, movementUnitKey: null, provenance: "DM", detail: "Table ruling" }
        ];
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
        runtime.expedition.pendingEncounter = {
            id: "encounter-occurrence-1",
            triggerSequence: 2,
            watchNumber: 4,
            outcome: "WanderingEncounter",
            expeditionElapsedHours: 13.5,
            hex: { q: 0, r: 0 },
            locationId: null,
            note: "A wandering encounter interrupts the expedition."
        };
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
            subjectType: null,
            encounterOutcome: "WanderingEncounter",
            encounterNote: "A wandering encounter interrupts the expedition.",
            encounterOccurrenceId: "encounter-occurrence-1"
        });
        break;
    case "abstract-spatial-course":
        runtime = abstractHexRuntimeFixture();
        runtime.expedition.intendedDirection = 2;
        runtime.expedition.actualDirection = 2;
        runtime.procedure.runtime.usesNavigationChecks = true;
        break;
    case "forced-travel-pending":
        runtime.expedition.intendedDirection = 4;
        survival = survivalFixture(true);
        effects = effectsFixture(true);
        break;
    case "forced-travel-one-participant":
        runtime.expedition.intendedDirection = 4;
        runtime.party = {
            ...runtime.party,
            members: [runtime.party.members[0]],
            marchingOrder: runtime.party.marchingOrder.filter(value => value.memberId === "member-1"),
            watchList: [],
            activityAssignments: runtime.party.activityAssignments.filter(value => value.participantId === "member-1")
        };
        survival = survivalFixture(true);
        effects = effectsFixture(false);
        break;
    case "forced-travel-zero-participants":
        runtime.expedition.intendedDirection = 4;
        runtime.party = {
            ...runtime.party,
            members: [],
            marchingOrder: [],
            watchList: [],
            activityAssignments: []
        };
        survival = survivalFixture(true);
        effects = effectsFixture(false);
        break;
    case "forced-travel-party-scope":
        runtime.expedition.intendedDirection = 4;
        survival = survivalFixture(true);
        survival.forcedTravelPolicy.failureTargetScope = "Party";
        effects = effectsFixture(false);
        break;
    case "forced-travel-mount":
        runtime.expedition.intendedDirection = 4;
        runtime.party = {
            ...runtime.party,
            movementContributors: [{
                id: "mount-1",
                kind: "Mount",
                key: "Pack mule",
                operation: "Base",
                scope: "MovementUnit",
                value: 6,
                unit: "mi",
                perUnit: "watch",
                distanceUnit: mile,
                symbolicValue: null,
                participantId: null,
                movementUnitKey: "pack-mule",
                replacesParticipantIds: [],
                provenance: "DM",
                note: null,
                enabled: true
            }]
        };
        survival = survivalFixture(true);
        survival.forcedTravelPolicy.failureTargetScope = "Mount";
        effects = effectsFixture(false);
        break;
    case "forced-travel-vehicle":
        runtime.expedition.intendedDirection = 4;
        runtime.party = {
            ...runtime.party,
            movementContributors: [{
                id: "vehicle-1",
                kind: "Vehicle",
                key: "River skiff",
                operation: "Base",
                scope: "MovementUnit",
                value: 6,
                unit: "mi",
                perUnit: "watch",
                distanceUnit: mile,
                symbolicValue: null,
                participantId: null,
                movementUnitKey: "river-skiff",
                replacesParticipantIds: [],
                provenance: "DM",
                note: null,
                enabled: true
            }]
        };
        survival = survivalFixture(true);
        survival.forcedTravelPolicy.failureTargetScope = "Vehicle";
        effects = effectsFixture(false);
        break;
    case "effects-workspace":
        runtime.expedition.intendedDirection = 1;
        runtime.expedition.actualDirection = 1;
        effects = effectsFixture(true);
        break;
    case "effects-journey-source":
        runtime.expedition.intendedDirection = 1;
        runtime.expedition.actualDirection = 1;
        effects = effectsFixture(true);
        effects.activeEffects[0].provenance = [{
            sourceKind: "JourneyEvent",
            sourceKey: "journey-event-consequence",
            sourceReference: "event-1",
            providerName: null,
            note: "event-1:8ac00000-0000-0000-0000-000000000001"
        }];
        effects.appliedConsequences = [{
            consequenceId: "8ac00000-0000-0000-0000-000000000001",
            consequenceKey: "journey-fatigue",
            status: "Applied",
            effectIds: ["effect-fatigue"],
            provenance: {
                sourceKind: "JourneyEvent",
                sourceKey: "journey-event-consequence",
                sourceReference: "event-1",
                providerName: null,
                note: "event-1:8ac00000-0000-0000-0000-000000000001"
            },
            detail: "Applied journey consequence '8ac00000-0000-0000-0000-000000000001'.",
            resolutionProvenance: null
        }];
        break;
    case "history-workspace":
        runtime = nonSpatialRuntimeFixture();
        survival = journeySurvivalFixture();
        effects = effectsFixture(true);
        journey = journeyFixture();
        journey.history = [{
            id: "journey-history-1",
            kind: "ResolutionRecorded",
            processId: "journey-1",
            stageKey: "pass",
            resolutionId: "8ac00000-0000-0000-0000-000000000001",
            eventOccurrenceId: null,
            detail: "Recorded resolution '8ac00000-0000-0000-0000-000000000001' for journey stage 'pass'.",
            completedWatches: 0,
            provenance: {
                sourceKind: "Dm",
                sourceKey: "journey-resolution",
                sourceReference: null,
                providerName: null,
                note: null
            }
        }, {
            id: "journey-history-2",
            kind: "ProcessCompleted",
            processId: "journey-1",
            stageKey: "pass",
            resolutionId: null,
            eventOccurrenceId: null,
            detail: "Completed process 'journey-1' after resolution '8ac00000-0000-0000-0000-000000000001'.",
            completedWatches: 0,
            provenance: {
                sourceKind: "Dm",
                sourceKey: "journey-process-complete",
                sourceReference: "journey-1",
                providerName: null,
                note: null
            }
        }];
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
            id: "event-1",
            processId: "journey-1",
            stageKey: "pass",
            trigger: "ProcessProgress",
            triggerReference: "journey-1:pass:progress",
            status: "ResolutionRequired",
            targetKind: "Role",
            targetRoleKey: "navigator",
            targetId: null,
            participantSnapshot: null,
            eventKey: null,
            eventType: null,
            environment: [],
            consequenceIds: [],
            provenance: {
                sourceKind: "Procedure",
                sourceKey: "journey-event-opportunity",
                sourceReference: null,
                providerName: null,
                note: null
            },
            note: null
        }];
        break;
    case "journey-consequence":
        runtime = nonSpatialRuntimeFixture();
        survival = journeySurvivalFixture();
        journey = journeyFixture();
        effects = effectsFixture(false);
        effects.pendingConsequences = [pendingJourneyConsequence("pending-journey-consequence")];
        journey.activeProcesses[0].stageStates[1].failures = 1;
        journey.activeProcesses[0].stageStates[1].complications = 2;
        break;
    case "journey-consequence-multiple":
        runtime = nonSpatialRuntimeFixture();
        survival = journeySurvivalFixture();
        journey = journeyFixture();
        effects = effectsFixture(false);
        effects.pendingConsequences = [
            pendingJourneyConsequence("pending-journey-consequence-1", "1"),
            pendingJourneyConsequence("pending-journey-consequence-2", "2")
        ];
        break;
    case "journey-consequence-resolved":
        runtime = nonSpatialRuntimeFixture();
        survival = journeySurvivalFixture();
        journey = journeyFixture();
        effects = effectsFixture(false);
        effects.appliedConsequences = [{
            consequenceId: "8ac00000-0000-0000-0000-000000000001",
            consequenceKey: "journey-fatigue",
            status: "Applied",
            effectIds: [],
            provenance: {
                sourceKind: "JourneyEvent",
                sourceKey: "journey-event-consequence",
                sourceReference: "event-1",
                providerName: null,
                note: null
            },
            detail: "Journey consequence resolved.",
            resolutionProvenance: null
        }];
        journey.activeProcesses[0].stageStates[1].failures = 1;
        journey.activeProcesses[0].stageStates[1].complications = 2;
        break;
    default:
        throw new Error("Unknown visual state: " + stateName);
}

localStorage.removeItem("hex-crawl.expedition." + runtime.id + ".travel-intent");
if (stateName === "persisted-course") {
    localStorage.setItem(
        "hex-crawl.expedition." + runtime.id + ".travel-intent",
        JSON.stringify({ direction: 4, pace: "fast" }));
}
if (stateName === "course-change-reload" || stateName === "course-clear-reload") {
    localStorage.setItem(
        "hex-crawl.expedition." + runtime.id + ".travel-intent",
        JSON.stringify({ direction: 1, pace: "normal" }));
}

const originalFetch = window.fetch.bind(window);
window.fetch = async input => {
    const url = typeof input === "string" ? input : input instanceof Request ? input.url : String(input);
    if (url.includes("/api/expeditions/" + runtime.id + "/survival")) return responseJson(survival);
    if (url.includes("/api/expeditions/" + runtime.id + "/effects")) return responseJson(effects);
    if (url.includes("/api/expeditions/" + runtime.id + "/journeys")) return responseJson(journey);
    return new Response(JSON.stringify({ detail: "visual-review stub" }), {
        status: 404,
        headers: { "Content-Type": "application/json" }
    });
};

const api = new Proxy({
    getExpedition: async () => runtime,
    getOverworld: async () => world,
    setExpeditionCourseIntent: async (_expeditionId, input) => {
        if (input.expectedVersion !== runtime.version) {
            throw new Error("Visual review stale course mutation.");
        }
        runtime = {
            ...runtime,
            version: runtime.version + 1,
            expedition: {
                ...runtime.expedition,
                intendedDirection: input.intendedDirection
            }
        };
        return runtime;
    },
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

let disposeExpedition = await renderExpedition(root, api, runtime.id, () => {}, undefined, null);

function findButton(label) {
    return Array.from(root.querySelectorAll("button")).find(button => button.textContent?.trim() === label) || null;
}

function findButtonContaining(label) {
    return Array.from(root.querySelectorAll("button")).find(button => button.textContent?.includes(label)) || null;
}

function findStatAction(label) {
    return Array.from(root.querySelectorAll(".hc-stat-action")).find(button =>
        button.querySelector(".hc-stat-action-label")?.textContent?.trim() === label) || null;
}

async function waitForRootText(text, attempts = 20) {
    for (let index = 0; index < attempts; index += 1) {
        if ((root.textContent || "").includes(text)) return;
        await new Promise(resolve => setTimeout(resolve, 0));
    }
    throw new Error(`Timed out waiting for rendered review text: ${text}`);
}

async function waitForCondition(predicate, label, attempts = 30) {
    for (let index = 0; index < attempts; index += 1) {
        if (predicate()) return;
        await new Promise(resolve => setTimeout(resolve, 0));
    }
    throw new Error(`Timed out waiting for visual review condition: ${label}`);
}

await new Promise(resolve => setTimeout(resolve, 0));

let focusReturnVerified = false;

if (stateName === "course-change-reload") {
    const changedEdge = root.querySelector('[data-adjacency-edge="5"]');
    changedEdge?.click();
    await waitForCondition(
        () => runtime.expedition.intendedDirection === 5
            && root.querySelector('[data-adjacency-edge="5"]')?.getAttribute("aria-pressed") === "true",
        "server-authoritative changed course");

    disposeExpedition();
    root.replaceChildren();
    localStorage.removeItem("hex-crawl.expedition." + runtime.id + ".travel-intent");
    disposeExpedition = await renderExpedition(root, api, runtime.id, () => {}, undefined, null);
    await waitForCondition(
        () => root.querySelector('[data-adjacency-edge="5"]')?.getAttribute("aria-pressed") === "true",
        "fresh client restored changed course");

    findButton("Resolve navigation")?.click();
    await waitForRootText("Intended course:");
} else if (stateName === "course-clear-reload") {
    const selectedEdge = root.querySelector('[data-adjacency-edge="1"]');
    selectedEdge?.click();
    await waitForCondition(
        () => runtime.expedition.intendedDirection === null
            && root.querySelectorAll('[data-adjacency-edge][aria-pressed="true"]').length === 0,
        "server-authoritative cleared course");

    disposeExpedition();
    root.replaceChildren();
    localStorage.removeItem("hex-crawl.expedition." + runtime.id + ".travel-intent");
    disposeExpedition = await renderExpedition(root, api, runtime.id, () => {}, undefined, null);
    await waitForCondition(
        () => root.querySelectorAll('[data-adjacency-edge][aria-pressed="true"]').length === 0,
        "fresh client retained no-course state");
} else if (stateName === "map-selected" || stateName === "map-nonadjacent") {
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
} else if (stateName === "navigation-pending" || stateName === "abstract-spatial-course") {
    findButton("Resolve navigation")?.click();
} else if (stateName === "movement-input-pending") {
    findButton("Continue travel")?.click();
} else if (stateName === "movement-composition") {
    findStatAction("Movement")?.click();
    await waitForRootText("Movement composition contributors");
} else if (stateName === "encounter-pending") {
    findButton("Resolve encounter")?.click();
} else if (stateName === "forced-travel-pending"
    || stateName === "forced-travel-one-participant"
    || stateName === "forced-travel-zero-participants"
    || stateName === "forced-travel-party-scope"
    || stateName === "forced-travel-mount"
    || stateName === "forced-travel-vehicle") {
    findButton("Resolve forced travel")?.click();
    await waitForRootText("Current requirement");
    const checkbox = Array.from(root.querySelectorAll("label"))
        .find(label => label.textContent?.includes("Check succeeded"))
        ?.querySelector('input[type="checkbox"]');
    if (checkbox) {
        checkbox.checked = false;
        checkbox.dispatchEvent(new Event("change", { bubbles: true }));
    }
} else if (stateName === "boundary-pending") {
    findButton("Resolve lost-party boundary decision")?.click();
}

if (stateName === "more-options-open") {
    findButton("More options")?.click();
} else if (stateName === "teleport-workspace") {
    findButton("Teleport party")?.click();
} else if (stateName === "journey-pending") {
    findButton("Resolve journey event")?.click();
    await waitForRootText("Pending journey event");
} else if (stateName === "effects-workspace" || stateName === "effects-journey-source") {
    findButtonContaining("Resources & effects")?.click();
    await waitForRootText("Active effects");
} else if (stateName === "history-workspace") {
    findStatAction("Time")?.click();
    await waitForRootText("Effects / consequences");
} else if (stateName === "focus-return") {
    const opener = findStatAction("Time");
    opener?.focus();
    opener?.click();
    await waitForRootText("Expedition history");
    findButton("Close")?.click();
    focusReturnVerified = document.activeElement === opener;
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
const journeyPrimaryText = root.querySelector(".hc-journey-primary")?.textContent || "";
const isNormalPresentationElement = element => {
    if (!isVisible(element)) return false;
    for (let parent = element.parentElement; parent; parent = parent.parentElement) {
        if (parent instanceof HTMLDetailsElement && !parent.open) return false;
    }
    return true;
};
const normalHistoryText = Array.from(root.querySelectorAll("[data-phase15-drawer] .hc-history li"))
    .filter(isNormalPresentationElement)
    .map(item => item.textContent || "")
    .join(" ");
const normalJourneyText = Array.from(root.querySelectorAll("[data-journey-panel] h3, [data-journey-panel] h4, [data-journey-panel] p, [data-journey-panel] li"))
    .filter(isNormalPresentationElement)
    .map(item => item.textContent || "")
    .join(" ");
const normalEffectText = Array.from(root.querySelectorAll("[data-effects-panel] h3, [data-effects-panel] h4, [data-effects-panel] p, [data-effects-panel] li"))
    .filter(isNormalPresentationElement)
    .map(item => item.textContent || "")
    .join(" ");
const forcedTravelTarget = root.querySelector("[data-forced-travel-target]");
const forcedTravelResolve = Array.from(root.querySelectorAll("button"))
    .find(button => button.textContent?.trim() === "Record forced-travel result");
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
    selectedDirection: root.querySelector('[data-adjacency-edge][aria-pressed="true"]')?.dataset.adjacencyEdge ?? null,
    runtimeIntendedDirection: runtime.expedition.isSpatial ? runtime.expedition.intendedDirection : null,
    runtimeActualDirection: runtime.expedition.isSpatial ? runtime.expedition.actualDirection : null,
    currentTravelCourseText: root.querySelector("[data-current-travel-course]")?.textContent?.trim() ?? null,
    navigationStatText: Array.from(root.querySelectorAll(".hc-stat-action")).find(item =>
        item.querySelector(".hc-stat-action-label")?.textContent?.trim() === "Navigation")?.textContent?.replace(/\s+/g, " ").trim() ?? null,
    focusedNavigationText: root.querySelector("[data-phase15-drawer]")?.textContent?.replace(/\s+/g, " ").trim() ?? null,
    storedTravelPreferences: localStorage.getItem("hex-crawl.expedition." + runtime.id + ".travel-intent"),
    continueButtons: buttons.filter(button => isVisible(button) && button.textContent?.trim() === "Continue travel").length,
    travelControlsButtons: buttons.filter(button => isVisible(button) && button.textContent?.trim() === "Travel controls").length,
    drawerCount: root.querySelectorAll("[data-phase15-drawer]").length,
    mapHeight: mapHost ? Math.round(mapHost.getBoundingClientRect().height) : 0,
    mapTop: mapHost ? Math.round(mapHost.getBoundingClientRect().top) : null,
    mapContextVisible: isVisible(mapContext),
    railVisible: isVisible(rail),
    currentTravelVisible: isVisible(currentTravel),
    currentTravelTop: currentTravel ? Math.round(currentTravel.getBoundingClientRect().top) : null,
    currentTravelInRail: Boolean(currentTravel?.parentElement?.classList.contains("hc-table-rail")),
    currentTravelCount: root.querySelectorAll("[data-current-travel]").length,
    changePaceButtons: buttons.filter(button => isVisible(button) && button.textContent?.trim() === "Change pace").length,
    paceTextInputs: root.querySelectorAll('input[name="pace"][type="text"]').length,
    paceSelects: root.querySelectorAll(".hc-current-travel-pace-editor select").length,
    partyActivitiesRailButtons: buttons.filter(button => isVisible(button) && button.textContent?.includes("Party & activities")).length,
    partySetupVisible: rootText.includes("Party not configured"),
    primaryAction: root.querySelector(".hc-current-action-primary")?.textContent?.trim() || null,
    focusedTitle: root.querySelector("[data-phase15-drawer] h2")?.textContent?.trim() || null,
    focusedEdge: document.activeElement?.matches?.("[data-adjacency-edge]") ?? false,
    journeyVisible: rootText.includes("Current stage") && rootText.includes("Progress") && rootText.includes("Roles") && rootText.includes("Pending"),
    journeyStageCount: root.querySelectorAll(".hc-stage-step").length,
    movementStatusVisible: Array.from(root.querySelectorAll(".hc-stat-action-label")).some(label => label.textContent?.trim() === "Movement"),
    movementLedgerVisible: isVisible(root.querySelector("[data-movement-composition-ledger]")),
    movementContributorRows: root.querySelectorAll(".hc-movement-ledger-table tbody tr").length,
    fakeSpatialStateVisible: !runtime.expedition.isSpatial && (
        rootText.includes("Current travel")
        || rootText.includes("Pace / travel mode")
        || rootText.includes("Current cell")
        || rootText.includes("Hex progress")
        || rootText.includes("Teleport party")
        || rootText.includes("Party & travel order")),
    teleportContextVisible: Array.from(root.querySelectorAll("button")).some(button => isVisible(button) && button.textContent?.trim() === "Teleport party here"),
    journeyConsequenceVisible: rootText.includes("1 failure") && rootText.includes("2 complications"),
    journeyPendingConsequenceVisible: journeyPrimaryText.includes("1 consequence needs resolution"),
    journeyMultiplePendingConsequenceVisible: journeyPrimaryText.includes("2 consequences need resolution"),
    journeyStageStateVisible: journeyPrimaryText.includes("Stage: 1 failure, 2 complications"),
    journeyNoPendingConsequencesVisible: journeyPrimaryText.includes("No pending consequences"),
    journeyPendingRawInternalsVisible: stateName === "journey-pending"
        && /ProcessProgress|journey-event-opportunity|event-1:pass:progress/.test(normalJourneyText),
    movementUnitVisible: rootText.includes("Effective distance (mi)"),
    navigationCourseReadOnly: !["navigation-pending", "abstract-spatial-course"].includes(stateName) || (
        rootText.includes("Intended course:")
        && !root.querySelector('[data-phase15-drawer] select[name="intendedDirection"]')
    ),
    abstractSpatialCourseReused: stateName !== "abstract-spatial-course" || (
        runtime.context.kind === "AbstractHex"
        && root.querySelector("[data-adjacency-select]")?.value === "2"
        && !root.querySelector('[data-phase15-drawer] select[name="intendedDirection"]')
    ),
    forcedTravelPrimaryDomainFacing: rootText.includes("Current requirement") && rootText.includes("Failure consequence:"),
    forcedTravelNamedTargetVisible: !stateName.startsWith("forced-travel-") || (
        rootText.includes("Affected scope: Participant")
        && rootText.includes("Affected character")
        && isVisible(forcedTravelTarget)
        && !rootText.includes("Participant, mount, or vehicle ID when required")
    ),
    forcedTravelTargetOptionCount: forcedTravelTarget?.querySelectorAll("option").length ?? 0,
    forcedTravelTargetLabels: Array.from(forcedTravelTarget?.querySelectorAll("option") ?? []).map(option => option.textContent?.trim() || ""),
    forcedTravelTargetValue: forcedTravelTarget?.value ?? null,
    forcedTravelTargetRequired: forcedTravelTarget?.required ?? false,
    forcedTravelTargetVisible: isVisible(forcedTravelTarget),
    forcedTravelMissingTargetBlocked: stateName !== "forced-travel-zero-participants" || (
        rootText.includes("no party members configured")
        && forcedTravelResolve?.disabled === true
    ),
    forcedTravelTechnicalExpanded: Array.from(root.querySelectorAll("details[open] > summary")).some(summary => summary.textContent?.trim() === "Advanced consequence details"),
    normalHistoryRawInternalsVisible: stateName === "history-workspace"
        && /8ac00000-0000-0000-0000-000000000001|ResolutionRecorded|ProcessCompleted|journey-resolution|journey-process-complete|adjust-level/.test(normalHistoryText),
    technicalHistoryRetained: stateName !== "history-workspace"
        || (
            rootText.includes("8ac00000-0000-0000-0000-000000000001")
            && rootText.includes("ResolutionRecorded")
            && rootText.includes("ProcessCompleted")
        ),
    journeyEffectSourceHumanized: stateName !== "effects-journey-source"
        || (
            normalEffectText.includes("Source: Journey event")
            && !normalEffectText.includes("8ac00000-0000-0000-0000-000000000001")
            && !normalEffectText.includes("journey-event-consequence")
        ),
    journeyEffectTechnicalRetained: stateName !== "effects-journey-source"
        || (
            rootText.includes("8ac00000-0000-0000-0000-000000000001")
            && rootText.includes("journey-event-consequence")
        ),
    activeEffectVisible: rootText.includes("Fatigue") && rootText.includes("Level 2") && rootText.includes("Clear effect"),
    effectRecoveryVisible: rootText.includes("Reduce 1 level") && rootText.includes("Recovery:"),
    unifiedHistoryVisible: rootText.includes("Journey") && rootText.includes("Travel / runtime") && rootText.includes("Effects / consequences"),
    positionProgressVisible: rootText.includes("4.5 / 12 mi") && rootText.includes("37.5% through current cell"),
    focusReturnedToOpener: focusReturnVerified,
    encounterResolutionVisible: rootText.includes("Mark encounter resolved")
        && !rootText.includes("Encounter resolved — continue travel")
};
document.getElementById("review-metrics").textContent = JSON.stringify(metrics);
document.documentElement.dataset.visualReviewReady = "true";
window.fetch = originalFetch;

}
