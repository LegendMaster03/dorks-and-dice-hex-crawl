import type { HexCrawlApi } from "../../api";
import { ProcedureComposerApi } from "../../procedure-composer-api";
import type {
    ProcedureCanonicalValidation,
    ProcedureComposer,
    ProcedureComposerOverrideInput,
    ProcedureMechanicOption,
    ProcedureModuleComposer,
    ProcedureParameterDefinition,
    ProcedureRevisionSummary
} from "../../procedure-composer-types";
import type { ProcedurePreset } from "../../types";
import { clearUiError, showUiError } from "../../ui-error";
import {
    createPendingOverride,
    executionSummary,
    groupComposerModules,
    inputSourceLabel,
    parameterDefinitions,
    saveBlocked,
    withBehavior,
    withParameter
} from "./procedure-composer-model";
import { ensurePhase15Styles } from "../../phase15-styles";
import {
    badge,
    disclosure,
    humanizeIdentifier,
    openWorkspaceDrawer,
    textElement
} from "../../ui/workspace";

export type ProcedureWorkspaceMode = "compact" | "advanced" | "json";

const modeStorageKey = "hex-crawl.procedure-mode";

export async function renderProcedureWorkspace(
    root: HTMLElement,
    api: HexCrawlApi,
    procedureId: string | null,
    navigate: (route: string, replace?: boolean) => void,
    initialRevision: number | null = null): Promise<() => void> {
    ensurePhase15Styles();

    let disposed = false;
    let draft: ProcedureComposer | null = null;
    let presets: ProcedurePreset[] = [];
    let revisions: ProcedureRevisionSummary[] = [];
    let sourcePresetKey: string | null = null;
    let sourceProcedureId = procedureId;
    let viewedRevision: number | null = initialRevision;
    let latestRevision: number | null = null;
    let savePending = false;
    let mode = readMode();
    let selectedArea: string | null = null;
    let jsonText = "";
    let jsonBaseline = "";
    let jsonLoadedFor: string | null = null;
    let jsonValidation: ProcedureCanonicalValidation | null = null;
    let jsonBusy = false;
    let activeDrawer: { close: () => void } | null = null;
    const pending = new Map<string, ProcedureComposerOverrideInput>();

    root.classList.add("hc-phase15");
    root.replaceChildren(loadingPanel("Loading exploration procedure…"));
    const composerApi = await ProcedureComposerApi.create(root);
    if (!sourceProcedureId) presets = await api.getProcedurePresets();
    if (disposed) return () => {};

    const historical = (): boolean =>
        sourceProcedureId !== null
        && viewedRevision !== null
        && latestRevision !== null
        && viewedRevision !== latestRevision;

    const currentInput = () => ({
        presetKey: sourceProcedureId ? null : sourcePresetKey,
        procedureId: sourceProcedureId,
        revision: viewedRevision,
        overrides: [...pending.values()]
    });

    const load = async (): Promise<void> => {
        activeDrawer?.close();
        activeDrawer = null;
        if (sourceProcedureId) {
            revisions = await composerApi.listRevisions(sourceProcedureId);
            latestRevision = revisions.reduce((maximum, value) => Math.max(maximum, value.revision), 0) || null;
            draft = viewedRevision == null
                ? await composerApi.getProcedure(sourceProcedureId)
                : await composerApi.getProcedure(sourceProcedureId, viewedRevision);
        } else {
            revisions = [];
            latestRevision = null;
            draft = await composerApi.composeDraft(currentInput());
        }
        invalidateJson();
        if (!disposed) render();
    };

    const refreshDraft = async (): Promise<void> => {
        if (!draft || historical()) return;
        setControlsDisabled(true);
        try {
            draft = await composerApi.composeDraft({
                presetKey: sourceProcedureId ? null : sourcePresetKey,
                procedureId: sourceProcedureId,
                overrides: [...pending.values()]
            });
            invalidateJson();
            if (!disposed) render();
        } catch (value) {
            const error = root.querySelector<HTMLElement>("[data-error]");
            if (error) showUiError(error, value);
            setControlsDisabled(false);
        }
    };

    const setMode = async (next: ProcedureWorkspaceMode): Promise<void> => {
        if (next === mode) return;
        if (mode === "json" && jsonText !== jsonBaseline) {
            const discard = globalThis.confirm("Discard unsaved JSON changes and switch modes?");
            if (!discard) return;
            invalidateJson();
        }
        mode = next;
        try {
            localStorage.setItem(modeStorageKey, mode);
        } catch {
            // Storage preference is optional.
        }
        render();
        if (mode === "json") await ensureJsonLoaded();
    };

    const ensureJsonLoaded = async (force = false): Promise<void> => {
        if (!draft || mode !== "json" || jsonBusy) return;
        const identity = `${draft.procedureId}:${draft.revision}:${pending.size}:${viewedRevision ?? "latest"}`;
        if (!force && jsonLoadedFor === identity) return;
        jsonBusy = true;
        render();
        try {
            const canonical = await composerApi.composeCanonicalDraft(currentInput());
            jsonText = canonical.canonicalJson;
            jsonBaseline = canonical.canonicalJson;
            jsonLoadedFor = identity;
            jsonValidation = null;
        } catch (value) {
            const error = root.querySelector<HTMLElement>("[data-error]");
            if (error) showUiError(error, value);
        } finally {
            jsonBusy = false;
            if (!disposed) render();
        }
    };

    const render = (): void => {
        if (!draft) return;
        activeDrawer?.close();
        activeDrawer = null;
        const current = draft;
        const page = document.createElement("section");
        page.className = "hc-page hc-phase15 hc-procedure-shell";

        const header = document.createElement("header");
        header.className = "hc-page-header";
        const heading = document.createElement("div");
        heading.append(
            textElement("h1", sourceProcedureId ? current.name : "Build an exploration procedure"),
            textElement(
                "p",
                sourceProcedureId
                    ? "Run and customize the campaign-owned procedure. Compact uses tabletop language; Advanced and JSON expose progressively lower-level control."
                    : "Choose a familiar starting procedure or begin with Custom, then adjust only the parts you want to change."));
        const nav = document.createElement("nav");
        nav.className = "hc-button-row";
        const home = button("DM tools", () => navigate("/"));
        nav.append(home);
        if (sourceProcedureId) nav.append(button("New procedure", () => navigate("/procedures")));
        header.append(heading, nav);
        page.append(header);

        const error = document.createElement("div");
        error.className = "hc-error";
        error.dataset.error = "";
        error.hidden = true;
        error.setAttribute("role", "alert");
        page.append(error);

        page.append(renderToolbar(current));
        if (!sourceProcedureId) page.append(renderPresetBrowser(current));
        page.append(renderSummaryStrip(current));

        const content = document.createElement("section");
        content.dataset.procedureModeContent = "";
        switch (mode) {
            case "compact":
                content.append(renderCompact(current));
                break;
            case "advanced":
                content.append(renderAdvanced(current));
                break;
            case "json":
                content.append(renderJson(current));
                break;
        }
        page.append(content);
        root.replaceChildren(page);
    };

    const renderToolbar = (current: ProcedureComposer): HTMLElement => {
        const toolbar = document.createElement("section");
        toolbar.className = "hc-panel hc-procedure-toolbar";

        const modes = document.createElement("div");
        modes.className = "hc-mode-switcher";
        modes.setAttribute("aria-label", "Procedure editing mode");
        for (const value of ["compact", "advanced", "json"] as ProcedureWorkspaceMode[]) {
            const control = document.createElement("button");
            control.type = "button";
            control.textContent = value === "json" ? "JSON" : capitalize(value);
            control.setAttribute("aria-pressed", String(mode === value));
            control.addEventListener("click", () => void setMode(value));
            modes.append(control);
        }
        toolbar.append(modes);

        const actions = document.createElement("div");
        actions.className = "hc-button-row";
        if (sourceProcedureId && revisions.length > 0) {
            const label = document.createElement("label");
            label.textContent = "Revision ";
            const select = document.createElement("select");
            select.dataset.composerControl = "";
            for (const revision of [...revisions].sort((a, b) => b.revision - a.revision)) {
                const option = document.createElement("option");
                option.value = String(revision.revision);
                option.textContent = String(revision.revision);
                select.append(option);
            }
            select.value = String(current.revision);
            select.addEventListener("change", () => {
                viewedRevision = latestRevision === Number(select.value) ? null : Number(select.value);
                pending.clear();
                void load();
            });
            label.append(select);
            actions.append(label);
        }

        if (mode !== "json") {
            const invalid = current.dependencies.hasErrors || current.modules.some(saveBlocked);
            const save = button(
                savePending ? "Saving…" : sourceProcedureId ? "Save new revision" : "Save procedure",
                () => void saveStructured());
            save.className = "hc-primary-action";
            save.dataset.composerControl = "";
            save.disabled = savePending
                || historical()
                || invalid
                || (sourceProcedureId !== null && pending.size === 0);
            actions.append(save);
        }
        toolbar.append(actions);
        return toolbar;
    };

    const renderPresetBrowser = (current: ProcedureComposer): HTMLElement => {
        const browser = document.createElement("section");
        browser.className = "hc-panel hc-preset-browser";
        const header = document.createElement("div");
        header.className = "hc-preset-browser-header";
        const text = document.createElement("div");
        text.append(
            textElement("h2", "Starting procedures"),
            textElement("p", "Inspect the workflow before using it. Presets are copied once; the saved procedure does not depend on the catalog afterward.", "hc-muted"));
        header.append(text);
        browser.append(header);

        const grid = document.createElement("div");
        grid.className = "hc-preset-grid";
        grid.append(renderCustomPresetCard(current));
        for (const preset of presets) grid.append(renderPresetCard(preset));
        browser.append(grid);
        return browser;
    };

    const renderCustomPresetCard = (current: ProcedureComposer): HTMLElement => {
        const card = document.createElement("article");
        card.className = `hc-preset-card${sourcePresetKey === null ? " is-selected" : ""}`;
        card.append(
            textElement("h3", "Custom"),
            textElement("p", "Start from Hex Crawl's generic baseline with no named preset origin."));
        const facts = document.createElement("dl");
        facts.className = "hc-preset-facts";
        appendFact(facts, "Workflow", presetWorkflow(current.modules));
        appendFact(facts, "Complexity", complexityLabel(current.modules.length));
        card.append(facts);
        const actions = document.createElement("div");
        actions.className = "hc-preset-actions";
        const use = button(sourcePresetKey === null ? "Using Custom" : "Use Custom", () => {
            sourcePresetKey = null;
            pending.clear();
            void refreshDraft();
        });
        use.disabled = sourcePresetKey === null;
        actions.append(use);
        card.append(actions);
        return card;
    };

    const renderPresetCard = (preset: ProcedurePreset): HTMLElement => {
        const card = document.createElement("article");
        card.className = `hc-preset-card${sourcePresetKey === preset.presetKey ? " is-selected" : ""}`;
        card.append(textElement("h3", preset.displayName), textElement("p", preset.description));
        const facts = document.createElement("dl");
        facts.className = "hc-preset-facts";
        appendFact(facts, "Travel", travelSummary(preset));
        appendFact(facts, "Navigation", navigationSummary(preset));
        appendFact(facts, "Activities", activitySummary(preset));
        appendFact(facts, "Encounters", encounterSummary(preset));
        appendFact(facts, "Resources", resourceSummary(preset));
        appendFact(facts, "Journeys", journeySummary(preset));
        appendFact(facts, "Complexity", complexityLabel(preset.procedure.modules.length));
        card.append(facts);

        const details = disclosure("Attribution and procedure details");
        const detailBody = document.createElement("div");
        detailBody.append(textElement("p", `Primary workflow: ${presetWorkflowFromPreset(preset)}`));
        if (preset.attribution) detailBody.append(textElement("p", preset.attribution, "hc-muted"));
        if (preset.disclaimer) detailBody.append(textElement("p", preset.disclaimer, "hc-muted"));
        details.append(detailBody);
        card.append(details);

        const actions = document.createElement("div");
        actions.className = "hc-preset-actions";
        const use = button(
            sourcePresetKey === preset.presetKey ? "Using this preset" : "Use this preset",
            () => {
                sourcePresetKey = preset.presetKey;
                pending.clear();
                void refreshDraft();
            });
        use.disabled = sourcePresetKey === preset.presetKey;
        actions.append(use);
        card.append(actions);
        return card;
    };

    const renderSummaryStrip = (current: ProcedureComposer): HTMLElement => {
        const strip = document.createElement("section");
        strip.className = "hc-procedure-summary-strip";
        strip.append(
            metric("Procedure", current.name),
            metric("Status", current.dependencies.hasErrors ? "Needs attention" : current.isExecutable ? "Ready" : "Structural / assisted"),
            metric("Modified areas", String(modifiedAreaCount(current))),
            metric("Needs attention", String(attentionAreaCount(current))));
        if (current.origin) {
            strip.append(metric(
                "Starting point",
                `${current.origin.presetDisplayName ?? "Preset"}${current.modifiedModuleCount > 0 ? " → Custom" : " default"}`));
        } else {
            strip.append(metric("Starting point", "Custom"));
        }
        if (sourceProcedureId) strip.append(metric("Revision", String(current.revision)));
        return strip;
    };

    const renderCompact = (current: ProcedureComposer): HTMLElement => {
        const wrapper = document.createElement("section");
        wrapper.className = "hc-panel";
        wrapper.append(
            textElement("h2", "Your exploration procedure"),
            textElement("p", "Select an area to inspect or change it. Normal Compact editing uses tabletop concepts rather than mechanic IDs or dependency keys.", "hc-muted"));

        const grid = document.createElement("div");
        grid.className = "hc-procedure-area-grid";
        for (const group of groupComposerModules(current.modules)) {
            const card = document.createElement("button");
            card.type = "button";
            card.className = "hc-area-card";
            card.dataset.area = group.section;
            const heading = document.createElement("span");
            heading.className = "hc-area-card-heading";
            heading.append(textElement("h3", group.section));
            const modified = group.modules.some(module => module.isModified);
            heading.append(badge(
                modified ? "Modified" : current.origin ? `${current.origin.presetDisplayName ?? "Preset"} default` : "Custom",
                modified ? "info" : "neutral"));
            card.append(heading);
            card.append(textElement("p", compactBehaviorSummary(group.modules), "hc-area-card-summary"));
            const attention = areaAttention(group.modules);
            const meta = document.createElement("span");
            meta.className = "hc-area-card-meta";
            meta.append(badge(`${group.modules.length} part${group.modules.length === 1 ? "" : "s"}`));
            meta.append(badge(
                attention === 0 ? "Ready" : `${attention} need${attention === 1 ? "s" : ""} attention`,
                attention === 0 ? "good" : "warning"));
            card.append(meta);
            card.addEventListener("click", () => openCompactArea(group.section, group.modules, card));
            grid.append(card);
        }
        wrapper.append(grid);
        return wrapper;
    };

    const openCompactArea = (
        area: string,
        modules: ProcedureModuleComposer[],
        trigger: HTMLElement): void => {
        if (!draft) return;
        selectedArea = area;
        activeDrawer = openWorkspaceDrawer(root, area, body => {
            body.append(textElement("p", compactBehaviorSummary(modules), "hc-muted"));
            if (draft?.origin) {
                body.append(textElement(
                    "p",
                    modules.some(module => module.isModified)
                        ? `${draft.origin.presetDisplayName ?? "Starting preset"} default → Custom`
                        : `${draft.origin.presetDisplayName ?? "Starting preset"} default · Unchanged`));
            }

            for (const module of modules) body.append(renderCompactModule(module));

            const actions = document.createElement("div");
            actions.className = "hc-area-actions";
            const restore = button(restoreLabel(), () => void restoreArea(modules));
            restore.disabled = historical() || !canRestoreArea(modules);
            actions.append(restore);
            const advanced = button("Advanced details", () => {
                activeDrawer?.close();
                activeDrawer = null;
                void setMode("advanced");
            });
            actions.append(advanced);
            body.append(actions);
        }, trigger);
    };

    const renderCompactModule = (module: ProcedureModuleComposer): HTMLElement => {
        const section = document.createElement("section");
        section.className = "hc-focus-workspace-module";
        const header = document.createElement("div");
        header.append(textElement("h3", module.displayName), textElement("p", module.purpose, "hc-muted"));
        section.append(header);

        const behavior = document.createElement("label");
        behavior.className = "hc-compact-field";
        behavior.append(textElement("span", "Behavior"));
        const select = document.createElement("select");
        select.dataset.composerControl = "";
        const alternatives = uniqueMechanics(module);
        for (const mechanic of alternatives) {
            const option = document.createElement("option");
            option.value = `${mechanic.key}@${mechanic.version}`;
            option.textContent = mechanic.displayName;
            select.append(option);
        }
        select.value = `${module.mechanic.key}@${module.mechanic.version}`;
        select.disabled = historical();
        select.addEventListener("change", () => {
            const selected = alternatives.find(candidate => `${candidate.key}@${candidate.version}` === select.value);
            if (!selected) return;
            pending.set(module.moduleKey, withBehavior(
                module,
                pending.get(module.moduleKey),
                selected.key,
                selected.version));
            void refreshDraft();
        });
        behavior.append(select, textElement("span", module.mechanic.description, "hc-muted"));
        section.append(behavior);

        const definitions = parameterDefinitions(module);
        if (definitions.length > 0) {
            const fields = document.createElement("div");
            fields.className = "hc-compact-field-grid";
            for (const [key, definition] of definitions) {
                fields.append(renderFriendlyParameter(module, key, definition));
            }
            section.append(fields);
        }

        const issues = module.validationIssues.length + module.dependencyIssues.filter(isActionableDependency).length;
        if (issues > 0) {
            const diagnostics = disclosure(`${issues} item${issues === 1 ? "" : "s"} need attention`, true);
            const body = document.createElement("div");
            for (const issue of module.validationIssues) {
                body.append(domainDiagnostic("Configuration needs attention", friendlyDiagnostic(issue), true));
            }
            for (const issue of module.dependencyIssues.filter(isActionableDependency)) {
                body.append(domainDiagnostic(
                    dependencyHeading(issue.kind),
                    compactDependencyMessage(issue),
                    issue.kind === "MissingRequiredProducer" || issue.kind === "MissingRequiredModule" || issue.kind === "IncompatibleMechanic"));
            }
            diagnostics.append(body);
            section.append(diagnostics);
        }
        return section;
    };

    const renderFriendlyParameter = (
        module: ProcedureModuleComposer,
        key: string,
        definition: ProcedureParameterDefinition): HTMLElement => {
        const label = document.createElement("label");
        label.className = "hc-compact-field";
        const fieldLabel = module.presentationMetadata[`parameter.${key}.label`]
            ?? module.presentationMetadata[`label.${key}`]
            ?? humanizeIdentifier(key);
        label.append(textElement("span", `${fieldLabel}${definition.required ? " *" : ""}`));
        const value = module.parameters[key] ?? definition.defaultValue ?? "";
        const commit = (next: string): void => {
            pending.set(module.moduleKey, withParameter(module, pending.get(module.moduleKey), key, next));
            void refreshDraft();
        };
        label.append(parameterControl(definition, value, commit, historical()));
        if (definition.description) label.append(textElement("span", definition.description, "hc-muted"));
        return label;
    };

    const renderAdvanced = (current: ProcedureComposer): HTMLElement => {
        const wrapper = document.createElement("section");
        wrapper.className = "hc-advanced-layout";
        const index = document.createElement("aside");
        index.className = "hc-panel hc-advanced-index";
        index.append(
            textElement("h2", "Procedure areas"),
            textElement("p", "Advanced exposes exact generic mechanics and contracts.", "hc-muted"));
        const list = document.createElement("ul");
        for (const group of groupComposerModules(current.modules)) {
            const item = document.createElement("li");
            const link = document.createElement("a");
            link.href = `#advanced-${slug(group.section)}`;
            link.textContent = group.section;
            item.append(link);
            list.append(item);
        }
        index.append(list);

        const main = document.createElement("main");
        for (const group of groupComposerModules(current.modules)) {
            const section = document.createElement("section");
            section.id = `advanced-${slug(group.section)}`;
            section.className = "hc-composer-section";
            section.append(textElement("h2", group.section));
            for (const module of group.modules) section.append(renderAdvancedModule(module));
            main.append(section);
        }
        wrapper.append(index, main);
        return wrapper;
    };

    const renderAdvancedModule = (module: ProcedureModuleComposer): HTMLElement => {
        const card = document.createElement("article");
        card.className = "hc-panel hc-advanced-module";
        card.append(textElement("h3", module.displayName), textElement("p", module.purpose, "hc-muted"));

        const behavior = document.createElement("label");
        behavior.className = "hc-compact-field";
        behavior.append(textElement("span", "Selected generic mechanic"));
        const select = document.createElement("select");
        select.dataset.composerControl = "";
        const alternatives = uniqueMechanics(module);
        for (const mechanic of alternatives) {
            const option = document.createElement("option");
            option.value = `${mechanic.key}@${mechanic.version}`;
            option.textContent = `${mechanic.displayName} · ${mechanic.key} · v${mechanic.version}`;
            select.append(option);
        }
        select.value = `${module.mechanic.key}@${module.mechanic.version}`;
        select.disabled = historical();
        select.addEventListener("change", () => {
            const selected = alternatives.find(candidate => `${candidate.key}@${candidate.version}` === select.value);
            if (!selected) return;
            pending.set(module.moduleKey, withBehavior(module, pending.get(module.moduleKey), selected.key, selected.version));
            void refreshDraft();
        });
        behavior.append(select);
        card.append(behavior, textElement("p", executionSummary(module), "hc-muted"));

        const parameters = disclosure("Parameters", true);
        const parameterBody = document.createElement("div");
        parameterBody.className = "hc-compact-field-grid";
        for (const [key, definition] of parameterDefinitions(module)) {
            const label = document.createElement("label");
            label.className = "hc-compact-field";
            label.append(textElement("span", `${key}${definition.required ? " *" : ""}`));
            const value = module.parameters[key] ?? definition.defaultValue ?? "";
            label.append(parameterControl(definition, value, next => {
                pending.set(module.moduleKey, withParameter(module, pending.get(module.moduleKey), key, next));
                void refreshDraft();
            }, historical()));
            if (definition.description) label.append(textElement("span", `${definition.type} · ${definition.description}`, "hc-muted"));
            parameterBody.append(label);
        }
        if (parameterBody.childElementCount === 0) parameterBody.append(textElement("p", "No parameters."));
        parameters.append(parameterBody);
        card.append(parameters);

        const contracts = disclosure("Inputs, outputs, and execution contract");
        const contractBody = document.createElement("div");
        contractBody.className = "hc-advanced-contracts";
        const inputs = document.createElement("section");
        inputs.append(textElement("h4", "Required inputs"));
        if (module.requiredInputs.length === 0) inputs.append(textElement("p", "No inputs."));
        for (const input of module.requiredInputs) {
            inputs.append(textElement("p", `${input.inputKey} — ${input.allowedSources.map(inputSourceLabel).join(", ") || "selected module"}`));
        }
        const outputs = document.createElement("section");
        outputs.append(textElement("h4", "Outputs / state"), textElement("p", module.outputs.join(", ") || "No declared outputs."));
        contractBody.append(inputs, outputs);
        contracts.append(contractBody);
        card.append(contracts);

        const diagnostics = disclosure("Diagnostics");
        const diagnosticBody = document.createElement("div");
        if (module.validationIssues.length === 0 && module.dependencyIssues.length === 0) {
            diagnosticBody.append(textElement("p", "No diagnostics."));
        }
        for (const issue of module.validationIssues) diagnosticBody.append(domainDiagnostic("Validation", issue, true));
        for (const issue of module.dependencyIssues) {
            const sources = issue.allowedInputSources.length > 0
                ? ` Permitted sources: ${issue.allowedInputSources.map(inputSourceLabel).join(", ")}.`
                : "";
            diagnosticBody.append(domainDiagnostic(issue.kind, `${issue.message}${sources}`, isActionableDependency(issue)));
        }
        diagnostics.append(diagnosticBody);
        card.append(diagnostics);
        return card;
    };

    const renderJson = (current: ProcedureComposer): HTMLElement => {
        const wrapper = document.createElement("section");
        wrapper.className = "hc-panel hc-json-editor";
        wrapper.append(
            textElement("h2", "Canonical procedure JSON"),
            textElement("p", "This is the same CampaignProcedure used by Compact and Advanced. Saving always goes through server parsing, domain validation, and optimistic concurrency.", "hc-muted"));

        if (jsonBusy || !jsonLoadedFor) {
            wrapper.append(loadingPanel("Loading canonical representation…"));
            queueMicrotask(() => void ensureJsonLoaded());
            return wrapper;
        }

        const textarea = document.createElement("textarea");
        textarea.value = jsonText;
        textarea.spellcheck = false;
        textarea.disabled = historical() || jsonBusy;
        textarea.setAttribute("aria-label", "Canonical campaign procedure JSON");
        textarea.addEventListener("input", () => {
            jsonText = textarea.value;
            jsonValidation = null;
            updateJsonStatus(wrapper);
        });
        wrapper.append(textarea);

        const actions = document.createElement("div");
        actions.className = "hc-json-actions";
        actions.append(
            button("Format", () => {
                try {
                    jsonText = JSON.stringify(JSON.parse(jsonText), null, 2);
                    textarea.value = jsonText;
                    jsonValidation = null;
                    updateJsonStatus(wrapper);
                } catch (error) {
                    jsonValidation = {
                        isValid: false,
                        procedureId: null,
                        revision: null,
                        error: error instanceof Error ? error.message : String(error),
                        lineNumber: null,
                        bytePositionInLine: null
                    };
                    updateJsonStatus(wrapper);
                }
            }),
            button("Validate", () => void validateJson()),
            button("Reload authoritative", () => {
                if (jsonText !== jsonBaseline && !globalThis.confirm("Discard unsaved JSON changes and reload the authoritative draft?")) return;
                invalidateJson();
                void ensureJsonLoaded(true);
            }));
        const saveJson = button(sourceProcedureId ? "Save canonical revision" : "Save canonical procedure", () => void saveJsonProcedure());
        saveJson.className = "hc-primary-action";
        saveJson.disabled = historical() || jsonBusy || jsonText === jsonBaseline;
        saveJson.dataset.jsonSave = "";
        actions.append(saveJson);
        wrapper.append(actions);

        const status = document.createElement("p");
        status.dataset.jsonStatus = "";
        status.className = "hc-json-status";
        wrapper.append(status);
        updateJsonStatus(wrapper);

        const guardrails = disclosure("Validation and authority", false);
        guardrails.append(textElement(
            "p",
            "Invalid JSON never becomes authoritative. Domain-invalid JSON is rejected by the server. A saved procedure revision must still match the current authoritative revision; stale edits receive a conflict instead of overwriting newer work."));
        wrapper.append(guardrails);
        return wrapper;
    };

    const updateJsonStatus = (host: ParentNode): void => {
        const status = host.querySelector<HTMLElement>("[data-json-status]");
        if (!status) return;
        const dirty = jsonText !== jsonBaseline;
        if (jsonValidation?.isValid) {
            status.className = "hc-json-status is-valid";
            status.textContent = `${dirty ? "Unsaved changes · " : ""}Server validation passed.`;
        } else if (jsonValidation?.error) {
            status.className = "hc-json-status is-error";
            const location = jsonValidation.lineNumber !== null
                ? ` Line ${jsonValidation.lineNumber + 1}${jsonValidation.bytePositionInLine !== null ? `, byte ${jsonValidation.bytePositionInLine + 1}` : ""}.`
                : "";
            status.textContent = `${jsonValidation.error}${location}`;
        } else {
            status.className = "hc-json-status";
            status.textContent = dirty ? "Unsaved canonical changes." : "Matches the authoritative draft.";
        }
        const save = host.querySelector<HTMLButtonElement>("[data-json-save]");
        if (save) save.disabled = historical() || jsonBusy || !dirty;
    };

    const validateJson = async (): Promise<boolean> => {
        if (jsonBusy) return false;
        const validatingText = jsonText;
        jsonBusy = true;
        updateJsonStatus(root);
        try {
            const validation = await composerApi.validateCanonical(validatingText);
            if (jsonText !== validatingText) {
                jsonValidation = null;
                updateJsonStatus(root);
                return false;
            }
            jsonValidation = validation;
            updateJsonStatus(root);
            return validation.isValid;
        } catch (value) {
            const error = root.querySelector<HTMLElement>("[data-error]");
            if (error) showUiError(error, value);
            return false;
        } finally {
            jsonBusy = false;
            updateJsonStatus(root);
        }
    };

    const saveJsonProcedure = async (): Promise<void> => {
        if (!draft || jsonBusy || historical()) return;
        const error = root.querySelector<HTMLElement>("[data-error]");
        if (error) clearUiError(error);
        if (!await validateJson()) return;
        jsonBusy = true;
        render();
        try {
            const saved = sourceProcedureId
                ? await composerApi.createCanonicalRevision(sourceProcedureId, {
                    expectedRevision: draft.revision,
                    canonicalJson: jsonText
                })
                : await composerApi.createCanonicalProcedure({
                    canonicalJson: jsonText,
                    presetKey: sourcePresetKey
                });
            pending.clear();
            sourceProcedureId = saved.procedureId;
            sourcePresetKey = null;
            viewedRevision = null;
            latestRevision = saved.revision;
            draft = saved;
            revisions = await composerApi.listRevisions(saved.procedureId);
            invalidateJson();
            if (!disposed) navigate(`/procedures/${encodeURIComponent(saved.procedureId)}`, true);
        } catch (value) {
            if (!disposed) {
                render();
                const currentError = root.querySelector<HTMLElement>("[data-error]");
                if (currentError) showUiError(currentError, value);
            }
        } finally {
            jsonBusy = false;
            if (!disposed && draft) render();
        }
    };

    const saveStructured = async (): Promise<void> => {
        if (!draft || savePending || historical()) return;
        const error = root.querySelector<HTMLElement>("[data-error]");
        if (error) clearUiError(error);
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
            invalidateJson();
            if (!disposed) navigate(`/procedures/${encodeURIComponent(saved.procedureId)}`, true);
        } catch (value) {
            if (!disposed) {
                render();
                const currentError = root.querySelector<HTMLElement>("[data-error]");
                if (currentError) showUiError(currentError, value);
            }
        } finally {
            savePending = false;
            if (!disposed && draft) render();
        }
    };

    const restoreLabel = (): string => "Discard unsaved changes";

    const canRestoreArea = (modules: ProcedureModuleComposer[]): boolean =>
        modules.some(module => pending.has(module.moduleKey));

    const restoreArea = async (modules: ProcedureModuleComposer[]): Promise<void> => {
        if (!draft || historical()) return;
        for (const module of modules) pending.delete(module.moduleKey);
        await refreshDraft();
    };

    const invalidateJson = (): void => {
        jsonText = "";
        jsonBaseline = "";
        jsonLoadedFor = null;
        jsonValidation = null;
    };

    const setControlsDisabled = (disabled: boolean): void => {
        root.querySelectorAll<HTMLInputElement | HTMLSelectElement | HTMLTextAreaElement | HTMLButtonElement>("[data-composer-control]")
            .forEach(control => { control.disabled = disabled; });
    };

    try {
        await load();
        if (mode === "json") await ensureJsonLoaded();
    } catch (value) {
        if (!disposed) {
            const page = document.createElement("section");
            page.className = "hc-page hc-phase15";
            page.append(textElement("h1", "Exploration procedure"));
            const error = document.createElement("div");
            error.className = "hc-error";
            error.dataset.error = "";
            error.setAttribute("role", "alert");
            page.append(error);
            root.replaceChildren(page);
            showUiError(error, value);
        }
    }

    return () => {
        disposed = true;
        activeDrawer?.close();
        root.classList.remove("hc-phase15");
    };
}

function readMode(): ProcedureWorkspaceMode {
    try {
        const value = localStorage.getItem(modeStorageKey);
        if (value === "compact" || value === "advanced" || value === "json") return value;
    } catch {
        // Preference storage is optional.
    }
    return "compact";
}

function button(label: string, action: () => void): HTMLButtonElement {
    const control = document.createElement("button");
    control.type = "button";
    control.textContent = label;
    control.addEventListener("click", action);
    return control;
}

function loadingPanel(message: string): HTMLElement {
    const panel = document.createElement("div");
    panel.className = "hc-loading-panel";
    panel.setAttribute("role", "status");
    panel.setAttribute("aria-live", "polite");
    const dot = document.createElement("span");
    dot.className = "hc-loading-dot";
    dot.setAttribute("aria-hidden", "true");
    panel.append(dot, textElement("span", message));
    return panel;
}

function metric(label: string, value: string): HTMLElement {
    const element = document.createElement("div");
    element.className = "hc-summary-metric";
    element.append(textElement("span", label), textElement("strong", value));
    return element;
}

function appendFact(list: HTMLDListElement, label: string, value: string): void {
    list.append(textElement("dt", label), textElement("dd", value));
}

function uniqueMechanics(module: ProcedureModuleComposer): ProcedureMechanicOption[] {
    const values = [module.mechanic, ...module.alternatives];
    const seen = new Set<string>();
    return values.filter(value => {
        const key = `${value.key}@${value.version}`;
        if (seen.has(key)) return false;
        seen.add(key);
        return true;
    });
}

function parameterControl(
    definition: ProcedureParameterDefinition,
    value: string,
    commit: (next: string) => void,
    disabled: boolean): HTMLElement {
    if (definition.type === "boolean") {
        const input = document.createElement("input");
        input.type = "checkbox";
        input.checked = value.toLowerCase() === "true";
        input.disabled = disabled;
        input.dataset.composerControl = "";
        input.addEventListener("change", () => commit(input.checked ? "true" : "false"));
        return input;
    }

    if (definition.type === "map<string>" || definition.type === "key-list") {
        const textarea = document.createElement("textarea");
        textarea.rows = definition.type === "map<string>" ? 4 : 3;
        textarea.value = definition.type === "key-list"
            ? value.split(";").filter(Boolean).join("\n")
            : value.split(";").filter(Boolean).join("\n");
        textarea.disabled = disabled;
        textarea.dataset.composerControl = "";
        textarea.placeholder = definition.type === "map<string>" ? "key=value · one mapping per line" : "one value per line";
        textarea.addEventListener("change", () => {
            const normalized = textarea.value
                .split(/\r?\n/)
                .map(part => part.trim())
                .filter(Boolean)
                .join(";");
            commit(normalized);
        });
        return textarea;
    }

    const input = document.createElement("input");
    input.type = definition.type === "integer" || definition.type === "number" ? "number" : "text";
    if (definition.type === "integer") input.step = "1";
    if (definition.type === "number") input.step = "any";
    input.value = value;
    input.required = definition.required;
    input.disabled = disabled;
    input.dataset.composerControl = "";
    input.addEventListener("change", () => commit(input.value));
    return input;
}

function compactBehaviorSummary(modules: ProcedureModuleComposer[]): string {
    const names = modules.map(module => module.mechanic.displayName).filter(Boolean);
    const distinct = [...new Set(names)];
    if (distinct.length === 0) return "No behavior selected.";
    if (distinct.length <= 2) return distinct.join(" · ");
    return `${distinct.slice(0, 2).join(" · ")} · ${distinct.length - 2} more`;
}

function modifiedAreaCount(current: ProcedureComposer): number {
    return groupComposerModules(current.modules).filter(group => group.modules.some(module => module.isModified)).length;
}

function attentionAreaCount(current: ProcedureComposer): number {
    return groupComposerModules(current.modules).filter(group => areaAttention(group.modules) > 0).length;
}

function areaAttention(modules: ProcedureModuleComposer[]): number {
    return modules.reduce((total, module) =>
        total + module.validationIssues.length + module.dependencyIssues.filter(isActionableDependency).length, 0);
}

function isActionableDependency(issue: { kind: string }): boolean {
    return issue.kind !== "ProducedButUnused";
}

function dependencyHeading(kind: string): string {
    switch (kind) {
        case "UnresolvedInput": return "A result still needs a source";
        case "MissingRequiredProducer": return "Required result is missing";
        case "MissingRequiredModule": return "Required procedure part is missing";
        case "IncompatibleMechanic": return "Selected behavior does not fit this area";
        case "OptionalProviderInputRequired": return "Optional provider input is needed";
        case "ExternalInputRequired": return "Runtime input is needed";
        case "ManualInputRequired": return "DM input is needed";
        default: return humanizeIdentifier(kind);
    }
}

function compactDependencyMessage(issue: { message: string; inputKey: string | null; allowedInputSources: string[] }): string {
    const sources = issue.allowedInputSources.map(inputSourceLabel);
    if (issue.inputKey && sources.length > 0) {
        return `${humanizeIdentifier(issue.inputKey)} is not supplied by another selected procedure part. It may come from ${sources.join(", ")}.`;
    }
    return friendlyDiagnostic(issue.message);
}

function friendlyDiagnostic(message: string): string {
    return message
        .replace(/Module '[^']+' /g, "This procedure area ")
        .replace(/input '([^']+)'/g, (_match, key: string) => `the ${humanizeIdentifier(key).toLowerCase()} result`)
        .replace(/Parameter '([^']+)'/g, (_match, key: string) => humanizeIdentifier(key));
}

function domainDiagnostic(title: string, message: string, blocking: boolean): HTMLElement {
    const element = document.createElement("p");
    element.className = "hc-domain-diagnostic";
    element.append(textElement("strong", title), document.createTextNode(message));
    if (blocking) element.dataset.blocking = "";
    return element;
}

function complexityLabel(moduleCount: number): string {
    if (moduleCount <= 8) return "Light";
    if (moduleCount <= 16) return "Moderate";
    return "Detailed";
}

function presetWorkflow(modules: ProcedureModuleComposer[]): string {
    const hasJourney = modules.some(module => module.category.toLowerCase().includes("journey"));
    const hasTime = modules.some(module => module.category.toLowerCase() === "time");
    if (hasJourney && !hasTime) return "Journey process";
    if (hasJourney) return "Travel with journey processes";
    return hasTime ? "Interval / travel procedure" : "Procedure-led exploration";
}

function presetWorkflowFromPreset(preset: ProcedurePreset): string {
    const names = preset.procedure.modules.map(module => module.moduleName.toLowerCase());
    const hasJourney = names.some(name => name.includes("journey") || name.includes("stage"));
    if (hasJourney && !preset.procedure.runtime) return "Journey process";
    if (hasJourney) return "Travel with journey processes";
    return preset.procedure.runtime ? "Spatial / interval travel" : "Procedure-led exploration";
}

function travelSummary(preset: ProcedurePreset): string {
    const runtime = preset.procedure.runtime;
    if (runtime) {
        const interval = runtime.intervalHours >= 24
            ? `${runtime.intervalHours / 24} day interval`
            : `${runtime.intervalHours} hour interval`;
        return `${interval} · ${humanizeIdentifier(runtime.travelResolution).toLowerCase()}`;
    }
    return preset.procedure.modules.some(module => /journey|stage|progress/i.test(module.moduleName))
        ? "Journey / progress based"
        : "Procedure-defined";
}

function navigationSummary(preset: ProcedurePreset): string {
    if (preset.procedure.runtime) {
        return preset.procedure.runtime.usesNavigationChecks
            ? preset.procedure.runtime.usesPersistentVeer ? "Checks with persistent off-course state" : "Navigation checks"
            : "No routine navigation checks";
    }
    return preset.procedure.modules.some(module => /navigation|route|guide/i.test(module.moduleName))
        ? "Procedure-defined navigation"
        : "Not emphasized";
}

function activitySummary(preset: ProcedurePreset): string {
    const matches = preset.procedure.modules.filter(module => /activity|role|party|watch/i.test(module.moduleName));
    if (matches.length === 0) return "Light / none";
    return matches.length === 1 ? matches[0].moduleName : `${matches.length} activity / role parts`;
}

function encounterSummary(preset: ProcedurePreset): string {
    const cadence = preset.procedure.runtime?.encounterCadence;
    if (cadence && cadence !== "None") return humanizeIdentifier(cadence);
    return preset.procedure.modules.some(module => /encounter/i.test(module.moduleName)) ? "Procedure-defined" : "Not used";
}

function resourceSummary(preset: ProcedurePreset): string {
    const count = preset.procedure.modules.filter(module => /resource|ration|food|water|survival|camp|fatigue|exposure/i.test(module.moduleName)).length;
    return count === 0 ? "Not emphasized" : count <= 2 ? "Meaningful" : "Detailed";
}

function journeySummary(preset: ProcedurePreset): string {
    const count = preset.procedure.modules.filter(module => /journey|stage|event|progress/i.test(module.moduleName)).length;
    return count === 0 ? "Not used" : count <= 2 ? "Available" : "Core workflow";
}

function capitalize(value: string): string {
    return value.charAt(0).toUpperCase() + value.slice(1);
}

function slug(value: string): string {
    return value.toLowerCase().replace(/[^a-z0-9]+/g, "-").replace(/^-|-$/g, "");
}
