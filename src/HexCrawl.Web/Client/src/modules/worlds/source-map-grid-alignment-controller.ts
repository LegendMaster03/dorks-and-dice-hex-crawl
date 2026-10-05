import type { HexCrawlApi } from "../../api";
import {
    isCanonicalSourceFit,
    type HexLatticeFit,
    type SourceMapGridAnalysis
} from "../../hex-grid-analysis";
import {
    buildRasterGridAlignmentProposal,
    selectPhysicalDistancePerHex,
    type RasterGridAlignmentProposal
} from "../../raster-grid-alignment";
import type { MapSurface } from "../../map-surface";
import type { GridDefinition, Overworld, SourceMapDetail } from "../../types";

export type GridAlignmentProgress = "detecting" | "applying";

export class SourceMapGridAlignmentController {
    private selectedMapId: string | null = null;
    private analysisAbortController: AbortController | null = null;
    private analysisGeneration = 0;

    public constructor(
        private readonly api: HexCrawlApi,
        private readonly mapSurface: MapSurface,
        private readonly getWorld: () => Overworld,
        private readonly applyWorld: (world: Overworld) => void,
        private readonly onSaved: () => Promise<void>) {}

    public async detectAndApply(
        sourceMap: SourceMapDetail | null,
        onProgress: (progress: GridAlignmentProgress) => void = () => {}): Promise<boolean> {
        if (!sourceMap) throw new Error("Select a reference map first.");
        if (sourceMap.pixelWidth <= 0 || sourceMap.pixelHeight <= 0) {
            throw new Error("This map image has no usable image dimensions.");
        }

        this.analysisAbortController?.abort();
        this.clearPreview();
        const abortController = new AbortController();
        const generation = ++this.analysisGeneration;
        this.analysisAbortController = abortController;
        this.selectedMapId = sourceMap.id;
        const world = this.getWorld();

        try {
            onProgress("detecting");
            let analyzed: SourceMapGridAnalysis;
            try {
                analyzed = await this.api.analyzeSourceMapGrid(
                    world.id,
                    sourceMap.id,
                    abortController.signal);
            } catch (error) {
                if (!this.isCurrent(generation, sourceMap.id, abortController)) return false;
                const detail = error instanceof Error ? ` ${error.message}` : "";
                throw new Error(
                    `Automatic grid detection is unavailable.${detail} The map remains visible; try again or use Manual placement.`);
            }

            if (!this.isCurrent(generation, sourceMap.id, abortController)) return false;
            if (!analyzed.fit
                || !isCanonicalSourceFit(analyzed, analyzed.analysis.sourceResolutionVerified)) {
                throw new Error(
                    "Hex Crawl could not verify this grid strongly enough to change the world automatically. "
                    + "The map remains visible; try detection again or use Manual placement.");
            }

            const fit = analyzed.fit;
            let proposal = buildRasterGridAlignmentProposal(
                fit,
                world.grid,
                sourceMap.alignment,
                {
                    pixelWidth: sourceMap.pixelWidth,
                    pixelHeight: sourceMap.pixelHeight
                });
            const assetUrl = this.api.sourceMapAssetUrl(world.id, sourceMap.id);
            const detectedDistancePerHex = await loadPhysicalDistancePerHex(
                assetUrl,
                sourceMap,
                fit,
                world.grid,
                abortController.signal);
            if (!this.isCurrent(generation, sourceMap.id, abortController)) return false;

            if (detectedDistancePerHex != null) {
                proposal = {
                    ...proposal,
                    grid: {
                        ...proposal.grid,
                        neighborCenterDistance: {
                            ...proposal.grid.neighborCenterDistance,
                            value: detectedDistancePerHex
                        }
                    },
                    warnings: proposal.warnings.filter(
                        warning => !warning.startsWith("Physical distance per hex was not detected"))
                };
            }

            this.mapSurface.renderer.registrationPreview = {
                sourceMapId: sourceMap.id,
                transform: proposal.alignment
            };
            this.mapSurface.renderer.gridPreview = proposal.grid;
            this.mapSurface.renderer.hiddenSourceMapIds.delete(sourceMap.id);
            this.mapSurface.requestRender();

            const currentWorld = this.getWorld();
            if (currentWorld.id !== world.id || currentWorld.version !== world.version) {
                throw new Error("The world changed while the grid was being detected. Run grid detection again.");
            }

            onProgress("applying");
            const updated = await applyGridAlignment(
                assetUrl,
                proposal,
                currentWorld.version,
                abortController.signal);
            if (!this.isCurrent(generation, sourceMap.id, abortController)) return false;

            this.applyWorld(updated);
            this.clearPreview();
            await this.onSaved();
            if (!this.isCurrent(generation, sourceMap.id, abortController)) return false;
            this.selectedMapId = null;
            return true;
        } catch (error) {
            if (!this.isCurrent(generation, sourceMap.id, abortController)) return false;
            this.clearPreview();
            throw error;
        } finally {
            if (this.analysisAbortController === abortController) {
                this.analysisAbortController = null;
            }
        }
    }

    public cancelIfMap(sourceMapId: string): void {
        if (this.selectedMapId === sourceMapId) this.cancel();
    }

    public cancelActive(): void {
        if (this.selectedMapId !== null) this.cancel();
    }

    public dispose(): void {
        this.cancel();
    }

    private isCurrent(
        generation: number,
        sourceMapId: string,
        abortController: AbortController): boolean {
        return !abortController.signal.aborted
            && generation === this.analysisGeneration
            && this.selectedMapId === sourceMapId;
    }

    private cancel(): void {
        this.analysisAbortController?.abort();
        this.analysisAbortController = null;
        this.analysisGeneration += 1;
        this.selectedMapId = null;
        this.clearPreview();
    }

    private clearPreview(): void {
        this.mapSurface.renderer.registrationPreview = null;
        this.mapSurface.renderer.gridPreview = null;
        this.mapSurface.requestRender();
    }
}

type WonderdraftAlignmentContext = {
    canMapProjectCoordinates: boolean;
    uniformScale: number | null;
    summary: {
        gridMetadata: Record<string, string>;
        physicalScale: {
            unitLabel: string;
            unitsPerPixel: number;
        } | null;
    };
};

async function loadPhysicalDistancePerHex(
    assetUrl: string,
    sourceMap: SourceMapDetail,
    fit: HexLatticeFit,
    grid: GridDefinition,
    signal: AbortSignal): Promise<number | null> {
    const isWonderdraft = sourceMap.importProvenance?.sourceType.toLocaleLowerCase() === "wonderdraft"
        && !!sourceMap.sourceArchive;
    if (!isWonderdraft) return null;

    try {
        const endpoint = assetUrl.replace(/\/asset(?:\?.*)?$/, "/wonderdraft/alignment-context");
        const response = await fetch(endpoint, {
            headers: { Accept: "application/json" },
            signal
        });
        if (!response.ok) return null;

        const context = await response.json() as WonderdraftAlignmentContext;
        if (!context.canMapProjectCoordinates || !context.uniformScale) return null;

        const physicalScale = context.summary.physicalScale;
        if (!physicalScale) return null;
        const sourceMetersPerUnit = metersPerWonderdraftUnit(physicalScale.unitLabel);
        const targetMetersPerUnit = grid.neighborCenterDistance.unit.metersPerUnit;
        if (!sourceMetersPerUnit || !targetMetersPerUnit || targetMetersPerUnit <= 0) return null;

        const unitsPerRasterPixel = physicalScale.unitsPerPixel / context.uniformScale;
        const directDistancePerHex = (
            fit.centerSpacingPixels
            * unitsPerRasterPixel
            * sourceMetersPerUnit) / targetMetersPerUnit;
        if (!Number.isFinite(directDistancePerHex) || directDistancePerHex <= 0) return null;

        const trustedCenterSpacingPixels = trustedWonderdraftCenterSpacing(
            context.summary.gridMetadata,
            fit,
            context.uniformScale);
        const crossCheckDistancePerHex = trustedCenterSpacingPixels == null
            ? null
            : (trustedCenterSpacingPixels
                * unitsPerRasterPixel
                * sourceMetersPerUnit) / targetMetersPerUnit;
        return selectPhysicalDistancePerHex({
            directDistancePerHex,
            crossCheckDistancePerHex,
            considerWholeUnits: grid.neighborCenterDistance.unit.kind === "Mile"
        }).distancePerHex;
    } catch (error) {
        if (signal.aborted) throw error;
        return null;
    }
}

function trustedWonderdraftCenterSpacing(
    metadata: Record<string, string>,
    fit: HexLatticeFit,
    projectToRasterScale: number): number | null {
    const rawSize = metadataValue(metadata, "grid.size");
    if (!rawSize) return null;
    const projectGridSize = Number(rawSize);
    if (!Number.isFinite(projectGridSize) || projectGridSize <= 0) return null;

    const rawType = metadataValue(metadata, "grid.type");
    const normalizedType = rawType?.trim().toLocaleLowerCase() ?? null;
    if (normalizedType != null && normalizedType !== "hex") return null;

    const scaledMetadataSize = projectGridSize * projectToRasterScale;
    const difference = Math.abs(fit.centerSpacingPixels - scaledMetadataSize);
    const relativeDifference = difference / Math.max(fit.centerSpacingPixels, scaledMetadataSize);
    return relativeDifference <= 0.03 ? scaledMetadataSize : null;
}

function metadataValue(metadata: Record<string, string>, key: string): string | null {
    const normalized = key.trim().toLocaleLowerCase();
    for (const [candidate, value] of Object.entries(metadata)) {
        if (candidate.trim().toLocaleLowerCase() === normalized) return value;
    }
    return null;
}

async function applyGridAlignment(
    assetUrl: string,
    proposal: RasterGridAlignmentProposal,
    expectedVersion: number,
    signal: AbortSignal): Promise<Overworld> {
    const endpoint = assetUrl.replace(/\/asset(?:\?.*)?$/, "/grid-alignment");
    const response = await fetch(endpoint, {
        method: "PUT",
        headers: {
            Accept: "application/json",
            "Content-Type": "application/json"
        },
        body: JSON.stringify({
            grid: proposal.grid,
            alignment: proposal.alignment,
            expectedVersion
        }),
        signal
    });
    if (!response.ok) {
        let detail = `Grid alignment failed (${response.status}).`;
        try {
            const problem = await response.json() as { error?: string; detail?: string; title?: string };
            detail = problem.error ?? problem.detail ?? problem.title ?? detail;
        } catch {
            // Preserve the status-based message when the response is not JSON.
        }
        throw new Error(detail);
    }
    return await response.json() as Overworld;
}

function metersPerWonderdraftUnit(label: string): number | null {
    switch (label.trim().toLocaleLowerCase()) {
        case "mile":
        case "miles":
        case "mi":
            return 1609.344;
        case "kilometer":
        case "kilometers":
        case "kilometre":
        case "kilometres":
        case "km":
            return 1000;
        default:
            return null;
    }
}
