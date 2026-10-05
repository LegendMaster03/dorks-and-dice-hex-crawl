import type { HexCrawlApi } from "../../api";
import { solveAffine } from "../../affine-registration";
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

        this.registration = { map: sourceMap, pairs: [], pendingSource: null };
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
        if (!this.registration || this.registration.pairs.length >= 3) return;
        const rect = this.image.getBoundingClientRect();
        if (rect.width <= 0 || rect.height <= 0 || this.image.naturalWidth <= 0 || this.image.naturalHeight <= 0) {
            return;
        }

        const sourcePixel = {
            x: (event.clientX - rect.left) * this.image.naturalWidth / rect.width,
            y: (event.clientY - rect.top) * this.image.naturalHeight / rect.height
        };
        this.registration.pendingSource = sourcePixel;
        this.mapHint.textContent =
            `Manual placement: image point ${this.registration.pairs.length + 1} selected at ${sourcePixel.x.toFixed(1)}, ${sourcePixel.y.toFixed(1)}. Click the corresponding world-map point.`;
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
                    "Placement preview is active. Inspect the map overlay, then save the placement or clear the points.";
                this.map.requestRender();
            } catch (value) {
                this.map.renderer.registrationPreview = null;
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
        if (!this.registration
            || this.registration.pairs.length !== 3
            || !this.map.renderer.registrationPreview) {
            throw new Error(
                "Three valid image/world landmark pairs are required before placement can be saved.");
        }

        const world = this.getWorld();
        const sourceMapId = this.registration.map.id;
        const updated = await this.api.registerSourceMap(
            world.id,
            sourceMapId,
            this.registration.pairs,
            world.version);
        this.applyWorld(updated);
        this.cancel();
        await this.onSaved(sourceMapId);
    }

    private renderStatus(): void {
        if (!this.registration) return;
        const pending = this.registration.pendingSource
            ? ` Pending image point: ${this.registration.pendingSource.x.toFixed(1)}, ${this.registration.pendingSource.y.toFixed(1)}.`
            : "";
        this.status.textContent =
            `${this.registration.pairs.length}/3 landmark pairs captured.${pending}`;
        required<HTMLButtonElement>(this.host, "[data-save-registration]").disabled =
            this.registration.pairs.length !== 3 || !this.map.renderer.registrationPreview;
    }
}
