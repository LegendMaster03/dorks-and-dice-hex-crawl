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
            <p class="hc-hint">A map set is a collection of alternate source versions of the same map and geographic extent. Keep GM/player versions, grid/gridless exports, numbered/keyed references, and other evidence for that map in one set. A neighboring regional map belongs in a different set. These source images remain independent so future image comparison can derive a common base and true visual-difference layers.</p>
            <label>Raster view <select data-source-map-raster-view>
                <option value="manual">Manual visibility</option>
                <option value="gm-grid">GM · baked grid</option>
                <option value="gm-gridless">GM · gridless</option>
                <option value="player-grid">Player · baked grid</option>
                <option value="player-gridless">Player · gridless</option>
            </select><span class="hc-hint">This is a temporary whole-image view switch. It coordinates GM/Player source maps across all map sets without treating the rasters themselves as the final layer model. Shared and auxiliary references keep their manual visibility.</span></label>
            <p class="hc-hint" data-source-map-raster-view-status></p>
            <div data-source-map-list></div>
            <form class="hc-form" data-source-map-upload>
                <p class="hc-subsection-title">Import map set</p>
                <label>Raster files <input name="file" type="file" accept="image/png,image/jpeg,image/webp" multiple required><span class="hc-hint">Select one image or a complete set of alternate versions of the same map.</span></label>
                <label>Map set <select name="geography"></select><span class="hc-hint">Choose an existing set to avoid spelling variants. Create a new set only for a different underlying map or geographic extent.</span></label>
                <label data-new-geography>New map set <input name="newGeography" placeholder="Bellowing Wilds"><span class="hc-hint">For example, Bellowing Wilds and neighboring Kylandria are separate map sets even though both occupy the same overworld. Separate regions remain manually registered for now; automatic landmark/road matching is future Surveyor work.</span></label>
                <div data-source-map-upload-files></div>
                <button type="submit" class="hc-primary-action">Upload reference maps</button>
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
                <label>Map set <select name="geographyKey"></select><span class="hc-hint">A set contains alternate versions of the same underlying map/extent. Registration remains per source image so differently cropped exports are not guessed.</span></label>
                <label>Source use <select name="role">
                    <option value="Gm">GM view source</option>
                    <option value="Player">Player view source</option>
                    <option value="Neutral">Shared / neutral reference</option>
                    <option value="Other">Auxiliary / reference only</option>
                </select></label>
                <label><input name="bakedGrid" type="checkbox"> Source image contains a baked-in hex grid</label>
                <div class="hc-button-row">
                    <button type="submit" class="hc-primary-action">Save metadata</button>
                    <button type="button" data-align-grid>Detect / repair hex grid</button>
                    <button type="button" data-register>Advanced registration</button>
                    <button type="button" class="hc-danger-action" data-delete>Delete raster map</button>
                </div>
            </form>
            <section data-registration-panel hidden>
                <p class="hc-subsection-title">Advanced map registration</p>
                <p class="hc-hint">Manual placement uses three matching landmarks. This can align a gridless representation to an established world grid or place a neighboring regional map into the same overworld. Automatic landmark/road-based regional alignment is not implemented yet.</p>
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
            if (!this.selected) return;
            this.gridAlignmentController.cancelIfMap(this.selected.id);
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
            roleLabelElement.append(document.createTextNode("Source use "));
            const role = document.createElement("select");
            role.name = "mapRole";
            role.innerHTML = sourceUseOptions();
            role.value = "Other";
            roleLabelElement.append(role);

            const gridLabel = document.createElement("label");
            const bakedGrid = document.createElement("input");
            bakedGrid.type = "checkbox";
            bakedGrid.name = "mapBakedGrid";
            gridLabel.append(bakedGrid, document.createTextNode(" Source image contains a baked-in hex grid"));

            row.append(heading, nameLabel, roleLabelElement, gridLabel);
            this.uploadFilesHost.append(row);
        }
    }

    private applyCoordinatedRasterView(): void {
        const view = this.rasterViewSelect.value as CoordinatedRasterView;
        if (view === "manual") {
            this.rasterViewStatus.textContent = "Manual visibility is active. Each reference map can be shown or hidden independently.";
            this.map.requestRender();
            return;
        }

        const target = coordinatedViewTarget(view);
        for (const sourceMap of this.details) {
            if (sourceMap.role !== "Gm" && sourceMap.role !== "Player") continue;
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
            : `${coordinatedViewLabel(view)} is active. No matching source image exists in map set${missingGroups.length === 1 ? "" : "s"}: ${missingGroups.join(", ")}.`;
        this.map.requestRender();
    }

    private ensureVisibleForEditing(sourceMap: SourceMapDetail): void {
        this.rasterViewSelect.value = "manual";
        this.map.renderer.hiddenSourceMapIds.delete(sourceMap.id);
        this.applyCoordinatedRasterView();
    }

    private renderList(): void {
        this.list.replaceChildren();
        if (this.details.length === 0) {
            const empty = document.createElement("div");
            empty.className = "hc-empty-state";
            const heading = document.createElement("strong");
            heading.textContent = "No reference maps yet.";
            const detail = document.createElement("span");
            detail.textContent = "Upload one image or a complete map set below. Unregistered rasters appear immediately with temporary centered placement.";
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
            groupHint.textContent = "Alternate source versions of the same map/extent. Registration stays per source image. GM/Player views can switch together across map sets; shared and auxiliary references remain independently visible.";
            group.append(groupHeading, groupHint);

            for (const sourceMap of this.details.filter(item => item.geographyKey === groupName)) {
                const row = document.createElement("div");
                row.className = "hc-status-section";
                const heading = document.createElement("strong");
                heading.textContent = sourceMap.name;
                const metadata = document.createElement("p");
                metadata.className = "hc-hint";
                metadata.textContent = `${sourceUseLabel(sourceMap.role)} · ${sourceMap.pixelWidth}×${sourceMap.pixelHeight} · ${sourceMap.containsBakedGrid ? "baked grid" : "gridless"} · ${sourceMap.alignment ? "registered" : "temporary centered placement"} · ${(sourceMap.importedContentCount ?? 0) > 0 ? `${sourceMap.importedContentCount} imported source records` : "no source records"}`;

                const controls = document.createElement("div");
                controls.className = "hc-button-row";
                const visibleLabel = document.createElement("label");
                const visible = document.createElement("input");
                visible.type = "checkbox";
                visible.checked = !this.map.renderer.hiddenSourceMapIds.has(sourceMap.id);
                visible.addEventListener("change", () => {
                    this.rasterViewSelect.value = "manual";
                    if (visible.checked) this.map.renderer.hiddenSourceMapIds.delete(sourceMap.id);
                    else this.map.renderer.hiddenSourceMapIds.add(sourceMap.id);
                    this.applyCoordinatedRasterView();
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
                alignButton.addEventListener("click", () =>
                    void this.run(null, () => this.detectGrid(sourceMap, alignButton)));

                const registerButton = document.createElement("button");
                registerButton.type = "button";
                registerButton.textContent = "Advanced registration";
                registerButton.addEventListener("click", () => {
                    this.selected = sourceMap;
                    this.ensureVisibleForEditing(sourceMap);
                    this.renderSelected();
                    this.gridAlignmentController.cancelIfMap(sourceMap.id);
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
                        : "Register the raster map before reviewing retained Wonderdraft source.";
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
            `${this.selected.pixelWidth}×${this.selected.pixelHeight} ${this.selected.mediaType}; ${this.selected.originalFileName ?? "original filename unavailable"}; ${this.selected.alignment ? "registered" : "shown with temporary placement until registered"}; ${this.selected.importedContentCount ?? 0} imported source records.`;
    }

    private async detectGrid(sourceMap: SourceMapDetail | null, button: HTMLButtonElement): Promise<void> {
        if (!sourceMap) throw new Error("Select a raster map first.");
        this.selected = sourceMap;
        this.ensureVisibleForEditing(sourceMap);
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
        const files = [...(input(this.uploadForm, "file").files ?? [])];
        if (files.length === 0) throw new Error("Choose at least one PNG, JPEG, or WebP raster file.");
        const rows = [...this.uploadFilesHost.querySelectorAll<HTMLElement>("[data-source-map-upload-index]")];
        if (rows.length !== files.length) throw new Error("The selected raster list changed. Choose the files again.");

        const geographyKey = this.geographySelect.value === newGeographyValue
            ? this.newGeographyInput.value.trim()
            : this.geographySelect.value;
        if (!geographyKey) throw new Error("A map set is required.");

        let world = this.getWorld();
        const existingSourceMapIds = new Set(world.sourceMaps.map(map => map.id));
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
        }

        this.uploadForm.reset();
        this.uploadFilesHost.replaceChildren();
        await this.refresh();

        const uploadedSourceMaps = this.details.filter(map => !existingSourceMapIds.has(map.id));
        for (const sourceMap of uploadedSourceMaps) this.map.renderer.hiddenSourceMapIds.delete(sourceMap.id);
        this.rasterViewSelect.value = "manual";
        this.applyCoordinatedRasterView();
        this.selected = uploadedSourceMaps[0] ?? null;
        this.renderSelected();
        if (uploadedSourceMaps.length > 0) {
            this.mapHint.textContent = `${uploadedSourceMaps.length} reference map${uploadedSourceMaps.length === 1 ? "" : "s"} uploaded to map set ${geographyKey}. Unregistered rasters are visible with temporary placement until registered.`;
        }
    }

    private async updateMetadata(): Promise<void> {
        if (!this.selected) throw new Error("Select a raster map first.");
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

function sourceUseOptions(): string {
    return [
        '<option value="Gm">GM view source</option>',
        '<option value="Player">Player view source</option>',
        '<option value="Neutral">Shared / neutral reference</option>',
        '<option value="Other">Auxiliary / reference only</option>'
    ].join("");
}

function sourceUseLabel(role: SourceMapRole): string {
    switch (role) {
        case "Gm": return "GM view source";
        case "Player": return "Player view source";
        case "Neutral": return "shared / neutral reference";
        case "Other": return "auxiliary / reference only";
    }
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
    return `${target.role === "Gm" ? "GM" : "Player"} · ${target.containsBakedGrid ? "baked grid" : "gridless"}`;
}

function displayNameFromFile(fileName: string): string {
    const withoutExtension = fileName.replace(/\.[^.]+$/, "");
    return withoutExtension.trim() || fileName;
}

function setPending(form: HTMLFormElement, pending: boolean): void {
    form.dataset.pending = String(pending);
    for (const button of form.querySelectorAll<HTMLButtonElement>("button")) button.disabled = pending;
}
