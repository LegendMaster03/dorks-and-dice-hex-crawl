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

export type EncounterCadence = "None" | "PerWatch" | "PerDay";
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

export type ProcedureRuntime = {
    intervalHours: number;
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

export type FocusedIntervalPolicySupport = "None" | "Supported" | "Unsupported";

export type FocusedIntervalPolicy = {
    support: FocusedIntervalPolicySupport;
    intervalHours: number | null;
    mechanicKey: string | null;
    mechanicVersion: number | null;
    executionHandler: string | null;
    unsupportedReason: string | null;
};

export type ProcedureAutomationLevel = "Manual" | "Assisted" | "Automatic";

export type ProcedureModule = {
    moduleKey: string;
    moduleName: string;
    mechanicKey: string;
    mechanicVersion: number;
    executionHandler: string;
    automationLevel: ProcedureAutomationLevel;
    parameters: Record<string, string>;
};

export type CampaignProcedure = {
    procedureId: string;
    revision: number;
    key: string;
    name: string;
    isExecutable: boolean;
    runtime: ProcedureRuntime | null;
    focusedIntervalPolicy: FocusedIntervalPolicy;
    modules: ProcedureModule[];
};

export type ProcedurePreset = {
    presetKey: string;
    displayName: string;
    description: string;
    category: string;
    presetRevision: number;
    procedure: CampaignProcedure;
    attribution: string | null;
    disclaimer: string | null;
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

export type ParticipantActivityAssignmentScope = "Party" | "Participant" | "Role";
export type ParticipantActivityPolicySupport = "None" | "Supported" | "Unsupported";

export type ParticipantActivityAssignment = {
    id: string;
    scope: ParticipantActivityAssignmentScope;
    participantId: string | null;
    activityKey: string | null;
    roleKey: string | null;
    note: string | null;
};

export type ParticipantActivityPolicy = {
    support: ParticipantActivityPolicySupport;
    assignmentScope: ParticipantActivityAssignmentScope | null;
    activityBudgetModel: string | null;
    activityKeys: string[];
    roleKeys: string[];
    mechanicKey: string | null;
    mechanicVersion: number | null;
    executionHandler: string | null;
    unsupportedReason: string | null;
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
    activeActivityAssignments: ParticipantActivityAssignment[];
    activeNavigationAidKey: string | null;
    activeSuppressesNavigationCheck: boolean;
    activeResetsVeerAtBoundary: boolean;
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
    activeActivityAssignments: ParticipantActivityAssignment[];
    activeNavigationAidKey: null;
    activeSuppressesNavigationCheck: false;
    activeResetsVeerAtBoundary: false;
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

export type MovementCapabilityContributorKind =
    | "Participant"
    | "Mount"
    | "Vehicle"
    | "Load"
    | "TravelMode"
    | "TerrainRoute"
    | "Environment"
    | "PersistentEffect"
    | "DmOverride";

export type MovementCapabilityOperation =
    | "Base"
    | "Replace"
    | "Multiply"
    | "Add"
    | "Cap"
    | "Floor"
    | "Cost"
    | "SymbolicLimit";

export type MovementCapabilityScope = "Party" | "Participant" | "MovementUnit";

export type MovementCapabilityContributor = {
    id: string;
    kind: MovementCapabilityContributorKind;
    key: string;
    operation: MovementCapabilityOperation;
    scope: MovementCapabilityScope;
    value: number | null;
    unit: string | null;
    perUnit: string | null;
    distanceUnit: DistanceUnit | null;
    symbolicValue: string | null;
    participantId: string | null;
    movementUnitKey: string | null;
    replacesParticipantIds: string[];
    provenance: string | null;
    note: string | null;
    enabled: boolean;
};

export type MovementCompositionPolicySupport = "None" | "Supported" | "Unsupported";
export type MovementTerrainPolicySupport = "None" | "Supported" | "Unsupported";
export type MovementCompositionStatus =
    | "Resolved"
    | "ReferenceFallback"
    | "InputRequired"
    | "RequiresAdjudication"
    | "Unsupported"
    | "Unavailable"
    | "Failed";
export type MovementReferenceUse = "None" | "AuthoritativeBase" | "Fallback" | "InformationalOnly";

export type MovementCompositionPolicy = {
    support: MovementCompositionPolicySupport;
    budgetModel: string | null;
    baseBudget: number | null;
    budgetUnit: string | null;
    limitingScope: string | null;
    terrainSupport: MovementTerrainPolicySupport;
    terrainAdjustmentModel: string | null;
    terrainAdjustments: Record<string, string>;
    routeAdjustmentModel: string | null;
    weatherAdjustmentModel: string | null;
    mechanicKey: string | null;
    mechanicVersion: number | null;
    executionHandler: string | null;
    unsupportedReason: string | null;
};

export type MovementAppliedContributor = {
    id: string | null;
    kind: MovementCapabilityContributorKind | null;
    key: string;
    operation: MovementCapabilityOperation;
    applied: boolean;
    value: number | null;
    symbolicValue: string | null;
    unit: string | null;
    perUnit: string | null;
    participantId: string | null;
    movementUnitKey: string | null;
    provenance: string | null;
    detail: string | null;
};

export type MovementCapabilityComposition = {
    policy: MovementCompositionPolicy;
    status: MovementCompositionStatus;
    effectiveValue: number | null;
    effectiveUnit: string | null;
    effectivePerUnit: string | null;
    effectiveDistanceUnit: DistanceUnit | null;
    limitingContributorKey: string | null;
    limitingParticipantId: string | null;
    preOverrideValue: number | null;
    contributors: MovementAppliedContributor[];
    provenance: string[];
    missingInputs: string[];
    diagnostics: string[];
    referenceUse: MovementReferenceUse;
    suggestedExpectedDistance: DistanceValue | null;
};

export type ExpeditionParty = {
    members: CrawlPartyMember[];
    marchingOrder: MarchingOrderPosition[];
    watchList: WatchRotationEntry[];
    standingOrders: StandingOrder[];
    activityAssignments: ParticipantActivityAssignment[];
    movementContributors?: MovementCapabilityContributor[];
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
    procedure: CampaignProcedure;
    presentation: PresentationProfile | null;
    pauseReason: RuntimePauseReason | null;
    remainingWatchHours: number;
    expedition: RuntimeExpedition;
    party: ExpeditionParty;
    participantActivityPolicy: ParticipantActivityPolicy;
    movementComposition: MovementCapabilityComposition;
    knownHexes: HexCoordinate[];
    knowledge: RuntimeKnowledgeEntry[];
    history: RuntimeEvent[];
};

export type RuntimeState = ExpeditionDetail;

export type ProcedureStartSelectionInput =
    | {
        procedureKey: string;
        procedureId?: never;
        procedureRevision?: never;
    }
    | {
        procedureKey?: never;
        procedureId: string;
        procedureRevision: number;
    };

export type StartExpeditionInput = {
    name: string;
    presentationKey: string;
    startHex: HexCoordinate;
} & ProcedureStartSelectionInput;

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
    context: StandaloneCrawlContextInput;
    startHex?: HexCoordinate;
} & ProcedureStartSelectionInput;

export type RuntimeAdvanceRequest = {
    expectedVersion: number;
    intendedDirection: number;
    paceKey: string;
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
