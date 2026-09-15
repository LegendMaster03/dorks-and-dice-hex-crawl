export type HexOrientation = "PointyTop" | "FlatTop";

export type WorldPoint = { x: number; y: number };
export type HexCoordinate = { q: number; r: number };

export type DistanceUnit = {
    kind: "Mile" | "Kilometer" | "Custom";
    symbol: string;
    metersPerUnit: number | null;
};

export type GridDefinition = {
    id: string;
    orientation: HexOrientation;
    coordinateConvention: "AxialQr";
    origin: WorldPoint;
    rotationDegrees: number;
    hexRadiusWorldUnits: number;
    neighborCenterDistance: { value: number; unit: DistanceUnit };
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

export type ToolHostContext = {
    contractVersion: number;
    toolSlug: string;
    siteMode: string;
    apiBaseUrl: string;
    toolBasePath: string;
    toolRoute: string;
    user?: { id: string; displayName: string } | null;
};
