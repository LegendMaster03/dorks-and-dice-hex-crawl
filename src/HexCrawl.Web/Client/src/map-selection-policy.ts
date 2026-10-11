import type { WorldCellGeometry, WorldCellId, WorldCellLookup } from "./types";

/** A selection request may outlive both its lookup and geometry fetch.
 * Only the latest versioned user intent can publish a result. */
export async function resolveLatestWorldCellSelection(
    lookup: () => Promise<WorldCellLookup>,
    resolve: (id: WorldCellId) => Promise<WorldCellGeometry>,
    isCurrent: () => boolean,
    tilingId: string,
    unavailable: (status: WorldCellLookup["status"]) => void,
    select: (geometry: WorldCellGeometry) => void
): Promise<void> {
    const result = await lookup();
    if (!isCurrent()) return;
    if (result.status !== "Unique" || result.candidates.length !== 1) {
        unavailable(result.status);
        return;
    }
    const geometry = await resolve(result.candidates[0]);
    // This second check protects against an older fetch overwriting a newer
    // selection, or a world version/revision changing during resolution.
    if (!isCurrent() || geometry.id.tilingId !== tilingId) return;
    select(geometry);
}
