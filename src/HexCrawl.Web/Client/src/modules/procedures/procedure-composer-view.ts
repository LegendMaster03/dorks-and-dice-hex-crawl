import type { HexCrawlApi } from "../../api";
import { ProcedureComposerApi } from "../../procedure-composer-api";
import type {
    ProcedureComposer,
    ProcedureComposerOverrideInput,
    ProcedureModuleComposer,
    ProcedureParameterDefinition,
    ProcedureRevisionSummary
} from "../../procedure-composer-types";
import type { ProcedurePreset } from "../../types";
import { clearUiError, showUiError } from "../../ui-error";
import {
    executionSummary,
    groupComposerModules,
    inputSourceLabel,
    parameterDefinitions,
    parseKeyListParameter,
    parseMapParameter,
    saveBlocked,
    serializeKeyListParameter,
    serializeMapParameter,
    withBehavior,
    withParameter
} from "./procedure-composer-model";

export async function renderProcedureComposer(
    root: HTMLElement,
    api: HexCrawlApi,
    procedureId: string | null,
    navigate: (route: string, replace?: boolean) => void): Promise<() => void> {
    ensureComposerStyles();

    let disposed = false;
    let draft: ProcedureComposer | null = null;
    let presets: ProcedurePreset[] = [];
    let revisions: ProcedureRevisionSummary[] = [];
    let sourcePresetKey: string | null = null;
    let sourceProcedureId = procedureId;
    let viewedRevision: number | null = null;
    let latestRevision: number | null = null;
    let savePending = false;
    const pending = new Map<string, ProcedureComposerOverrideInput>();

    root.innerHTML = loadingMarkup("Loading Procedure Composer…");
    const composerApi = await ProcedureComposerApi.create(root);
    presets = await api.getProcedurePresets();
    if (disposed) return () => {};

    const load = async (): Promise<void> => {
        if (sourceProcedureId) {
            revisions = await composerApi.listRevisions(sourceProcedureId);
            latestRevision = revisions.reduce(
                (maximum, revision) => Math.max(maximum, revision.revision),
                0) || null;
            draft = viewedRevision == null
                ? await composerApi.getProcedure(sourceProcedureId)
                : await composerApi.getProcedure(sourceProcedureId, viewedRevision);
        } else {
            draft = await composerApi.composeDraft({
                presetKey: sourcePresetKey,
                overrides: [...pending.values()]
            });
        }
        if (!disposed) render();
    };

    const refreshDraft = async (): Promise<void> => {
        if (!draft) return;
        root.querySelectorAll<HTMLInputElement | HTMLSelectElement | HTMLTextAreaElement | HTMLButtonElement>(
            "[data-composer-control]").forEach(control => { control.disabled = true; });
        try {
            draft = sourceProcedureId
                ? await composerApi.composeDraft({
                    procedureId: sourceProcedureId,
                    overrides: [...pending.values()]
                })
                : await composerApi.composeDraft({
                    presetKey: sourcePresetKey,
                    overrides: [...pending.values()]
                });
            if (!disposed) render();
        } catch (value) {
            const error = root.querySelector<HTMLElement>("[data-error]");
            if (error) showUiError(error, value);
            root.querySelectorAll<HTMLInputElement | HTMLSelectElement | HTMLTextAreaElement | HTMLButtonElement>(
                "[data-composer-control]").forEach(control => { control.disabled = false; });
        }
    };

    const historical = (): boolean =>
        sourceProcedureId !== null
        && viewedRevision !== null
        && latestRevision !== null
        && viewedRevision !== latestRevision;

    const render = (): void => {
        if (!draft) return;
        const current = draft;
        const isHistorical = historical();
        root.innerHTML = `
            <section class="hc-page hc-composer">
                <header class="hc-page-header">
                    <div>
                        <h1>Procedure Composer</h1>
                        <p>Build a campaign-owned exploration procedure from generic mechanics. Presets are starting recipes, not runtime ruleset identities.</p>
                    </div>
                    <nav class="hc-button-row">
                        <button type="button" data-home>DM tools</button>
                        ${sourceProcedureId ? '<button type="button" data-new>New procedure</button>' : ""}
                    </nav>
                </header>
                <div class="hc-error" data-error hidden role="alert"></div>
                <section class="hc-panel" data-source-panel></section>
                <section class="hc-composer-summary" data-summary></section>
                <section class="hc-panel" data-dependencies></section>
                <div class="hc-composer-layout">
                    <aside class="hc-panel hc-composer-index" data-index></aside>
                    <main data-modules></main>
                </div>
            </section>`;

        root.querySelector<HTMLButtonElement>("[data-home]")?.addEventListener("click", () => navigate("/"));
        root.querySelector<HTMLButtonElement>("[data-new]")?.addEventListener("click", () => navigate("/procedures"));

        renderSourcePanel(required(root, "[data-source-panel]"), current, isHistorical);
        renderSummary(required(root, "[data-summary]"), current, isHistorical);
        renderDependencySummary(required(root, "[data-dependencies]"), current);
        renderModules(required(root, "[data-modules]"), required(root, "[data-index]"), current, isHistorical);

        const sourceSelect = root.querySelector<HTMLSelectElement>("[data-preset]");
        sourceSelect?.addEventListener("change", () => {
            sourcePresetKey = sourceSelect.value || null;
            pending.clear();
            void refreshDraft();
        });

        root.querySelector<HTMLButtonElement>("[data-save]")?.addEventListener("click", () => {
            if (savePending || !draft) return;
            void save();
        });

        root.querySelector<HTMLButtonElement>("[data-latest]")?.addEventListener("click", () => {
            viewedRevision = null;
            pending.clear();
            void load();
        });

        root.querySelectorAll<HTMLButtonElement>("[data-revision]").forEach(button => {
            button.addEventListener("click", () => {
                viewedRevision = Number(button.dataset.revision);
                pending.clear();
                void load();
            });
        });
    };

    const renderSourcePanel = (
        host: HTMLElement,
        current: ProcedureComposer,
        isHistorical: boolean): void => {
        host.replaceChildren();
        const heading = textElement("h2", sourceProcedureId ? "Saved procedure" : "Start with");
        host.append(heading);

        if (!sourceProcedureId) {
            const label = document.createElement("label");
            label.textContent = "Starting point ";
            const select = document.createElement("select");
            select.dataset.preset = "";
            select.dataset.composerControl = "";
            select.append(option("", "Custom — no origin preset"));
            for (const preset of presets) select.append(option(preset.presetKey, preset.displayName));
            select.value = sourcePresetKey ?? "";
            label.append(select);
            host.append(
                label,
                muted("Selecting a preset materializes its generic recipe. All editing after that point uses the campaign-owned procedure snapshot."));
            return;
        }

        host.append(
            textElement("p", `${current.name} · revision ${current.revision}`),
            muted(isHistorical
                ? "This is an immutable historical revision. Return to the latest revision to continue editing."
                : "You are editing the latest saved campaign procedure revision."));
        if (revisions.length > 0) {
            const row = document.createElement("div");
            row.className = "hc-button-row hc-composer-revisions";
            for (const revision of [...revisions].sort((a, b) => b.revision - a.revision)) {
                const button = document.createElement("button");
                button.type = "button";
                button.dataset.revision = String(revision.revision);
                button.textContent = `Revision ${revision.revision}`;
                if (revision.revision === current.revision) button.disabled = true;
                row.append(button);
            }
            if (isHistorical) {
                const latest = document.createElement("button");
                latest.type = "button";
                latest.dataset.latest = "";
                latest.textContent = "Return to latest";
                row.append(latest);
            }
            host.append(row);
        }
    };

    const renderSummary = (
        host: HTMLElement,
        current: ProcedureComposer,
        isHistorical: boolean): void => {
        host.replaceChildren();
        const primary = document.createElement("section");
        primary.className = "hc-panel";
        primary.append(
            textElement("h2", current.name),
            textElement(
                "p",
                `${current.modifiedModuleCount} modified module${current.modifiedModuleCount === 1 ? "" : "s"} · ${current.modificationCount} recorded change${current.modificationCount === 1 ? "" : "s"} · revision ${current.revision}`),
            muted(current.isExecutable
                ? "The current snapshot can bind to the native runtime."
                : "The current snapshot contains structural, incomplete, or unsupported mechanics and is not fully native-executable. It remains a valid Composer target where domain validation permits it."));

        const invalid = current.dependencies.hasErrors
            || current.modules.some(saveBlocked);
        const save = document.createElement("button");
        save.type = "button";
        save.className = "hc-primary-action";
        save.dataset.save = "";
        save.dataset.composerControl = "";
        save.textContent = savePending
            ? "Saving…"
            : sourceProcedureId
                ? "Save new revision"
                : "Save campaign procedure";
        save.disabled = savePending
            || isHistorical
            || invalid
            || (sourceProcedureId !== null && pending.size === 0);
        primary.append(save);

        const provenance = document.createElement("section");
        provenance.className = "hc-panel hc-composer-provenance";
        provenance.append(textElement("h3", "Origin"));
        if (!current.origin) {
            provenance.append(muted("No origin preset. This procedure is fully described by its materialized generic snapshot."));
        } else {
            provenance.append(textElement(
                "p",
                `${current.origin.presetDisplayName ?? current.origin.presetKey ?? "Preset"}${current.origin.presetRevision ? ` · preset revision ${current.origin.presetRevision}` : ""}`));
            if (current.origin.attribution) provenance.append(muted(current.origin.attribution));
            if (current.origin.disclaimer) provenance.append(muted(current.origin.disclaimer));
        }
        host.append(primary, provenance);
    };

    const renderDependencySummary = (host: HTMLElement, current: ProcedureComposer): void => {
        host.replaceChildren(textElement("h2", "Dependency status"));
        if (current.dependencies.issues.length === 0) {
            host.append(textElement("p", "All selected module inputs have a selected producer."));
            return;
        }

        const unresolved = current.dependencies.issues.filter(issue => issue.kind === "UnresolvedInput");
        const missing = current.dependencies.issues.filter(issue =>
            issue.kind === "MissingRequiredProducer"
            || issue.kind === "MissingRequiredModule");
        host.append(textElement(
            "p",
            `${missing.length} blocking dependency issue${missing.length === 1 ? "" : "s"} · ${unresolved.length} unresolved input${unresolved.length === 1 ? "" : "s"} · ${current.dependencies.issues.length} total diagnostic${current.dependencies.issues.length === 1 ? "" : "s"}`));
        if (unresolved.length > 0) {
            host.append(muted("Unresolved inputs are not automatically errors. Each module shows every permitted source reported by the domain evaluator."));
        }
    };

    const renderModules = (
        host: HTMLElement,
        index: HTMLElement,
        current: ProcedureComposer,
        isHistorical: boolean): void => {
        host.replaceChildren();
        index.replaceChildren(textElement("h2", "Procedure areas"));
        const indexList = document.createElement("ul");
        indexList.className = "hc-composer-index-list";
        index.append(indexList);

        for (const group of groupComposerModules(current.modules)) {
            const anchor = `composer-${slug(group.section)}`;
            const link = document.createElement("a");
            link.href = `#${anchor}`;
            link.textContent = `${group.section} (${group.modules.length})`;
            const item = document.createElement("li");
            item.append(link);
            indexList.append(item);

            const section = document.createElement("section");
            section.className = "hc-composer-section";
            section.id = anchor;
            section.append(textElement("h2", group.section));
            for (const module of group.modules) {
                section.append(renderModule(module, isHistorical));
            }
            host.append(section);
        }
    };

    const renderModule = (
        module: ProcedureModuleComposer,
        isHistorical: boolean): HTMLElement => {
        const card = document.createElement("article");
        card.className = `hc-panel hc-composer-module${module.isModified ? " is-modified" : ""}`;
        card.dataset.moduleKey = module.moduleKey;

        const heading = document.createElement("div");
        heading.className = "hc-panel-heading";
        const title = document.createElement("div");
        title.append(textElement("h3", module.displayName), muted(module.purpose));
        const badges = document.createElement("div");
        badges.className = "hc-composer-badges";
        badges.append(badge(module.mechanic.automationLevel));
        badges.append(badge(module.mechanic.executionSupport));
        if (module.isModified) badges.append(badge(`Modified ×${module.modificationCount}`));
        heading.append(title, badges);
        card.append(heading);

        const behavior = document.createElement("label");
        behavior.className = "hc-composer-behavior";
        behavior.append(document.createTextNode("Behavior "));
        const select = document.createElement("select");
        select.dataset.composerControl = "";
        const alternatives = [...module.alternatives];
        if (!alternatives.some(candidate =>
            candidate.key === module.mechanic.key
            && candidate.version === module.mechanic.version)) {
            alternatives.unshift(module.mechanic);
        }
        for (const mechanic of alternatives) {
            select.append(option(
                `${mechanic.key}@${mechanic.version}`,
                `${mechanic.displayName} · v${mechanic.version}`));
        }
        select.value = `${module.mechanic.key}@${module.mechanic.version}`;
        select.disabled = isHistorical;
        select.addEventListener("change", () => {
            const selected = alternatives.find(candidate =>
                `${candidate.key}@${candidate.version}` === select.value);
            if (!selected) return;
            pending.set(
                module.moduleKey,
                withBehavior(
                    module,
                    pending.get(module.moduleKey),
                    selected.key,
                    selected.version));
            void refreshDraft();
        });
        behavior.append(select);
        card.append(behavior);
        card.append(muted(module.mechanic.description));
        const execution = textElement("p", executionSummary(module));
        execution.className = "hc-composer-execution";
        card.append(execution);

        const parameterSection = document.createElement("section");
        parameterSection.className = "hc-composer-parameters";
        parameterSection.append(textElement("h4", "Parameters"));
        const definitions = parameterDefinitions(module);
        if (definitions.length === 0) {
            parameterSection.append(muted("This mechanic has no catalog-defined parameters."));
        } else {
            for (const [key, definition] of definitions) {
                parameterSection.append(renderParameter(module, key, definition, isHistorical));
            }
        }
        card.append(parameterSection);

        const contracts = document.createElement("div");
        contracts.className = "hc-composer-contracts";
        const inputs = document.createElement("section");
        inputs.append(textElement("h4", "Required inputs"));
        if (module.requiredInputs.length === 0) inputs.append(muted("No inputs."));
        for (const input of module.requiredInputs) {
            const allowed = input.allowedSources.length > 0
                ? input.allowedSources.map(inputSourceLabel).join(", ")
                : "selected module only";
            inputs.append(textElement("p", `${input.inputKey} — ${allowed}`));
        }
        const outputs = document.createElement("section");
        outputs.append(textElement("h4", "Outputs / state"));
        if (module.outputs.length === 0) outputs.append(muted("No declared outputs."));
        else outputs.append(textElement("p", module.outputs.join(", ")));
        contracts.append(inputs, outputs);
        card.append(contracts);

        const diagnostics = document.createElement("section");
        diagnostics.className = "hc-composer-diagnostics";
        diagnostics.append(textElement("h4", "Diagnostics"));
        if (module.validationIssues.length === 0 && module.dependencyIssues.length === 0) {
            diagnostics.append(muted("No unresolved requirements for this module."));
        }
        for (const issue of module.validationIssues) {
            diagnostics.append(diagnostic("Validation", issue, true));
        }
        for (const issue of module.dependencyIssues) {
            const sourceText = issue.allowedInputSources.length > 0
                ? ` Permitted sources: ${issue.allowedInputSources.map(inputSourceLabel).join(", ")}.`
                : "";
            diagnostics.append(diagnostic(
                issue.kind,
                `${issue.message}${sourceText}`,
                issue.kind === "MissingRequiredProducer"
                    || issue.kind === "MissingRequiredModule"
                    || issue.kind === "IncompatibleMechanic"));
        }
        card.append(diagnostics);
        return card;
    };

    const renderParameter = (
        module: ProcedureModuleComposer,
        key: string,
        definition: ProcedureParameterDefinition,
        isHistorical: boolean): HTMLElement => {
        const wrapper = document.createElement("div");
        wrapper.className = "hc-composer-parameter";
        const label = document.createElement("label");
        const title = document.createElement("span");
        title.className = "hc-composer-parameter-label";
        title.textContent = `${key}${definition.required ? " *" : ""}`;
        label.append(title);
        const value = module.parameters[key] ?? definition.defaultValue ?? "";

        const commit = (next: string): void => {
            pending.set(
                module.moduleKey,
                withParameter(module, pending.get(module.moduleKey), key, next));
            void refreshDraft();
        };

        if (definition.type === "boolean") {
            const input = document.createElement("input");
            input.type = "checkbox";
            input.checked = value.toLowerCase() === "true";
            input.disabled = isHistorical;
            input.dataset.composerControl = "";
            input.addEventListener("change", () => commit(input.checked ? "true" : "false"));
            label.append(input);
        } else if (definition.type === "map<string>") {
            label.append(renderMapEditor(value, commit, isHistorical));
        } else if (definition.type === "key-list") {
            const textarea = document.createElement("textarea");
            textarea.rows = 3;
            textarea.value = parseKeyListParameter(value).join("\n");
            textarea.placeholder = "One key per line";
            textarea.disabled = isHistorical;
            textarea.dataset.composerControl = "";
            textarea.addEventListener("change", () =>
                commit(serializeKeyListParameter(textarea.value.split(/\r?\n/))));
            label.append(textarea);
        } else {
            const input = document.createElement("input");
            input.type = definition.type === "integer" || definition.type === "number"
                ? "number"
                : "text";
            if (definition.type === "integer") input.step = "1";
            if (definition.type === "number") input.step = "any";
            input.value = value;
            input.required = definition.required;
            input.disabled = isHistorical;
            input.dataset.composerControl = "";
            input.addEventListener("change", () => commit(input.value));
            label.append(input);
        }

        wrapper.append(label);
        if (definition.description) wrapper.append(muted(`${definition.type} — ${definition.description}`));
        return wrapper;
    };

    const renderMapEditor = (
        value: string,
        commit: (value: string) => void,
        isHistorical: boolean): HTMLElement => {
        const editor = document.createElement("div");
        editor.className = "hc-composer-map-editor";

        const read = (): Array<{ key: string; value: string }> =>
            [...editor.querySelectorAll<HTMLElement>("[data-map-row]")].map(row => ({
                key: row.querySelector<HTMLInputElement>("[data-map-key]")?.value ?? "",
                value: row.querySelector<HTMLInputElement>("[data-map-value]")?.value ?? ""
            }));

        const addRow = (entry = { key: "", value: "" }): void => {
            const row = document.createElement("div");
            row.className = "hc-composer-map-row";
            row.dataset.mapRow = "";
            const key = document.createElement("input");
            key.type = "text";
            key.value = entry.key;
            key.placeholder = "key";
            key.dataset.mapKey = "";
            key.dataset.composerControl = "";
            const mapValue = document.createElement("input");
            mapValue.type = "text";
            mapValue.value = entry.value;
            mapValue.placeholder = "value";
            mapValue.dataset.mapValue = "";
            mapValue.dataset.composerControl = "";
            const remove = document.createElement("button");
            remove.type = "button";
            remove.textContent = "Remove";
            remove.dataset.composerControl = "";
            key.disabled = mapValue.disabled = remove.disabled = isHistorical;
            key.addEventListener("change", () => commit(serializeMapParameter(read())));
            mapValue.addEventListener("change", () => commit(serializeMapParameter(read())));
            remove.addEventListener("click", () => {
                row.remove();
                commit(serializeMapParameter(read()));
            });
            row.append(key, mapValue, remove);
            editor.append(row);
        };

        const entries = parseMapParameter(value);
        for (const entry of entries) addRow(entry);
        if (entries.length === 0) addRow();

        const add = document.createElement("button");
        add.type = "button";
        add.textContent = "Add mapping";
        add.dataset.composerControl = "";
        add.disabled = isHistorical;
        add.addEventListener("click", () => addRow());
        editor.append(add);
        return editor;
    };

    const save = async (): Promise<void> => {
        if (!draft || savePending) return;
        const error = required<HTMLElement>(root, "[data-error]");
        clearUiError(error);
        savePending = true;
        render();
        try {
            const saved = sourceProcedureId
                ? await composerApi.createRevision(sourceProcedureId, {
                    expectedRevision: draft.revision,
                    overrides: [...pending.values()]
                })
                : await composerApi.createProcedure({
                    presetKey: sourcePresetKey,
                    overrides: [...pending.values()]
                });
            pending.clear();
            sourceProcedureId = saved.procedureId;
            sourcePresetKey = null;
            viewedRevision = null;
            latestRevision = saved.revision;
            draft = saved;
            revisions = await composerApi.listRevisions(saved.procedureId);
            if (!disposed) {
                navigate(`/procedures/${encodeURIComponent(saved.procedureId)}`, true);
            }
        } catch (value) {
            if (!disposed) {
                render();
                const currentError = required<HTMLElement>(root, "[data-error]");
                showUiError(currentError, value);
            }
        } finally {
            savePending = false;
            if (!disposed && draft) render();
        }
    };

    try {
        await load();
    } catch (value) {
        if (!disposed) {
            root.innerHTML = `
                <section class="hc-page">
                    <header class="hc-page-header"><div><h1>Procedure Composer</h1></div></header>
                    <div class="hc-error" data-error role="alert"></div>
                </section>`;
            showUiError(required(root, "[data-error]"), value);
        }
    }

    return () => { disposed = true; };
}

function loadingMarkup(message: string): string {
    return `
        <section class="hc-page">
            <div class="hc-loading-panel" role="status">
                <span class="hc-loading-dot" aria-hidden="true"></span>
                <span>${message}</span>
            </div>
        </section>`;
}

function ensureComposerStyles(): void {
    if (document.getElementById("hex-crawl-procedure-composer-styles")) return;
    const style = document.createElement("style");
    style.id = "hex-crawl-procedure-composer-styles";
    style.textContent = `
        .hc-composer-summary { display:grid; grid-template-columns:minmax(0,2fr) minmax(16rem,1fr); gap:1rem; }
        .hc-composer-layout { display:grid; grid-template-columns:minmax(13rem,18rem) minmax(0,1fr); gap:1rem; align-items:start; }
        .hc-composer-index { position:sticky; top:1rem; }
        .hc-composer-index-list { margin:0; padding-left:1.2rem; display:grid; gap:.45rem; }
        .hc-composer-section { display:grid; gap:1rem; margin-bottom:1.5rem; scroll-margin-top:1rem; }
        .hc-composer-module { border-left:4px solid transparent; }
        .hc-composer-module.is-modified { border-left-color:currentColor; }
        .hc-composer-badges { display:flex; gap:.4rem; flex-wrap:wrap; justify-content:flex-end; }
        .hc-composer-badge { border:1px solid currentColor; border-radius:999px; padding:.15rem .5rem; font-size:.78rem; white-space:nowrap; }
        .hc-composer-behavior { display:grid; gap:.35rem; margin:.9rem 0; font-weight:600; }
        .hc-composer-execution { margin:.5rem 0 1rem; }
        .hc-composer-parameters { display:grid; gap:.7rem; }
        .hc-composer-parameter { display:grid; gap:.2rem; }
        .hc-composer-parameter label { display:grid; gap:.3rem; }
        .hc-composer-parameter-label { font-weight:600; }
        .hc-composer-parameter input[type="text"],
        .hc-composer-parameter input[type="number"],
        .hc-composer-parameter textarea,
        .hc-composer-behavior select { width:100%; box-sizing:border-box; }
        .hc-composer-contracts { display:grid; grid-template-columns:repeat(2,minmax(0,1fr)); gap:1rem; margin-top:1rem; }
        .hc-composer-diagnostics { margin-top:1rem; }
        .hc-composer-diagnostic { border-left:3px solid currentColor; padding:.45rem .65rem; margin:.4rem 0; }
        .hc-composer-diagnostic.is-blocking { font-weight:600; }
        .hc-composer-map-editor { display:grid; gap:.4rem; }
        .hc-composer-map-row { display:grid; grid-template-columns:minmax(8rem,1fr) minmax(8rem,2fr) auto; gap:.4rem; }
        .hc-composer-revisions { flex-wrap:wrap; }
        .hc-composer-provenance { opacity:.88; }
        @media (max-width: 860px) {
            .hc-composer-summary, .hc-composer-layout, .hc-composer-contracts { grid-template-columns:1fr; }
            .hc-composer-index { position:static; }
            .hc-composer-map-row { grid-template-columns:1fr; }
        }`;
    document.head.append(style);
}

function diagnostic(kind: string, message: string, blocking: boolean): HTMLElement {
    const element = document.createElement("p");
    element.className = `hc-composer-diagnostic${blocking ? " is-blocking" : ""}`;
    element.textContent = `${kind}: ${message}`;
    return element;
}

function badge(value: string): HTMLElement {
    const element = document.createElement("span");
    element.className = "hc-composer-badge";
    element.textContent = value;
    return element;
}

function muted(value: string): HTMLElement {
    const element = textElement("p", value);
    element.className = "hc-muted";
    return element;
}

function textElement<K extends keyof HTMLElementTagNameMap>(
    tag: K,
    value: string): HTMLElementTagNameMap[K] {
    const element = document.createElement(tag);
    element.textContent = value;
    return element;
}

function option(value: string, label: string): HTMLOptionElement {
    const element = document.createElement("option");
    element.value = value;
    element.textContent = label;
    return element;
}

function required<T extends Element>(root: ParentNode, selector: string): T {
    const value = root.querySelector<T>(selector);
    if (!value) throw new Error(`Required Composer element '${selector}' was not found.`);
    return value;
}

function slug(value: string): string {
    return value.toLowerCase().replace(/[^a-z0-9]+/g, "-").replace(/^-|-$/g, "");
}
