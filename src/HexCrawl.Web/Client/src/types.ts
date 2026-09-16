export type HexOrientation = "PointyTop" | "FlatTop";
export type HexCoordinateConvention = "AxialQr";
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
    coordinateConvention: HexCoordinateConvention;
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
    position: WorldPoint | null;
    path: WorldPoint[] | null;
    boundary: WorldPoint[] | null;
};

export type Location = {
    id: string;
    name: string;
    category: string;
    position: WorldPoint;
    discoverability: "Obvious" | "Hidden" | "Conditional";
};

export type SourceMapRole = "Gm" | "Player" | "Neutral" | "Other";

export type MapRegistrationTransform = {
    kind: "Affine" | "Projective";
    m11: number;
    m12: number;
    m13: number;
    m21: number;
    m22: number;
    m23: number;
    m31: number;
    m32: number;
};

export type RegistrationControlPoint = {
    sourcePixel: WorldPoint;
    worldPoint: WorldPoint;
};

export type SourceMapRepresentation = {
    id: string;
    geographyKey: string;
    name: string;
    role: SourceMapRole;
    assetKey: string;
    containsBakedGrid: boolean;
    alignment: MapRegistrationTransform | null;
    worldCoverageBoundary: WorldPoint[];
};

export type SourceMapDetail = SourceMapRepresentation & {
    pixelWidth: number;
    pixelHeight: number;
    mediaType: string;
    originalFileName: string | null;
};

export type SourceMapList = {
    overworldVersion: number;
    sourceMaps: SourceMapDetail[];
};

export type OverworldSummary = {
    id: string;
    name: string;
    version: number;
    createdAt: string;
    updatedAt: string;
};

export type Overworld = {
    id: string;
    name: string;
    version: number;
    createdAt: string;
    updatedAt: string;
    grid: GridDefinition;
    features: SpatialFeature[];
    locations: Location[];
    sourceMaps: SourceMapRepresentation[];
};

export type DemoWorld = Overworld;

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
    directionChangesCostProgress: boolean;
    supportsDeliberateDoubleBack: boolean;
    startingExitProgressFactor: number;
    nearExitProgressFactor: number;
    farExitProgressFactor: number;
    backExitProgressFactor: number;
    directionChangeProgressCostFactor: number;
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
    learnedAt: string | null;
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

export type ExpeditionSummary = {
    id: string;
    overworldId: string;
    name: string;
    procedureName: string;
    version: number;
    createdAt: string;
    updatedAt: string;
};

export type ExpeditionDetail = {
    id: string;
    overworldId: string;
    name: string;
    version: number;
    createdAt: string;
    updatedAt: string;
    profile: RuntimeProfile;
    pauseReason: "ConditionsReviewRequired" | "LostRecognitionRequired" | "EncounterTriggered" | "BacktrackBoundaryReached" | null;
    remainingWatchHours: number;
    expedition: RuntimeExpedition;
    knowledge: RuntimeKnowledgeEntry[];
    history: RuntimeEvent[];
};

export type RuntimeState = ExpeditionDetail;

export type RuntimeAdvanceRequest = {
    expectedVersion: number;
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
    navigationOutcome?: "NotRequired" | "Succeeded" | "Failed";
    veerSteps?: number;
    encounterOutcome?: "None" | "WanderingEncounter" | "KeyedLocationDiscovery" | "ManualCustom";
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
