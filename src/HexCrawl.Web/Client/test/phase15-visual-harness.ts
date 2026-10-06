// @ts-nocheck
import { ensureStyles } from "../src/styles";
import { renderProcedureAuthoringWorkspace } from "../src/modules/procedures/procedure-authoring-view";

const query = new URLSearchParams(location.search);
const scenario = query.get("scenario") ?? "home";
const theme = query.get("theme") ?? "light";
document.documentElement.dataset.bsTheme = theme === "dark" ? "dark" : "light";

const root = document.querySelector("#tool-root");
ensureStyles();

const labels = {
    "time.interval": "Travel period",
    "party.activities": "Travel activities",
    "movement.budget": "Available movement",
    "movement.terrain": "Terrain and routes",
    "movement.resolution": "Movement resolution",
    "movement.hex-progress": "Cell progress",
    "navigation.check": "Navigation checks",
    "navigation.outcome": "Getting lost and recovery",
    "encounters.cadence": "Encounter checks",
    "encounters.schedule": "Encounter schedule",
    "survival.resources": "Food, water, and supplies",
    "exploration.foraging": "Foraging",
    "survival.camping": "Camping",
    "time.forced-travel": "Forced travel",
    "survival.exposure": "Environmental exposure",
    "effects.expedition": "Persistent effects",
    "journey.process": "Multi-stage journey",
    "journey.events": "Journey events",
    "procedure.helpers": "Automatic resolution"
};

const purposes = {
    "time.interval": "Sets the repeating travel period.",
    "party.activities": "Assigns expedition activities and travel roles.",
    "movement.budget": "Defines how much movement is available.",
    "movement.terrain": "Applies terrain, route, and weather movement rules.",
    "movement.resolution": "Resolves distance or whole-cell movement.",
    "movement.hex-progress": "Tracks progress across map cells.",
    "navigation.check": "Determines whether navigation checks are required.",
    "navigation.outcome": "Defines becoming lost, recognition, and recovery.",
    "encounters.cadence": "Checks for encounters at the configured cadence.",
    "encounters.schedule": "Adds contextual encounter timing.",
    "survival.resources": "Tracks expedition resources.",
    "exploration.foraging": "Resolves foraging activity.",
    "survival.camping": "Resolves camping and watches.",
    "time.forced-travel": "Resolves travel beyond the normal limit.",
    "survival.exposure": "Resolves harmful environmental exposure.",
    "effects.expedition": "Tracks persistent expedition effects and recovery.",
    "journey.process": "Runs ordered journey stages and progress.",
    "journey.events": "Creates journey event opportunities.",
    "procedure.helpers": "Generates supported routine results."
};

function parameterType(key, value) {
    if (key === "durationTicks") return "integer";
    if (value === "true" || value === "false") return "boolean";
    if (/Factor$/.test(key) || ["baseBudget","normalTravelLimit","timeCost"].includes(key)) return "number";
    if (/Keys$|Kinds$|Sources$/.test(key)) return "key-list";
    if (/Adjustments$/.test(key)) return "map<string,string>";
    return "string";
}

function composerModule(moduleKey, parameters = {}, options = {}) {
    const schema = Object.fromEntries(Object.entries(parameters).map(([key, value]) => [key, {
        type: parameterType(key, value),
        required: false,
        description: `Configure ${key}.`,
        defaultValue: value
    }]));
    const mechanic = {
        key: options.mechanicKey ?? `${moduleKey}-policy`,
        displayName: options.mechanicName ?? "Default behavior",
        description: purposes[moduleKey] ?? "Generic procedure behavior.",
        version: 1,
        executionHandler: options.executionHandler ?? "generic-procedure",
        automationLevel: options.automationLevel ?? "Manual",
        executionSupport: options.executionSupport ?? "Declarative",
        inputs: [],
        outputs: options.outputs ?? [],
        parameterSchema: schema,
        compatibilityTags: []
    };
    return {
        moduleKey,
        category: options.category ?? "Exploration",
        displayName: labels[moduleKey] ?? moduleKey,
        purpose: purposes[moduleKey] ?? "Generic procedure behavior.",
        executionStage: options.executionStage ?? "Travel",
        reads: options.reads ?? [],
        produces: options.outputs ?? [],
        requiredDependencies: options.requiredDependencies ?? [],
        optionalDependencies: options.optionalDependencies ?? [],
        presentationMetadata: {},
        mechanic,
        alternatives: [mechanic],
        configurationSchema: schema,
        parameters,
        requiredInputs: [],
        outputs: options.outputs ?? [],
        dependencyIssues: options.dependencyIssues ?? [],
        isModified: options.isModified ?? false,
        modificationCount: options.isModified ? 1 : 0,
        validationIssues: options.validationIssues ?? []
    };
}

const time = () => composerModule("time.interval", { durationTicks: "144000000000" }, {
    automationLevel: "Automatic", executionSupport: "Native", executionHandler: "fixed-interval"
});
const movement = () => composerModule("movement.resolution", {
    travelResolution: "ContinuousDistance",
    actualDistanceResolution: "Variable",
    tracksIntraHexProgress: "true"
}, { automationLevel: "Automatic", executionSupport: "Native", executionHandler: "movement-resolution-policy" });
const progress = () => composerModule("movement.hex-progress", {
    startingExitProgressFactor: "0.5",
    nearExitProgressFactor: "0.5",
    farExitProgressFactor: "1",
    backExitProgressFactor: "0.5",
    directionChangesCostProgress: "true",
    directionChangeProgressCostFactor: "0.5",
    supportsDeliberateDoubleBack: "true"
});
const navigation = () => composerModule("navigation.check", {
    usesNavigationChecks: "true",
    usesPersistentVeer: "true"
});
const navOutcome = () => composerModule("navigation.outcome", {
    checkTriggerModel: "per-watch-when-navigation-required",
    failureStateModel: "lost-until-recognized",
    directionalErrorModel: "persistent-veer",
    recognitionModel: "boundary-check",
    reorientationModel: "procedure-check"
});
const activities = () => composerModule("party.activities", {
    assignmentScope: "participant",
    activityBudgetModel: "per-watch",
    activityKeys: "travel;reconnoiter;forage;make-camp;lookout",
    roleKeys: "navigator;lookout;forager;scout"
});
const budget = () => composerModule("movement.budget", {
    budgetModel: "activity-and-distance",
    baseBudget: "1",
    budgetUnit: "watch",
    limitingScope: "party-limiting"
});
const terrain = () => composerModule("movement.terrain", {
    adjustmentModel: "activity-cost",
    terrainAdjustments: "open=1;difficult=2;severe=3",
    routeAdjustmentModel: "road-improves-one-step",
    weatherAdjustmentModel: "manual"
});
const cadence = () => composerModule("encounters.cadence", { cadence: "PerWatch" }, {
    automationLevel: "Automatic", executionSupport: "Native", executionHandler: "encounter-cadence"
});
const schedule = () => composerModule("encounters.schedule", {
    scheduleModel: "contextual",
    travelChecksPerInterval: "1",
    campCheck: "true",
    terrainProbabilityModel: "procedure"
});
const resources = () => composerModule("survival.resources", {
    resourceKinds: "food;water;light",
    inventoryModel: "supply-die",
    consumptionModel: "usage-roll",
    consumptionInterval: "watch"
});
const foraging = () => composerModule("exploration.foraging", {
    resolutionModel: "activity-check",
    timeCost: "1",
    timeUnit: "watch-activity",
    movementTradeoff: "replaces-activity"
});
const camping = () => composerModule("survival.camping", {
    resolutionModel: "activity-check",
    timeCost: "1",
    timeUnit: "watch",
    watchModel: "assigned-lookout"
});
const forced = () => composerModule("time.forced-travel", {
    normalTravelLimit: "2",
    limitUnit: "watches",
    checkModel: "escalating-check",
    failureConsequence: "fatigue"
});
const exposure = () => composerModule("survival.exposure", {
    exposureKinds: "cold;heat;storm",
    checkModel: "environment-triggered",
    consequenceModel: "fatigue",
    scope: "participant"
});
const effects = () => composerModule("effects.expedition", {
    effectKinds: "fatigue",
    accumulationModel: "levels",
    recoveryModel: "safe-rest",
    scope: "participant"
});
const helpers = () => composerModule("procedure.helpers", {
    "travel.enabled": "true",
    "navigation.enabled": "true",
    "encounter.enabled": "true"
}, { automationLevel: "Automatic", executionSupport: "Native", executionHandler: "resolution-helpers" });
const journey = () => composerModule("journey.process", {
    processModel: "sequential-stages",
    stageKeys: "route;events;arrival",
    progressModel: "journey-progress",
    roleModel: "current-at-resolution",
    completionModel: "final-stage"
});
const journeyEvents = () => composerModule("journey.events", {
    triggerModel: "process-progress",
    triggerSources: "process-progress;stage-transition;explicit",
    linkMode: "process-linked",
    targetingModel: "journey-role",
    terrainInfluence: "difficulty",
    consequenceModel: "event-and-fatigue",
    requiresResolvedTrigger: "true",
    blocksRelevantTravelWhileResolutionRequired: "false"
});

const drafts = {
    empty: [],
    simple: [time(), movement()],
    complex: [
        time(), activities(), budget(), terrain(), movement(), progress(), navigation(), navOutcome(),
        cadence(), schedule(), resources(), foraging(), camping(), forced(), exposure(), effects(), helpers()
    ],
    journey: [journey(), journeyEvents(), effects()]
};

function draft(id) {
    const modules = (drafts[id] ?? []).map(value => structuredClone(value));
    if (id === "complex") {
        modules.find(value => value.moduleKey === "movement.terrain").validationIssues =
            ["Weather adjustment remains DM-resolved when the procedure has no authoritative weather provider."];
    }
    return {
        procedureId: id === "empty" ? "00000000-0000-0000-0000-000000000000" : `11111111-1111-1111-1111-${id.padEnd(12,"0").slice(0,12)}`,
        revision: 3,
        key: `visual-${id}`,
        name: id === "complex" ? "Traditional hexcrawl procedure"
            : id === "journey" ? "Journey-first procedure"
            : id === "simple" ? "Simple interval travel"
            : "Blank custom procedure",
        isExecutable: id !== "journey",
        modificationCount: id === "complex" ? 2 : 0,
        modifiedModuleCount: id === "complex" ? 2 : 0,
        origin: id === "complex"
            ? { presetKey:"alexandrian-advanced", presetDisplayName:"Alexandrian Advanced", presetRevision:1, attribution:"Procedure research based on The Alexandrian hexcrawl watch checklist.", disclaimer:null }
            : null,
        modules,
        dependencies: { hasErrors:false, issues:[] },
        overrides: []
    };
}

function presetModule(module) {
    return {
        moduleKey: module.moduleKey,
        moduleName: module.displayName,
        mechanicKey: module.mechanic.key,
        mechanicVersion: 1,
        executionHandler: module.mechanic.executionHandler,
        automationLevel: module.mechanic.automationLevel,
        parameters: module.parameters
    };
}
function preset(key, displayName, description, modules, intervalHours, category="Familiar procedures") {
    return {
        presetKey:key,
        displayName,
        description,
        category,
        presetRevision:1,
        procedure:{
            procedureId:`22222222-2222-2222-2222-${key.replace(/[^a-z0-9]/g,"").padEnd(12,"0").slice(0,12)}`,
            revision:1,
            key,
            name:displayName,
            isExecutable:intervalHours != null,
            runtime: intervalHours == null ? null : {
                intervalHours,
                travelResolution:"ContinuousDistance",
                actualDistanceResolution:"Variable",
                encounterCadence:"PerWatch",
                usesNavigationChecks:true,
                usesPersistentVeer:true,
                tracksIntraHexProgress:true,
                directionChangesCostProgress:true,
                supportsDeliberateDoubleBack:true,
                startingExitProgressFactor:.5,
                nearExitProgressFactor:.5,
                farExitProgressFactor:1,
                backExitProgressFactor:.5,
                directionChangeProgressCostFactor:.5,
                resolutionHelpers:null
            },
            focusedIntervalPolicy:{support:"Supported",intervalHours,mechanicKey:"time",mechanicVersion:1,executionHandler:"time",unsupportedReason:null},
            modules:modules.map(presetModule)
        },
        attribution:`${displayName} procedure research.`,
        disclaimer:key === "one-ring-2e" ? "Exact publisher event tables and modifiers remain explicit DM input." : null
    };
}

const alexModules=[time(),movement(),progress(),navigation(),cadence(),helpers()];
const bxTime=composerModule("time.interval",{durationTicks:"864000000000"});
const bxModules=[bxTime,budget(),terrain(),navOutcome(),composerModule("encounters.cadence",{cadence:"PerDay"}),resources(),foraging()];
const survivalTime=composerModule("time.interval",{durationTicks:"216000000000"});
const survivalModules=[survivalTime,activities(),budget(),terrain(),resources(),foraging(),camping(),forced(),effects()];
const journeyModules=[journey(),journeyEvents(),effects()];

const presets=[
    preset("alexandrian-advanced","Alexandrian Advanced","Four-hour watch procedure with detailed movement, progress, navigation, encounters, and optional automatic resolution.",alexModules,4),
    preset("bx","B/X","Day-scale wilderness travel with terrain-sensitive movement, navigation outcomes, encounters, resources, and foraging.",bxModules,24),
    preset("forbidden-lands","Forbidden Lands","Quarter-day travel focused on party activities, terrain, resources, camping, forced travel, and persistent conditions.",survivalModules,6),
    preset("one-ring-2e","The One Ring 2e","Role-driven journey stages and events without inventing a repeating travel interval.",journeyModules,null),
    preset("simple-fixed-distance","Simple Fixed Distance","Minimal four-hour fixed-distance travel with partial cell progress and no navigation or encounter checks.",[time(),movement(),progress()],4,"Generic starting points"),
    preset("simple-hex-step","Simple Hex Step","Minimal four-hour whole-cell travel without partial progress, navigation, or encounter checks.",[time(),composerModule("movement.resolution",{travelResolution:"HexSteps",actualDistanceResolution:"Fixed",tracksIntraHexProgress:"false"})],4,"Generic starting points")
];

const savedProcedures=[
    {procedureId:"simple",revision:2,key:"simple",name:"West Marches travel",campaignId:null,originPresetKey:"bx",originPresetDisplayName:"B/X",isExecutable:true,moduleCount:7,createdAt:"2026-10-01T00:00:00Z"},
    {procedureId:"complex",revision:3,key:"complex",name:"Northreach traditional crawl",campaignId:null,originPresetKey:"alexandrian-advanced",originPresetDisplayName:"Alexandrian Advanced",isExecutable:true,moduleCount:17,createdAt:"2026-10-02T00:00:00Z"},
    {procedureId:"journey",revision:4,key:"journey",name:"Long road journeys",campaignId:null,originPresetKey:"one-ring-2e",originPresetDisplayName:"The One Ring 2e",isExecutable:false,moduleCount:3,createdAt:"2026-10-03T00:00:00Z"},
    {procedureId:"empty",revision:1,key:"empty",name:"Custom exploration draft",campaignId:null,originPresetKey:null,originPresetDisplayName:null,isExecutable:false,moduleCount:0,createdAt:"2026-10-04T00:00:00Z"}
];

const jsonResponse = value => new Response(JSON.stringify(value), {
    status:200, headers:{"Content-Type":"application/json"}
});

globalThis.fetch=async(input,init={})=>{
    const url=new URL(typeof input==="string"?input:input.url,location.origin);
    const method=(init.method??"GET").toUpperCase();
    if(url.pathname==="/api/procedures" && method==="GET") return jsonResponse(savedProcedures);
    if(/^\/api\/procedures\/[^/]+\/revisions$/.test(url.pathname) && method==="GET"){
        const id=url.pathname.split("/")[3];
        const value=draft(id);
        return jsonResponse([{procedureId:value.procedureId,revision:value.revision,name:value.name,modificationCount:value.modificationCount,createdAt:"2026-10-05T00:00:00Z"}]);
    }
    if(url.pathname==="/api/procedures/composer/draft" && method==="POST"){
        const body=JSON.parse(String(init.body??"{}"));
        if(body.procedureId) return jsonResponse(draft(String(body.procedureId)));
        if(body.presetKey){
            const selected=presets.find(value=>value.presetKey===body.presetKey);
            const generated=draft("complex");
            generated.name=selected?.displayName??"Preset procedure";
            generated.modules=(selected?.procedure.modules??[]).map(value=>composerModule(value.moduleKey,value.parameters));
            return jsonResponse(generated);
        }
        return jsonResponse(draft("empty"));
    }
    if(url.pathname==="/api/procedures/composer/canonical/draft" && method==="POST"){
        return jsonResponse({procedureId:draft("complex").procedureId,revision:3,canonicalJson:JSON.stringify(draft("complex"),null,2)});
    }
    if(url.pathname==="/api/procedures/composer/canonical/validate" && method==="POST"){
        return jsonResponse({isValid:true,procedureId:draft("complex").procedureId,revision:3,error:null,lineNumber:null,bytePositionInLine:null});
    }
    return new Response(JSON.stringify({error:`Unhandled visual harness request: ${method} ${url.pathname}`}),{status:500,headers:{"Content-Type":"application/json"}});
};

const fakeApi={ getProcedurePresets: async()=>presets };

if(scenario.startsWith("advanced")) localStorage.setItem("hex-crawl.procedure-authoring.mode","advanced");
else if(scenario==="json") localStorage.setItem("hex-crawl.procedure-authoring.mode","json");
else localStorage.setItem("hex-crawl.procedure-authoring.mode","compact");

let procedureId=null;
if(["simple","complex","journey","advanced-complex","json","focus"].includes(scenario)) {
    procedureId=scenario==="advanced-complex"||scenario==="json"||scenario==="focus"?"complex":scenario;
} else if(scenario==="advanced-empty") procedureId="empty";

await renderProcedureAuthoringWorkspace(root,fakeApi,procedureId,()=>{});

const waitFrame=()=>new Promise(resolve=>requestAnimationFrame(()=>requestAnimationFrame(resolve)));
if(scenario==="catalog" || scenario.startsWith("inspect-")){
    root.querySelector(".hc-procedure-home-section button")?.click();
    const browse=[...root.querySelectorAll("button")].find(value=>value.textContent==="Browse all presets");
    browse?.click();
    await waitFrame();
}
if(scenario.startsWith("inspect-")){
    const wanted={
        "inspect-alex":"Alexandrian Advanced",
        "inspect-bx":"B/X",
        "inspect-survival":"Forbidden Lands",
        "inspect-journey":"The One Ring 2e"
    }[scenario];
    const card=[...root.querySelectorAll(".hc-preset-card")].find(value=>value.querySelector("h3")?.textContent===wanted);
    [...(card?.querySelectorAll("button")??[])].find(value=>value.textContent==="Inspect")?.click();
    await waitFrame();
}
if(scenario==="blank"){
    [...root.querySelectorAll("button")].find(value=>value.textContent==="Build a custom procedure")?.click();
    for(let i=0;i<30 && !root.textContent.includes("Choose a starting rule");i++) await new Promise(resolve=>setTimeout(resolve,20));
}
if(scenario==="focus"){
    [...root.querySelectorAll("button")].find(value=>value.textContent?.toLowerCase().includes("edit travel period"))?.click();
    await waitFrame();
}
if(scenario==="advanced-complex"){
    root.querySelector('[data-module-key="movement.terrain"]')?.click();
    await waitFrame();
}
await waitFrame();
document.body.dataset.visualReady="true";
