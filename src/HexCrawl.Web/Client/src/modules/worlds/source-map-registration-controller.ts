import type { HexCrawlApi } from "../../api";
import {
    affineRegistrationSaveAllowed,
    evaluateAffineRegistrationHoldout,
    maxAffineHoldoutErrorSourcePixels,
    solveAffine
} from "../../affine-registration";
import type { MapSurface } from "../../map-surface";
import type {
    Overworld,
    RegistrationControlPoint,
    SourceMapDetail,
    WorldPoint
} from "../../types";
import { showUiError } from "../../ui-error";
import { required } from "../../ui/dom";

type RegistrationState = {
    map: SourceMapDetail;
    pairs: RegistrationControlPoint[];
    pendingSource: WorldPoint | null;
    holdoutErrorSourcePixels: number | null;
};

export class SourceMapRegistrationController {
    private registration: RegistrationState | null = null;
    private readonly panel: HTMLElement;
    private readonly image: HTMLImageElement;
    private readonly status: HTMLElement;

    public constructor(
        private readonly host: HTMLDetailsElement,
        private readonly api: HexCrawlApi,
        private readonly map: MapSurface,
        private readonly getWorld: () => Overworld,
        private readonly applyWorld: (world: Overworld) => void,
        private readonly mapHint: HTMLElement,
        private readonly errorHost: HTMLElement,
        private readonly runMutation: (action: () => Promise<void>) => void,
        private readonly onSaved: (sourceMapId: string) => Promise<void>) {
        this.panel = required(this.host, "[data-registration-panel]");
        this.image = required(this.host, "[data-registration-image]");
        this.status = required(this.host, "[data-registration-status]");

        required<HTMLButtonElement>(this.host, "[data-clear-registration]")
            .addEventListener("click", () => this.clear());
        required<HTMLButtonElement>(this.host, "[data-save-registration]")
            .addEventListener("click", () => this.runMutation(() => this.save()));
        required<HTMLButtonElement>(this.host, "[data-cancel-registration]")
            .addEventListener("click", () => this.cancel());
        this.image.addEventListener("click", event => this.captureSourcePoint(event));
        this.map.setClickInterceptor(point => this.consumeWorldClick(point));
    }

    public begin(sourceMap: SourceMapDetail | null): void {
        if (!sourceMap) throw new Error("Select a reference map first.");
        if (sourceMap.pixelWidth <= 0 || sourceMap.pixelHeight <= 0) {
            throw new Error("This map image has no usable image dimensions and must be re-imported before manual placement.");
        }

        this.registration = {
            map: sourceMap, pairs: [], pendingSource: null, holdoutErrorSourcePixels: null
        };
        this.panel.hidden = false;
        this.image.src = this.api.sourceMapAssetUrl(this.getWorld().id, sourceMap.id);
        this.map.renderer.hiddenSourceMapIds.delete(sourceMap.id);
        this.map.renderer.registrationPreview = null;
        this.mapHint.textContent =
            "Manual placement: click a landmark in the reference image first, then click the same landmark on the world map.";
        this.renderStatus();
        this.map.requestRender();
    }

    public cancelIfMap(sourceMapId: string): void {
        if (this.registration?.map.id === sourceMapId) this.cancel();
    }

    public cancelActive(): void {
        if (this.registration) this.cancel();
    }

    public dispose(): void {
        this.cancel();
        this.map.setClickInterceptor(null);
        this.image.removeAttribute("src");
    }

    private captureSourcePoint(event: MouseEvent): void {
        if (!this.registration || this.registration.pairs.length >= 4) return;
        if (this.registration.pairs.length === 3 && !this.map.renderer.registrationPreview) return;
        const rect = this.image.getBoundingClientRect();
        if (rect.width <= 0 || rect.height <= 0 || this.image.naturalWidth <= 0 || this.image.naturalHeight <= 0) {
            return;
        }

        const sourcePixel = {
            x: (event.clientX - rect.left) * this.image.naturalWidth / rect.width,
            y: (event.clientY - rect.top) * this.image.naturalHeight / rect.height
        };
        this.registration.pendingSource = sourcePixel;
        this.mapHint.textContent = this.registration.pairs.length === 3
            ? "Independent check: select the matching world-map position for this fourth image landmark."
            : `Manual placement: image point ${this.registration.pairs.length + 1} selected at ${sourcePixel.x.toFixed(1)}, ${sourcePixel.y.toFixed(1)}. Click the corresponding world-map point.`;
        this.renderStatus();
    }

    private consumeWorldClick(point: WorldPoint): boolean {
        if (!this.registration) return false;
        if (!this.registration.pendingSource) {
            this.mapHint.textContent =
                "Manual placement is active. Choose an image point before clicking the world map.";
            return true;
        }

        this.registration.pairs.push({
            sourcePixel: this.registration.pendingSource,
            worldPoint: point
        });
        this.registration.pendingSource = null;

        if (this.registration.pairs.length === 3) {
            try {
                const transform = solveAffine(this.registration.pairs);
                this.map.renderer.registrationPreview = {
                    sourceMapId: this.registration.map.id,
                    transform
                };
                this.mapHint.textContent =
                    "Placement preview is active. Save using three landmarks, or click a fourth image/world landmark pair for an independent accuracy check. The check does not change the preview.";
                this.map.requestRender();
            } catch (value) {
                this.map.renderer.registrationPreview = null;
                showUiError(this.errorHost, value);
            }
        } else if (this.registration.pairs.length === 4) {
            // Keep the exact three-point fit unchanged. The fourth pair is
            // validation-only; never send it to the existing three-point API.
            const preview = this.map.renderer.registrationPreview;
            try {
                if (!preview || preview.sourceMapId !== this.registration.map.id) {
                    throw new Error("No registration preview is available for validation.");
                }
                const result = evaluateAffineRegistrationHoldout(
                    preview.transform, this.registration.pairs[3], this.registration.pairs.slice(0, 3));
                this.registration.holdoutErrorSourcePixels = result.errorSourcePixels;
                this.mapHint.textContent = result.errorSourcePixels <= maxAffineHoldoutErrorSourcePixels
                    ? `Independent fourth landmark is within ${result.errorSourcePixels.toFixed(1)} source pixels. You may save this placement.`
                    : `Independent fourth landmark differs by ${result.errorSourcePixels.toFixed(1)} source pixels (limit ${maxAffineHoldoutErrorSourcePixels}). Clear the points and realign before saving.`;
            } catch (value) {
                this.registration.holdoutErrorSourcePixels = null;
                this.mapHint.textContent = "Independent landmark could not be verified. Clear the points and retry.";
                showUiError(this.errorHost, value);
            }
        } else {
            this.mapHint.textContent =
                `Manual placement: ${this.registration.pairs.length} of 3 landmark pairs captured. Choose the next image point.`;
        }

        this.renderStatus();
        return true;
    }

    private clear(): void {
        if (!this.registration) return;
        this.registration.pairs = [];
        this.registration.pendingSource = null;
        this.registration.holdoutErrorSourcePixels = null;
        this.map.renderer.registrationPreview = null;
        this.mapHint.textContent =
            "Placement points cleared. Choose an image point to begin again.";
        this.renderStatus();
        this.map.requestRender();
    }

    private cancel(): void {
        this.registration = null;
        this.panel.hidden = true;
        this.image.removeAttribute("src");
        this.map.renderer.registrationPreview = null;
        this.mapHint.textContent =
            "Use the authoring controls to place geometry. Drag to pan; wheel zooms.";
        this.map.requestRender();
    }

    private async save(): Promise<void> {
        if (!this.registration || !affineRegistrationSaveAllowed(
            this.registration.pairs.length,
            !!this.map.renderer.registrationPreview,
            !!this.registration.pendingSource,
            this.registration.holdoutErrorSourcePixels)) {
            throw new Error(
                "Choose three valid landmark pairs. If a fourth validation point was provided, correct any discrepancy before saving.");
        }

        const world = this.getWorld();
        const sourceMapId = this.registration.map.id;
        const updated = await this.api.registerSourceMap(
            world.id,
            sourceMapId,
            this.registration.pairs.slice(0, 3),
            world.version);
        this.applyWorld(updated);
        this.cancel();
        await this.onSaved(sourceMapId);
    }

    private renderStatus(): void {
        if (!this.registration) return;
        const state = this.registration;
        const pending = state.pendingSource
            ? ` Pending image point: ${state.pendingSource.x.toFixed(1)}, ${state.pendingSource.y.toFixed(1)}.`
            : "";
        const extra = state.pairs.length === 3
            ? " Fourth landmark is optional and checks placement independently; it is not stored."
            : state.pairs.length === 4
                ? state.holdoutErrorSourcePixels === null
                    ? " Independent landmark check was not valid."
                    : ` Independent fourth-landmark error: ${state.holdoutErrorSourcePixels.toFixed(1)} source pixels.`
                : "";
        this.status.textContent =
            `${Math.min(state.pairs.length, 3)}/3 fitting landmarks captured.${extra}${pending}`;
        required<HTMLButtonElement>(this.host, "[data-save-registration]").disabled =
            !affineRegistrationSaveAllowed(state.pairs.length,
                !!this.map.renderer.registrationPreview, !!state.pendingSource,
                state.holdoutErrorSourcePixels);
    }
}
