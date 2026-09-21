import type {
    HexCrawlApi,
    WonderdraftCandidate,
    WonderdraftCandidatePreview,
    WonderdraftImportSelection,
    WonderdraftInspection
} from "./api";
import type { Overworld, SourceMapDetail } from "./types";
import { input, required, select } from "./ui/dom";

export class WonderdraftImportController {
    private preview: WonderdraftCandidatePreview | null = null;
    private readonly form: HTMLFormElement;
    private readonly result: HTMLElement;
    private readonly sourceMapSelect: HTMLSelectElement;

    public constructor(
        host: HTMLDetailsElement,
        private readonly api: HexCrawlApi,
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
            this.runMutation(this.form, () => this.inspect());
        });
    }

    public setSourceMaps(sourceMaps: readonly SourceMapDetail[]): void {
        const previous = this.sourceMapSelect.value;
        const registered = sourceMaps.filter(map => map.alignment);
        this.sourceMapSelect.replaceChildren();

        const inspectionOnly = document.createElement("option");
        inspectionOnly.value = "";
        inspectionOnly.textContent = registered.length > 0
            ? "Inspection only — do not map candidates"
            : "No registered source maps available";
        this.sourceMapSelect.append(inspectionOnly);

        for (const map of registered) {
            const option = document.createElement("option");
            option.value = map.id;
            option.textContent = `${map.name} — ${map.geographyKey}`;
            this.sourceMapSelect.append(option);
        }

        if (registered.some(map => map.id === previous)) {
            this.sourceMapSelect.value = previous;
        }
    }

    public dispose(): void {
        this.preview = null;
    }

    private async inspect(): Promise<void> {
        const file = input(this.form, "file").files?.[0];
        if (!file) throw new Error("Choose a .wonderdraft_map project file.");

        const world = this.getWorld();
        const sourceMapId = this.sourceMapSelect.value;
        if (!sourceMapId) {
            this.preview = null;
            const result = await this.api.inspectWonderdraftProject(world.id, file);
            this.renderInspection(result, file.name, null);
            return;
        }

        const preview = await this.api.previewWonderdraftCandidates(world.id, sourceMapId, file);
        this.preview = preview;
        this.renderInspection(preview.summary, file.name, preview);
    }

    private renderInspection(
        inspection: WonderdraftInspection,
        fileName: string,
        preview: WonderdraftCandidatePreview | null): void {
        this.result.replaceChildren();

        const heading = document.createElement("strong");
        heading.textContent = fileName;
        const dimensions = document.createElement("p");
        dimensions.className = "hc-hint";
        dimensions.textContent =
            `${inspection.pixelWidth}×${inspection.pixelHeight} · Wonderdraft format ${inspection.formatVersion ?? "unknown"} · ${inspection.hasGrid ? "grid configured" : "no grid configuration"}`;
        const content = document.createElement("p");
        content.className = "hc-hint";
        content.textContent =
            `${inspection.labelCount} labels · ${inspection.symbolCount} symbols · ${inspection.pathCount} paths · ${inspection.territoryCount} territories`;

        this.result.append(heading, dimensions, content);
        const packs = [...inspection.includedDefaultPacks, ...inspection.includedPacks];
        if (packs.length > 0) {
            const packText = document.createElement("p");
            packText.className = "hc-hint";
            packText.textContent = `Referenced asset packs: ${packs.join(", ")}`;
            this.result.append(packText);
        }

        if (preview) {
            const scale = document.createElement("p");
            scale.className = "hc-hint";
            scale.textContent =
                `Project pixels mapped through the selected raster at ×${preview.sourceScaleX.toFixed(4)} X and ×${preview.sourceScaleY.toFixed(4)} Y before applying its saved registration.`;
            this.result.append(scale);
            this.renderCandidates(preview.candidates);
        } else {
            const note = document.createElement("p");
            note.className = "hc-hint";
            note.textContent =
                "Inspection only. Register a source-map representation and select it above to review candidate geometry in overworld coordinates.";
            this.result.append(note);
        }

        const persistence = document.createElement("p");
        persistence.className = "hc-hint";
        persistence.textContent =
            "No world data was changed and the Wonderdraft project was not persisted.";
        this.result.append(persistence);
        this.result.hidden = false;
    }

    private renderCandidates(candidates: WonderdraftCandidate[]): void {
        const supported = candidates.filter(candidate => !candidate.problem).length;
        const summary = document.createElement("p");
        summary.className = "hc-hint";
        summary.textContent =
            `${supported} of ${candidates.length} records have supported geometry. Every candidate defaults to Skip; choose an explicit semantic target and category to import it.`;
        this.result.append(summary);

        const maximumRendered = 200;
        for (const candidate of candidates.slice(0, maximumRendered)) {
            const row = document.createElement("div");
            row.className = "hc-status-section";
            row.dataset.wonderdraftCandidate = candidate.key;

            const heading = document.createElement("strong");
            heading.textContent = `${candidate.sourceKind}: ${candidate.displayName}`;
            const geometry = document.createElement("p");
            geometry.className = "hc-hint";
            geometry.textContent = candidate.problem
                ? `Not importable yet: ${candidate.problem}`
                : describeGeometry(candidate);
            row.append(heading, geometry);

            if (candidate.descriptor) {
                const descriptor = document.createElement("p");
                descriptor.className = "hc-hint";
                descriptor.textContent = candidate.descriptor;
                row.append(descriptor);
            }

            if (!candidate.problem) {
                const targetLabel = document.createElement("label");
                targetLabel.append(document.createTextNode("Import as "));
                const target = document.createElement("select");
                target.dataset.importTarget = "true";
                target.append(option("Skip", "Skip"));
                if (candidate.geometryKind === "Point") {
                    target.append(option("Location", "Location"), option("Point feature", "PointFeature"));
                } else if (candidate.geometryKind === "Line") {
                    target.append(option("Line feature", "LineFeature"));
                } else {
                    target.append(option("Region feature", "RegionFeature"));
                }
                targetLabel.append(target);

                const fields = document.createElement("div");
                fields.hidden = true;
                fields.dataset.importFields = "true";

                const nameLabel = document.createElement("label");
                nameLabel.append(document.createTextNode("Name "));
                const name = document.createElement("input");
                name.dataset.importName = "true";
                name.value = candidate.displayName;
                nameLabel.append(name);

                const categoryLabel = document.createElement("label");
                categoryLabel.append(document.createTextNode("Category "));
                const category = document.createElement("input");
                category.dataset.importCategory = "true";
                category.placeholder = candidate.geometryKind === "Line"
                    ? "road"
                    : candidate.geometryKind === "Region"
                        ? "region"
                        : "settlement";
                categoryLabel.append(category);

                const discoverabilityLabel = document.createElement("label");
                discoverabilityLabel.hidden = true;
                discoverabilityLabel.dataset.importDiscoverabilityRow = "true";
                discoverabilityLabel.append(document.createTextNode("Discoverability "));
                const discoverability = document.createElement("select");
                discoverability.dataset.importDiscoverability = "true";
                discoverability.append(
                    option("Obvious", "Obvious"),
                    option("Hidden", "Hidden"),
                    option("Conditional", "Conditional"));
                discoverabilityLabel.append(discoverability);

                fields.append(nameLabel, categoryLabel, discoverabilityLabel);
                target.addEventListener("change", () => {
                    fields.hidden = target.value === "Skip";
                    discoverabilityLabel.hidden = target.value !== "Location";
                });
                row.append(targetLabel, fields);
            }

            this.result.append(row);
        }

        if (candidates.length > maximumRendered) {
            const truncated = document.createElement("p");
            truncated.className = "hc-hint";
            truncated.textContent =
                `Showing the first ${maximumRendered} candidates. Additional candidates remain skipped unless reviewed in a smaller project or later paging UI.`;
            this.result.append(truncated);
        }

        const importButton = document.createElement("button");
        importButton.type = "button";
        importButton.className = "hc-primary-action";
        importButton.textContent = "Import selected candidates";
        importButton.addEventListener("click", () =>
            this.runMutation(this.form, () => this.importCandidates()));
        this.result.append(importButton);
    }

    private async importCandidates(): Promise<void> {
        const preview = this.preview;
        if (!preview) {
            throw new Error("Review the Wonderdraft project against a registered source map first.");
        }
        if (this.sourceMapSelect.value !== preview.sourceMapId) {
            throw new Error(
                "The selected source map changed. Review the Wonderdraft project again before importing.");
        }

        const file = input(this.form, "file").files?.[0];
        if (!file) {
            throw new Error("Choose the .wonderdraft_map project file again before importing.");
        }

        const selections: WonderdraftImportSelection[] = [];
        for (const row of this.result.querySelectorAll<HTMLElement>("[data-wonderdraft-candidate]")) {
            const target = row.querySelector<HTMLSelectElement>("[data-import-target]");
            if (!target || target.value === "Skip") continue;

            const name = row.querySelector<HTMLInputElement>("[data-import-name]")?.value.trim() ?? "";
            const category =
                row.querySelector<HTMLInputElement>("[data-import-category]")?.value.trim() ?? "";
            if (!name || !category) {
                throw new Error(
                    "Every selected Wonderdraft candidate requires a name and category.");
            }

            const discoverability =
                row.querySelector<HTMLSelectElement>("[data-import-discoverability]");
            selections.push({
                candidateKey: row.dataset.wonderdraftCandidate!,
                target: target.value as WonderdraftImportSelection["target"],
                name,
                category,
                discoverability: target.value === "Location"
                    ? (discoverability?.value as WonderdraftImportSelection["discoverability"]
                        ?? "Obvious")
                    : null
            });
        }

        if (selections.length === 0) {
            throw new Error("Choose at least one Wonderdraft candidate to import.");
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
        this.result.replaceChildren();

        const success = document.createElement("p");
        success.className = "hc-hint";
        success.textContent =
            `Imported ${selections.length} reviewed Wonderdraft candidate${selections.length === 1 ? "" : "s"} as semantic world objects.`;
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

function describeGeometry(candidate: WonderdraftCandidate): string {
    if (candidate.worldPosition) {
        return `${candidate.geometryKind} at world ${candidate.worldPosition.x.toFixed(3)}, ${candidate.worldPosition.y.toFixed(3)}`;
    }
    return `${candidate.geometryKind} with ${candidate.worldPoints.length} transformed points`;
}
