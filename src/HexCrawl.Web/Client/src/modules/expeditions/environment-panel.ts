import type { HexCrawlApi } from "../../api";
import type {
    EnvironmentAnnotation,
    EnvironmentFact,
    EnvironmentValueKind,
    EnvironmentWorkbench,
    WorldEnvironment
} from "../../environment-types";
import type { ExpeditionDetail, Overworld } from "../../types";

const knownDimensions = [
    "terrain", "route", "weather", "visibility", "elevation", "depth",
    "water", "current", "temperature", "hazard", "regional-effect"
];

export class ExpeditionEnvironmentPanel {
    private readonly panel: HTMLDetailsElement;
    private readonly body: HTMLDivElement;
    private disposed = false;
    private refreshToken = 0;
    private world: Overworld | null = null;
    private worldEnvironment: WorldEnvironment | null = null;
    private workbench: EnvironmentWorkbench | null = null;

    public constructor(
        root: HTMLElement,
        private readonly api: HexCrawlApi,
        private readonly getRuntime: () => ExpeditionDetail,
        private readonly onRuntimeChanged: (next: ExpeditionDetail) => void,
        private readonly mutate: (control: HTMLButtonElement | null, action: () => Promise<void>) => Promise<void>) {
        const host = root.querySelector<HTMLElement>(".hc-sidebar")
            ?? root.querySelector<HTMLElement>(".hc-columns > .hc-panel:last-child")
            ?? root.querySelector<HTMLElement>(".hc-page");
        if (!host) throw new Error("Expedition environment panel requires a running-sheet host.");

        this.panel = document.createElement("details");
        this.panel.className = "hc-environment-panel";
        this.panel.open = true;
        const summary = document.createElement("summary");
        summary.textContent = "Current environment";
        this.body = document.createElement("div");
        this.body.className = "hc-form";
        this.body.dataset.environmentPanel = "";
        this.panel.append(summary, this.body);
        const partyPanel = host.querySelector(".hc-party-editor-panel");
        if (partyPanel?.nextSibling) host.insertBefore(this.panel, partyPanel.nextSibling);
        else host.append(this.panel);
    }

    public sync(): void {
        void this.refresh();
    }

    public dispose(): void {
        this.disposed = true;
        this.panel.remove();
    }

    private async refresh(): Promise<void> {
        const token = ++this.refreshToken;
        const runtime = this.getRuntime();
        this.body.replaceChildren(hint("Loading environment context…"));
        try {
            const [workbench, world, worldEnvironment] = await Promise.all([
                this.api.getExpeditionEnvironment(runtime.id),
                runtime.overworldId ? this.api.getOverworld(runtime.overworldId) : Promise.resolve(null),
                runtime.overworldId ? this.api.getWorldEnvironment(runtime.overworldId) : Promise.resolve(null)
            ]);
            if (this.disposed || token !== this.refreshToken) return;
            this.workbench = workbench;
            this.world = world;
            this.worldEnvironment = worldEnvironment;
            this.render();
        } catch (error) {
            if (this.disposed || token !== this.refreshToken) return;
            const message = document.createElement("p");
            message.className = "hc-hint";
            message.textContent = error instanceof Error ? error.message : String(error);
            this.body.replaceChildren(message);
        }
    }

    private render(): void {
        if (!this.workbench) return;
        this.body.replaceChildren();
        this.body.append(this.renderEffectiveContext());
        this.body.append(this.renderCurrentEditor());
        if (this.worldEnvironment) this.body.append(this.renderWorldEditor());
    }

    private renderEffectiveContext(): HTMLElement {
        const workbench = this.workbench!;
        const section = document.createElement("section");
        const heading = document.createElement("h4");
        heading.textContent = `Effective context · ${workbench.effectiveContext.status}`;
        section.append(heading);

        const effective = workbench.effectiveContext.facts.filter(item => item.effective);
        if (effective.length === 0) {
            section.append(hint("No current environment facts are defined. This does not prevent manual procedure execution."));
        } else {
            const list = document.createElement("ul");
            list.className = "hc-compact-list";
            for (const item of effective) {
                const row = document.createElement("li");
                row.textContent = `${item.fact.dimension}: ${factValue(item.fact)} · ${sourceLabel(item.source.kind, item.source.featureName)}`;
                list.append(row);
            }
            section.append(list);
        }

        for (const conflict of workbench.effectiveContext.conflicts) {
            const warning = document.createElement("p");
            warning.className = "hc-hint hc-warning";
            warning.textContent = `${conflict.dimension}: ${conflict.detail}`;
            section.append(warning);
        }
        for (const diagnostic of workbench.evaluation.diagnostics) section.append(hint(diagnostic));
        for (const unsupported of workbench.evaluation.unsupportedSemantics) section.append(hint(`Manual interpretation: ${unsupported}`));

        const movement = workbench.movementComposition;
        const movementLine = document.createElement("p");
        movementLine.className = "hc-hint";
        const resolved = movement.effectiveValue === null
            ? movement.status
            : `${movement.effectiveValue}${movement.effectiveUnit ? ` ${movement.effectiveUnit}` : ""}${movement.effectivePerUnit ? `/${movement.effectivePerUnit}` : ""}`;
        movementLine.textContent = `Environment-aware movement: ${resolved}`;
        section.append(movementLine);
        return section;
    }

    private renderCurrentEditor(): HTMLElement {
        const section = document.createElement("section");
        const heading = document.createElement("h4");
        heading.textContent = "Current / temporary conditions";
        section.append(heading, hint("These facts belong to this expedition session. DM overrides supersede lower-authority world/current facts without mutating the world."));

        section.append(this.factRows(
            "Current state",
            this.workbench!.state.currentFacts,
            factId => void this.saveExpedition(
                this.workbench!.state.currentFacts.filter(fact => fact.id !== factId),
                this.workbench!.state.overrides)));
        section.append(this.factRows(
            "DM overrides",
            this.workbench!.state.overrides,
            factId => void this.saveExpedition(
                this.workbench!.state.currentFacts,
                this.workbench!.state.overrides.filter(fact => fact.id !== factId))));

        const form = factForm("Add current environment fact", false);
        const authority = document.createElement("select");
        authority.name = "authority";
        authority.innerHTML = '<option value="current">Current/session state</option><option value="override">DM override</option>';
        const authorityLabel = document.createElement("label");
        authorityLabel.textContent = "Authority";
        authorityLabel.append(authority);
        form.insertBefore(authorityLabel, form.querySelector("button"));
        form.addEventListener("submit", event => {
            event.preventDefault();
            const fact = readFactForm(form);
            if (!fact) return;
            const current = [...this.workbench!.state.currentFacts];
            const overrides = [...this.workbench!.state.overrides];
            if (authority.value === "override") overrides.push(fact);
            else current.push(fact);
            void this.saveExpedition(current, overrides);
        });
        section.append(form);
        return section;
    }

    private renderWorldEditor(): HTMLElement {
        const section = document.createElement("section");
        const heading = document.createElement("h4");
        heading.textContent = "Static world environment";
        section.append(heading, hint("Author persistent world truth explicitly at world, current-hex, or existing spatial-feature scope."));

        const facts = this.worldEnvironment!.annotations.flatMap(annotation =>
            annotation.facts.map(fact => ({ annotation, fact })));
        if (facts.length === 0) section.append(hint("No static environment annotations are authored for this world."));
        else {
            const list = document.createElement("div");
            for (const item of facts) {
                const row = document.createElement("div");
                row.className = "hc-discovery-row";
                const label = document.createElement("span");
                label.textContent = `${scopeLabel(item.annotation)} · ${item.fact.dimension}: ${factValue(item.fact)}`;
                const remove = document.createElement("button");
                remove.type = "button";
                remove.textContent = "Remove";
                remove.addEventListener("click", () => void this.removeWorldFact(item.annotation.id, item.fact.id, remove));
                row.append(label, remove);
                list.append(row);
            }
            section.append(list);
        }

        const form = factForm("Add static world fact", true);
        const scope = form.elements.namedItem("scope") as HTMLSelectElement;
        const feature = form.elements.namedItem("featureId") as HTMLSelectElement;
        const runtime = this.getRuntime();
        if (!runtime.expedition.isSpatial) {
            for (const option of [...scope.options]) if (option.value === "Hex") option.disabled = true;
        }
        feature.innerHTML = '<option value="">Select spatial feature</option>';
        for (const item of this.world?.features ?? []) {
            const option = document.createElement("option");
            option.value = item.id;
            option.textContent = `${item.name} · ${item.kind}`;
            feature.append(option);
        }
        feature.closest("label")!.hidden = scope.value !== "SpatialFeature";
        scope.addEventListener("change", () => {
            feature.closest("label")!.hidden = scope.value !== "SpatialFeature";
        });
        form.addEventListener("submit", event => {
            event.preventDefault();
            const fact = readFactForm(form);
            if (!fact) return;
            const selectedScope = scope.value as "World" | "Hex" | "SpatialFeature";
            const currentHex = runtime.expedition.isSpatial ? runtime.expedition.currentHex : null;
            if (selectedScope === "Hex" && !currentHex) return;
            if (selectedScope === "SpatialFeature" && !feature.value) return;
            const annotation: EnvironmentAnnotation = {
                id: crypto.randomUUID(),
                scope: {
                    kind: selectedScope,
                    hex: selectedScope === "Hex" ? currentHex : null,
                    featureId: selectedScope === "SpatialFeature" ? feature.value : null
                },
                facts: [fact]
            };
            void this.saveWorld([...this.worldEnvironment!.annotations, annotation]);
        });
        section.append(form);
        return section;
    }

    private factRows(title: string, facts: EnvironmentFact[], removeFact: (id: string) => void): HTMLElement {
        const wrapper = document.createElement("div");
        const label = document.createElement("strong");
        label.textContent = title;
        wrapper.append(label);
        if (facts.length === 0) {
            wrapper.append(hint("None."));
            return wrapper;
        }
        for (const fact of facts) {
            const row = document.createElement("div");
            row.className = "hc-discovery-row";
            const text = document.createElement("span");
            text.textContent = `${fact.dimension}: ${factValue(fact)}`;
            const remove = document.createElement("button");
            remove.type = "button";
            remove.textContent = "Remove";
            remove.addEventListener("click", () => removeFact(fact.id));
            row.append(text, remove);
            wrapper.append(row);
        }
        return wrapper;
    }

    private async saveExpedition(currentFacts: EnvironmentFact[], overrides: EnvironmentFact[]): Promise<void> {
        await this.mutate(null, async () => {
            const runtime = this.getRuntime();
            await this.api.updateExpeditionEnvironment(runtime.id, {
                expectedVersion: runtime.version,
                currentFacts,
                overrides
            });
            const next = await this.api.getExpedition(runtime.id);
            this.onRuntimeChanged(next);
        });
    }

    private async saveWorld(annotations: EnvironmentAnnotation[]): Promise<void> {
        await this.mutate(null, async () => {
            const current = this.worldEnvironment;
            if (!current) return;
            this.worldEnvironment = await this.api.replaceWorldEnvironment(current.overworldId, {
                expectedVersion: current.version,
                annotations
            });
            await this.refresh();
        });
    }

    private async removeWorldFact(annotationId: string, factId: string, button: HTMLButtonElement): Promise<void> {
        const annotations = this.worldEnvironment!.annotations
            .map(annotation => annotation.id !== annotationId
                ? annotation
                : { ...annotation, facts: annotation.facts.filter(fact => fact.id !== factId) })
            .filter(annotation => annotation.facts.length > 0);
        await this.mutate(button, async () => {
            const current = this.worldEnvironment;
            if (!current) return;
            this.worldEnvironment = await this.api.replaceWorldEnvironment(current.overworldId, {
                expectedVersion: current.version,
                annotations
            });
            await this.refresh();
        });
    }
}

function factForm(buttonText: string, includeScope: boolean): HTMLFormElement {
    const form = document.createElement("form");
    form.className = "hc-form hc-environment-fact-form";
    const dimensions = knownDimensions.map(value => `<option value="${value}">${value}</option>`).join("");
    form.innerHTML = `
        ${includeScope ? `<label>Scope <select name="scope"><option value="World">World</option><option value="Hex">Current hex</option><option value="SpatialFeature">Spatial feature</option></select></label><label>Feature <select name="featureId"></select></label>` : ""}
        <label>Dimension <input name="dimension" list="hc-environment-dimensions" required><datalist id="hc-environment-dimensions">${dimensions}</datalist></label>
        <label>Value type <select name="valueKind"><option value="Tag">Tag/value</option><option value="Measurement">Measurement</option></select></label>
        <label data-tag-row>Value <input name="tag"></label>
        <label data-measurement-row hidden>Numeric value <input name="measurement" type="number" step="any"></label>
        <label data-unit-row hidden>Unit <input name="unit" placeholder="C, ft, m, knots, custom unit"></label>
        <label>Provenance <input name="provenance" placeholder="optional source/audit note"></label>
        <label>Note <input name="note" placeholder="optional descriptive note"></label>
        <button type="submit">${buttonText}</button>`;
    const kind = form.elements.namedItem("valueKind") as HTMLSelectElement;
    const sync = (): void => {
        const measurement = kind.value === "Measurement";
        form.querySelector<HTMLElement>("[data-tag-row]")!.hidden = measurement;
        form.querySelector<HTMLElement>("[data-measurement-row]")!.hidden = !measurement;
        form.querySelector<HTMLElement>("[data-unit-row]")!.hidden = !measurement;
    };
    kind.addEventListener("change", sync);
    sync();
    return form;
}

function readFactForm(form: HTMLFormElement): EnvironmentFact | null {
    const value = (name: string): string => (form.elements.namedItem(name) as HTMLInputElement | HTMLSelectElement | null)?.value.trim() ?? "";
    const dimension = value("dimension");
    const valueKind = value("valueKind") as EnvironmentValueKind;
    if (!dimension) return null;
    let tag: string | null = null;
    let measurement: { value: number; unit: string } | null = null;
    if (valueKind === "Measurement") {
        const numeric = Number(value("measurement"));
        const unit = value("unit");
        if (!Number.isFinite(numeric) || !unit) return null;
        measurement = { value: numeric, unit };
    } else {
        tag = value("tag");
        if (!tag) return null;
    }
    return {
        id: crypto.randomUUID(),
        dimension,
        valueKind,
        tag,
        measurement,
        provenance: value("provenance") || null,
        note: value("note") || null
    };
}

function factValue(fact: EnvironmentFact): string {
    return fact.valueKind === "Measurement" && fact.measurement
        ? `${fact.measurement.value} ${fact.measurement.unit}`
        : fact.tag ?? "—";
}

function sourceLabel(kind: string, featureName: string | null): string {
    if (kind === "SpatialFeature") return featureName ? `feature ${featureName}` : "spatial feature";
    if (kind === "ExpeditionCurrent") return "current session";
    if (kind === "DmOverride") return "DM override";
    return kind.toLowerCase();
}

function scopeLabel(annotation: EnvironmentAnnotation): string {
    if (annotation.scope.kind === "Hex" && annotation.scope.hex) return `hex ${annotation.scope.hex.q},${annotation.scope.hex.r}`;
    if (annotation.scope.kind === "SpatialFeature") return `feature ${annotation.scope.featureId ?? "unknown"}`;
    return "world";
}

function hint(text: string): HTMLParagraphElement {
    const value = document.createElement("p");
    value.className = "hc-hint";
    value.textContent = text;
    return value;
}
