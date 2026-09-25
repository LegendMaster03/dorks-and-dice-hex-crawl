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
    importedContentCount: number;
    importProvenance: {
        sourceType: string;
        sourceFingerprint: string;
        importedAt: string;
        sourceRecordCount: number;
    } | null;
    sourceArchive: {
        length: number;
        mediaType: string;
        originalFileName: string | null;
    } | null;
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

export type EncounterCadence = "None" | "PerWatch" | "PerDay" | "Custom";
export type TravelResolutionMode = "ContinuousDistance" | "HexSteps";
export type ActualDistanceResolutionMode = "Fixed" | "VariableResolved";
export type ResolutionSource = "ProcedureDefault" | "AutomaticRoll" | "ManualRoll" | "ExternalSystem" | "DmOverride";

export type DiceRollFormula = {
    diceCount: number;
    dieSides: number;
    modifier: number;
};

export type ProcedureResolutionHelpers = {
    travel: {
        roll: DiceRollFormula;
        distanceFactorPerRollPoint: number;
    } | null;
    navigation: {
        checkRoll: DiceRollFormula;
    } | null;
    encounter: {
        checkRoll: DiceRollFormula;
        wanderingResults: number[];
        keyedLocationResults: number[];
        timingSlots: number;
    } | null;
};

export type RuntimeProfile = {
    key: string;
    name: string;
    watchHours: number;
    travelResolution: TravelResolutionMode;
    actualDistanceResolution: ActualDistanceResolutionMode;
    encounterCadence: EncounterCadence;
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
    resolutionHelpers: ProcedureResolutionHelpers | null;
};

export type PresentationProfile = {
    key: string;
    name: string;
    playerGrid: "Hidden" | "Visible";
    terrainMode: "HiddenUntilKnown" | "AlwaysVisible" | "Manual";
    automationMode: "Automatic" | "DmControlled";
    markEnteredHexKnown: boolean;
    initiallyKnownFeatureCategories: string[];
    initiallyKnownLocationCategories: string[];
    allowPlayerAnnotations: boolean;
};

export type RuntimePauseReason = "ConditionsReviewRequired" | "LostRecognitionRequired" | "EncounterTriggered" | "BacktrackBoundaryReached";

export type SpatialRuntimeExpedition = {
    id: string;
    isSpatial: true;
    currentHex: HexCoordinate;
    position: WorldPoint | null;
    positionPrecision: "Exact" | "HexAnchor" | null;
    entryDirection: number | null;
    lastTravelDirection: number | null;
    intendedDirection: number | null;
    actualDirection: number | null;
    isLost: boolean;
    veerSteps: number;
    veerDegrees: number;
    distanceTraveled: DistanceValue;
    hexProgress: DistanceValue;
    exitRequirement: DistanceValue | null;
    elapsedTravelHours: number;
    currentDay: number;
    completedWatches: number;
    activeWatchNumber: number | null;
    activeWatchTotalHours: number | null;
    activeWatchElapsedHours: number | null;
    activeWatchRemainingHours: number | null;
    activeWatchPendingDecision: RuntimePauseReason | null;
    activePaceKey: string | null;
    activeActivities: string[];
    activeNavigationAidKey: string | null;
    activeDeliberateDoubleBack: boolean;
    activeContinueAcrossBoundaries: boolean;
    activeEncounterKind: "None" | "WanderingEncounter" | "KeyedLocationDiscovery" | "ManualCustom" | null;
    activeEncounterHour: number | null;
    activeEncounterHandled: boolean | null;
};

export type NonSpatialRuntimeExpedition = {
    id: string;
    isSpatial: false;
    currentHex: null;
    position: null;
    positionPrecision: null;
    entryDirection: null;
    lastTravelDirection: null;
    intendedDirection: null;
    actualDirection: null;
    isLost: null;
    veerSteps: null;
    veerDegrees: null;
    distanceTraveled: null;
    hexProgress: null;
    exitRequirement: null;
    elapsedTravelHours: number;
    currentDay: number;
    completedWatches: number;
    activeWatchNumber: number | null;
    activeWatchTotalHours: number | null;
    activeWatchElapsedHours: number | null;
    activeWatchRemainingHours: number | null;
    activeWatchPendingDecision: null;
    activePaceKey: null;
    activeActivities: [];
    activeNavigationAidKey: null;
    activeDeliberateDoubleBack: false;
    activeContinueAcrossBoundaries: false;
    activeEncounterKind: null;
    activeEncounterHour: null;
    activeEncounterHandled: null;
};

export type RuntimeExpedition = SpatialRuntimeExpedition | NonSpatialRuntimeExpedition;

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
    hex: HexCoordinate | null;
    message: string;
    distanceValue: number | null;
    distanceUnit: string | null;
    subjectId: string | null;
    subjectType: string | null;
};

export type CrawlSessionContextKind = "WorldBound" | "AbstractHex" | "NonSpatial";

export type CrawlContext = {
    kind: CrawlSessionContextKind;
    name: string;
    overworldId: string | null;
    orientation: HexOrientation | null;
    hexCenterDistance: DistanceValue | null;
};

export type ExpeditionSummary = {
    id: string;
    context: CrawlContext;
    name: string;
    procedureName: string;
    version: number;
    createdAt: string;
    updatedAt: string;
};

export type CrawlPartyMember = {
    id: string;
    name: string;
    externalCharacterId: string | null;
    countsTowardPartyMovement: boolean;
};

export type MarchingOrderPosition = {
    memberId: string;
    rank: number;
    file: number;
};

export type WatchRotationEntry = {
    slot: number;
    memberIds: string[];
    label: string | null;
};

export type StandingOrder = {
    id: string;
    text: string;
    enabled: boolean;
};

export type PartyMovementReference = {
    perHour: DistanceValue | null;
    perWatch: DistanceValue | null;
    perMarch: DistanceValue | null;
    limitingMemberId: string | null;
    note: string | null;
};

export type ExpeditionParty = {
    members: CrawlPartyMember[];
    marchingOrder: MarchingOrderPosition[];
    watchList: WatchRotationEntry[];
    standingOrders: StandingOrder[];
    defaultNavigatorMemberId: string | null;
    baseMovement: PartyMovementReference | null;
};

export type UpdateExpeditionPartyRequest = ExpeditionParty & {
    expectedVersion: number;
};

export type ExpeditionDetail = {
    id: string;
    overworldId: string | null;
    context: CrawlContext;
    name: string;
    version: number;
    createdAt: string;
    updatedAt: string;
    profile: RuntimeProfile;
    presentation: PresentationProfile | null;
    pauseReason: RuntimePauseReason | null;
    remainingWatchHours: number;
    expedition: RuntimeExpedition;
    party: ExpeditionParty;
    knownHexes: HexCoordinate[];
    knowledge: RuntimeKnowledgeEntry[];
    history: RuntimeEvent[];
};

export type RuntimeState = ExpeditionDetail;

export type StartExpeditionInput = {
    name: string;
    procedureKey: string;
    presentationKey: string;
    startHex: HexCoordinate;
    procedureSnapshot?: RuntimeProfile;
};

export type StandaloneCrawlContextInput =
    | {
        kind: "AbstractHex";
        name: string;
        orientation: HexOrientation;
        hexCenterDistance: number;
        distanceUnit: DistanceUnit;
    }
    | {
        kind: "NonSpatial";
        name: string;
    };

export type StartStandaloneCrawlSessionInput = {
    name: string;
    procedureKey: string;
    context: StandaloneCrawlContextInput;
    startHex?: HexCoordinate;
    procedureSnapshot?: RuntimeProfile;
};

export type RuntimeAdvanceRequest = {
    expectedVersion: number;
    intendedDirection: number;
    paceKey: string;
    activities: string[];
    navigationAidKey: string;
    suppressesNavigationCheck: boolean;
    resetsVeerAtBoundary: boolean;
    effectiveDistance?: number;
    expectedDistance?: number;
    actualDistance?: number;
    hexSteps?: number;
    resolutionSource: ResolutionSource;
    travelResolutionSource?: ResolutionSource;
    travelResolutionNote?: string;
    navigationResolutionSource?: ResolutionSource;
    navigationResolutionNote?: string;
    encounterResolutionSource?: ResolutionSource;
    encounterResolutionNote?: string;
    boundaryResolutionSource?: ResolutionSource;
    boundaryResolutionNote?: string;
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
    generatedProcedureResolutionId?: string;
};

export type ProcedureResolutionHelperRequest = {
    expectedVersion: number;
    expectedDistance?: number;
    suppressesNavigationCheck: boolean;
    deliberateDoubleBack: boolean;
    navigationDifficultyClass?: number;
    navigationModifier: number;
    failureVeerSteps?: number;
    keyedLocationId?: string;
};

export type ProcedureResolutionRoll = {
    purpose: string;
    formula: string;
    dice: number[];
    modifier: number;
    total: number;
};

export type ProcedureResolvedTravel = {
    expectedDistance: number;
    actualDistance: number;
    provenance: { source: ResolutionSource; note: string | null };
};

export type ProcedureResolvedNavigation = {
    outcome: "NotRequired" | "Succeeded" | "Failed";
    veerSteps: number | null;
    provenance: { source: ResolutionSource; note: string | null };
};

export type ProcedureResolvedEncounter = {
    kind: "None" | "WanderingEncounter" | "KeyedLocationDiscovery" | "ManualCustom";
    occursAtHours: number | null;
    locationId: string | null;
    note: string | null;
    provenance: { source: ResolutionSource; note: string | null };
};

export type ProcedureResolutionHelperResult = {
    expeditionVersion: number;
    generatedResolutionId: string | null;
    auditSequence: number | null;
    travel: ProcedureResolvedTravel | null;
    navigation: ProcedureResolvedNavigation | null;
    encounter: ProcedureResolvedEncounter | null;
    rolls: ProcedureResolutionRoll[];
    notes: string[];
};

export type TravelWatchAssistantRequest = {
    expectedVersion: number;
    elapsedHours: number;
    distance?: number;
    hexSteps?: number;
    resultingHex: HexCoordinate;
    hexProgress?: number;
    intendedDirection?: number;
    actualDirection?: number;
    completeWatch: boolean;
    resolutionSource: ResolutionSource;
    resolutionNote?: string;
    note?: string;
};

export type NonSpatialWatchAssistantRequest = {
    expectedVersion: number;
    elapsedHours: number;
    resolutionSource: ResolutionSource;
    resolutionNote?: string;
    note?: string;
};

export type NavigationAssistantRequest = {
    expectedVersion: number;
    isLost: boolean;
    veerSteps: number;
    intendedDirection?: number;
    resolutionSource: ResolutionSource;
    resolutionNote?: string;
    note?: string;
};

export type EncounterCadenceAssistantRequest = {
    expectedVersion: number;
    outcome: "None" | "WanderingEncounter" | "ManualCustom";
    resolutionSource: ResolutionSource;
    resolutionNote?: string;
    note?: string;
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
