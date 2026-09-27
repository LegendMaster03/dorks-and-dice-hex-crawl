import type { HexCrawlApi } from "../../api";
import { detectHexLattice, type HexLatticeDetection, type HexLatticeFit } from "../../grid-lattice-detector";
import { buildRasterGridAlignmentProposal, type RasterGridAlignmentProposal } from "../../raster-grid-alignment";
import type { MapSurface } from "../../map-surface";
import type { GridDefinition, Overworld, SourceMapDetail } from "../../types";
import { required } from "../../ui/dom";

const AnalysisMaximumDimension = 768;
const AutomaticApplyConfidence = 0.54;
const PhysicalScaleConfirmationTolerance = 0.01;

export class SourceMapGridAlignmentController {
    private selectedMap: SourceMapDetail | null = null;
    private detection: HexLatticeDetection | null = null;
    private proposal: RasterGridAlignmentProposal | null = null;
    private physicalScaleChange: PhysicalScaleChange | null = null;
    private readonly panel: HTMLElement;
    private readonly status: HTMLElement;
    private readonly applyButton: HTMLButtonElement;
    private readonly previewButton: HTMLButtonElement;
    private readonly cancelButton: HTMLButtonElement;

    public constructor(
        private readonly host: HTMLDetailsElement,
        private readonly api: HexCrawlApi,
        private readonly mapSurface: MapSurface,
        private readonly getWorld: () => Overworld,
        private readonly applyWorld: (world: Overworld) => void,
        private readonly runMutation: (action: () => Promise<void>) => void,
        private readonly onSaved: () => Promise<void>) {
        this.panel = required(this.host, "[data-grid-alignment-panel]");
        this.status = required(this.host, "[data-grid-alignment-status]");
        this.applyButton = required(this.host, "[data-grid-alignment-apply]");
        this.previewButton = required(this.host, "[data-grid-alignment-preview]");
        this.cancelButton = required(this.host, "[data-grid-alignment-cancel]");

        this.previewButton.addEventListener("click", () =>
            this.runMutation(() => this.detectAndPreview()));
        this.applyButton.addEventListener("click", () =>
            this.runMutation(() => this.apply()));
        this.cancelButton.addEventListener("click", () => this.cancel());
    }

    public begin(sourceMap: SourceMapDetail | null): void {
        if (!sourceMap) throw new Error("Select a raster map first.");
        if (sourceMap.pixelWidth <= 0 || sourceMap.pixelHeight <= 0) {
            throw new Error("This source map has no usable raster dimensions.");
        }

        this.selectedMap = sourceMap;
        this.detection = null;
        this.proposal = null;
        this.physicalScaleChange = null;
        this.panel.hidden = false;
        this.applyButton.disabled = true;
        this.status.textContent = sourceMap.containsBakedGrid
            ? "Ready to detect the baked hex lattice. Detection does not change the saved world."
            : "This map is marked gridless. Detection can still check the raster, but Hex Crawl will not invent a grid if evidence is absent.";
        this.clearPreview();
    }

    public async beginAndPreview(sourceMap: SourceMapDetail | null): Promise<void> {
        this.begin(sourceMap);
        try {
            await this.detectAndPreview();
        } catch (value) {
            this.detection = null;
            this.proposal = null;
            this.physicalScaleChange = null;
            this.applyButton.disabled = true;
            this.clearPreview();
            const detail = value instanceof Error ? value.message : "Unknown raster-analysis error.";
            this.status.textContent =
                `Automatic grid preview could not complete: ${detail} Use Detect and preview to retry, or use Advanced registration as a fallback.`;
        }
    }

    public cancelIfMap(sourceMapId: string): void {
        if (this.selectedMap?.id === sourceMapId) this.cancel();
    }

    public dispose(): void {
        this.cancel();
    }

    private async detectAndPreview(): Promise<void> {
        const sourceMap = this.selectedMap;
        if (!sourceMap) throw new Error("Select a raster map first.");
        this.applyButton.disabled = true;
        this.physicalScaleChange = null;
        this.status.textContent = "Analyzing repeated hex-grid evidence across the raster…";
        this.clearPreview();

        const world = this.getWorld();
        const assetUrl = this.api.sourceMapAssetUrl(world.id, sourceMap.id);
        const analyzed = await analyzeRaster(
            assetUrl,
            sourceMap.pixelWidth,
            sourceMap.pixelHeight);
        const detection = detectHexLattice(analyzed.raster, {
            minimumSpacingPixels: Math.max(8, Math.floor(12 * analyzed.scale)),
            minimumConfidence: AutomaticApplyConfidence
        });
        this.detection = rescaleDetection(detection, analyzed.scale);

        if (!this.detection.fit) {
            this.proposal = null;
            this.status.textContent = `${statusLabel(this.detection.status)}: ${this.detection.reason}`;
            return;
        }

        const fit = this.detection.fit;
        let proposal = buildRasterGridAlignmentProposal(
            fit,
            world.grid,
            sourceMap.alignment,
            {
                pixelWidth: sourceMap.pixelWidth,
                pixelHeight: sourceMap.pixelHeight
            });
        const scaleContext = await loadPhysicalScaleContext(assetUrl, sourceMap, fit, world.grid);
        if (scaleContext.distancePerHex != null) {
            proposal = {
                ...proposal,
                grid: {
                    ...proposal.grid,
                    neighborCenterDistance: {
                        ...proposal.grid.neighborCenterDistance,
                        value: scaleContext.distancePerHex
                    }
                },
                warnings: proposal.warnings.filter(
                    warning => !warning.startsWith("Physical distance per hex was not detected"))
            };
        }
        this.proposal = proposal;
        this.physicalScaleChange = detectPhysicalScaleChange(world.grid, proposal.grid);
        this.mapSurface.renderer.registrationPreview = {
            sourceMapId: sourceMap.id,
            transform: proposal.alignment
        };
        this.mapSurface.renderer.gridPreview = proposal.grid;
        this.mapSurface.renderer.hiddenSourceMapIds.delete(sourceMap.id);
        this.mapSurface.requestRender();

        const summary = [
            `${statusLabel(this.detection.status)}: ${fit.orientation === "PointyTop" ? "pointy-top" : "flat-top"} lattice`,
            `${fit.centerSpacingPixels.toFixed(2)} px center spacing`,
            `${fit.rotationDegrees.toFixed(2)}° raster rotation`,
            `confidence ${(fit.confidence * 100).toFixed(1)}%`,
            `residual ${fit.residualPixels.toFixed(2)} px`,
            `coverage ${(fit.supportCoverage * 100).toFixed(1)}%`
        ];
        if (scaleContext.summary) summary.push(scaleContext.summary);
        const warnings = [
            ...proposal.warnings,
            ...scaleContext.warnings,
            ...impactWarnings(world, sourceMap.id, proposal.grid)
        ];
        if (this.physicalScaleChange) {
            warnings.push(
                `Physical distance changes from ${formatPhysicalScale(this.physicalScaleChange.currentValue, this.physicalScaleChange.unitSymbol)} to ${formatPhysicalScale(this.physicalScaleChange.proposedValue, this.physicalScaleChange.unitSymbol)}. Apply requires explicit confirmation.`);
        }
        this.status.textContent = `${summary.join(" · ")}.${warnings.length > 0 ? ` ${warnings.join(" ")}` : ""}`;
        this.applyButton.disabled = this.detection.status !== "detected"
            || fit.confidence < AutomaticApplyConfidence;
    }

    private async apply(): Promise<void> {
        const sourceMap = this.selectedMap;
        const detection = this.detection;
        const proposal = this.proposal;
        if (!sourceMap || !detection?.fit || !proposal || detection.status !== "detected") {
            throw new Error("A high-confidence grid detection must be previewed before it can be applied.");
        }
        if (detection.fit.confidence < AutomaticApplyConfidence) {
            throw new Error("The detected lattice is below the automatic-apply confidence threshold.");
        }

        if (this.physicalScaleChange) {
            const scale = this.physicalScaleChange;
            const confirmed = window.confirm(
                `The verified map scale changes physical distance per hex from ${formatPhysicalScale(scale.currentValue, scale.unitSymbol)} to ${formatPhysicalScale(scale.proposedValue, scale.unitSymbol)}. Apply this physical-scale change together with the raster/grid alignment?`);
            if (!confirmed) {
                this.status.textContent = "Alignment was not applied because the physical-scale change was not confirmed. The preview remains available.";
                return;
            }
        }

        const world = this.getWorld();
        const updated = await applyGridAlignment(
            this.api.sourceMapAssetUrl(world.id, sourceMap.id),
            proposal,
            world.version);
        this.applyWorld(updated);
        this.cancel();
        await this.onSaved();
    }

    private cancel(): void {
        this.selectedMap = null;
        this.detection = null;
        this.proposal = null;
        this.physicalScaleChange = null;
        this.panel.hidden = true;
        this.applyButton.disabled = true;
        this.clearPreview();
    }

    private clearPreview(): void {
        this.mapSurface.renderer.registrationPreview = null;
        this.mapSurface.renderer.gridPreview = null;
        this.mapSurface.requestRender();
    }
}

type AnalyzedRaster = {
    raster: { width: number; height: number; pixels: Uint8Array };
    scale: number;
};

type WonderdraftAlignmentContext = {
    rasterRelationship: string;
    canMapProjectCoordinates: boolean;
    uniformScale: number | null;
    explanation: string;
    summary: {
        gridMetadata: Record<string, string>;
        physicalScale: {
            unitLabel: string;
            unitsPerPixel: number;
        } | null;
    };
};

type PhysicalScaleContext = {
    distancePerHex: number | null;
    summary: string | null;
    warnings: string[];
};

type WonderdraftGridCrossCheck = {
    summary: string | null;
    warning: string | null;
};

type PhysicalScaleChange = {
    currentValue: number;
    proposedValue: number;
    unitSymbol: string;
};

async function analyzeRaster(
    assetUrl: string,
    expectedWidth: number,
    expectedHeight: number): Promise<AnalyzedRaster> {
    const response = await fetch(assetUrl, { headers: { Accept: "image/*" } });
    if (!response.ok) throw new Error(`Raster analysis could not load the source image (${response.status}).`);
    const blob = await response.blob();
    const bitmap = await createImageBitmap(blob);
    try {
        if (bitmap.width !== expectedWidth || bitmap.height !== expectedHeight) {
            throw new Error(
                `Stored raster dimensions ${bitmap.width}×${bitmap.height} do not match source-map metadata ${expectedWidth}×${expectedHeight}.`);
        }
        const scale = Math.min(1, AnalysisMaximumDimension / Math.max(bitmap.width, bitmap.height));
        const width = Math.max(1, Math.round(bitmap.width * scale));
        const height = Math.max(1, Math.round(bitmap.height * scale));
        const canvas = document.createElement("canvas");
        canvas.width = width;
        canvas.height = height;
        const context = canvas.getContext("2d", { willReadFrequently: true });
        if (!context) throw new Error("The browser could not create a raster-analysis canvas.");
        context.drawImage(bitmap, 0, 0, width, height);
        const rgba = context.getImageData(0, 0, width, height).data;
        const pixels = new Uint8Array(width * height);
        for (let source = 0, target = 0; source < rgba.length; source += 4, target++) {
            pixels[target] = Math.round(
                (0.2126 * rgba[source])
                + (0.7152 * rgba[source + 1])
                + (0.0722 * rgba[source + 2]));
        }
        return { raster: { width, height, pixels }, scale };
    } finally {
        bitmap.close();
    }
}

function rescaleDetection(detection: HexLatticeDetection, scale: number): HexLatticeDetection {
    if (!detection.fit || scale === 1) return detection;
    const fit = detection.fit;
    return {
        ...detection,
        fit: {
            ...fit,
            centerSpacingPixels: fit.centerSpacingPixels / scale,
            anchorPixel: {
                x: fit.anchorPixel.x / scale,
                y: fit.anchorPixel.y / scale
            },
            residualPixels: fit.residualPixels / scale
        }
    };
}

async function loadPhysicalScaleContext(
    assetUrl: string,
    sourceMap: SourceMapDetail,
    fit: HexLatticeFit,
    grid: GridDefinition): Promise<PhysicalScaleContext> {
    const isWonderdraft = sourceMap.importProvenance?.sourceType.toLocaleLowerCase() === "wonderdraft"
        && !!sourceMap.sourceArchive;
    if (!isWonderdraft) return { distancePerHex: null, summary: null, warnings: [] };

    const endpoint = assetUrl.replace(/\/asset(?:\?.*)?$/, "/wonderdraft/alignment-context");
    const response = await fetch(endpoint, { headers: { Accept: "application/json" } });
    if (!response.ok) {
        return {
            distancePerHex: null,
            summary: null,
            warnings: [`Retained Wonderdraft scale context could not be read (${response.status}); physical distance is unchanged.`]
        };
    }
    const context = await response.json() as WonderdraftAlignmentContext;
    if (!context.canMapProjectCoordinates || !context.uniformScale) {
        return {
            distancePerHex: null,
            summary: null,
            warnings: [context.explanation]
        };
    }

    const gridCrossCheck = compareWonderdraftGridMetadata(
        context.summary.gridMetadata,
        fit,
        context.uniformScale);
    const warnings = gridCrossCheck.warning ? [gridCrossCheck.warning] : [];
    const summaries = gridCrossCheck.summary ? [gridCrossCheck.summary] : [];

    const physicalScale = context.summary.physicalScale;
    if (!physicalScale) {
        return {
            distancePerHex: null,
            summary: summaries.length > 0 ? summaries.join(" · ") : null,
            warnings
        };
    }
    const sourceMetersPerUnit = metersPerWonderdraftUnit(physicalScale.unitLabel);
    const targetMetersPerUnit = grid.neighborCenterDistance.unit.metersPerUnit;
    if (!sourceMetersPerUnit || !targetMetersPerUnit || targetMetersPerUnit <= 0) {
        return {
            distancePerHex: null,
            summary: summaries.length > 0 ? summaries.join(" · ") : null,
            warnings: [
                ...warnings,
                `Wonderdraft provides scale in ${physicalScale.unitLabel}, but it can not be converted to the world's configured distance unit; physical distance is unchanged.`
            ]
        };
    }

    const unitsPerRasterPixel = physicalScale.unitsPerPixel / context.uniformScale;
    const distanceMeters = fit.centerSpacingPixels * unitsPerRasterPixel * sourceMetersPerUnit;
    const distancePerHex = distanceMeters / targetMetersPerUnit;
    if (!Number.isFinite(distancePerHex) || distancePerHex <= 0) {
        return {
            distancePerHex: null,
            summary: summaries.length > 0 ? summaries.join(" · ") : null,
            warnings: [...warnings, "Wonderdraft physical scale produced an invalid distance and was ignored."]
        };
    }
    summaries.push(
        `physical scale ${distancePerHex.toFixed(3)} ${grid.neighborCenterDistance.unit.symbol}/hex from retained Wonderdraft scale-bar metadata (${context.rasterRelationship})`);
    return {
        distancePerHex,
        summary: summaries.join(" · "),
        warnings
    };
}

function compareWonderdraftGridMetadata(
    metadata: Record<string, string>,
    fit: HexLatticeFit,
    projectToRasterScale: number): WonderdraftGridCrossCheck {
    const rawSize = metadata["grid.size"];
    if (!rawSize) return { summary: null, warning: null };
    const projectGridSize = Number(rawSize);
    if (!Number.isFinite(projectGridSize) || projectGridSize <= 0) {
        return {
            summary: null,
            warning: `Wonderdraft grid.size metadata '${rawSize}' is not a usable positive number and was ignored.`
        };
    }

    const scaledMetadataSize = projectGridSize * projectToRasterScale;
    const difference = Math.abs(fit.centerSpacingPixels - scaledMetadataSize);
    const relativeDifference = difference / Math.max(fit.centerSpacingPixels, scaledMetadataSize);
    const comparison = `Wonderdraft grid.size ${projectGridSize.toFixed(3)} project px → ${scaledMetadataSize.toFixed(2)} raster px; detected spacing differs ${(relativeDifference * 100).toFixed(2)}%`;

    // Treat grid.size as an opaque candidate size and only report agreement or disagreement.
    // Humblewood supplies direct fixture evidence that grid.size=80 agrees with its baked
    // 80-pixel center spacing. Raster detection remains authoritative so other Wonderdraft
    // versions and grid patterns do not inherit an unverified semantic assumption.
    if (relativeDifference <= 0.03) {
        return { summary: `${comparison} (agreement)`, warning: null };
    }
    return {
        summary: null,
        warning: `${comparison}. The metadata is retained only as a cross-check; raster geometry is not overridden.`
    };
}

async function applyGridAlignment(
    assetUrl: string,
    proposal: RasterGridAlignmentProposal,
    expectedVersion: number): Promise<Overworld> {
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
        })
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

function impactWarnings(world: Overworld, sourceMapId: string, proposedGrid: GridDefinition): string[] {
    if (sameGridGeometry(world.grid, proposedGrid)) return [];
    const otherPlacedMaps = world.sourceMaps.filter(map => map.id !== sourceMapId && map.alignment).length;
    const retained = [
        `${world.locations.length} location${world.locations.length === 1 ? "" : "s"}`,
        `${world.features.length} feature${world.features.length === 1 ? "" : "s"}`,
        `${otherPlacedMaps} other placed reference map${otherPlacedMaps === 1 ? "" : "s"}`
    ];
    return [
        `Grid geometry will change; ${retained.join(", ")} keep their existing world coordinates. Review the preview before applying.`
    ];
}

function detectPhysicalScaleChange(current: GridDefinition, proposed: GridDefinition): PhysicalScaleChange | null {
    const currentDistance = current.neighborCenterDistance;
    const proposedDistance = proposed.neighborCenterDistance;
    const sameUnit = currentDistance.unit.kind === proposedDistance.unit.kind
        && currentDistance.unit.symbol === proposedDistance.unit.symbol
        && currentDistance.unit.metersPerUnit === proposedDistance.unit.metersPerUnit;
    if (!sameUnit) {
        return {
            currentValue: currentDistance.value,
            proposedValue: proposedDistance.value,
            unitSymbol: proposedDistance.unit.symbol
        };
    }

    const denominator = Math.max(Math.abs(currentDistance.value), Math.abs(proposedDistance.value), 1e-9);
    const relativeDifference = Math.abs(proposedDistance.value - currentDistance.value) / denominator;
    return relativeDifference > PhysicalScaleConfirmationTolerance
        ? {
            currentValue: currentDistance.value,
            proposedValue: proposedDistance.value,
            unitSymbol: proposedDistance.unit.symbol
        }
        : null;
}

function formatPhysicalScale(value: number, unitSymbol: string): string {
    return `${value.toFixed(3)} ${unitSymbol}/hex`;
}

function sameGridGeometry(first: GridDefinition, second: GridDefinition): boolean {
    return first.orientation === second.orientation
        && Math.abs(first.origin.x - second.origin.x) < 1e-9
        && Math.abs(first.origin.y - second.origin.y) < 1e-9
        && Math.abs(first.rotationDegrees - second.rotationDegrees) < 1e-9
        && Math.abs(first.hexRadiusWorldUnits - second.hexRadiusWorldUnits) < 1e-9
        && Math.abs(first.neighborCenterDistance.value - second.neighborCenterDistance.value) < 1e-9;
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

function statusLabel(status: HexLatticeDetection["status"]): string {
    switch (status) {
        case "detected": return "Detected";
        case "inconclusive": return "Inconclusive";
        case "gridless": return "No reliable grid";
    }
}
