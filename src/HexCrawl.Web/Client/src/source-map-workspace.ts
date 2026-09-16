import type { HexCrawlApi } from "./api";
import { solveAffine } from "./affine-registration";
import type { MapSurface } from "./map-surface";
import type {
    Overworld,
    RegistrationControlPoint,
    SourceMapDetail,
    SourceMapRole,
    WorldPoint
} from "./types";
import { clearUiError, showUiError } from "./ui-error";

const newGeographyValue = "__new_geography__";

type RegistrationState = {
    map: SourceMapDetail;
    pairs: RegistrationControlPoint[];
    pendingSource: WorldPoint | null;
};

export class SourceMapWorkspace {
    private details: SourceMapDetail[] = [];
    private selected: SourceMapDetail | null = null;
    private registration: RegistrationState | null = null;
    private readonly list: HTMLElement;
    private readonly uploadForm: HTMLFormElement;
    private readonly editForm: HTMLFormElement;
    private readonly registrationPanel: HTMLElement;
    private readonly registrationImage: HTMLImageElement;
    private readonly registrationStatus: HTMLElement;
    private readonly geographySelect: HTMLSelectElement;
    private readonly newGeographyInput: HTMLInputElement;
    private disposed = false;

    public constructor(
        private readonly host: HTMLDetailsElement,
        private readonly api: HexCrawlApi,
        private readonly map: MapSurface,
        private readonly getWorld: () => Overworld,
        private readonly applyWorld: (world: Overworld) => void,
        private readonly mapHint: HTMLElement,
        private readonly errorHost: HTMLElement) {
        this.host.open = true;
        this.host.innerHTML = `
            <summary>Source maps</summary>
            <p class="hc-hint">Raster maps are presentation/evidence layers. Semantic locations and features remain independent world truth.</p>
            <div data-source-map-list></div>
            <form class="hc-form" data-source-map-upload>
                <p class="hc-subsection-title">Import raster representation</p>
                <label>Raster file <input name="file" type="file" accept="image/png,image/jpeg,image/webp" required></label>
                <label>Representation name <input name="name" required></label>
                <label>Role <select name="role"><option value="Gm">GM</option><option value="Player">Player</option><option value="Neutral">Neutral</option><option value="Other">Other</option></select></label>
                <label>Geography group <select name="geography"></select></label>
                <label data-new-geography>New geography group <input name="newGeography" placeholder="Bellowing Wilds"></label>
                <label><input name="bakedGrid" type="checkbox"> Image contains a baked-in hex grid</label>
                <button type="submit" class="hc-primary-action">Upload source map</button>
            </form>
            <form class="hc-form" data-source-map-edit hidden>
                <p class="hc-subsection-title">Selected representation</p>
                <p class="hc-hint" data-source-map-selected-meta></p>
                <label>Name <input name="name" required></label>
                <label>Geography group <input name="geographyKey" required></label>
                <label>Role <select name="role"><option value="Gm">GM</option><option value="Player">Player</option><option value="Neutral">Neutral</option><option value="Other">Other</option></select></label>
                <label><input name="bakedGrid" type="checkbox"> Image contains a baked-in hex grid</label>
                <div class="hc-button-row">
                    <button type="submit" class="hc-primary-action">Save metadata</button>
                    <button type="button" data-register>Register / re-register</button>
                    <button type="button" class="hc-danger-action" data-delete>Delete representation</button>
                </div>
            </form>
            <section data-registration-panel hidden>
                <p class="hc-subsection-title">Affine registration</p>
                <p class="hc-hint">Choose a point in the source image, then click the same place on the overworld. Repeat for three non-collinear pairs.</p>
                <img data-registration-image alt="Source map registration preview" style="display:block;max-width:100%;max-height:280px;object-fit:contain;cursor:crosshair;border:1px solid rgba(0,0,0,.2)">
                <p class="hc-hint" data-registration-status></p>
                <div class="hc-button-row">
                    <button type="button" data-clear-registration>Clear points</button>
                    <button type="button" class="hc-primary-action" data-save-registration disabled>Save registration</button>
                    <button type="button" data-cancel-registration>Cancel</button>
                </div>
            </section>`;

        this.list = required(this.host, "[data-source-map-list]");
        this.uploadForm = required(this.host, "[data-source-map-upload]");
        this.editForm = required(this.host, "[data-source-map-edit]");
        this.registrationPanel = required(this.host, "[data-registration-panel]");
        this.registrationImage = required(this.host, "[data-registration-image]");
        this.registrationStatus = required(this.host, "[data-registration-status]");
        this.geographySelect = select(this.uploadForm, "geography");
        this.newGeographyInput = input(this.uploadForm, "newGeography");

        this.geographySelect.addEventListener("change", () => this.syncNewGeographyVisibility());
        this.uploadForm.addEventListener("submit", event => {
            event.preventDefault();
            void this.run(this.uploadForm, () => this.upload());
        });
        this.editForm.addEventListener("submit", event => {
            event.preventDefault();
            void this.run(this.editForm, () => this.updateMetadata());
        });
        required<HTMLButtonElement>(this.host, "[data-register]").addEventListener("click", () => this.beginRegistration());
        required<HTMLButtonElement>(this.host, "[data-delete]").addEventListener("click", () => void this.run(null, () => this.deleteSelected()));
        required<HTMLButtonElement>(this.host, "[data-clear-registration]").addEventListener("click", () => this.clearRegistration());
        required<HTMLButtonElement>(this.host, "[data-save-registration]").addEventListener("click", () => void this.run(null, () => this.saveRegistration()));
        required<HTMLButtonElement>(this.host, "[data-cancel-registration]").addEventListener("click", () => this.cancelRegistration());
        this.registrationImage.addEventListener("click", event => this.captureSourcePoint(event));
        this.map.setClickInterceptor(point => this.consumeWorldClick(point));
    }

    public async initialize(): Promise<void> {
        await this.refresh();
    }

    public async refresh(): Promise<void> {
        if (this.disposed) return;
        const result = await this.api.listSourceMaps(this.getWorld().id);
        this.details = result.sourceMaps;
        if (this.selected) this.selected = this.details.find(item => item.id === this.selected!.id) ?? null;
        this.renderGeographies();
        this.renderList();
        this.renderSelected();
    }

    public dispose(): void {
        this.disposed = true;
        this.cancelRegistration();
        this.map.setClickInterceptor(null);
        this.registrationImage.removeAttribute("src");
    }

    private renderGeographies(): void {
        const previous = this.geographySelect.value;
        const groups = [...new Set(this.details.map(map => map.geographyKey))].sort((a, b) => a.localeCompare(b));
        this.geographySelect.replaceChildren();
        const create = document.createElement("option");
        create.value = newGeographyValue;
        create.textContent = "Create new geography group";
        this.geographySelect.append(create);
        for (const group of groups) {
            const option = document.createElement("option");
            option.value = group;
            option.textContent = group;
            this.geographySelect.append(option);
        }
        this.geographySelect.value = groups.includes(previous) ? previous : newGeographyValue;
        this.syncNewGeographyVisibility();
    }

    private syncNewGeographyVisibility(): void {
        const wrapper = required<HTMLElement>(this.uploadForm, "[data-new-geography]");
        const creating = this.geographySelect.value === newGeographyValue;
        wrapper.hidden = !creating;
        this.newGeographyInput.required = creating;
    }

    private renderList(): void {
        this.list.replaceChildren();
        if (this.details.length === 0) {
            const empty = document.createElement("p");
            empty.className = "hc-hint";
            empty.textContent = "No raster source maps have been imported.";
            this.list.append(empty);
            return;
        }
        for (const map of this.details) {
            const row = document.createElement("div");
            row.className = "hc-status-section";
            const heading = document.createElement("strong");
            heading.textContent = map.name;
            const metadata = document.createElement("p");
            metadata.className = "hc-hint";
            metadata.textContent = `${roleLabel(map.role)} · ${map.geographyKey} · ${map.pixelWidth}×${map.pixelHeight} · ${map.containsBakedGrid ? "baked grid" : "gridless"} · ${map.alignment ? "registered" : "unregistered"}`;
            const controls = document.createElement("div");
            controls.className = "hc-button-row";
            const visibleLabel = document.createElement("label");
            const visible = document.createElement("input");
            visible.type = "checkbox";
            visible.checked = !this.map.renderer.hiddenSourceMapIds.has(map.id);
            visible.addEventListener("change", () => {
                if (visible.checked) this.map.renderer.hiddenSourceMapIds.delete(map.id);
                else this.map.renderer.hiddenSourceMapIds.add(map.id);
                this.map.requestRender();
            });
            visibleLabel.append(visible, document.createTextNode(" Visible"));
            const selectButton = document.createElement("button");
            selectButton.type = "button";
            selectButton.textContent = "Edit";
            selectButton.addEventListener("click", () => {
                this.selected = map;
                this.renderSelected();
            });
            const registerButton = document.createElement("button");
            registerButton.type = "button";
            registerButton.textContent = map.alignment ? "Re-register" : "Register";
            registerButton.addEventListener("click", () => {
                this.selected = map;
                this.renderSelected();
                this.beginRegistration();
            });
            controls.append(visibleLabel, selectButton, registerButton);
            row.append(heading, metadata, controls);
            this.list.append(row);
        }
    }

    private renderSelected(): void {
        this.editForm.hidden = !this.selected;
        if (!this.selected) return;
        input(this.editForm, "name").value = this.selected.name;
        input(this.editForm, "geographyKey").value = this.selected.geographyKey;
        select(this.editForm, "role").value = this.selected.role;
        input(this.editForm, "bakedGrid").checked = this.selected.containsBakedGrid;
        required<HTMLElement>(this.editForm, "[data-source-map-selected-meta]").textContent =
            `${this.selected.pixelWidth}×${this.selected.pixelHeight} ${this.selected.mediaType}; ${this.selected.originalFileName ?? "original filename unavailable"}; ${this.selected.alignment ? "registered" : "not registered"}.`;
    }

    private async upload(): Promise<void> {
        const fileInput = input(this.uploadForm, "file");
        const file = fileInput.files?.[0];
        if (!file) throw new Error("Choose a PNG, JPEG, or WebP raster file.");
        const geographyKey = this.geographySelect.value === newGeographyValue
            ? this.newGeographyInput.value.trim()
            : this.geographySelect.value;
        if (!geographyKey) throw new Error("A geography group is required.");
        const world = this.getWorld();
        const updated = await this.api.uploadSourceMap(world.id, {
            file,
            name: input(this.uploadForm, "name").value.trim(),
            geographyKey,
            role: select(this.uploadForm, "role").value as SourceMapRole,
            containsBakedGrid: input(this.uploadForm, "bakedGrid").checked,
            expectedVersion: world.version
        });
        this.applyWorld(updated);
        this.uploadForm.reset();
        await this.refresh();
    }

    private async updateMetadata(): Promise<void> {
        if (!this.selected) throw new Error("Select a source-map representation first.");
        const world = this.getWorld();
        const updated = await this.api.updateSourceMap(world.id, this.selected.id, {
            name: input(this.editForm, "name").value.trim(),
            geographyKey: input(this.editForm, "geographyKey").value.trim(),
            role: select(this.editForm, "role").value as SourceMapRole,
            containsBakedGrid: input(this.editForm, "bakedGrid").checked,
            expectedVersion: world.version
        });
        this.applyWorld(updated);
        await this.refresh();
    }

    private async deleteSelected(): Promise<void> {
        if (!this.selected) throw new Error("Select a source-map representation first.");
        const world = this.getWorld();
        const id = this.selected.id;
        const updated = await this.api.deleteSourceMap(world.id, id, world.version);
        this.map.renderer.hiddenSourceMapIds.delete(id);
        if (this.registration?.map.id === id) this.cancelRegistration();
        this.selected = null;
        this.applyWorld(updated);
        await this.refresh();
    }

    private beginRegistration(): void {
        if (!this.selected) throw new Error("Select a source-map representation first.");
        if (this.selected.pixelWidth <= 0 || this.selected.pixelHeight <= 0) {
            throw new Error("This source map has no raster dimensions and must be re-imported before registration.");
        }
        this.registration = { map: this.selected, pairs: [], pendingSource: null };
        this.registrationPanel.hidden = false;
        this.registrationImage.src = this.api.sourceMapAssetUrl(this.getWorld().id, this.selected.id);
        this.map.renderer.hiddenSourceMapIds.delete(this.selected.id);
        this.map.renderer.registrationPreview = null;
        this.mapHint.textContent = "Registration mode: click a landmark in the source image first, then click the same landmark on the overworld.";
        this.renderRegistrationStatus();
        this.map.requestRender();
    }

    private captureSourcePoint(event: MouseEvent): void {
        if (!this.registration) return;
        if (this.registration.pairs.length >= 3) return;
        const rect = this.registrationImage.getBoundingClientRect();
        if (rect.width <= 0 || rect.height <= 0 || this.registrationImage.naturalWidth <= 0 || this.registrationImage.naturalHeight <= 0) return;
        const sourcePixel = {
            x: (event.clientX - rect.left) * this.registrationImage.naturalWidth / rect.width,
            y: (event.clientY - rect.top) * this.registrationImage.naturalHeight / rect.height
        };
        this.registration.pendingSource = sourcePixel;
        this.mapHint.textContent = `Registration mode: source point ${this.registration.pairs.length + 1} selected at ${sourcePixel.x.toFixed(1)}, ${sourcePixel.y.toFixed(1)}. Click the corresponding overworld point.`;
        this.renderRegistrationStatus();
    }

    private consumeWorldClick(point: WorldPoint): boolean {
        if (!this.registration) return false;
        if (!this.registration.pendingSource) {
            this.mapHint.textContent = "Registration mode is active. Choose a source-image point before clicking the overworld.";
            return true;
        }
        this.registration.pairs.push({ sourcePixel: this.registration.pendingSource, worldPoint: point });
        this.registration.pendingSource = null;
        if (this.registration.pairs.length === 3) {
            try {
                const transform = solveAffine(this.registration.pairs);
                this.map.renderer.registrationPreview = { sourceMapId: this.registration.map.id, transform };
                this.mapHint.textContent = "Registration preview is active. Inspect the raster overlay, then save or clear the control points.";
                this.map.requestRender();
            } catch (value) {
                this.map.renderer.registrationPreview = null;
                showUiError(this.errorHost, value);
            }
        } else {
            this.mapHint.textContent = `Registration mode: ${this.registration.pairs.length} of 3 pairs captured. Choose the next source-image point.`;
        }
        this.renderRegistrationStatus();
        return true;
    }

    private clearRegistration(): void {
        if (!this.registration) return;
        this.registration.pairs = [];
        this.registration.pendingSource = null;
        this.map.renderer.registrationPreview = null;
        this.mapHint.textContent = "Registration points cleared. Choose a source-image point to begin again.";
        this.renderRegistrationStatus();
        this.map.requestRender();
    }

    private cancelRegistration(): void {
        this.registration = null;
        this.registrationPanel.hidden = true;
        this.registrationImage.removeAttribute("src");
        this.map.renderer.registrationPreview = null;
        this.mapHint.textContent = "Use the authoring controls to place geometry. Shift-drag or middle-drag pans; wheel zooms.";
        this.map.requestRender();
    }

    private async saveRegistration(): Promise<void> {
        if (!this.registration || this.registration.pairs.length !== 3 || !this.map.renderer.registrationPreview) {
            throw new Error("Three valid source/world control-point pairs are required before registration can be saved.");
        }
        const world = this.getWorld();
        const updated = await this.api.registerSourceMap(
            world.id,
            this.registration.map.id,
            this.registration.pairs,
            world.version);
        this.applyWorld(updated);
        const selectedId = this.registration.map.id;
        this.cancelRegistration();
        await this.refresh();
        this.selected = this.details.find(item => item.id === selectedId) ?? null;
        this.renderSelected();
    }

    private renderRegistrationStatus(): void {
        if (!this.registration) return;
        const pending = this.registration.pendingSource
            ? ` Pending source: ${this.registration.pendingSource.x.toFixed(1)}, ${this.registration.pendingSource.y.toFixed(1)}.`
            : "";
        this.registrationStatus.textContent = `${this.registration.pairs.length}/3 pairs captured.${pending}`;
        required<HTMLButtonElement>(this.host, "[data-save-registration]").disabled =
            this.registration.pairs.length !== 3 || !this.map.renderer.registrationPreview;
    }

    private async run(form: HTMLFormElement | null, action: () => Promise<void>): Promise<void> {
        clearUiError(this.errorHost);
        if (form?.dataset.pending === "true") return;
        if (form) setPending(form, true);
        try {
            await action();
        } catch (value) {
            if (!this.disposed) showUiError(this.errorHost, value);
        } finally {
            if (form && !this.disposed) setPending(form, false);
        }
    }
}

function roleLabel(role: SourceMapRole): string {
    return role === "Gm" ? "GM" : role;
}

function setPending(form: HTMLFormElement, pending: boolean): void {
    form.dataset.pending = String(pending);
    for (const button of form.querySelectorAll<HTMLButtonElement>("button")) button.disabled = pending;
}

function required<T extends Element>(root: ParentNode, selector: string): T {
    const value = root.querySelector<T>(selector);
    if (!value) throw new Error(`Missing ${selector}`);
    return value;
}

function input(root: ParentNode, name: string): HTMLInputElement {
    const value = root.querySelector<HTMLInputElement>(`input[name="${name}"]`);
    if (!value) throw new Error(`Missing input ${name}`);
    return value;
}

function select(root: ParentNode, name: string): HTMLSelectElement {
    const value = root.querySelector<HTMLSelectElement>(`select[name="${name}"]`);
    if (!value) throw new Error(`Missing select ${name}`);
    return value;
}
