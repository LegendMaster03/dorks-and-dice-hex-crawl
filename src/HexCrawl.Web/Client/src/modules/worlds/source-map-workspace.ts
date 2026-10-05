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
type CoordinatedRasterView = "manual" | "gm-grid" | "gm-gridless" | "player-grid" | "player-gridless";

export class SourceMapWorkspace {
    private details: SourceMapDetail[] = [];
    private selected: SourceMapDetail | null = null;
    private readonly gridAlignmentController: SourceMapGridAlignmentController;
    private readonly registrationController: SourceMapRegistrationController;
    private readonly wonderdraftController: WonderdraftImportController;
    private readonly list: HTMLElement;
    private readonly uploadForm: HTMLFormElement;
    private readonly uploadFilesHost: HTMLElement;
    private readonly editForm: HTMLFormElement;
    private readonly geographySelect: HTMLSelectElement;
    private readonly newGeographyInput: HTMLInputElement;
    private readonly editGeographySelect: HTMLSelectElement;
    private readonly rasterViewSelect: HTMLSelectElement;
    private readonly rasterViewStatus: HTMLElement;
    private coordinatedRasterView: CoordinatedRasterView = "manual";
    private manualHiddenSourceMapIds: Set<string> | null = null;
    private gridAlignmentBusy = false;
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
            <p class="hc-hint">A map set is a collection of alternate source versions of the same map and geographic extent. Keep GM/player versions, copies with or without a visible hex grid, numbered/keyed references, and other useful versions of that map in one set. A neighboring regional map belongs in a different set.</p>
            <details>
                <summary>Technical details</summary>
                <p class="hc-hint">Internally, these are stored as source-map representations. The images remain independent evidence so future image comparison can derive a common base and true visual-difference layers without treating whole map images as the final layer model.</p>
            </details>
            <label>Map view <select data-source-map-raster-view>
                <option value="manual">Manual visibility</option>
                <option value="gm-grid">GM · grid shown</option>
                <option value="gm-gridless">GM · no printed grid</option>
                <option value="player-grid">Player · grid shown</option>
                <option value="player-gridless">Player · no printed grid</option>
            </select><span class="hc-hint">Switches the GM/Player map version for every map set at once. Shared and auxiliary references keep their manual visibility.</span></label>
            <p class="hc-hint" data-source-map-raster-view-status></p>
            <div data-source-map-list></div>
            <form class="hc-form" data-source-map-upload>
                <p class="hc-subsection-title">Import map set</p>
                <label>Map images <input name="file" type="file" accept="image/png,image/jpeg,image/webp" multiple required><span class="hc-hint">Select one image or a complete set of alternate versions of the same map.</span></label>
                <label>Map set <select name="geography"></select><span class="hc-hint">Choose an existing set to avoid spelling variants and add another version without retyping its name. Create a new set only for a different underlying map or geographic extent.</span></label>
                <label data-new-geography>New map set <input name="newGeography" placeholder="Region or map name"><span class="hc-hint">A neighboring region or separate regional map should use its own map set. Separate regions remain manually placed for now; automatic landmark/road matching is future Surveyor work.</span></label>
                <div data-source-map-upload-files></div>
                <button type="submit" class="hc-primary-action">Upload reference maps</button>
            </form>
            <form class="hc-form" data-wonderdraft-inspect>
                <p class="hc-subsection-title">Import Wonderdraft project</p>
                <p class="hc-hint">Select the matching image export. Hex Crawl preserves Wonderdraft source content first, automatically uses compatible physical scale when available, and only asks for optional semantic promotion afterward.</p>
                <label>Wonderdraft project <input name="file" type="file" accept=".wonderdraft_map" required></label>
                <label>Matching reference map <select name="sourceMap"></select></label>
                <button type="submit">Import source / inspect</button>
                <div class="hc-status-section" data-wonderdraft-result hidden></div>
            </form>
            <form class="hc-form" data-source-map-edit hidden>
                <p class="hc-subsection-title">Selected reference map</p>
                <p class="hc-hint" data-source-map-selected-meta></p>
                <label>Name <input name="name" required></label>
                <label>Map set <select name="geographyKey"></select><span class="hc-hint">A set contains alternate versions of the same underlying map/extent. Alignment is saved separately for each image so differently cropped exports are not guessed.</span></label>
                <label>Version type <select name="role">
                    <option value="Gm">GM version</option>
                    <option value="Player">Player version</option>
                    <option value="Neutral">Shared / neutral reference</option>
                    <option value="Other">Auxiliary / reference only</option>
                </select></label>
                <label><input name="bakedGrid" type="checkbox"> Image includes a visible hex grid</label>
                <div class="hc-button-row">
                    <button type="submit" class="hc-primary-action">Save metadata</button>
                    <button type="button" data-align-grid>Detect / repair hex grid</button>
                    <button type="button" data-register>Manual placement</button>
                    <button type="button" class="hc-danger-action" data-delete>Delete reference map</button>
                </div>
            </form>
            <section data-registration-panel hidden>
                <p class="hc-subsection-title">Manual map placement</p>
                <p class="hc-hint">Manual placement uses three matching landmarks. This can align a gridless version to an established world grid or place a neighboring regional map into the same overworld. Automatic landmark/road-based regional alignment is not implemented yet.</p>
                <img data-registration-image alt="Reference map manual placement" style="display:block;max-width:100%;max-height:280px;object-fit:contain;cursor:crosshair;border:1px solid rgba(0,0,0,.2)">
                <p class="hc-hint" data-registration-status></p>
                <div class="hc-button-row">
                    <button type="button" data-clear-registration>Clear points</button>
                    <button type="button" class="hc-primary-action" data-save-registration disabled>Save placement</button>
                    <button type="button" data-cancel-registration>Cancel</button>
                </div>
            </section>`;

        this.list = required(this.host, "[data-source-map-list]");
        this.uploadForm = required(this.host, "[data-source-map-upload]");
        this.uploadFilesHost = required(this.uploadForm, "[data-source-map-upload-files]");
        this.editForm = required(this.host, "[data-source-map-edit]");
        this.geographySelect = select(this.uploadForm, "geography");
        this.newGeographyInput = input(this.uploadForm, "newGeography");
        this.editGeographySelect = select(this.editForm, "geographyKey");
        this.rasterViewSelect = required<HTMLSelectElement>(this.host, "[data-source-map-raster-view]");
        this.rasterViewStatus = required<HTMLElement>(this.host, "[data-source-map-raster-view-status]");

        this.geographySelect.addEventListener("change", () => this.syncNewGeographyVisibility());
        input(this.uploadForm, "file").addEventListener("change", () => this.renderUploadFiles());
        this.rasterViewSelect.addEventListener("change", () => this.applyCoordinatedRasterView());
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
                    this.ensureVisibleForEditing(importedSourceMap);
                    this.renderSelected();
                }
            });

        const selectedAlignButton = required<HTMLButtonElement>(this.host, "[data-align-grid]");
        selectedAlignButton.addEventListener("click", () =>
            void this.run(null, () => this.detectGrid(this.selected, selectedAlignButton)));
        required<HTMLButtonElement>(this.host, "[data-register]").addEventListener("click", () => {
            if (!this.selected || this.gridAlignmentBusy) return;
            this.gridAlignmentController.cancelActive();
            this.ensureVisibleForEditing(this.selected);
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
        this.renderEditGeographies();
        this.wonderdraftController.setSourceMaps(this.details);
        this.renderList();
        this.renderSelected();
        this.applyCoordinatedRasterView();
        this.setGridAlignmentBusy(this.gridAlignmentBusy);
    }

    public dispose(): void {
        this.disposed = true;
        this.gridAlignmentController.dispose();
        this.registrationController.dispose();
        this.wonderdraftController.dispose();
    }

    private geographyGroups(): string[] {
        return [...new Set(this.details.map(map => map.geographyKey))]
            .sort((a, b) => a.localeCompare(b));
    }

    private renderGeographies(): void {
        const previous = this.geographySelect.value;
        const groups = this.geographyGroups();
        this.geographySelect.replaceChildren();
        for (const group of groups) {
            const option = document.createElement("option");
            option.value = group;
            option.textContent = group;
            this.geographySelect.append(option);
        }
        const create = document.createElement("option");
        create.value = newGeographyValue;
        create.textContent = "Create new map set";
        this.geographySelect.append(create);

        if (groups.includes(previous)) this.geographySelect.value = previous;
        else this.geographySelect.value = groups[0] ?? newGeographyValue;
        this.syncNewGeographyVisibility();
    }

    private renderEditGeographies(): void {
        this.editGeographySelect.replaceChildren();
        for (const group of this.geographyGroups()) {
            const option = document.createElement("option");
            option.value = group;
            option.textContent = group;
            this.editGeographySelect.append(option);
        }
    }

    private syncNewGeographyVisibility(): void {
        const wrapper = required<HTMLElement>(this.uploadForm, "[data-new-geography]");
        const creating = this.geographySelect.value === newGeographyValue;
        wrapper.hidden = !creating;
        this.newGeographyInput.required = creating;
    }

    private renderUploadFiles(): void {
        this.uploadFilesHost.replaceChildren();
        const files = [...(input(this.uploadForm, "file").files ?? [])];
        for (const [index, file] of files.entries()) {
            const row = document.createElement("section");
            row.className = "hc-status-section";
            row.dataset.sourceMapUploadIndex = String(index);

            const heading = document.createElement("strong");
            heading.textContent = file.name;

            const nameLabel = document.createElement("label");
            nameLabel.append(document.createTextNode("Map name "));
            const name = document.createElement("input");
            name.name = "mapName";
            name.required = true;
            name.value = displayNameFromFile(file.name);
            nameLabel.append(name);

            const roleLabelElement = document.createElement("label");
            roleLabelElement.append(document.createTextNode("Version type "));
            const role = document.createElement("select");
            role.name = "mapRole";
            role.innerHTML = sourceUseOptions();
            role.value = "Other";
            roleLabelElement.append(role);

            const gridLabel = document.createElement("label");
            const bakedGrid = document.createElement("input");
            bakedGrid.type = "checkbox";
            bakedGrid.name = "mapBakedGrid";
            gridLabel.append(bakedGrid, document.createTextNode(" Image includes a visible hex grid"));

            row.append(heading, nameLabel, roleLabelElement, gridLabel);
            this.uploadFilesHost.append(row);
        }
    }

    private applyCoordinatedRasterView(): void {
        const view = this.rasterViewSelect.value as CoordinatedRasterView;
        if (view === "manual") {
            if (this.coordinatedRasterView !== "manual" && this.manualHiddenSourceMapIds) {
                for (const sourceMap of this.details) {
                    if (!isCoordinatedViewSource(sourceMap)) continue;
                    if (this.manualHiddenSourceMapIds.has(sourceMap.id)) {
                        this.map.renderer.hiddenSourceMapIds.add(sourceMap.id);
                    } else {
                        this.map.renderer.hiddenSourceMapIds.delete(sourceMap.id);
                    }
                }
            }
            this.coordinatedRasterView = "manual";
            this.manualHiddenSourceMapIds = null;
            this.rasterViewStatus.textContent = "Manual visibility is active. Each reference map can be shown or hidden independently.";
            this.syncVisibilityControls();
            this.map.requestRender();
            return;
        }

        if (this.coordinatedRasterView === "manual") {
            this.manualHiddenSourceMapIds = new Set(
                this.details
                    .filter(isCoordinatedViewSource)
                    .filter(sourceMap => this.map.renderer.hiddenSourceMapIds.has(sourceMap.id))
                    .map(sourceMap => sourceMap.id));
        }
        this.coordinatedRasterView = view;

        const target = coordinatedViewTarget(view);
        for (const sourceMap of this.details) {
            if (!isCoordinatedViewSource(sourceMap)) continue;
            const matches = sourceMap.role === target.role
                && sourceMap.containsBakedGrid === target.containsBakedGrid;
            if (matches) this.map.renderer.hiddenSourceMapIds.delete(sourceMap.id);
            else this.map.renderer.hiddenSourceMapIds.add(sourceMap.id);
        }

        const missingGroups = this.geographyGroups().filter(group =>
            !this.details.some(map =>
                map.geographyKey === group
                && map.role === target.role
                && map.containsBakedGrid === target.containsBakedGrid));
        this.rasterViewStatus.textContent = missingGroups.length === 0
            ? `${coordinatedViewLabel(view)} is available in every map set.`
            : `${coordinatedViewLabel(view)} is active. No matching version exists in map set${missingGroups.length === 1 ? "" : "s"}: ${missingGroups.join(", ")}.`;
        this.syncVisibilityControls();
        this.map.requestRender();
    }

    private switchToManualVisibility(): void {
        this.rasterViewSelect.value = "manual";
        this.applyCoordinatedRasterView();
    }

    private syncVisibilityControls(): void {
        for (const checkbox of this.list.querySelectorAll<HTMLInputElement>("[data-source-map-visible-id]")) {
            const id = checkbox.dataset.sourceMapVisibleId;
            if (id) checkbox.checked = !this.map.renderer.hiddenSourceMapIds.has(id);
        }
    }

    private ensureVisibleForEditing(sourceMap: SourceMapDetail): void {
        if (isCoordinatedViewSource(sourceMap)) this.switchToManualVisibility();
        this.map.renderer.hiddenSourceMapIds.delete(sourceMap.id);
        this.syncVisibilityControls();
        this.map.requestRender();
    }

    private renderList(): void {
        this.list.replaceChildren();
        if (this.details.length === 0) {
            const empty = document.createElement("div");
            empty.className = "hc-empty-state";
            const heading = document.createElement("strong");
            heading.textContent = "No reference maps yet.";
            const detail = document.createElement("span");
            detail.textContent = "Upload one image or a complete map set below. Unaligned map images appear immediately with temporary centered placement.";
            empty.append(heading, detail);
            this.list.append(empty);
            return;
        }

        for (const groupName of this.geographyGroups()) {
            const group = document.createElement("section");
            group.className = "hc-status-section";
            const groupHeading = document.createElement("strong");
            groupHeading.textContent = `Map set: ${groupName}`;
            const groupHint = document.createElement("p");
            groupHint.className = "hc-hint";
            groupHint.textContent = "Alternate versions of the same map/extent. Alignment is saved per image. GM/Player views can switch together across map sets; shared and auxiliary references remain independently visible.";
            group.append(groupHeading, groupHint);

            for (const sourceMap of this.details.filter(item => item.geographyKey === groupName)) {
                const row = document.createElement("div");
                row.className = "hc-status-section";
                const heading = document.createElement("strong");
                heading.textContent = sourceMap.name;
                const metadata = document.createElement("p");
                metadata.className = "hc-hint";
                metadata.textContent = `${sourceUseLabel(sourceMap.role)} · ${sourceMap.pixelWidth}×${sourceMap.pixelHeight} · ${sourceMap.containsBakedGrid ? "grid shown" : "no printed grid"} · ${sourceMap.alignment ? "aligned" : "temporary centered placement"} · ${(sourceMap.importedContentCount ?? 0) > 0 ? `${sourceMap.importedContentCount} imported source records` : "no source records"}`;

                const controls = document.createElement("div");
                controls.className = "hc-button-row";
                const visibleLabel = document.createElement("label");
                const visible = document.createElement("input");
                visible.type = "checkbox";
                visible.dataset.sourceMapVisibleId = sourceMap.id;
                visible.checked = !this.map.renderer.hiddenSourceMapIds.has(sourceMap.id);
                visible.addEventListener("change", () => {
                    if (isCoordinatedViewSource(sourceMap)) this.switchToManualVisibility();
                    if (visible.checked) this.map.renderer.hiddenSourceMapIds.delete(sourceMap.id);
                    else this.map.renderer.hiddenSourceMapIds.add(sourceMap.id);
                    this.syncVisibilityControls();
                    this.map.requestRender();
                });
                visibleLabel.append(visible, document.createTextNode(" Visible"));

                const selectButton = document.createElement("button");
                selectButton.type = "button";
                selectButton.textContent = "Edit";
                selectButton.addEventListener("click", () => {
                    this.selected = sourceMap;
                    this.ensureVisibleForEditing(sourceMap);
                    this.renderSelected();
                });

                const alignButton = document.createElement("button");
                alignButton.type = "button";
                alignButton.textContent = sourceMap.alignment ? "Repair grid alignment" : "Detect grid";
                alignButton.disabled = this.gridAlignmentBusy;
                alignButton.addEventListener("click", () =>
                    void this.run(null, () => this.detectGrid(sourceMap, alignButton)));

                const registerButton = document.createElement("button");
                registerButton.type = "button";
                registerButton.textContent = "Manual placement";
                registerButton.disabled = this.gridAlignmentBusy;
                registerButton.addEventListener("click", () => {
                    if (this.gridAlignmentBusy) return;
                    this.selected = sourceMap;
                    this.gridAlignmentController.cancelActive();
                    this.ensureVisibleForEditing(sourceMap);
                    this.renderSelected();
                    this.registrationController.begin(this.selected);
                });
                controls.append(visibleLabel, selectButton, alignButton, registerButton);

                const hasRetainedWonderdraft =
                    (sourceMap.importedContentCount ?? 0) > 0
                    && sourceMap.importProvenance?.sourceType.toLocaleLowerCase() === "wonderdraft"
                    && !!sourceMap.sourceArchive;
                if (hasRetainedWonderdraft) {
                    const reviewButton = document.createElement("button");
                    reviewButton.type = "button";
                    reviewButton.textContent = "Review source";
                    reviewButton.disabled = !sourceMap.alignment;
                    reviewButton.title = sourceMap.alignment
                        ? "Review the retained Wonderdraft source without uploading the project again."
                        : "Place the reference map before reviewing retained Wonderdraft source.";
                    reviewButton.addEventListener("click", () => {
                        this.selected = sourceMap;
                        this.ensureVisibleForEditing(sourceMap);
                        this.renderSelected();
                        void this.run(null, () => this.wonderdraftController.openStoredReview(sourceMap));
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
        this.editGeographySelect.value = this.selected.geographyKey;
        select(this.editForm, "role").value = this.selected.role;
        input(this.editForm, "bakedGrid").checked = this.selected.containsBakedGrid;
        required<HTMLElement>(this.editForm, "[data-source-map-selected-meta]").textContent =
            `${this.selected.pixelWidth}×${this.selected.pixelHeight} ${this.selected.mediaType}; ${this.selected.originalFileName ?? "original filename unavailable"}; ${this.selected.alignment ? "aligned" : "shown with temporary placement until aligned"}; ${this.selected.importedContentCount ?? 0} imported source records.`;
    }

    private async detectGrid(sourceMap: SourceMapDetail | null, button: HTMLButtonElement): Promise<void> {
        if (!sourceMap) throw new Error("Select a reference map first.");
        if (this.gridAlignmentBusy) throw new Error("Another grid alignment is already in progress.");
        this.selected = sourceMap;
        this.ensureVisibleForEditing(sourceMap);
        this.renderSelected();
        this.registrationController.cancelActive();
        const idleText = sourceMap.alignment ? "Repair grid alignment" : "Detect grid";
        this.setGridAlignmentBusy(true);
        button.textContent = "Analyzing…";
        try {
            const applied = await this.gridAlignmentController.detectAndApply(sourceMap, progress => {
                button.textContent = progress === "detecting" ? "Analyzing…" : "Applying…";
            });
            if (applied) this.mapHint.textContent = `Grid alignment updated for ${sourceMap.name}.`;
        } finally {
            this.setGridAlignmentBusy(false);
            const current = this.details.find(item => item.id === sourceMap.id);
            button.textContent = current?.alignment ? "Repair grid alignment" : idleText;
        }
    }

    private setGridAlignmentBusy(busy: boolean): void {
        this.gridAlignmentBusy = busy;
        for (const button of this.host.querySelectorAll<HTMLButtonElement>("[data-align-grid], [data-register]")) {
            button.disabled = busy;
        }
    }

    private async upload(): Promise<void> {
        const files = [...(input(this.uploadForm, "file").files ?? [])];
        if (files.length === 0) throw new Error("Choose at least one PNG, JPEG, or WebP map image.");
        const rows = [...this.uploadFilesHost.querySelectorAll<HTMLElement>("[data-source-map-upload-index]")];
        if (rows.length !== files.length) throw new Error("The selected image list changed. Choose the files again.");

        const geographyKey = this.geographySelect.value === newGeographyValue
            ? this.newGeographyInput.value.trim()
            : this.geographySelect.value;
        if (!geographyKey) throw new Error("A map set is required.");

        let world = this.getWorld();
        const existingSourceMapIds = new Set(world.sourceMaps.map(map => map.id));
        let completed = 0;
        try {
            for (const [index, file] of files.entries()) {
                const row = rows.find(candidate => candidate.dataset.sourceMapUploadIndex === String(index));
                if (!row) throw new Error(`Missing metadata for ${file.name}.`);
                const updated = await this.api.uploadSourceMap(world.id, {
                    file,
                    name: input(row, "mapName").value.trim(),
                    geographyKey,
                    role: select(row, "mapRole").value as SourceMapRole,
                    containsBakedGrid: input(row, "mapBakedGrid").checked,
                    expectedVersion: world.version
                });
                this.applyWorld(updated);
                world = updated;
                completed += 1;
            }
        } catch (error) {
            if (completed === 0) throw error;
            this.uploadForm.reset();
            this.uploadFilesHost.replaceChildren();
            await this.refresh();
            const detail = error instanceof Error ? error.message : String(error);
            throw new Error(
                `${completed} map image${completed === 1 ? " was" : "s were"} uploaded before the remaining import failed. `
                + `Those maps are already in ${geographyKey}; re-select only the images that are still missing. ${detail}`);
        }

        this.uploadForm.reset();
        this.uploadFilesHost.replaceChildren();
        await this.refresh();

        const uploadedSourceMaps = this.details.filter(map => !existingSourceMapIds.has(map.id));
        this.switchToManualVisibility();
        for (const sourceMap of uploadedSourceMaps) this.map.renderer.hiddenSourceMapIds.delete(sourceMap.id);
        this.syncVisibilityControls();
        this.map.requestRender();
        this.selected = uploadedSourceMaps[0] ?? null;
        this.renderSelected();
        if (uploadedSourceMaps.length > 0) {
            this.mapHint.textContent = `${uploadedSourceMaps.length} reference map${uploadedSourceMaps.length === 1 ? "" : "s"} uploaded to map set ${geographyKey}. Unaligned map images are visible with temporary placement until aligned.`;
        }
    }

    private async updateMetadata(): Promise<void> {
        if (!this.selected) throw new Error("Select a reference map first.");
        const world = this.getWorld();
        const updated = await this.api.updateSourceMap(world.id, this.selected.id, {
            name: input(this.editForm, "name").value.trim(),
            geographyKey: this.editGeographySelect.value,
            role: select(this.editForm, "role").value as SourceMapRole,
            containsBakedGrid: input(this.editForm, "bakedGrid").checked,
            expectedVersion: world.version
        });
        this.applyWorld(updated);
        await this.refresh();
    }

    private async deleteSelected(): Promise<void> {
        if (!this.selected) throw new Error("Select a reference map first.");
        const world = this.getWorld();
        const id = this.selected.id;
        const updated = await this.api.deleteSourceMap(world.id, id, world.version);
        this.map.renderer.hiddenSourceMapIds.delete(id);
        this.manualHiddenSourceMapIds?.delete(id);
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

function sourceUseOptions(): string {
    return [
        '<option value="Gm">GM version</option>',
        '<option value="Player">Player version</option>',
        '<option value="Neutral">Shared / neutral reference</option>',
        '<option value="Other">Auxiliary / reference only</option>'
    ].join("");
}

function sourceUseLabel(role: SourceMapRole): string {
    switch (role) {
        case "Gm": return "GM version";
        case "Player": return "Player version";
        case "Neutral": return "shared / neutral reference";
        case "Other": return "auxiliary / reference only";
    }
}

function isCoordinatedViewSource(sourceMap: SourceMapDetail): boolean {
    return sourceMap.role === "Gm" || sourceMap.role === "Player";
}

function coordinatedViewTarget(view: Exclude<CoordinatedRasterView, "manual">): {
    role: "Gm" | "Player";
    containsBakedGrid: boolean;
} {
    return {
        role: view.startsWith("gm-") ? "Gm" : "Player",
        containsBakedGrid: view.endsWith("-grid")
    };
}

function coordinatedViewLabel(view: Exclude<CoordinatedRasterView, "manual">): string {
    const target = coordinatedViewTarget(view);
    return `${target.role === "Gm" ? "GM" : "Player"} · ${target.containsBakedGrid ? "grid shown" : "no printed grid"}`;
}

function displayNameFromFile(fileName: string): string {
    const withoutExtension = fileName.replace(/\.[^.]+$/, "");
    return withoutExtension.trim() || fileName;
}

function setPending(form: HTMLFormElement, pending: boolean): void {
    form.dataset.pending = String(pending);
    for (const button of form.querySelectorAll<HTMLButtonElement>("button")) button.disabled = pending;
}
