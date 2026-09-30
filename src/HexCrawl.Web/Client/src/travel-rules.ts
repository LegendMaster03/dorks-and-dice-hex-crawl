import type { ProcedureResolutionHelperRequest } from "./types";

export const travelEnvironmentMechanicKeys = {
    walkDistance: "travel.overland.walk-distance",
    hustleDistance: "travel.overland.hustle-distance",
    terrainDistanceFactor: "travel.overland.terrain-distance-factor",
    avoidGettingLost: "travel.navigation.avoid-getting-lost"
} as const;

export type TravelEnvironmentProviderMetadata = {
    providerKey: string;
    displayName: string;
    isDefault: boolean;
};

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

export type TravelEnvironmentProviderCatalog = {
    scope: string;
    campaignId: string | null;
    revisionNumber: number | null;
    publishedAt: string | null;
    mechanics: TravelEnvironmentMechanicView[];
};

// Retain the API client's established exported name while the payload now represents
// provider availability plus the provider-neutral capability catalog.
export type TravelEnvironmentCatalog = {
    provider: TravelEnvironmentProviderMetadata | null;
    availability: "available" | "unavailable" | "failed" | string;
    catalog: TravelEnvironmentProviderCatalog | null;
    detail: string | null;
};

export type SourceBackedProcedureResolutionHelperRequest = ProcedureResolutionHelperRequest & {
    travelDistanceRule?: "walk" | "hustle";
    baseSpeedFeet?: number;
    terrain?: string;
    route?: string;
    navigationRiskFactors?: string[];
};
