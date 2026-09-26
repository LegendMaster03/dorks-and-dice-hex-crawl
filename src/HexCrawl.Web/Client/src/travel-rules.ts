import type { ProcedureResolutionHelperRequest } from "./types";

export const travelEnvironmentMechanicKeys = {
    walkDistance: "travel.overland.walk-distance",
    hustleDistance: "travel.overland.hustle-distance",
    terrainDistanceFactor: "travel.overland.terrain-distance-factor",
    avoidGettingLost: "travel.navigation.avoid-getting-lost"
} as const;

export type TravelEnvironmentInputDefinition = {
    key: string;
    valueKind: string;
    required: boolean;
    allowedValues: string[] | null;
};

export type TravelEnvironmentMechanicDefinition = {
    mechanicKey: string;
    kind: string;
    displayName: string;
    resolutionKind: string;
    inputs: TravelEnvironmentInputDefinition[];
    factorSemantic: string | null;
    scale: string | null;
};

export type TravelEnvironmentMechanicView = {
    mechanicKey: string;
    state: string;
    canResolve: boolean;
    definition: TravelEnvironmentMechanicDefinition | null;
};

export type TravelEnvironmentCatalog = {
    scope: string;
    campaignId: string | null;
    revisionNumber: number | null;
    publishedAt: string | null;
    mechanics: TravelEnvironmentMechanicView[];
};

export type SourceBackedProcedureResolutionHelperRequest = ProcedureResolutionHelperRequest & {
    travelDistanceRule?: "walk" | "hustle";
    baseSpeedFeet?: number;
    terrain?: string;
    route?: string;
    navigationRiskFactors?: string[];
};
