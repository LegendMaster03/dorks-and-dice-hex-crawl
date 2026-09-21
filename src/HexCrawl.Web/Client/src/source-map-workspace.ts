import type { HexCrawlApi } from "./api";
import type { MapSurface } from "./map-surface";
import type {
    Overworld,
    SourceMapDetail,
    SourceMapRole
} from "./types";
import { clearUiError, showUiError } from "./ui-error";
import { input, required, select } from "./ui/dom";
import { SourceMapRegistrationController } from "./source-map-registration-controller";
import { WonderdraftImportController } from "./wonderdraft-import-controller";

const newGeographyValue = "__new_geography__";

export class SourceMapWorkspace {
    private details: SourceMapDetail[] = [];
    private selected: SourceMapDetail | null = null;
    private readonly registrationController: SourceMapRegistrationController;
    private readonly wonderdraftController: WonderdraftImportController;
    private readonly list: HTMLElement;
    private readonly uploadForm: HTMLFormElement;
    private readonly editForm: HTMLFormElement;
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
            <form class="hc-form" data-wonderdraft-inspect>
                <p class="hc-subsection-title">Review Wonderdraft project</p>
                <p class="hc-hint">Read native Wonderdraft structure without persisting the project. Choose a registered raster representation to map import candidates into overworld coordinates.</p>
                <label>Wonderdraft project <input name="file" type="file" accept=".wonderdraft_map" required></label>
                <label>Registered source map <select name="sourceMap"></select></label>
                <button type="submit">Inspect / review project</button>
                <div class="hc-status-section" data-wonderdraft-result hidden></div>
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
        this.registrationController = new SourceMapRegistrationController(
            this.host,
            this.api,
            this.map,
            this.getWorld,
            this.applyWorld,
            this.mapHint,
            this.errorHost,
            action => { void this.run(null, action); },
            async () => { await this.refresh(); });
        this.wonderdraftController = new WonderdraftImportController(
            this.host,
            this.api,
            this.getWorld,
            this.applyWorld,
            (form, action) => { void this.run(form, action); },
            async () => { await this.refresh(); });
        required<HTMLButtonElement>(this.host, "[data-register]").addEventListener("click", () =>
            this.registrationController.begin(this.selected));
        required<HTMLButtonElement>(this.host, "[data-delete]").addEventListener("click", () =>
            void this.run(null, () => this.deleteSelected()));
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
        this.wonderdraftController.setSourceMaps(this.details);
        this.renderList();
        this.renderSelected();
    }

    public dispose(): void {
        this.disposed = true;
        this.registrationController.dispose();
        this.wonderdraftController.dispose();
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
                this.registrationController.begin(this.selected);
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
        this.registrationController.cancelIfMap(id);
        this.selected = null;
        this.applyWorld(updated);
        await this.refresh();
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

