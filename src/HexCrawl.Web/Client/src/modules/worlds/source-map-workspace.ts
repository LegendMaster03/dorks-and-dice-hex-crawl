import type { HexCrawlApi } from "../../api";
import type { MapSurface } from "../../map-surface";
import type {
    Overworld,
    SourceMapDetail,
    SourceMapRole
} from "../../types";
import { clearUiError, showUiError } from "../../ui-error";
import { input, required, select } from "../../ui/dom";
 import { SourceMapRegistrationController } from "./source-map-registration-controller";
import { WonderdraftImportController } from "./wonderdraft-import-controller";

const newGeographyValue = "__new_geography__";
type CoordinatedRasterView = "manual" | "gm-grid" | "gm-gridless" | "player-grid" | "player-gridless";

export class SourceMapWorkspace {
    private details: SourceMapDetail[] = [];
    private selected: SourceMapDetail | null = null;
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
    private compatibilityGeneration = 0;
    private gridFitRequestGeneration = 0;
    private gridFitPreview: {
        sourceMapId: string;
        worldVersion: number;
        alignment: import("../../types").MapRegistrationTransform;
        observedColumns: number | null;
        observedRows: number | null;
        margins: { leftPercent: number; topPercent: number; rightPercent: number; bottomPercent: number };
    } | null = null;
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
            <p class="hc-hint">A map set is a collection of alternate source versions of the same map and geographic extent. Keep GM/player versions, copies with or without a visible cell grid, numbered/keyed references, and other useful versions of that map in one set. A neighboring regional map belongs in a different set.</p>
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
                <label><input name="bakedGrid" type="checkbox"> Image includes visible tile boundaries</label>
                <div class="hc-button-row">
                    <button type="submit" class="hc-primary-action">Save metadata</button>
                    <button type="button" data-fit-playable-grid>Preview playable grid fit</button>
                    <button type="button" data-check-pattern title="Read-only experimental image comparison">Check cell pattern</button>
                    <button type="button" data-register>Manual placement</button>
                    <button type="button" class="hc-danger-action" data-delete>Delete reference map</button>
                </div>
                <section class="hc-status-section" data-playable-fit-settings>
                    <p class="hc-hint">Decorative borders and surrounding margins are supported. Enter the actual printed grid size to check it against this world's finite cell grid. The detector can identify repeating patterns, but cannot independently verify a finite row or column count.</p>
                    <div class="hc-form-grid">
                        <label>Printed columns <input data-grid-columns type="number" min="1" step="1" placeholder="Count on image"></label>
                        <label>Printed rows <input data-grid-rows type="number" min="1" step="1" placeholder="Count on image"></label>
                        <label>Left margin (%) <input data-grid-margin-left type="number" min="0" max="99" step="any" value="0"></label>
                        <label>Top margin (%) <input data-grid-margin-top type="number" min="0" max="99" step="any" value="0"></label>
                        <label>Right margin (%) <input data-grid-margin-right type="number" min="0" max="99" step="any" value="0"></label>
                        <label>Bottom margin (%) <input data-grid-margin-bottom type="number" min="0" max="99" step="any" value="0"></label>
                    </div>
                    <p class="hc-hint">Margins are excluded from the grid fit, but the complete image is retained. For rotated artwork or nonrectangular grid content, use detected pattern registration or manual placement.</p>
                </section>
                <section class="hc-status-section" data-playable-fit-preview hidden aria-live="polite">
                    <p data-playable-fit-description></p>
                    <div class="hc-button-row">
                        <button type="button" class="hc-primary-action" data-accept-playable-fit>Save this image fit</button>
                        <button type="button" data-cancel-playable-fit>Discard preview</button>
                    </div>
                </section>
                <section class="hc-status-section" data-pattern-assessment hidden aria-live="polite"></section>
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

        for (const field of this.editForm.querySelectorAll<HTMLInputElement>(
            "[data-grid-columns], [data-grid-rows], [data-grid-margin-left], " +
            "[data-grid-margin-top], [data-grid-margin-right], [data-grid-margin-bottom]")) {
            field.addEventListener("input", () => this.clearFullGridFit());
        }
        required<HTMLButtonElement>(this.host, "[data-fit-playable-grid]")
            .addEventListener("click", () =>
                void this.run(null, () => this.previewFullGridFit()));
        required<HTMLButtonElement>(this.host, "[data-accept-playable-fit]")
            .addEventListener("click", () =>
                void this.run(null, () => this.acceptFullGridFit()));
        required<HTMLButtonElement>(this.host, "[data-cancel-playable-fit]")
            .addEventListener("click", () => this.clearFullGridFit());
        required<HTMLButtonElement>(this.host, "[data-check-pattern]")
            .addEventListener("click", () =>
                void this.run(null, () => this.assessPattern(this.selected)));
        required<HTMLButtonElement>(this.host, "[data-register]").addEventListener("click", () => {
            if (!this.selected) return;
            this.clearFullGridFit();
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
        this.compatibilityGeneration++;
        this.clearFullGridFit();
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
            gridLabel.append(bakedGrid, document.createTextNode(" Image includes printed tile boundaries"));

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
                    const requestedVisible = visible.checked;
                    if (isCoordinatedViewSource(sourceMap)) this.switchToManualVisibility();
                    if (requestedVisible) this.map.renderer.hiddenSourceMapIds.delete(sourceMap.id);
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

                const registerButton = document.createElement("button");
                registerButton.type = "button";
                registerButton.textContent = "Manual placement";
                registerButton.addEventListener("click", () => {
                    this.clearFullGridFit();
                    this.selected = sourceMap;
                    this.ensureVisibleForEditing(sourceMap);
                    this.renderSelected();
                    this.registrationController.begin(this.selected);
                });
                controls.append(visibleLabel, selectButton);
                controls.append(registerButton);

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
                        this.clearFullGridFit();
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

    private clearFullGridFit(): void {
        this.gridFitRequestGeneration++;
        const preview = this.gridFitPreview;
        if (preview && this.map.renderer.registrationPreview?.sourceMapId === preview.sourceMapId
            && this.map.renderer.registrationPreview.transform === preview.alignment) {
            this.map.renderer.registrationPreview = null;
            this.map.requestRender();
        }
        this.gridFitPreview = null;
        required<HTMLElement>(this.editForm, "[data-playable-fit-preview]").hidden = true;
        required<HTMLButtonElement>(this.editForm, "[data-accept-playable-fit]").disabled = true;
    }

    private gridFitOptions(): {
        observedColumns: number | null;
        observedRows: number | null;
        margins: { leftPercent: number; topPercent: number; rightPercent: number; bottomPercent: number };
    } {
        const value = (selector: string): string =>
            required<HTMLInputElement>(this.editForm, selector).value.trim();
        const columns = value("[data-grid-columns]");
        const rows = value("[data-grid-rows]");
        if (Boolean(columns) !== Boolean(rows))
            throw new Error("Enter both printed grid columns and rows, or leave both blank.");
        const count = (raw: string): number | null => {
            if (!raw) return null;
            const n = Number(raw);
            if (!Number.isSafeInteger(n) || n < 1)
                throw new Error("Printed grid dimensions must be positive whole numbers.");
            return n;
        };
        const margin = (selector: string): number => {
            const n = Number(value(selector));
            if (!Number.isFinite(n) || n < 0 || n >= 100)
                throw new Error("Grid margins must be percentages between 0 and 100 (exclusive).");
            return n;
        };
        const margins = {
            leftPercent: margin("[data-grid-margin-left]"),
            topPercent: margin("[data-grid-margin-top]"),
            rightPercent: margin("[data-grid-margin-right]"),
            bottomPercent: margin("[data-grid-margin-bottom]")
        };
        if (margins.leftPercent + margins.rightPercent >= 100
            || margins.topPercent + margins.bottomPercent >= 100)
            throw new Error("The specified margins leave no image area for the printed grid.");
        return { observedColumns: count(columns), observedRows: count(rows), margins };
    }

    private async previewFullGridFit(): Promise<void> {
        const source = this.selected;
        if (!source) throw new Error("Select a map image before fitting it.");
        this.clearFullGridFit();
        // A detected-grid candidate has its own preview/accept controls.
        // Never leave those controls active after swapping the overlay to
        // an independently computed full-board fit.
        this.compatibilityGeneration++;
        const assessment = required<HTMLElement>(this.editForm, "[data-pattern-assessment]");
        assessment.hidden = true;
        assessment.replaceChildren();
        assessment.dataset.sourceMapId = "";
        required<HTMLButtonElement>(this.editForm, "[data-check-pattern]").disabled = false;
        const requestGeneration = this.gridFitRequestGeneration;
        const world = this.getWorld();
        const options = this.gridFitOptions();
        const candidate = await this.api.previewPlayableGridFit(world.id, source.id, options);
        if (this.disposed || requestGeneration !== this.gridFitRequestGeneration
            || this.selected?.id !== source.id
            || this.getWorld().version !== candidate.worldVersion)
            throw new Error("The source map or its grid changed. Reload and preview again.");
        this.registrationController.cancelActive();
        // Remove any previous motif candidate overlay before presenting a
        // mismatch or a new fit. A stale overlay must never imply that an
        // incompatible grid was successfully positioned.
        if (this.map.renderer.registrationPreview) {
            this.map.renderer.registrationPreview = null;
            this.map.requestRender();
        }
        this.ensureVisibleForEditing(source);
        this.gridFitPreview = null;
        required<HTMLElement>(this.editForm, "[data-playable-fit-description]").textContent =
            candidate.note;
        required<HTMLElement>(this.editForm, "[data-playable-fit-preview]").hidden = false;
        required<HTMLButtonElement>(this.editForm, "[data-accept-playable-fit]").disabled =
            candidate.status !== "reported-match" || candidate.alignment === null;
        if (candidate.alignment !== null) {
            this.map.renderer.registrationPreview = {
                sourceMapId: source.id, transform: candidate.alignment
            };
            this.map.requestRender();
            this.gridFitPreview = {
                sourceMapId: source.id,
                worldVersion: candidate.worldVersion,
                alignment: candidate.alignment,
                observedColumns: options.observedColumns,
                observedRows: options.observedRows,
                margins: options.margins
            };
            this.mapHint.textContent =
                "Check the printed cell boundaries against the playable grid inside the chosen margins. The source image is not cropped.";
        } else {
            this.mapHint.textContent =
                "The reported printed grid dimensions conflict with the playable map. Read the explanation and correct the map or dimensions.";
        }
    }

    private async acceptFullGridFit(): Promise<void> {
        const preview = this.gridFitPreview;
        const world = this.getWorld();
        if (!preview || preview.observedColumns === null || preview.observedRows === null
            || !this.selected || this.selected.id !== preview.sourceMapId
            || world.version !== preview.worldVersion
            || this.map.renderer.registrationPreview?.sourceMapId !== preview.sourceMapId
            || this.map.renderer.registrationPreview.transform !== preview.alignment)
            throw new Error("The grid-fit preview is no longer current. Preview it again.");
        const saved = await this.api.savePlayableGridFit(
            world.id, preview.sourceMapId, preview.worldVersion, {
                observedColumns: preview.observedColumns,
                observedRows: preview.observedRows,
                margins: preview.margins
            });
        this.clearFullGridFit();
        this.applyWorld(saved);
        await this.refresh();
        this.mapHint.textContent =
            "Grid image fit saved with the declared margins. The original raster was preserved. Manual placement remains available.";
    }

    private renderSelected(): void {
        this.editForm.hidden = !this.selected;
        if (this.gridFitPreview
            && (this.gridFitPreview.sourceMapId !== this.selected?.id
                || this.gridFitPreview.worldVersion !== this.getWorld().version
                || this.map.renderer.registrationPreview?.sourceMapId !== this.gridFitPreview.sourceMapId
                || this.map.renderer.registrationPreview.transform !== this.gridFitPreview.alignment))
            this.clearFullGridFit();
        if (!this.selected) return;
        const assessment = required<HTMLElement>(this.editForm, "[data-pattern-assessment]");
        if (assessment.dataset.sourceMapId !== this.selected.id) {
            if (this.map.renderer.registrationPreview?.sourceMapId === assessment.dataset.sourceMapId) {
                this.map.renderer.registrationPreview = null;
                this.map.requestRender();
            }
            assessment.hidden = true;
            assessment.replaceChildren();
            assessment.dataset.sourceMapId = this.selected.id;
        }
        const bounds = this.getWorld().tiling?.playableBounds;
        const fitButton = required<HTMLButtonElement>(this.editForm, "[data-fit-playable-grid]");
        fitButton.disabled = !bounds;
        fitButton.title = bounds
            ? `Preview fitting the image's printed grid to the ${bounds.width}×${bounds.height} playable period grid. Borders may be excluded using margins.`
            : "Set finite world map bounds first. Unbounded worlds cannot fit the whole raster to a board.";
        input(this.editForm, "name").value = this.selected.name;
        this.editGeographySelect.value = this.selected.geographyKey;
        select(this.editForm, "role").value = this.selected.role;
        input(this.editForm, "bakedGrid").checked = this.selected.containsBakedGrid;
        required<HTMLElement>(this.editForm, "[data-source-map-selected-meta]").textContent =
            `${this.selected.pixelWidth}×${this.selected.pixelHeight} ${this.selected.mediaType}; ${this.selected.originalFileName ?? "original filename unavailable"}; ${this.selected.alignment ? "aligned" : "shown with temporary placement until aligned"}; ${this.selected.importedContentCount ?? 0} imported source records.`;
    }

    private async assessPattern(sourceMap: SourceMapDetail | null): Promise<void> {
        if (!sourceMap) throw new Error("Select a reference map first.");
        this.clearFullGridFit();
        const generation = ++this.compatibilityGeneration;
        const button = required<HTMLButtonElement>(this.editForm, "[data-check-pattern]");
        const status = required<HTMLElement>(this.editForm, "[data-pattern-assessment]");
        const selectedWorld = this.getWorld();
        status.hidden = false;
        status.dataset.sourceMapId = sourceMap.id;
        status.textContent = "Checking the image against the saved world geometry…";
        button.disabled = true;
        try {
            // A world may exist before any ruleset. Only a currently selected
            // pinned saved revision adds a procedural constraint to this preview.
            const selectedProcedure = this.host.closest(".hc-world-editor")
                ?.querySelector<HTMLSelectElement>('[data-expedition-form] select[name="procedure"]')?.value;
            let pinned: { id: string; revision: number } | undefined;
            if (selectedProcedure?.startsWith("saved:")) {
                const encoded = selectedProcedure.slice("saved:".length);
                const separator = encoded.lastIndexOf(":");
                const revision = Number(encoded.slice(separator + 1));
                if (separator > 0 && Number.isSafeInteger(revision) && revision > 0) {
                    pinned = { id: encoded.slice(0, separator), revision };
                }
            }
            const assessment = await this.api.assessSourceMapCompatibility(
                selectedWorld.id, sourceMap.id, pinned);
            if (this.disposed || generation !== this.compatibilityGeneration
                || this.selected?.id !== sourceMap.id) return;
            if (this.getWorld().version !== assessment.worldVersion) {
                status.textContent = "The world changed during analysis. Reload and check again.";
                return;
            }
            status.replaceChildren();
            const result = document.createElement("p");
            result.textContent = assessment.explanation;
            status.append(result);
            if (pinned) {
                const rule = document.createElement("p");
                rule.className = "hc-hint";
                rule.textContent = `Checked against saved exploration ruleset revision ${pinned.revision}.`;
                status.append(rule);
            }
            if (assessment.blockers.length) {
                const warning = document.createElement("p");
                warning.textContent = "Changing world geometry would also affect:";
                const list = document.createElement("ul");
                for (const blocker of assessment.blockers) {
                    const item = document.createElement("li");
                    item.textContent = blocker;
                    list.append(item);
                }
                status.append(warning, list);
            }
            const next = document.createElement("p");
            next.className = "hc-hint";
            next.textContent = assessment.registration?.status === "candidates"
                ? assessment.status === "structure-match"
                    ? "The image matches the saved world pattern. Preview the verified placement before saving it."
                    : "The image can be registered to the unchanged world below. The selected exploration ruleset remains incompatible or unverified and must be resolved separately before starting an expedition."
                : assessment.status === "structure-match"
                    ? "Existing world geometry is unchanged. Review registration or use manual placement before accepting map alignment."
                : assessment.status === "mismatch"
                    ? assessment.canCorrectInPlace
                        ? "This world has no conflicting spatial data, so you may use the verified correction option below."
                        : "Keep this world configuration and use another map, or create a separate world. Existing spatial data and rulesets will not be converted automatically."
                    : "Use manual placement or a different image. No world, procedure, or map registration was changed.";
            status.append(next);
            if (assessment.status === "mismatch" && assessment.canCorrectInPlace
                && assessment.correction?.status === "candidates"
                && assessment.correction.candidates.length) {
                const correction = assessment.correction;
                const heading = document.createElement("p");
                heading.textContent =
                    `This unused world can safely use the observed ${assessment.observedShape ?? "cell pattern"} instead. The original map images and saved world identity will be preserved. Cell identities will change.`;
                const controls = document.createElement("div");
                controls.className = "hc-form";
                const selector = document.createElement("select");
                selector.setAttribute("aria-label", "Choose the detected replacement placement");
                for (const candidate of correction.candidates) {
                    const option = document.createElement("option");
                    option.value = String(candidate.index);
                    option.textContent = `Placement ${candidate.index + 1} — ${candidate.maximumResidualSourcePixels.toFixed(1)} image-pixel maximum error`;
                    selector.append(option);
                }
                const hint = document.createElement("p");
                hint.className = "hc-hint";
                hint.textContent =
                    "The map preview still shows the OLD cell boundaries. The detected replacement cells will appear after acceptance. Multiple placements can be geometrically equivalent; choose the orientation by the map image and its geography.";
                const preview = document.createElement("button");
                preview.type = "button";
                preview.textContent = "Preview replacement image placement";
                const apply = document.createElement("button");
                apply.type = "button";
                apply.className = "hc-primary-action";
                apply.textContent = `Use ${assessment.observedShape ?? "these cells"} for this unused world`;
                apply.disabled = true;
                let previewedIndex: number | null = null;
                selector.addEventListener("change", () => {
                    previewedIndex = null;
                    apply.disabled = true;
                    if (this.map.renderer.registrationPreview?.sourceMapId === sourceMap.id) {
                        this.map.renderer.registrationPreview = null;
                        this.map.requestRender();
                    }
                });
                preview.addEventListener("click", () => {
                    if (this.disposed || generation !== this.compatibilityGeneration
                        || this.selected?.id !== sourceMap.id
                        || this.getWorld().version !== assessment.worldVersion) return;
                    const chosen = correction.candidates.find(item =>
                        item.index === Number(selector.value));
                    if (!chosen) return;
                    this.registrationController.cancelActive();
                    this.ensureVisibleForEditing(sourceMap);
                    this.map.renderer.registrationPreview = {
                        sourceMapId: sourceMap.id, transform: chosen.alignment
                    };
                    this.map.requestRender();
                    previewedIndex = chosen.index;
                    apply.disabled = false;
                });
                apply.addEventListener("click", () => {
                    if (previewedIndex === null) return;
                    const chosen = correction.candidates.find(item => item.index === previewedIndex);
                    if (!chosen) return;
                    const confirmed = window.confirm(
                        `Use ${assessment.observedShape ?? "the observed pattern"} as this world's cells?\n\nThis is only available for an unused world. The existing cell tiling will be replaced with a new verified identity, and the selected image placement will be saved. Original image assets are preserved.\n\nThe current cell outlines shown in the preview are the old geometry.`);
                    if (!confirmed) return;
                    apply.disabled = true;
                    void this.run(null, async () => {
                        if (this.disposed || generation !== this.compatibilityGeneration
                            || this.selected?.id !== sourceMap.id
                            || this.getWorld().version !== assessment.worldVersion)
                            throw new Error("The world changed. Check the image again before correcting its cells.");
                        const corrected = await this.api.applyMotifCorrection(
                            selectedWorld.id, sourceMap.id, chosen.index,
                            chosen.alignment, assessment.worldVersion, pinned);
                        this.map.renderer.registrationPreview = null;
                        this.applyWorld(corrected);
                        status.textContent =
                            "Unused world cells were replaced with the verified observed pattern. Existing map assets remain intact.";
                        this.mapHint.textContent =
                            "New world cells are now active; inspect the map and choose your starting cell.";
                        await this.refresh();
                        this.map.requestRender();
                    });
                });
                controls.append(heading, selector, preview, apply, hint);
                status.append(controls);
            }
            // Registration to unchanged world geometry is independent of
            // whether an optional selected ruleset may run there.
            if (assessment.registration?.status === "candidates") {
                const registration = assessment.registration;
                const detail = document.createElement("p");
                detail.className = "hc-hint";
                detail.textContent = registration.explanation;
                status.append(detail);
                if (registration.status === "candidates" && registration.candidates.length) {
                    const controls = document.createElement("div");
                    controls.className = "hc-form";
                    const selector = document.createElement("select");
                    selector.setAttribute("aria-label", "Choose verified image placement");
                    for (const candidate of registration.candidates) {
                        const option = document.createElement("option");
                        option.value = String(candidate.index);
                        option.textContent = `Placement ${candidate.index + 1} — maximum motif disagreement ${candidate.maximumResidualSourcePixels.toFixed(1)} image pixels`;
                        selector.append(option);
                    }
                    const preview = document.createElement("button");
                    preview.type = "button";
                    preview.textContent = "Preview placement";
                    const accept = document.createElement("button");
                    accept.type = "button";
                    accept.className = "hc-primary-action";
                    accept.textContent = "Use this placement";
                    accept.disabled = true;
                    const previewHint = document.createElement("p");
                    previewHint.className = "hc-hint";
                    previewHint.textContent =
                        "Preview a candidate against the reference image. Acceptance changes this image's placement only, not the world, its cells, or any saved ruleset.";
                    let previewedIndex: number | null = null;
                    const clearPreview = (): void => {
                        previewedIndex = null;
                        accept.disabled = true;
                        if (this.map.renderer.registrationPreview?.sourceMapId === sourceMap.id) {
                            this.map.renderer.registrationPreview = null;
                            this.map.requestRender();
                        }
                    };
                    selector.addEventListener("change", clearPreview);
                    preview.addEventListener("click", () => {
                        if (this.disposed || generation !== this.compatibilityGeneration
                            || this.selected?.id !== sourceMap.id
                            || this.getWorld().version !== assessment.worldVersion) return;
                        const chosen = registration.candidates.find(item =>
                            item.index === Number(selector.value));
                        if (!chosen) return;
                        this.registrationController.cancelActive();
                        this.ensureVisibleForEditing(sourceMap);
                        this.map.renderer.registrationPreview = {
                            sourceMapId: sourceMap.id, transform: chosen.alignment
                        };
                        this.map.requestRender();
                        previewedIndex = chosen.index;
                        accept.disabled = false;
                        previewHint.textContent =
                            "Check the entire overlay, especially distant features and cell boundaries. Choose Use this placement only if it matches.";
                    });
                    accept.addEventListener("click", () => {
                        if (previewedIndex === null) return;
                        const chosen = registration.candidates.find(item => item.index === previewedIndex);
                        if (!chosen) return;
                        accept.disabled = true;
                        void this.run(null, async () => {
                            if (this.disposed || generation !== this.compatibilityGeneration
                                || this.selected?.id !== sourceMap.id
                                || this.getWorld().version !== assessment.worldVersion)
                                throw new Error("The map or world changed. Check the image again before accepting placement.");
                            const saved = await this.api.applyMotifRegistration(
                                selectedWorld.id, sourceMap.id, chosen.index, chosen.alignment,
                                assessment.worldVersion);
                            clearPreview();
                            this.applyWorld(saved);
                            status.textContent =
                                "The selected image placement was saved. The existing world geometry and rulesets were preserved.";
                            this.mapHint.textContent =
                                "Detected image alignment saved. You can still repair it using manual placement.";
                            await this.refresh();
                        });
                    });
                    controls.append(selector, preview, accept, previewHint);
                    status.append(controls);
                }
            }
        } catch (failure) {
            if (!this.disposed && generation === this.compatibilityGeneration
                && this.selected?.id === sourceMap.id) {
                status.textContent =
                    "Automatic pattern analysis failed. Existing world geometry was not changed. "
                    + "Use manual placement or retry.";
            }
            throw failure;
        } finally {
            if (!this.disposed && generation === this.compatibilityGeneration)
                button.disabled = false;
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
        // A single grid-bearing reference for an explicitly bounded world
        // gets an immediate, unsaved alignment preview. Never infer the count
        // of printed tiles or commit geometry solely from raster dimensions.
        if (uploadedSourceMaps.length === 1 && this.selected?.containsBakedGrid
            && this.getWorld().tiling?.playableBounds) {
            try {
                await this.previewFullGridFit();
            } catch (failure) {
                const reason = failure instanceof Error ? failure.message : String(failure);
                this.mapHint.textContent =
                    `Grid-fit preview was not available: ${reason} Use verified grid detection or manual placement.`;
            }
        }
        if (uploadedSourceMaps.length > 0 && !this.gridFitPreview) {
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
