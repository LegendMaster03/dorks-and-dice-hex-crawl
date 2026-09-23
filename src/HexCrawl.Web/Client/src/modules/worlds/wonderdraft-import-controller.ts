import type {
    HexCrawlApi,
    WonderdraftCandidate,
    WonderdraftCandidatePreview,
    WonderdraftImportSelection,
    WonderdraftInspection,
    WonderdraftSourceImportResult
} from "../../api";
import type { MapSurface } from "../../map-surface";
import type { Overworld, SourceMapDetail } from "../../types";
import { input, required, select } from "../../ui/dom";

type ReviewMode = "Suggested" | "All" | WonderdraftCandidate["sourceKind"];
type CandidateDraft = {
    target: "Skip" | WonderdraftImportSelection["target"];
    name: string;
    category: string;
    discoverability: "Obvious" | "Hidden" | "Conditional";
};

export class WonderdraftImportController {
    private preview: WonderdraftCandidatePreview | null = null;
    private readonly drafts = new Map<string, CandidateDraft>();
    private readonly form: HTMLFormElement;
    private readonly result: HTMLElement;
    private readonly sourceMapSelect: HTMLSelectElement;

    public constructor(
        host: HTMLDetailsElement,
        private readonly api: HexCrawlApi,
        private readonly map: MapSurface,
        private readonly getWorld: () => Overworld,
        private readonly applyWorld: (world: Overworld) => void,
        private readonly runMutation: (
            form: HTMLFormElement | null,
            action: () => Promise<void>) => void,
        private readonly onImported: () => Promise<void>) {
        this.form = required(host, "[data-wonderdraft-inspect]");
        this.result = required(host, "[data-wonderdraft-result]");
        this.sourceMapSelect = select(this.form, "sourceMap");

        this.form.addEventListener("submit", event => {
            event.preventDefault();
            this.runMutation(this.form, () => this.inspectOrImport());
        });
        this.map.setReviewSelectionHandler(id => this.selectReviewCandidate(id));
    }

    public setSourceMaps(sourceMaps: readonly SourceMapDetail[]): void {
        const previous = this.sourceMapSelect.value;
        const available = sourceMaps.filter(map => map.pixelWidth > 0 && map.pixelHeight > 0);
        this.sourceMapSelect.replaceChildren();

        const inspectionOnly = document.createElement("option");
        inspectionOnly.value = "";
        inspectionOnly.textContent = available.length > 0
            ? "Inspect only — do not attach to a raster"
            : "No raster maps available";
        this.sourceMapSelect.append(inspectionOnly);

        for (const map of available) {
            const option = document.createElement("option");
            option.value = map.id;
            option.textContent =
                `${map.name} — ${map.geographyKey} — ${map.alignment ? "placed" : "automatic placement if scale permits"}`;
            this.sourceMapSelect.append(option);
        }

        if (available.some(map => map.id === previous)) {
            this.sourceMapSelect.value = previous;
        }
    }

    public dispose(): void {
        this.preview = null;
        this.drafts.clear();
        this.map.renderer.reviewOverlay = null;
        this.map.setReviewSelectionHandler(null);
        this.map.requestRender();
    }

    private async inspectOrImport(): Promise<void> {
        const file = input(this.form, "file").files?.[0];
        if (!file) throw new Error("Choose a .wonderdraft_map project file.");

        const world = this.getWorld();
        const sourceMapId = this.sourceMapSelect.value;
        this.preview = null;
        this.drafts.clear();
        this.map.renderer.reviewOverlay = null;
        this.map.requestRender();

        if (!sourceMapId) {
            const inspection = await this.api.inspectWonderdraftProject(world.id, file);
            this.renderInspection(inspection, file.name, null, null);
            return;
        }

        const imported = await this.api.importWonderdraftSource(
            world.id,
            sourceMapId,
            file,
            world.version);
        this.applyWorld(imported.world);
        await this.onImported();

        if (imported.registrationMode === "SourceOnly") {
            this.renderInspection(imported.summary, file.name, null, imported);
            return;
        }

        const preview = await this.api.previewWonderdraftCandidates(
            imported.world.id,
            sourceMapId,
            file);
        this.preview = preview;
        this.renderInspection(preview.summary, file.name, preview, imported);
    }

    private renderInspection(
        inspection: WonderdraftInspection,
        fileName: string,
        preview: WonderdraftCandidatePreview | null,
        imported: WonderdraftSourceImportResult | null): void {
        this.result.replaceChildren();

        const heading = document.createElement("strong");
        heading.textContent = fileName;
        const dimensions = document.createElement("p");
        dimensions.className = "hc-hint";
        dimensions.textContent =
            `${inspection.pixelWidth}×${inspection.pixelHeight} · Wonderdraft format ${inspection.formatVersion ?? "unknown"} · ${inspection.hasGrid ? "grid metadata present" : "no grid metadata"}`;
        const content = document.createElement("p");
        content.className = "hc-hint";
        content.textContent =
            `${inspection.labelCount} labels · ${inspection.symbolCount} symbols · ${inspection.pathCount} paths · ${inspection.territoryCount} territories`;

        this.result.append(heading, dimensions, content);

        if (inspection.physicalScale) {
            const scale = inspection.physicalScale;
            const scaleText = document.createElement("p");
            scaleText.className = "hc-hint";
            scaleText.textContent =
                `Wonderdraft physical scale: ${formatNumber(scale.distancePerSegment)} ${scale.unitLabel} × ${scale.segmentCount} segments over ${formatNumber(scale.pixelLength)} project pixels (${scale.unitsPerPixel.toPrecision(6)} ${scale.unitLabel}/pixel).`;
            this.result.append(scaleText);
        }

        const packs = [...inspection.includedDefaultPacks, ...inspection.includedPacks];
        if (packs.length > 0) {
            const packText = document.createElement("p");
            packText.className = "hc-hint";
            packText.textContent = `Referenced asset packs: ${packs.join(", ")}`;
            this.result.append(packText);
        }

        if (imported) {
            const sourceStatus = document.createElement("p");
            sourceStatus.className = "hc-hint";
            sourceStatus.textContent =
                `Imported ${imported.sourceRecordCount} source records without promoting decorative cartography to world truth. ${imported.registrationNote ?? ""}`.trim();
            this.result.append(sourceStatus);
        } else {
            const note = document.createElement("p");
            note.className = "hc-hint";
            note.textContent =
                "Inspection only. Select the matching raster map to preserve source records and determine project-to-world placement.";
            this.result.append(note);
        }

        if (preview) {
            const scale = document.createElement("p");
            scale.className = "hc-hint";
            scale.textContent =
                `Project coordinates map to the selected raster at ×${preview.sourceScaleX.toFixed(4)} X and ×${preview.sourceScaleY.toFixed(4)} Y. Semantic promotion below is optional; source content is already retained.`;
            this.result.append(scale);
            this.renderCandidates(preview.candidates);
        } else if (imported?.registrationMode === "SourceOnly") {
            const placement = document.createElement("p");
            placement.className = "hc-hint";
            placement.textContent =
                "The project and raster are locked together, but their absolute world placement is still unknown. Use advanced map registration once for the whole raster; individual Wonderdraft records do not need registration.";
            this.result.append(placement);
        }

        if (!imported) {
            const persistence = document.createElement("p");
            persistence.className = "hc-hint";
            persistence.textContent =
                "No world data was changed and the Wonderdraft project was not persisted.";
            this.result.append(persistence);
        }
        this.result.hidden = false;
    }

    private renderCandidates(candidates: WonderdraftCandidate[]): void {
        const supported = candidates.filter(candidate => !candidate.problem).length;
        const summary = document.createElement("p");
        summary.className = "hc-hint";
        summary.textContent =
            `${supported} of ${candidates.length} source records have supported geometry. They are already preserved with the source map. Use this workspace only to promote records that should become semantic locations or features.`;
        this.result.append(summary);

        const controls = document.createElement("div");
        controls.className = "hc-inline";

        const modeLabel = document.createElement("label");
        modeLabel.append(document.createTextNode("Show "));
        const mode = document.createElement("select");
        for (const [label, value] of [
            ["Suggested semantic review", "Suggested"],
            ["All source records", "All"],
            ["Labels", "Label"],
            ["Symbols", "Symbol"],
            ["Paths", "Path"],
            ["Territories", "Territory"]
        ] as const) {
            mode.append(option(label, value));
        }
        modeLabel.append(mode);

        const symbolGroups = summarizeSymbolGroups(candidates);
        const groupLabel = document.createElement("label");
        groupLabel.append(document.createTextNode("Symbol group "));
        const group = document.createElement("select");
        group.append(option("All symbol groups", ""));
        for (const [name, count] of symbolGroups) {
            group.append(option(`${humanizeSourceGroup(name)} (${count})`, name));
        }
        groupLabel.append(group);

        const searchLabel = document.createElement("label");
        searchLabel.append(document.createTextNode("Search "));
        const search = document.createElement("input");
        search.type = "search";
        search.placeholder = "Name, texture, type, style, metadata";
        searchLabel.append(search);

        controls.append(modeLabel, groupLabel, searchLabel);
        this.result.append(controls);

        if (symbolGroups.length > 0) {
            const groupSummary = document.createElement("p");
            groupSummary.className = "hc-hint";
            groupSummary.textContent =
                `Symbol groups: ${symbolGroups.slice(0, 8)
                    .map(([name, count]) => `${humanizeSourceGroup(name)} ${count}`)
                    .join(" · ")}${symbolGroups.length > 8 ? " · …" : ""}`;
            this.result.append(groupSummary);
        }

        const status = document.createElement("p");
        status.className = "hc-hint";
        const pageHost = document.createElement("div");
        const pager = document.createElement("div");
        pager.className = "hc-button-row";
        const previous = document.createElement("button");
        previous.type = "button";
        previous.textContent = "Previous";
        const next = document.createElement("button");
        next.type = "button";
        next.textContent = "Next";
        pager.append(previous, next);

        this.result.append(status, pageHost, pager);

        const pageSize = 50;
        let page = 0;
        const renderPage = (): void => {
            const reviewMode = mode.value as ReviewMode;
            const groupApplies = ["Suggested", "All", "Symbol"].includes(reviewMode);
            groupLabel.hidden = !groupApplies;
            const selectedGroup = groupApplies ? group.value : "";
            const query = search.value.trim().toLocaleLowerCase();
            const filtered = candidates.filter(candidate => {
                if (reviewMode === "Suggested" && !isSuggestedSemanticReview(candidate)) return false;
                if (reviewMode !== "Suggested" && reviewMode !== "All" && candidate.sourceKind !== reviewMode) return false;
                if (selectedGroup && symbolSourceGroup(candidate) !== selectedGroup) return false;
                if (!query) return true;
                return candidateSearchText(candidate).includes(query);
            });
            const pageCount = Math.max(1, Math.ceil(filtered.length / pageSize));
            page = Math.min(page, pageCount - 1);
            const start = page * pageSize;
            const visible = filtered.slice(start, start + pageSize);
            this.map.renderer.reviewOverlay = {
                items: visible
                    .filter(candidate => !candidate.problem)
                    .map(candidate => ({
                        id: candidate.key,
                        kind: candidate.geometryKind,
                        position: candidate.worldPosition,
                        points: candidate.worldPoints
                    })),
                selectedId: null
            };
            this.map.requestRender();

            status.textContent = filtered.length === 0
                ? "No source records match this review filter."
                : `Showing ${start + 1}–${start + visible.length} of ${filtered.length} reviewable source records · page ${page + 1} of ${pageCount}.`;
            previous.disabled = page === 0;
            next.disabled = page >= pageCount - 1;
            pageHost.replaceChildren();

            for (const candidate of visible) {
                pageHost.append(this.renderCandidateRow(candidate, () => {
                    if (!this.map.renderer.reviewOverlay) return;
                    this.map.renderer.reviewOverlay.selectedId = candidate.key;
                    this.map.requestRender();
                }));
            }
        };

        mode.addEventListener("change", () => { page = 0; renderPage(); });
        group.addEventListener("change", () => { page = 0; renderPage(); });
        search.addEventListener("input", () => { page = 0; renderPage(); });
        previous.addEventListener("click", () => { if (page > 0) page--; renderPage(); });
        next.addEventListener("click", () => { page++; renderPage(); });
        renderPage();

        const importButton = document.createElement("button");
        importButton.type = "button";
        importButton.className = "hc-primary-action";
        importButton.textContent = "Promote selected semantic objects";
        importButton.addEventListener("click", () =>
            this.runMutation(this.form, () => this.importCandidates()));
        this.result.append(importButton);
    }

    private renderCandidateRow(
        candidate: WonderdraftCandidate,
        onHighlight: () => void): HTMLElement {
        const row = document.createElement("div");
        row.className = "hc-status-section";
        row.dataset.wonderdraftCandidate = candidate.key;

        const heading = document.createElement("strong");
        heading.textContent = `${candidate.sourceKind}: ${candidate.displayName}`;
        const geometry = document.createElement("p");
        geometry.className = "hc-hint";
        geometry.textContent = candidate.problem
            ? `Source record retained, but semantic promotion is unavailable: ${candidate.problem}`
            : describeGeometry(candidate);
        const highlight = document.createElement("button");
        highlight.type = "button";
        highlight.textContent = "Highlight on map";
        highlight.disabled = !!candidate.problem;
        highlight.addEventListener("click", onHighlight);
        row.append(heading, geometry, highlight);

        if (candidate.descriptor) {
            const descriptor = document.createElement("p");
            descriptor.className = "hc-hint";
            descriptor.textContent = candidate.descriptor;
            row.append(descriptor);
        }

        const metadata = sourceMetadataSummary(candidate);
        if (metadata) {
            const metadataText = document.createElement("p");
            metadataText.className = "hc-hint";
            metadataText.textContent = metadata;
            row.append(metadataText);
        }

        if (candidate.problem) return row;

        const draft = this.drafts.get(candidate.key) ?? {
            target: "Skip",
            name: candidate.displayName,
            category: "",
            discoverability: "Obvious"
        } satisfies CandidateDraft;
        this.drafts.set(candidate.key, draft);

        const targetLabel = document.createElement("label");
        targetLabel.append(document.createTextNode("Semantic role "));
        const target = document.createElement("select");
        target.append(option("Keep as source content only", "Skip"));
        if (candidate.geometryKind === "Point") {
            target.append(option("Location", "Location"), option("Point feature", "PointFeature"));
        } else if (candidate.geometryKind === "Line") {
            target.append(option("Line feature", "LineFeature"));
        } else {
            target.append(option("Region feature", "RegionFeature"));
        }
        target.value = draft.target;
        targetLabel.append(target);

        const fields = document.createElement("div");
        fields.hidden = draft.target === "Skip";

        const nameLabel = document.createElement("label");
        nameLabel.append(document.createTextNode("Name "));
        const name = document.createElement("input");
        name.value = draft.name;
        nameLabel.append(name);

        const categoryLabel = document.createElement("label");
        categoryLabel.append(document.createTextNode("Category "));
        const category = document.createElement("input");
        category.value = draft.category;
        category.placeholder = candidate.geometryKind === "Line"
            ? "road"
            : candidate.geometryKind === "Region"
                ? "region"
                : "settlement";
        categoryLabel.append(category);

        const discoverabilityLabel = document.createElement("label");
        discoverabilityLabel.hidden = draft.target !== "Location";
        discoverabilityLabel.append(document.createTextNode("Discoverability "));
        const discoverability = document.createElement("select");
        discoverability.append(
            option("Obvious", "Obvious"),
            option("Hidden", "Hidden"),
            option("Conditional", "Conditional"));
        discoverability.value = draft.discoverability;
        discoverabilityLabel.append(discoverability);

        const sync = (): void => {
            draft.target = target.value as CandidateDraft["target"];
            draft.name = name.value.trim();
            draft.category = category.value.trim();
            draft.discoverability = discoverability.value as CandidateDraft["discoverability"];
            fields.hidden = draft.target === "Skip";
            discoverabilityLabel.hidden = draft.target !== "Location";
        };
        target.addEventListener("change", sync);
        name.addEventListener("input", sync);
        category.addEventListener("input", sync);
        discoverability.addEventListener("change", sync);

        fields.append(nameLabel, categoryLabel, discoverabilityLabel);
        row.append(targetLabel, fields);
        return row;
    }

    private selectReviewCandidate(id: string): void {
        const overlay = this.map.renderer.reviewOverlay;
        if (!overlay || !overlay.items.some(item => item.id === id)) return;
        overlay.selectedId = id;
        this.map.requestRender();

        for (const row of this.result.querySelectorAll<HTMLElement>("[data-wonderdraft-candidate]")) {
            const selected = row.dataset.wonderdraftCandidate === id;
            row.toggleAttribute("data-selected", selected);
            if (selected) row.scrollIntoView({ block: "nearest" });
        }
    }

    private async importCandidates(): Promise<void> {
        const preview = this.preview;
        if (!preview) {
            throw new Error("Attach the Wonderdraft project to a placed raster map before promoting semantic objects.");
        }
        if (this.sourceMapSelect.value !== preview.sourceMapId) {
            throw new Error(
                "The selected source map changed. Import the Wonderdraft source again before semantic promotion.");
        }

        const file = input(this.form, "file").files?.[0];
        if (!file) {
            throw new Error("Choose the .wonderdraft_map project file again before importing.");
        }

        const selections: WonderdraftImportSelection[] = [];
        for (const [candidateKey, draft] of this.drafts) {
            if (draft.target === "Skip") continue;
            if (!draft.name || !draft.category) {
                throw new Error(
                    "Every semantic promotion requires a name and category. Source-only records require no classification.");
            }
            selections.push({
                candidateKey,
                target: draft.target,
                name: draft.name,
                category: draft.category,
                discoverability: draft.target === "Location" ? draft.discoverability : null
            });
        }

        if (selections.length === 0) {
            throw new Error("Choose at least one source record to promote to world truth.");
        }

        const world = this.getWorld();
        const updated = await this.api.importWonderdraftCandidates(
            world.id,
            preview.sourceMapId,
            file,
            selections,
            world.version);
        this.applyWorld(updated);
        this.preview = null;
        this.drafts.clear();
        this.map.renderer.reviewOverlay = null;
        this.map.requestRender();
        this.result.replaceChildren();

        const success = document.createElement("p");
        success.className = "hc-hint";
        success.textContent =
            `Promoted ${selections.length} Wonderdraft source record${selections.length === 1 ? "" : "s"} to semantic world objects. The complete source import remains attached to the raster map.`;
        this.result.append(success);
        await this.onImported();
    }
}

function option(label: string, value: string): HTMLOptionElement {
    const element = document.createElement("option");
    element.value = value;
    element.textContent = label;
    return element;
}

function isSuggestedSemanticReview(candidate: WonderdraftCandidate): boolean {
    if (candidate.problem) return false;
    if (candidate.sourceKind !== "Symbol") return true;
    const searchable = candidateSearchText(candidate);
    return [
        "settlement",
        "capital",
        "city",
        "town",
        "village",
        "hamlet",
        "castle",
        "fort",
        "gate",
        "ruin",
        "temple",
        "shrine",
        "mine",
        "port",
        "harbor",
        "harbour",
        "bridge"
    ].some(token => searchable.includes(token));
}

function candidateSearchText(candidate: WonderdraftCandidate): string {
    return [
        candidate.displayName,
        candidate.descriptor ?? "",
        ...Object.entries(candidate.properties)
            .flatMap(([key, value]) => [key, value])
    ].join(" ").toLocaleLowerCase();
}

function summarizeSymbolGroups(candidates: readonly WonderdraftCandidate[]): Array<[string, number]> {
    const counts = new Map<string, number>();
    for (const candidate of candidates) {
        const group = symbolSourceGroup(candidate);
        if (!group) continue;
        counts.set(group, (counts.get(group) ?? 0) + 1);
    }
    return [...counts.entries()]
        .sort((left, right) => right[1] - left[1] || left[0].localeCompare(right[0]));
}

function symbolSourceGroup(candidate: WonderdraftCandidate): string {
    if (candidate.sourceKind !== "Symbol") return "";
    const type = sourceProperty(candidate, "type");
    if (type && type.toLocaleLowerCase() !== "symbol") return type;

    const descriptor = candidate.descriptor?.replace(/\\/g, "/") ?? "";
    const parts = descriptor.split("/").filter(Boolean);
    const symbolsIndex = parts.findIndex(part => part.toLocaleLowerCase() === "symbols");
    if (symbolsIndex >= 0 && symbolsIndex + 1 < parts.length) return parts[symbolsIndex + 1];
    const spritesIndex = parts.findIndex(part => part.toLocaleLowerCase() === "sprites");
    if (spritesIndex >= 0 && spritesIndex + 1 < parts.length) return parts[spritesIndex + 1];
    return "other";
}

function sourceProperty(candidate: WonderdraftCandidate, key: string): string | null {
    const match = Object.entries(candidate.properties)
        .find(([propertyKey]) => propertyKey.toLocaleLowerCase() === key.toLocaleLowerCase());
    return match?.[1] ?? null;
}

function sourceMetadataSummary(candidate: WonderdraftCandidate): string | null {
    const keys = ["type", "style", "scale", "rotation", "z_index", "width"];
    const values = keys
        .map(key => {
            const value = sourceProperty(candidate, key);
            return value ? `${key}: ${value}` : null;
        })
        .filter((value): value is string => value !== null);
    return values.length > 0 ? values.join(" · ") : null;
}

function humanizeSourceGroup(value: string): string {
    return value
        .replace(/[_-]+/g, " ")
        .replace(/\b\w/g, match => match.toLocaleUpperCase());
}

function describeGeometry(candidate: WonderdraftCandidate): string {
    if (candidate.geometryKind === "Point") {
        return "Point source record. Use Highlight on map to inspect its placement over the raster.";
    }
    return `${candidate.geometryKind} source record with ${candidate.worldPoints.length} vertices. Use Highlight on map to inspect its geometry over the raster.`;
}

function formatNumber(value: number): string {
    return Number.isInteger(value) ? String(value) : value.toLocaleString(undefined, { maximumFractionDigits: 6 });
}
