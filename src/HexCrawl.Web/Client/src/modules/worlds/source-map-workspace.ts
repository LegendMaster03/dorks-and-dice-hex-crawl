import type { HexCrawlApi } from "../../api";
import type { MapSurface } from "../../map-surface";
import type {
    Overworld,
    SourceMapDetail,
    SourceMapRole
} from "../../types";
import { clearUiError, showUiError } from "../../ui-error";
import { input, required, select } from "../../ui/dom";
import { SourceMapGridAlignmentController } from "./source-map-grid-alignment-controller";
import { SourceMapRegistrationController } from "./source-map-registration-controller";
import { WonderdraftImportController } from "./wonderdraft-import-controller";

const newGeographyValue = "__new_geography__";

export class SourceMapWorkspace {
    private details: SourceMapDetail[] = [];
    private selected: SourceMapDetail | null = null;
    private readonly gridAlignmentController: SourceMapGridAlignmentController;
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
            <summary>Reference maps</summary>
            <p class="hc-hint">Reference maps are alternate raster views of the same continuous overworld. Put maps of the same geographic extent in one map group, such as GM/player and grid/gridless versions of the same map. Use a different group for a different region or extent. Visibility switches let you layer or swap representations after they are registered.</p>
            <div data-source-map-list></div>
            <form class="hc-form" data-source-map-upload>
                <p class="hc-subsection-title">Import raster map</p>
                <label>Raster file <input name="file" type="file" accept="image/png,image/jpeg,image/webp" required></label>
                <label>Map name <input name="name" required></label>
                <label>Role <select name="role"><option value="Gm">GM</option><option value="Player">Player</option><option value="Neutral">Neutral</option><option value="Other">Other</option></select><span class="hc-hint">Use GM and Player for alternate audience versions of the same geography.</span></label>
                <label>Map group <select name="geography"></select><span class="hc-hint">A map group means the same geographic extent, not merely the same campaign. A common group may contain GM/player and grid/gridless variants.</span></label>
                <label data-new-geography>New map group <input name="newGeography" placeholder="Bellowing Wilds"><span class="hc-hint">For a separate regional map, create a separate group and register it into the shared overworld coordinate space.</span></label>
                <label><input name="bakedGrid" type="checkbox"> Image contains a baked-in hex grid</label>
                <button type="submit" class="hc-primary-action">Upload raster map</button>
            </form>
            <form class="hc-form" data-wonderdraft-inspect>
                <p class="hc-subsection-title">Import Wonderdraft project</p>
                <p class="hc-hint">Select the matching raster export. Hex Crawl preserves Wonderdraft source content first, automatically uses compatible physical scale when available, and only asks for optional semantic promotion afterward.</p>
                <label>Wonderdraft project <input name="file" type="file" accept=".wonderdraft_map" required></label>
                <label>Matching raster map <select name="sourceMap"></select></label>
                <button type="submit">Import source / inspect</button>
                <div class="hc-status-section" data-wonderdraft-result hidden></div>
            </form>
            <form class="hc-form" data-source-map-edit hidden>
                <p class="hc-subsection-title">Selected raster map</p>
                <p class="hc-hint" data-source-map-selected-meta></p>
                <label>Name <input name="name" required></label>
                <label>Map group <input name="geographyKey" required><span class="hc-hint">Maps in the same group represent the same geographic extent. This does not automatically copy registration between differently cropped images.</span></label>
                <label>Role <select name="role"><option value="Gm">GM</option><option value="Player">Player</option><option value="Neutral">Neutral</option><option value="Other">Other</option></select></label>
                <label><input name="bakedGrid" type="checkbox"> Image contains a baked-in hex grid</label>
                <div class="hc-button-row">
                    <button type="submit" class="hc-primary-action">Save metadata</button>
                    <button type="button" data-align-grid>Detect / repair hex grid</button>
                    <button type="button" data-register>Advanced registration</button>
                    <button type="button" class="hc-danger-action" data-delete>Delete raster map</button>
                </div>
            </form>
            <section data-registration-panel hidden>
                <p class="hc-subsection-title">Advanced map registration</p>
                <p class="hc-hint">Manual placement uses three matching landmarks. This can align a gridless representation to an already established world grid, or place a different regional map into the same overworld. Choose a landmark in the raster, choose the same place on the world map, and repeat three times.</p>
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
        this.gridAlignmentController = new SourceMapGridAlignmentController(
            this.api,
            this.map,
            this.getWorld,
            this.applyWorld,
            async () => { await this.refresh(); });
        this.wonderdraftController = new WonderdraftImportController(
            this.host,
            this.api,
            this.map,
            this.getWorld,
            this.applyWorld,
            (form, action) => { void this.run(form, action); },
            async () => {
                await this.refresh();
                const wonderdraftForm = required<HTMLFormElement>(this.host, "[data-wonderdraft-inspect]");
                const importedSourceMapId = select(wonderdraftForm, "sourceMap").value;
                const importedSourceMap = this.details.find(map => map.id === importedSourceMapId) ?? null;
                if (importedSourceMap) {
                    this.selected = importedSourceMap;
                    this.renderSelected();
                }
            });

        const selectedAlignButton = required<HTMLButtonElement>(this.host, "[data-align-grid]");
        selectedAlignButton.addEventListener("click", () =>
            void this.run(null, () => this.detectGrid(this.selected, selectedAlignButton)));
        required<HTMLButtonElement>(this.host, "[data-register]").addEventListener("click", () => {
            if (this.selected) this.gridAlignmentController.cancelIfMap(this.selected.id);
            this.registrationController.begin(this.selected);
        });
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
        this.gridAlignmentController.dispose();
        this.registrationController.dispose();
        this.wonderdraftController.dispose();
    }

    private renderGeographies(): void {
        const previous = this.geographySelect.value;
        const groups = [...new Set(this.details.map(map => map.geographyKey))].sort((a, b) => a.localeCompare(b));
        this.geographySelect.replaceChildren();
        const create = document.createElement("option");
        create.value = newGeographyValue;
        create.textContent = "Create new map group";
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
            const empty = document.createElement("div");
            empty.className = "hc-empty-state";
            const heading = document.createElement("strong");
            heading.textContent = "No reference maps yet.";
            const detail = document.createElement("span");
            detail.textContent = "Upload a PNG, JPEG, or WebP below. Maps appear immediately with temporary placement until registered.";
            empty.append(heading, detail);
            this.list.append(empty);
            return;
        }

        const groups = [...new Set(this.details.map(map => map.geographyKey))].sort((a, b) => a.localeCompare(b));
        for (const groupName of groups) {
            const group = document.createElement("section");
            group.className = "hc-status-section";
            const groupHeading = document.createElement("strong");
            groupHeading.textContent = `Map group: ${groupName}`;
            const groupHint = document.createElement("p");
            groupHint.className = "hc-hint";
            groupHint.textContent = "Same geographic extent. Use visibility to layer or switch GM/player and grid/gridless representations. Registration remains per image so differently cropped exports are not guessed.";
            group.append(groupHeading, groupHint);

            for (const map of this.details.filter(item => item.geographyKey === groupName)) {
                const row = document.createElement("div");
                row.className = "hc-status-section";
                const heading = document.createElement("strong");
                heading.textContent = map.name;
                const metadata = document.createElement("p");
                metadata.className = "hc-hint";
                metadata.textContent = `${roleLabel(map.role)} · ${map.pixelWidth}×${map.pixelHeight} · ${map.containsBakedGrid ? "baked grid" : "gridless"} · ${map.alignment ? "registered" : "temporary centered placement"} · ${(map.importedContentCount ?? 0) > 0 ? `${map.importedContentCount} imported source records` : "no source records"}`;
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

                const alignButton = document.createElement("button");
                alignButton.type = "button";
                alignButton.textContent = map.alignment ? "Repair grid alignment" : "Detect grid";
                alignButton.addEventListener("click", () =>
                    void this.run(null, () => this.detectGrid(map, alignButton)));

                const registerButton = document.createElement("button");
                registerButton.type = "button";
                registerButton.textContent = "Advanced registration";
                registerButton.addEventListener("click", () => {
                    this.selected = map;
                    this.renderSelected();
                    this.gridAlignmentController.cancelIfMap(map.id);
                    this.registrationController.begin(this.selected);
                });
                controls.append(visibleLabel, selectButton, alignButton, registerButton);

                const hasRetainedWonderdraft =
                    (map.importedContentCount ?? 0) > 0
                    && map.importProvenance?.sourceType.toLocaleLowerCase() === "wonderdraft"
                    && !!map.sourceArchive;
                if (hasRetainedWonderdraft) {
                    const reviewButton = document.createElement("button");
                    reviewButton.type = "button";
                    reviewButton.textContent = "Review source";
                    reviewButton.disabled = !map.alignment;
                    reviewButton.title = map.alignment
                        ? "Review the retained Wonderdraft source without uploading the project again."
                        : "Register the raster map before reviewing retained Wonderdraft source.";
                    reviewButton.addEventListener("click", () => {
                        this.selected = map;
                        this.renderSelected();
                        void this.run(null, () => this.wonderdraftController.openStoredReview(map));
                    });
                    controls.append(reviewButton);
                }
                row.append(heading, metadata, controls);
                group.append(row);
            }
            this.list.append(group);
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
            `${this.selected.pixelWidth}×${this.selected.pixelHeight} ${this.selected.mediaType}; ${this.selected.originalFileName ?? "original filename unavailable"}; ${this.selected.alignment ? "registered" : "shown with temporary placement until registered"}; ${this.selected.importedContentCount ?? 0} imported source records.`;
    }

    private async detectGrid(sourceMap: SourceMapDetail | null, button: HTMLButtonElement): Promise<void> {
        if (!sourceMap) throw new Error("Select a raster map first.");
        this.selected = sourceMap;
        this.renderSelected();
        this.registrationController.cancelIfMap(sourceMap.id);
        const idleText = sourceMap.alignment ? "Repair grid alignment" : "Detect grid";
        button.disabled = true;
        try {
            await this.gridAlignmentController.detectAndApply(sourceMap, progress => {
                button.textContent = progress === "detecting" ? "Analyzing…" : "Applying…";
            });
            this.mapHint.textContent = `Grid alignment updated for ${sourceMap.name}.`;
        } finally {
            button.disabled = false;
            const current = this.details.find(item => item.id === sourceMap.id);
            button.textContent = current?.alignment ? "Repair grid alignment" : idleText;
        }
    }

    private async upload(): Promise<void> {
        const fileInput = input(this.uploadForm, "file");
        const file = fileInput.files?.[0];
        if (!file) throw new Error("Choose a PNG, JPEG, or WebP raster file.");
        const geographyKey = this.geographySelect.value === newGeographyValue
            ? this.newGeographyInput.value.trim()
            : this.geographySelect.value;
        if (!geographyKey) throw new Error("A map group is required.");
        const world = this.getWorld();
        const existingSourceMapIds = new Set(world.sourceMaps.map(map => map.id));
        const containsBakedGrid = input(this.uploadForm, "bakedGrid").checked;
        const updated = await this.api.uploadSourceMap(world.id, {
            file,
            name: input(this.uploadForm, "name").value.trim(),
            geographyKey,
            role: select(this.uploadForm, "role").value as SourceMapRole,
            containsBakedGrid,
            expectedVersion: world.version
        });
        this.applyWorld(updated);
        this.uploadForm.reset();
        await this.refresh();

        const uploadedSourceMap = this.details.find(map => !existingSourceMapIds.has(map.id)) ?? null;
        if (!uploadedSourceMap) return;
        this.selected = uploadedSourceMap;
        this.renderSelected();
        this.map.renderer.hiddenSourceMapIds.delete(uploadedSourceMap.id);
        this.map.requestRender();
        this.mapHint.textContent = `${uploadedSourceMap.name} is visible with temporary placement. Detect its baked grid or use Advanced registration when you are ready to place it.`;
    }

    private async updateMetadata(): Promise<void> {
        if (!this.selected) throw new Error("Select a raster map first.");
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
        if (!this.selected) throw new Error("Select a raster map first.");
        const world = this.getWorld();
        const id = this.selected.id;
        const updated = await this.api.deleteSourceMap(world.id, id, world.version);
        this.map.renderer.hiddenSourceMapIds.delete(id);
        this.gridAlignmentController.cancelIfMap(id);
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
