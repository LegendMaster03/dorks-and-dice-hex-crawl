import type { HexCrawlApi } from "../../api";
import {
    rasterGridCanonicalResidualLimit,
    type HexLatticeDetection,
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
import { required } from "../../ui/dom";

const RecommendedApplyConfidence = 0.54;
const PhysicalScaleConfirmationTolerance = 0.01;

export class SourceMapGridAlignmentController {
    private selectedMap: SourceMapDetail | null = null;
    private detection: HexLatticeDetection | null = null;
    private proposal: RasterGridAlignmentProposal | null = null;
    private physicalScaleChange: PhysicalScaleChange | null = null;
    private sourceResolutionVerified = false;
    private applyWarnings: string[] = [];
    private analysisAbortController: AbortController | null = null;
    private analysisGeneration = 0;
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

        this.analysisAbortController?.abort();
        this.analysisAbortController = null;
        this.analysisGeneration += 1;
        this.selectedMap = sourceMap;
        this.detection = null;
        this.proposal = null;
        this.physicalScaleChange = null;
        this.sourceResolutionVerified = false;
        this.applyWarnings = [];
        this.panel.hidden = false;
        this.panel.setAttribute("aria-busy", "false");
        this.applyButton.disabled = true;
        this.applyButton.textContent = "Apply alignment";
        this.applyButton.title = "";
        this.setAnalysisBusy(false);
        this.status.textContent = sourceMap.containsBakedGrid
            ? "Ready to detect the baked hex lattice. Detection does not change the saved world."
            : "This map is marked gridless. Detection can still check the raster, but Hex Crawl will not invent a grid if evidence is absent.";
        this.clearPreview();
        this.panel.scrollIntoView({ behavior: "smooth", block: "nearest" });
    }

    public async beginAndPreview(sourceMap: SourceMapDetail | null): Promise<void> {
        this.begin(sourceMap);
        await this.detectAndPreview();
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
        this.applyButton.textContent = "Apply alignment";
        this.applyButton.title = "";
        this.applyWarnings = [];
        this.sourceResolutionVerified = false;
        this.physicalScaleChange = null;
        this.status.textContent = "Analyzing repeated hex-grid evidence across the raster…";
        this.clearPreview();
        this.panel.scrollIntoView({ behavior: "smooth", block: "nearest" });

        const world = this.getWorld();
        this.analysisAbortController?.abort();
        const abortController = new AbortController();
        const generation = ++this.analysisGeneration;
        this.analysisAbortController = abortController;
        this.setAnalysisBusy(true);

        try {
            let analyzed: SourceMapGridAnalysis;
            try {
                analyzed = await this.api.analyzeSourceMapGrid(world.id, sourceMap.id, abortController.signal);
            } catch (error) {
                if (abortController.signal.aborted || generation !== this.analysisGeneration) return;
                this.detection = null;
                this.proposal = null;
                const detail = error instanceof Error ? error.message : "Surveyor did not return an analysis result.";
                this.status.textContent = `Automatic analysis unavailable: ${detail} Advanced registration remains available.`;
                return;
            } finally {
                if (this.analysisAbortController === abortController) this.analysisAbortController = null;
            }

            if (abortController.signal.aborted
                || generation !== this.analysisGeneration
                || this.selectedMap?.id !== sourceMap.id) return;

            this.sourceResolutionVerified = analyzed.analysis.sourceResolutionVerified;
            this.detection = { status: analyzed.status, fit: analyzed.fit, reason: analyzed.reason };
            const assetUrl = this.api.sourceMapAssetUrl(world.id, sourceMap.id);
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
            if (abortController.signal.aborted
                || generation !== this.analysisGeneration
                || this.selectedMap?.id !== sourceMap.id) return;

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
                `worst distant residual ${fit.residualPixels.toFixed(2)} px`,
                `coverage ${(fit.supportCoverage * 100).toFixed(1)}%`,
                this.sourceResolutionVerified ? "source-resolution phase verified" : "downscaled phase only"
            ];
            if (scaleContext.summary) summary.splice(1, 0, scaleContext.summary);

            const canonicalResidualLimit = rasterGridCanonicalResidualLimit(fit.centerSpacingPixels);
            const manualReviewWarnings: string[] = [];
            if (!this.sourceResolutionVerified) {
                manualReviewWarnings.push("Source-resolution phase verification is unavailable.");
            }
            if (this.detection.status !== "detected") {
                manualReviewWarnings.push(this.detection.reason);
            }
            if (fit.confidence < RecommendedApplyConfidence) {
                manualReviewWarnings.push(
                    `Confidence ${(fit.confidence * 100).toFixed(1)}% is below the ${(RecommendedApplyConfidence * 100).toFixed(0)}% recommended threshold.`);
            }
            if (fit.residualPixels > canonicalResidualLimit) {
                manualReviewWarnings.push(
                    `The final rigid overlay misses at least one distant region by ${fit.residualPixels.toFixed(2)} px; the recommended limit is ${canonicalResidualLimit.toFixed(2)} px for this lattice spacing.`);
            }
            this.applyWarnings = [...new Set(manualReviewWarnings)];

            const warnings = [
                ...proposal.warnings,
                ...scaleContext.warnings,
                ...this.applyWarnings,
                ...impactWarnings(world, sourceMap.id, proposal.grid)
            ];
            if (this.physicalScaleChange) {
                warnings.push(
                    `Physical distance changes from ${formatPhysicalScale(this.physicalScaleChange.currentValue, this.physicalScaleChange.unitSymbol)} to ${formatPhysicalScale(this.physicalScaleChange.proposedValue, this.physicalScaleChange.unitSymbol)}. Apply requires explicit confirmation.`);
            }
            if (this.applyWarnings.length > 0) {
                warnings.push("The preview can still be explicitly applied after confirming these warnings.");
            }
            this.status.textContent = `${summary.join(" · ")}.${warnings.length > 0 ? ` ${warnings.join(" ")}` : ""}`;
            this.applyButton.disabled = false;
            this.applyButton.textContent = this.applyWarnings.length > 0 ? "Apply anyway" : "Apply alignment";
            this.applyButton.title = this.applyWarnings.length > 0
                ? "Apply this preview after reviewing and confirming the detection warnings."
                : "Save this detected raster/grid alignment.";
            this.panel.scrollIntoView({ behavior: "smooth", block: "nearest" });
        } finally {
            if (generation === this.analysisGeneration && this.selectedMap?.id === sourceMap.id) {
                this.setAnalysisBusy(false);
            }
        }
    }

    private async apply(): Promise<void> {
        const sourceMap = this.selectedMap;
        const detection = this.detection;
        const proposal = this.proposal;
        if (!sourceMap || !detection?.fit || !proposal) {
            throw new Error("Preview a detected grid alignment before applying it.");
        }

        const confirmationWarnings = [...this.applyWarnings];
        if (this.physicalScaleChange) {
            const scale = this.physicalScaleChange;
            confirmationWarnings.push(
                `Physical distance per hex changes from ${formatPhysicalScale(scale.currentValue, scale.unitSymbol)} to ${formatPhysicalScale(scale.proposedValue, scale.unitSymbol)}.`);
        }
        if (confirmationWarnings.length > 0) {
            const confirmed = window.confirm(
                `This alignment has warnings:\n\n- ${confirmationWarnings.join("\n- ")}\n\nApply this preview anyway?`);
            if (!confirmed) {
                this.status.textContent = "Alignment was not applied. The preview remains available for review or re-detection.";
                return;
            }
        }

        const world = this.getWorld();
        this.applyButton.disabled = true;
        this.applyButton.textContent = "Applying…";
        this.status.textContent = "Applying the reviewed raster/grid alignment…";
        try {
            const updated = await applyGridAlignment(
                this.api.sourceMapAssetUrl(world.id, sourceMap.id),
                proposal,
                world.version);
            this.applyWorld(updated);
            this.cancel();
            await this.onSaved();
        } catch (error) {
            if (this.selectedMap?.id === sourceMap.id) {
                this.applyButton.disabled = false;
                this.applyButton.textContent = this.applyWarnings.length > 0 ? "Apply anyway" : "Apply alignment";
            }
            throw error;
        }
    }

    private cancel(): void {
        this.analysisAbortController?.abort();
        this.analysisAbortController = null;
        this.analysisGeneration += 1;
        this.selectedMap = null;
        this.detection = null;
        this.proposal = null;
        this.physicalScaleChange = null;
        this.sourceResolutionVerified = false;
        this.applyWarnings = [];
        this.panel.hidden = true;
        this.panel.setAttribute("aria-busy", "false");
        this.applyButton.disabled = true;
        this.applyButton.textContent = "Apply alignment";
        this.applyButton.title = "";
        this.setAnalysisBusy(false);
        this.clearPreview();
    }

    private setAnalysisBusy(busy: boolean): void {
        this.panel.setAttribute("aria-busy", String(busy));
        this.previewButton.disabled = busy;
        this.previewButton.textContent = busy ? "Analyzing…" : "Re-run detection";
    }

    private clearPreview(): void {
        this.mapSurface.renderer.registrationPreview = null;
        this.mapSurface.renderer.gridPreview = null;
        this.mapSurface.requestRender();
    }
}

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
    trustedCenterSpacingPixels: number | null;
};

type PhysicalScaleChange = {
    currentValue: number;
    proposedValue: number;
    unitSymbol: string;
};

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
    const directDistancePerHex = distanceMeters / targetMetersPerUnit;
    if (!Number.isFinite(directDistancePerHex) || directDistancePerHex <= 0) {
        return {
            distancePerHex: null,
            summary: summaries.length > 0 ? summaries.join(" · ") : null,
            warnings: [...warnings, "Wonderdraft physical scale produced an invalid distance and was ignored."]
        };
    }

    const crossCheckDistancePerHex = gridCrossCheck.trustedCenterSpacingPixels == null
        ? null
        : (gridCrossCheck.trustedCenterSpacingPixels
            * unitsPerRasterPixel
            * sourceMetersPerUnit) / targetMetersPerUnit;
    const selection = selectPhysicalDistancePerHex({
        directDistancePerHex,
        crossCheckDistancePerHex,
        considerWholeUnits: grid.neighborCenterDistance.unit.kind === "Mile"
    });
    const distancePerHex = selection.distancePerHex;
    if (selection.usedWholeUnitCandidate && selection.crossCheckDistancePerHex != null) {
        summaries.push(
            `physical scale ${distancePerHex.toFixed(3)} ${grid.neighborCenterDistance.unit.symbol}/hex; whole-mile candidate selected because it better reconciles the direct scale-bar/raster estimate ${selection.directDistancePerHex.toFixed(3)} ${grid.neighborCenterDistance.unit.symbol}/hex with the Wonderdraft grid.size estimate ${selection.crossCheckDistancePerHex.toFixed(3)} ${grid.neighborCenterDistance.unit.symbol}/hex (${context.rasterRelationship})`);
    } else {
        summaries.push(
            `physical scale ${distancePerHex.toFixed(3)} ${grid.neighborCenterDistance.unit.symbol}/hex from retained Wonderdraft scale-bar metadata (${context.rasterRelationship})`);
    }
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
    const rawSize = metadataValue(metadata, "grid.size");
    if (!rawSize) {
        return {
            summary: null,
            warning: "Wonderdraft alignment metadata did not expose grid.size, so no project-grid size cross-check is available.",
            trustedCenterSpacingPixels: null
        };
    }
    const projectGridSize = Number(rawSize);
    if (!Number.isFinite(projectGridSize) || projectGridSize <= 0) {
        return {
            summary: null,
            warning: `Wonderdraft grid.size metadata '${rawSize}' is not a usable positive number and was ignored.`,
            trustedCenterSpacingPixels: null
        };
    }

    const scaledMetadataSize = projectGridSize * projectToRasterScale;
    const difference = Math.abs(fit.centerSpacingPixels - scaledMetadataSize);
    const relativeDifference = difference / Math.max(fit.centerSpacingPixels, scaledMetadataSize);
    const rawType = metadataValue(metadata, "grid.type");
    const normalizedType = rawType?.trim().toLocaleLowerCase() ?? null;
    const typeDetail = rawType ? `; grid.type=${rawType}` : "; grid.type not exposed";
    const comparison = `Wonderdraft grid.size=${projectGridSize.toFixed(3)} project px → ${scaledMetadataSize.toFixed(2)} raster px; raster detector ${fit.centerSpacingPixels.toFixed(2)} px; difference ${(relativeDifference * 100).toFixed(2)}%${typeDetail}`;

    // grid.size is only an independent cross-check. The baked raster remains the
    // authoritative geometry even when the retained project metadata agrees exactly.
    // An explicit non-hex grid type invalidates the cross-check; missing grid.type is
    // reported but does not hide an otherwise matching Wonderdraft grid.size value.
    if (normalizedType != null && normalizedType !== "hex") {
        return {
            summary: null,
            warning: `${comparison}. Wonderdraft grid.type is not hex, so grid.size is not used as a hex-spacing cross-check.`,
            trustedCenterSpacingPixels: null
        };
    }
    if (relativeDifference <= 0.03) {
        return {
            summary: `${comparison} (agreement)`,
            warning: null,
            trustedCenterSpacingPixels: scaledMetadataSize
        };
    }
    return {
        summary: null,
        warning: `${comparison}. The metadata is retained only as a cross-check; raster geometry is not overridden.`,
        trustedCenterSpacingPixels: null
    };
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
