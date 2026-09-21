import type { HexCrawlApi } from "./api";
import type { ToolRoute } from "./tool-route";

export type KnownToolRoute = Exclude<ToolRoute, { kind: "unknown" }>;
export type ClientRouteKind = KnownToolRoute["kind"];
export type ClientRouteCleanup = () => void;
export type ClientNavigate = (route: string, replace?: boolean) => void;

export interface ClientRouteContext {
    root: HTMLElement;
    api: HexCrawlApi;
    navigate: ClientNavigate;
}

export interface HexCrawlClientModule {
    readonly id: string;
    readonly routeKinds: readonly ClientRouteKind[];

    loadingMessage(route: KnownToolRoute): string;

    render(route: KnownToolRoute, context: ClientRouteContext): Promise<ClientRouteCleanup>;
}
