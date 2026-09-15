export type HexOrientation = "PointyTop" | "FlatTop";

export type WorldPoint = { x: number; y: number };
export type HexCoordinate = { q: number; r: number };

export type DistanceUnit = {
    kind: "Mile" | "Kilometer" | "Custom";
    symbol: string;
    metersPerUnit: number | null;
};

export type DistanceValue = { value: number; unit: DistanceUnit };

export type GridDefinition = {
    id: string;
    orientation: HexOrientation;
    coordinateConvention: "AxialQr";
    origin: WorldPoint;
    rotationDegrees: number;
    hexRadiusWorldUnits: number;
    neighborCenterDistance: DistanceValue;
};

export type SpatialFeature = {
    id: string;
    name: string;
    category: string;
    kind: "Point" | "Line" | "Region";
    position?: WorldPoint;
    path?: WorldPoint[];
    boundary?: WorldPoint[];
};

export type Location = {
    id: string;
    name: string;
    category: string;
    position: WorldPoint;
    discoverability: "Obvious" | "Hidden" | "Conditional";
};

export type DemoWorld = {
    id: string;
    name: string;
    grid: GridDefinition;
    features: SpatialFeature[];
    locations: Location[];
};

export type RuntimeProfile = {
    key: string;
    name: string;
    watchHours: number;
    travelResolution: "ContinuousDistance" | "HexSteps";
    actualDistanceResolution: "Fixed" | "VariableResolved";
    encounterCadence: "None" | "PerWatch" | "PerDay" | "Custom";
    usesNavigationChecks: boolean;
    usesPersistentVeer: boolean;
    tracksIntraHexProgress: boolean;
};

export type RuntimeExpedition = {
    id: string;
    currentHex: HexCoordinate;
    position: WorldPoint;
    positionPrecision: "Exact" | "HexAnchor";
    intendedDirection: number | null;
    actualDirection: number | null;
    isLost: boolean;
    veerSteps: number;
    veerDegrees: number;
    distanceTraveled: DistanceValue;
    hexProgress: DistanceValue;
    exitRequirement: DistanceValue | null;
    elapsedTravelHours: number;
    completedWatches: number;
    activeWatchNumber: number | null;
    activePaceKey: string | null;
    activeActivities: string[];
    activeNavigationAidKey: string | null;
};

export type RuntimeKnowledgeEntry = {
    subjectId: string;
    subjectType: "Location" | "Feature" | "Terrain" | "Route";
    state: "Observed" | "Discovered" | "Revealed";
    source: string | null;
};

export type RuntimeEvent = {
    sequence: number;
    watchNumber: number;
    kind: string;
    expeditionElapsedHours: number;
    hex: HexCoordinate;
    message: string;
    distanceValue: number | null;
    distanceUnit: string | null;
    subjectId: string | null;
    subjectType: string | null;
};

export type RuntimeState = {
    profile: RuntimeProfile;
    hexCenterDistance: DistanceValue;
    expedition: RuntimeExpedition;
    pauseReason: "ConditionsReviewRequired" | "LostRecognitionRequired" | "EncounterTriggered" | "BacktrackBoundaryReached" | null;
    remainingWatchHours: number;
    knowledge: RuntimeKnowledgeEntry[];
    history: RuntimeEvent[];
};

export type RuntimeAdvanceRequest = {
    intendedDirection: number;
    paceKey: string;
    activities: string[];
    navigationAidKey: string;
    suppressesNavigationCheck: boolean;
    resetsVeerAtBoundary: boolean;
    expectedDistance?: number;
    actualDistance?: number;
    hexSteps?: number;
    resolutionSource: "ProcedureDefault" | "AutomaticRoll" | "ManualRoll" | "ExternalSystem" | "DmOverride";
    navigationOutcome?: "success" | "failure";
    veerSteps?: number;
    encounterOutcome?: "none" | "wandering" | "location" | "manual";
    encounterHour?: number;
    locationId?: string;
    encounterNote?: string;
    deliberateDoubleBack: boolean;
    continueAcrossBoundaries: boolean;
    recognizedLost?: boolean;
    reorient?: boolean;
    dmOverrideNote?: string;
};

export type ToolHostContext = {
    contractVersion: number;
    toolSlug: string;
    siteMode: string;
    apiBaseUrl: string;
    toolBasePath: string;
    toolRoute: string;
    user?: { id: string; displayName: string } | null;
};
