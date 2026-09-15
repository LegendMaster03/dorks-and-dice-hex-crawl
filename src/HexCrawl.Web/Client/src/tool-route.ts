export function deriveToolRoute(basePath: string, pathname: string, initialRoute?: string | null): string {
    const normalizedBase = basePath.length > 1 ? basePath.replace(/\/$/, "") : "/";

    if (normalizedBase === "/") {
        return pathname || initialRoute || "/";
    }

    if (pathname === normalizedBase) {
        return "/";
    }

    if (pathname.startsWith(`${normalizedBase}/`)) {
        return pathname.slice(normalizedBase.length) || "/";
    }

    return initialRoute || "/";
}
