import { hexToWorld } from "../../hex-math.js";
import type { HexCrawlApi } from "../../api";
import type { HexCoordinate, Overworld, PeriodicCellAddress } from "../../types";

/**
 * Legacy q/r is a UI convenience, never expedition authority.
 * Resolve its world-space center through the accepted server tiling; do not
 * assume a motif id or fabricate a spatial cursor.
 */
export async function resolveHexStartCell(
    api: Pick<HexCrawlApi, "lookupWorldCell">,
    world: Overworld,
    hex: HexCoordinate): Promise<PeriodicCellAddress> {
    if (!world.grid || !world.tiling)
        throw new Error("A saved hex world with accepted cell topology is required.");
    if (!Number.isSafeInteger(hex.q) || !Number.isSafeInteger(hex.r))
        throw new Error("Starting hex coordinates must be safe integers.");
    const lookup = await api.lookupWorldCell(world.id, hexToWorld(world.grid, hex));
    if (lookup.status !== "Unique" || lookup.candidates.length !== 1)
        throw new Error("The starting grid coordinate does not resolve to one authoritative world cell.");
    const cell = lookup.candidates[0];
    if (cell.tilingId !== world.tiling.id
        || !cell.address.motifCellId
        || !Number.isSafeInteger(cell.address.translation.u)
        || !Number.isSafeInteger(cell.address.translation.v))
        throw new Error("The selected cell does not belong to the current world tiling.");
    return cell.address;
}
