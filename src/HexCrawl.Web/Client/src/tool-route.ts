export type ToolRoute =
    | { kind: "worlds" }
    | { kind: "world"; worldId: string }
    | { kind: "edit"; worldId: string }
    | { kind: "expedition"; worldId: string; expeditionId: string }
    | { kind: "unknown"; path: string };

export function deriveToolRoute(basePath: string, pathname: string, initialRoute?: string | null): string {
    const normalizedBase = normalizeBase(basePath);
    if (normalizedBase === "/") return pathname || initialRoute || "/";
    if (pathname === normalizedBase) return "/";
    if (pathname.startsWith(`${normalizedBase}/`)) return pathname.slice(normalizedBase.length) || "/";
    return initialRoute || "/";
}

export function parseToolRoute(path: string): ToolRoute {
    const normalized = normalizeRoute(path);
    if (normalized === "/" || normalized === "/worlds") return { kind: "worlds" };

    let match = normalized.match(/^\/worlds\/([^/]+)$/);
    if (match) return { kind: "world", worldId: decodeURIComponent(match[1]) };
    match = normalized.match(/^\/worlds\/([^/]+)\/edit$/);
    if (match) return { kind: "edit", worldId: decodeURIComponent(match[1]) };
    match = normalized.match(/^\/worlds\/([^/]+)\/expeditions\/([^/]+)$/);
    if (match) return {
        kind: "expedition",
        worldId: decodeURIComponent(match[1]),
        expeditionId: decodeURIComponent(match[2])
    };
    return { kind: "unknown", path: normalized };
}

export function canonicalExpeditionRoute(
    routeWorldId: string,
    expedition: { id: string; overworldId: string }): string | null {
    if (routeWorldId === expedition.overworldId) return null;
    return `/worlds/${encodeURIComponent(expedition.overworldId)}/expeditions/${encodeURIComponent(expedition.id)}`;
}

export function toolRelativeHref(basePath: string, route: string): string {
    const base = normalizeBase(basePath);
    const normalizedRoute = normalizeRoute(route);
    return base === "/" ? normalizedRoute : `${base}${normalizedRoute}`;
}

export function navigateTool(basePath: string, route: string, replace = false): void {
    const href = toolRelativeHref(basePath, route);
    if (replace) history.replaceState({}, "", href);
    else history.pushState({}, "", href);
    window.dispatchEvent(new PopStateEvent("popstate"));
}

function normalizeBase(basePath: string): string {
    if (!basePath || basePath === "/") return "/";
    const withSlash = basePath.startsWith("/") ? basePath : `/${basePath}`;
    return withSlash.replace(/\/$/, "");
}

function normalizeRoute(route: string): string {
    if (!route || route === "/") return "/";
    const withSlash = route.startsWith("/") ? route : `/${route}`;
    return withSlash.length > 1 ? withSlash.replace(/\/$/, "") : withSlash;
}
