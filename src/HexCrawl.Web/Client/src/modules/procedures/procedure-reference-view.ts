import { ProcedureComposerApi } from "../../procedure-composer-api";
import type {
    ProcedureReference,
    ProcedureReferenceDiagnostic,
    ProcedureReferenceInput,
    ProcedureReferenceModule,
    ProcedureReferenceParameter
} from "../../procedure-reference-types";
import { showUiError } from "../../ui-error";

export async function renderProcedureReference(
    root: HTMLElement,
    procedureId: string,
    revision: number | null,
    navigate: (route: string, replace?: boolean) => void): Promise<() => void> {
    ensureReferenceStyles();
    root.innerHTML = `<section class="hc-page"><p class="hc-muted">Loading procedure reference…</p></section>`;
    const referenceApi = await ProcedureComposerApi.create(root);
    let disposed = false;

    try {
        const reference = await referenceApi.getReference(procedureId, revision);
        if (!disposed) render(root, reference, navigate);
    } catch (value) {
        if (!disposed) {
            root.innerHTML = `<section class="hc-page hc-procedure-reference"><div class="hc-error" data-error role="alert"></div></section>`;
            const error = root.querySelector<HTMLElement>("[data-error]");
            if (error) showUiError(error, value);
        }
    }

    return () => { disposed = true; };
}

function render(
    root: HTMLElement,
    reference: ProcedureReference,
    navigate: (route: string, replace?: boolean) => void): void {
    root.replaceChildren();
    const page = element("section", "hc-page hc-procedure-reference");
    page.dataset.procedureId = reference.procedureId;
    page.dataset.procedureRevision = String(reference.revision);

    const header = element("header", "hc-page-header hc-reference-header");
    const title = document.createElement("div");
    title.append(
        text("p", "Procedure reference", "hc-reference-kicker"),
        text("h1", reference.name),
        text("p", `Revision ${reference.revision} · ${moduleCount(reference)} modules`));
    const actions = element("nav", "hc-button-row hc-reference-actions");
    const back = button("Back to procedure");
    back.dataset.referenceBack = "";
    back.addEventListener("click", () => navigate(
        `/procedures/${encodeURIComponent(reference.procedureId)}/revisions/${reference.revision}`));
    const print = button("Print reference");
    print.dataset.referencePrint = "";
    print.addEventListener("click", () => window.print());
    actions.append(back, print);
    header.append(title, actions);
    page.append(header, renderSummary(reference));

    const layout = element("div", "hc-reference-layout");
    const index = element("aside", "hc-panel hc-reference-index");
    index.append(text("h2", "Procedure areas"));
    const indexList = document.createElement("ul");
    for (const section of reference.sections) {
        const item = document.createElement("li");
        const link = document.createElement("a");
        link.href = `#reference-${slug(section.name)}`;
        link.textContent = `${section.name} (${section.modules.length})`;
        item.append(link);
        indexList.append(item);
    }
    index.append(indexList);

    const content = document.createElement("main");
    content.className = "hc-reference-content";
    for (const section of reference.sections) {
        const sectionElement = element("section", "hc-reference-section");
        sectionElement.id = `reference-${slug(section.name)}`;
        sectionElement.append(text("h2", section.name));
        for (const module of section.modules) sectionElement.append(renderModule(module));
        content.append(sectionElement);
    }
    layout.append(index, content);
    page.append(layout);
    root.append(page);
}

function renderSummary(reference: ProcedureReference): HTMLElement {
    const summary = element("section", "hc-reference-summary");
    const snapshot = element("section", "hc-panel");
    snapshot.append(
        text("h2", "Procedure snapshot"),
        text("p", reference.isExecutable
            ? "This exact procedure revision currently binds to native execution."
            : "This exact procedure revision includes structural, manual, or unsupported behavior and is not fully native-executable."),
        text("p", `${reference.modifiedModuleCount} modified modules · ${reference.modificationCount} recorded changes.`, "hc-muted"));

    const dependency = element("section", "hc-panel");
    dependency.append(text("h2", "Dependency status"));
    if (reference.dependencies.issues.length === 0) {
        dependency.append(text("p", "All selected module inputs have a selected producer."));
    } else {
        const unresolved = reference.dependencies.issues.filter(issue => issue.kind === "UnresolvedInput").length;
        const blocking = reference.dependencies.issues.filter(issue =>
            issue.kind === "MissingRequiredProducer"
            || issue.kind === "MissingRequiredModule"
            || issue.kind === "IncompatibleMechanic").length;
        dependency.append(
            text("p", `${blocking} blocking · ${unresolved} unresolved input · ${reference.dependencies.issues.length} total diagnostics.`),
            text("p", "Unresolved inputs can remain valid when the stored contract permits DM, provider, or runtime-state sources.", "hc-muted"));
    }

    const origin = element("section", "hc-panel");
    origin.append(text("h2", "Origin"));
    if (!reference.origin) {
        origin.append(text("p", "No origin preset is recorded. The materialized snapshot fully defines this reference.", "hc-muted"));
    } else {
        const name = reference.origin.presetDisplayName ?? reference.origin.presetKey ?? "Recorded preset";
        origin.append(
            text("p", `${name}${reference.origin.presetRevision == null ? "" : ` · preset revision ${reference.origin.presetRevision}`}`),
            text("p", "Origin is provenance only; the procedure sections below come from this saved revision.", "hc-muted"));
        if (reference.origin.attribution) origin.append(text("p", reference.origin.attribution, "hc-muted"));
        if (reference.origin.disclaimer) origin.append(text("p", reference.origin.disclaimer, "hc-muted"));
    }
    summary.append(snapshot, dependency, origin);
    return summary;
}

function renderModule(module: ProcedureReferenceModule): HTMLElement {
    const card = element("article", `hc-panel hc-reference-module${module.isModified ? " is-modified" : ""}`);
    card.dataset.moduleKey = module.moduleKey;
    const heading = element("div", "hc-panel-heading");
    const title = document.createElement("div");
    title.append(text("h3", module.displayName), text("p", module.purpose, "hc-muted"));
    const badges = element("div", "hc-reference-badges");
    badges.append(badge(module.mechanic.automationLevel), badge(executionSupportLabel(module.mechanic.executionSupport)));
    if (module.isModified) badges.append(badge(`Modified ×${module.modificationCount}`));
    heading.append(title, badges);
    card.append(heading);

    const behavior = document.createElement("section");
    behavior.append(
        text("h4", "Selected behavior"),
        text("p", module.mechanic.displayName, "hc-reference-behavior-name"),
        text("p", module.mechanic.description),
        text("p", module.mechanic.executionStatus, "hc-reference-execution"));
    card.append(behavior, renderParameters(module.parameters));

    const contracts = element("div", "hc-reference-contracts");
    contracts.append(renderInputs(module.requiredInputs), renderOutputs(module), renderDiagnostics(module.diagnostics));
    card.append(contracts);

    if (module.isModified) {
        const modifications = document.createElement("section");
        modifications.append(
            text("h4", "Campaign modifications"),
            text("p", `${module.modificationCount} recorded changes target this module. The values above are the current effective snapshot.`));
        if (module.modificationNotes.length > 0) {
            const list = document.createElement("ul");
            for (const note of module.modificationNotes) list.append(listItem(note));
            modifications.append(list);
        }
        card.append(modifications);
    }
    return card;
}

function renderParameters(parameters: ProcedureReferenceParameter[]): HTMLElement {
    const section = document.createElement("section");
    section.append(text("h4", "Parameters"));
    if (parameters.length === 0) {
        section.append(text("p", "No stored parameters.", "hc-muted"));
        return section;
    }
    const list = element("dl", "hc-reference-definition-list");
    for (const parameter of parameters) {
        const term = document.createElement("dt");
        term.append(document.createTextNode(parameter.displayName));
        if (parameter.isUnknown) term.append(badge("Stored unknown parameter"));
        const value = document.createElement("dd");
        if (parameter.mapEntries.length > 0) {
            const table = document.createElement("table");
            table.className = "hc-reference-map";
            const body = document.createElement("tbody");
            for (const entry of parameter.mapEntries) {
                const row = document.createElement("tr");
                row.append(text("th", entry.key), text("td", entry.value || "—"));
                body.append(row);
            }
            table.append(body);
            value.append(table);
        } else if (parameter.listValues.length > 0) {
            const values = document.createElement("ul");
            for (const item of parameter.listValues) values.append(listItem(item));
            value.append(values);
        } else {
            value.append(document.createTextNode(parameter.displayValue || "—"));
        }
        if (parameter.description) value.append(text("p", parameter.description, "hc-muted"));
        if (parameter.technicalDetail) value.append(text("p", parameter.technicalDetail, "hc-reference-technical"));
        list.append(term, value);
    }
    section.append(list);
    return section;
}

function renderInputs(inputs: ProcedureReferenceInput[]): HTMLElement {
    const section = document.createElement("section");
    section.append(text("h4", "Required inputs"));
    if (inputs.length === 0) {
        section.append(text("p", "No required inputs.", "hc-muted"));
        return section;
    }
    const list = document.createElement("dl");
    for (const input of inputs) {
        const details: string[] = [];
        if (input.producerModules.length > 0) details.push(`Selected producer: ${input.producerModules.join(", ")}`);
        if (input.allowedSources.length > 0) details.push(`Allowed sources: ${input.allowedSources.map(source => source.label).join(", ")}`);
        list.append(text("dt", input.displayName), text("dd", details.length > 0 ? details.join(". ") : "Selected module producer only."));
    }
    section.append(list);
    return section;
}

function renderOutputs(module: ProcedureReferenceModule): HTMLElement {
    const section = document.createElement("section");
    section.append(text("h4", "Outputs / state"));
    if (module.outputs.length === 0) {
        section.append(text("p", "No declared outputs.", "hc-muted"));
    } else {
        const list = document.createElement("ul");
        for (const output of module.outputs) list.append(listItem(output.displayName));
        section.append(list);
    }
    return section;
}

function renderDiagnostics(diagnostics: ProcedureReferenceDiagnostic[]): HTMLElement {
    const section = document.createElement("section");
    section.append(text("h4", "Diagnostics"));
    if (diagnostics.length === 0) {
        section.append(text("p", "No dependency diagnostics for this module.", "hc-muted"));
    } else {
        const list = document.createElement("ul");
        for (const diagnostic of diagnostics) {
            const sources = diagnostic.allowedSources.length > 0
                ? ` Allowed sources: ${diagnostic.allowedSources.map(source => source.label).join(", ")}.`
                : "";
            list.append(listItem(`${diagnosticLabel(diagnostic.kind)} — ${diagnostic.message}${sources}`));
        }
        section.append(list);
    }
    return section;
}

function diagnosticLabel(kind: string): string {
    if (kind === "UnresolvedInput") return "Unresolved input";
    if (kind === "MissingRequiredProducer") return "Missing required producer";
    if (kind === "MissingRequiredModule") return "Missing required module";
    if (kind === "IncompatibleMechanic") return "Incompatible mechanic";
    return kind.replace(/([a-z])([A-Z])/g, "$1 $2");
}

function executionSupportLabel(value: string): string {
    if (value === "Native") return "Native execution";
    if (value === "Declarative") return "Structural / declarative";
    return "Unsupported handler/version";
}

function moduleCount(reference: ProcedureReference): number {
    return reference.sections.reduce((count, section) => count + section.modules.length, 0);
}

function badge(label: string): HTMLElement {
    return text("span", label, "hc-reference-badge");
}

function listItem(value: string): HTMLLIElement {
    return text("li", value) as HTMLLIElement;
}

function text(tag: string, value: string, className?: string): HTMLElement {
    const node = document.createElement(tag);
    if (className) node.className = className;
    node.textContent = value;
    return node;
}

function element(tag: string, className: string): HTMLElement {
    const node = document.createElement(tag);
    node.className = className;
    return node;
}

function button(label: string): HTMLButtonElement {
    const node = document.createElement("button");
    node.type = "button";
    node.textContent = label;
    return node;
}

function slug(value: string): string {
    return value.toLowerCase().replace(/[^a-z0-9]+/g, "-").replace(/^-|-$/g, "");
}

function ensureReferenceStyles(): void {
    if (document.getElementById("hc-procedure-reference-styles")) return;
    const style = document.createElement("style");
    style.id = "hc-procedure-reference-styles";
    style.textContent = `
        .hc-procedure-reference { max-width: 1180px; margin: 0 auto; }
        .hc-reference-kicker { margin: 0 0 .2rem; text-transform: uppercase; letter-spacing: .08em; font-size: .78rem; opacity: .7; }
        .hc-reference-summary { display: grid; grid-template-columns: repeat(3, minmax(0, 1fr)); gap: 1rem; margin-bottom: 1rem; }
        .hc-reference-layout { display: grid; grid-template-columns: minmax(190px, 240px) minmax(0, 1fr); gap: 1rem; align-items: start; }
        .hc-reference-index { position: sticky; top: 1rem; }
        .hc-reference-content { min-width: 0; }
        .hc-reference-section { scroll-margin-top: 1rem; }
        .hc-reference-module { break-inside: avoid; margin-bottom: 1rem; }
        .hc-reference-module.is-modified { border-inline-start-width: 4px; }
        .hc-reference-badges { display: flex; flex-wrap: wrap; gap: .4rem; justify-content: flex-end; }
        .hc-reference-badge { display: inline-flex; border: 1px solid currentColor; border-radius: 999px; padding: .15rem .5rem; font-size: .78rem; opacity: .82; margin-left: .35rem; }
        .hc-reference-behavior-name { font-weight: 700; }
        .hc-reference-execution { padding: .65rem .8rem; border-inline-start: 3px solid currentColor; }
        .hc-reference-contracts { display: grid; grid-template-columns: repeat(3, minmax(0, 1fr)); gap: 1rem; margin-top: 1rem; }
        .hc-reference-definition-list { display: grid; grid-template-columns: minmax(150px, .45fr) minmax(0, 1fr); gap: .65rem 1rem; }
        .hc-reference-definition-list dt { font-weight: 700; }
        .hc-reference-definition-list dd { margin: 0; min-width: 0; }
        .hc-reference-map { width: 100%; border-collapse: collapse; }
        .hc-reference-map th, .hc-reference-map td { padding: .25rem .5rem; border-bottom: 1px solid currentColor; text-align: left; vertical-align: top; }
        .hc-reference-map th { width: 40%; font-weight: 600; }
        .hc-reference-technical { font-size: .82rem; opacity: .65; }
        @media (max-width: 820px) {
            .hc-reference-summary, .hc-reference-contracts, .hc-reference-layout { grid-template-columns: 1fr; }
            .hc-reference-index { position: static; }
            .hc-reference-definition-list { grid-template-columns: 1fr; gap: .25rem; }
        }
        @media print {
            .hc-reference-actions, .hc-reference-index, .hc-shell-nav, .hc-shell-header, nav[data-tool-nav] { display: none !important; }
            .hc-procedure-reference { max-width: none; }
            .hc-reference-layout { display: block; }
            .hc-reference-summary { grid-template-columns: repeat(2, minmax(0, 1fr)); }
            .hc-reference-module { box-shadow: none !important; break-inside: avoid-page; }
            .hc-reference-section > h2, .hc-panel-heading, h4 { break-after: avoid; }
            a { color: inherit; text-decoration: none; }
        }
    `;
    document.head.append(style);
}
