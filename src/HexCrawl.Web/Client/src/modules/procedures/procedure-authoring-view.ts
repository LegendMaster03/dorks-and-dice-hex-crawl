import type { HexCrawlApi } from "../../api";
import { ensurePhase15Styles } from "../../phase15-styles";
import { ProcedureComposerApi } from "../../procedure-composer-api";
import type {
    ProcedureCanonicalValidation,
    ProcedureComposer,
    ProcedureComposerModuleSelectionInput,
    ProcedureComposerOverrideInput,
    ProcedureMechanicOption,
    ProcedureModuleComposer,
    ProcedureParameterDefinition,
    ProcedureRevisionSummary
} from "../../procedure-composer-types";
import type { ProcedurePreset } from "../../types";
import { clearUiError, showUiError } from "../../ui-error";
import { badge, openWorkspaceDrawer, textElement, type WorkspaceDrawer } from "../../ui/workspace";
import {
    executionSummary,
    groupComposerModules,
    inputSourceLabel,
    parameterDefinitions,
    saveBlocked,
    withBehavior,
    withParameter
} from "./procedure-composer-model";

export type ProcedureAuthoringMode = "compact" | "advanced" | "json";
type EntryState = "landing" | "presets" | "workspace";

type StructureArea = {
    key: string;
    title: string;
    question: string;
    usedLabel: string;
    omittedLabel: string;
    detail: string;
    moduleKeys: string[];
};

const modeStorageKey = "hex-crawl.procedure-authoring.mode";
const structureAreas: StructureArea[] = [
    {
        key: "time",
        title: "Time / structure",
        question: "Does expedition play use repeating travel periods or watches?",
        usedLabel: "Use repeating travel periods",
        omittedLabel: "No repeating interval",
        detail: "A journey/process can drive progress without a repeating travel interval.",
        moduleKeys: ["time.interval"]
    },
    {
        key: "movement",
        title: "Movement",
        question: "Does this procedure resolve ordinary travel movement?",
        usedLabel: "Resolve travel movement",
        omittedLabel: "Movement is manual or journey-driven",
        detail: "Includes movement resolution, hex progress, movement budget, and terrain/route movement.",
        moduleKeys: ["movement.resolution", "movement.hex-progress", "movement.budget", "movement.terrain"]
    },
    {
        key: "navigation",
        title: "Navigation",
        question: "Does the expedition use navigation checks or persistent off-course state?",
        usedLabel: "Use navigation procedure",
        omittedLabel: "No navigation procedure",
        detail: "Navigation behavior and failure/recovery state remain separate generic procedure parts.",
        moduleKeys: ["navigation.check", "navigation.outcome"]
    },
    {
        key: "activities",
        title: "Activities / roles",
        question: "Does the party assign expedition activities or roles?",
        usedLabel: "Use party activities / roles",
        omittedLabel: "No activity assignment procedure",
        detail: "Assignments may be participant-, party-, or role-oriented depending on the selected behavior.",
        moduleKeys: ["party.activities"]
    },
    {
        key: "encounters",
        title: "Encounters",
        question: "Does the procedure schedule or require encounter checks?",
        usedLabel: "Use encounter procedure",
        omittedLabel: "No encounter procedure",
        detail: "Cadence and the broader encounter schedule remain independently editable after inclusion.",
        moduleKeys: ["encounters.cadence", "encounters.schedule"]
    },
    {
        key: "survival",
        title: "Survival / resources",
        question: "Does expedition play track survival, resources, camping, foraging, or travel consequences?",
        usedLabel: "Use survival / resource procedure",
        omittedLabel: "Omit survival / resource procedure",
        detail: "This area covers resources, foraging, camping, forced travel, and persistent expedition effects.",
        moduleKeys: ["survival.resources", "exploration.foraging", "survival.camping", "time.forced-travel", "effects.expedition"]
    },
    {
        key: "journey",
        title: "Journeys",
        question: "Is there a separate multi-stage journey process?",
        usedLabel: "Use journey process",
        omittedLabel: "No separate journey process",
        detail: "Journey stages, progress, roles, events, and explicit resolution can operate without a fabricated map or watch.",
        moduleKeys: ["journey.process", "journey.events"]
    }
];

const moduleLabels = new Map<string, string>([
    ["time.interval", "Travel interval"],
    ["movement.resolution", "Movement resolution"],
    ["movement.hex-progress", "Hex progress"],
    ["movement.budget", "Movement budget"],
    ["movement.terrain", "Terrain and route movement"],
    ["navigation.check", "Navigation checks"],
    ["navigation.outcome", "Navigation outcome"],
    ["party.activities", "Participant activities"],
    ["encounters.cadence", "Encounter cadence"],
    ["encounters.schedule", "Encounter schedule"],
    ["survival.resources", "Resource consumption"],
    ["exploration.foraging", "Foraging"],
    ["survival.camping", "Camping"],
    ["time.forced-travel", "Forced travel"],
    ["effects.expedition", "Persistent expedition effects"],
    ["journey.process", "Multi-stage journey process"],
    ["journey.events", "Journey events"],
    ["procedure.helpers", "Resolution helpers"]
]);

export async function renderProcedureAuthoringWorkspace(
    root: HTMLElement,
    api: HexCrawlApi,
    procedureId: string | null,
    navigate: (route: string, replace?: boolean) => void,
    requestedRevision: number | null = null): Promise<() => void> {
    ensurePhase15Styles();
    root.classList.add("hc-phase15");

    let disposed = false;
    let entry: EntryState = procedureId ? "workspace" : "landing";
    let mode = readMode();
    let sourcePresetKey: string | null = null;
    let sourceProcedureId = procedureId;
    let viewedRevision = requestedRevision;
    let latestRevision: number | null = null;
    let draft: ProcedureComposer | null = null;
    let presets: ProcedurePreset[] = [];
    let revisions: ProcedureRevisionSummary[] = [];
    let savePending = false;
    let refreshSerial = 0;
    let activeDrawer: WorkspaceDrawer | null = null;
    let activeCompactArea: string | null = null;
    let closingDrawerForRender = false;
    const pending = new Map<string, ProcedureComposerOverrideInput>();
    const moduleSelections = new Map<string, boolean>();

    let jsonText = "";
    let jsonBaseline = "";
    let jsonLoadedFor: string | null = null;
    let jsonBusy = false;
    let jsonValidation: ProcedureCanonicalValidation | null = null;

    root.replaceChildren(loadingPanel("Loading exploration procedures…"));
    const composerApi = await ProcedureComposerApi.create(root);
    presets = await api.getProcedurePresets();
    if (disposed) return () => {};

    const historical = (): boolean =>
        sourceProcedureId !== null
        && viewedRevision !== null
        && latestRevision !== null
        && viewedRevision !== latestRevision;

    const currentInput = () => ({
        presetKey: sourceProcedureId ? null : sourcePresetKey,
        procedureId: sourceProcedureId,
        revision: sourceProcedureId ? viewedRevision : null,
        moduleSelections: [...moduleSelections.entries()].map(([moduleKey, included]) => ({ moduleKey, included })),
        overrides: [...pending.values()]
    });

    const invalidateJson = (): void => {
        jsonText = "";
        jsonBaseline = "";
        jsonLoadedFor = null;
        jsonValidation = null;
    };

    const closeActiveDrawerForRender = (): void => {
        const current = activeDrawer;
        activeDrawer = null;
        activeCompactArea = null;
        if (!current) return;
        closingDrawerForRender = true;
        try {
            current.close();
        } finally {
            closingDrawerForRender = false;
        }
    };

    const loadWorkspace = async (): Promise<void> => {
        if (sourceProcedureId) {
            revisions = await composerApi.listRevisions(sourceProcedureId);
            latestRevision = revisions.reduce((max, item) => Math.max(max, item.revision), 0) || null;
        } else {
            revisions = [];
            latestRevision = null;
        }
        draft = await composerApi.composeDraft(currentInput());
        invalidateJson();
        if (!disposed) render();
    };

    const refreshDraft = async (): Promise<void> => {
        const serial = ++refreshSerial;
        const error = root.querySelector<HTMLElement>("[data-error]");
        if (error) clearUiError(error);
        setControlsDisabled(true);
        try {
            const next = await composerApi.composeDraft(currentInput());
            if (disposed || serial !== refreshSerial) return;
            draft = next;
            invalidateJson();
            if (!refreshCompactDrawer(next)) render();
        } catch (value) {
            if (disposed || serial !== refreshSerial) return;
            if (error) showUiError(error, value);
            setControlsDisabled(false);
        }
    };

    const enterCustom = async (): Promise<void> => {
        sourcePresetKey = null;
        pending.clear();
        moduleSelections.clear();
        entry = "workspace";
        mode = "compact";
        await loadWorkspace();
    };

    const enterPreset = async (presetKey: string): Promise<void> => {
        sourcePresetKey = presetKey;
        pending.clear();
        moduleSelections.clear();
        entry = "workspace";
        mode = "compact";
        await loadWorkspace();
    };

    const changeStartingPoint = (): void => {
        if (sourceProcedureId) return;
        if ((pending.size > 0 || moduleSelections.size > 0)
            && !globalThis.confirm("Discard unsaved procedure changes and choose a different starting point?")) return;
        pending.clear();
        moduleSelections.clear();
        sourcePresetKey = null;
        draft = null;
        invalidateJson();
        entry = "landing";
        render();
    };

    const setMode = (next: ProcedureAuthoringMode): void => {
        if (mode === next) return;
        mode = next;
        try { localStorage.setItem(modeStorageKey, mode); } catch { /* preference storage is optional */ }
        render();
        if (mode === "json") void ensureJsonLoaded();
    };

    const render = (): void => {
        closeActiveDrawerForRender();
        if (entry === "landing" && !sourceProcedureId) {
            renderLanding();
            return;
        }
        if (entry === "presets" && !sourceProcedureId) {
            renderPresetBrowser();
            return;
        }
        renderWorkspace();
    };

    const basePage = (title: string, subtitle: string): HTMLElement => {
        const page = document.createElement("section");
        page.className = "hc-page hc-phase15 hc-procedure-workspace";
        const header = document.createElement("header");
        header.className = "hc-page-header";
        const copy = document.createElement("div");
        copy.append(textElement("h1", title), textElement("p", subtitle));
        const nav = document.createElement("nav");
        nav.className = "hc-button-row";
        const home = button("DM tools", () => navigate("/"));
        nav.append(home);
        if (sourceProcedureId) nav.append(button("New procedure", () => navigate("/procedures")));
        header.append(copy, nav);
        page.append(header);
        const error = document.createElement("div");
        error.className = "hc-error";
        error.dataset.error = "";
        error.hidden = true;
        error.setAttribute("role", "alert");
        page.append(error);
        return page;
    };

    const renderLanding = (): void => {
        const page = basePage(
            "Create an exploration procedure",
            "Choose a familiar starting point or define the expedition procedure directly in tabletop terms.");
        const choices = document.createElement("div");
        choices.className = "hc-preset-grid hc-procedure-entry-grid";
        choices.append(
            entryCard(
                "Start from a known procedure",
                "Browse familiar systems and published exploration procedures, then customize the materialized campaign copy.",
                "Browse presets",
                () => { entry = "presets"; render(); }),
            entryCard(
                "Build my own",
                "Define time, movement, navigation, encounters, activities, survival, journeys, and other expedition procedures without inheriting a named system.",
                "Build custom procedure",
                () => void enterCustom()));
        page.append(choices);
        root.replaceChildren(page);
    };

    const renderPresetBrowser = (): void => {
        const page = basePage(
            "Choose a starting procedure",
            "Presets are creation-time recipes. The saved procedure is an independent campaign-owned snapshot.");
        const controls = document.createElement("div");
        controls.className = "hc-button-row";
        controls.append(
            button("Back", () => { entry = "landing"; render(); }),
            button("Build my own instead", () => void enterCustom()));
        page.append(controls);
        const grid = document.createElement("div");
        grid.className = "hc-preset-grid";
        for (const preset of presets) {
            const card = document.createElement("article");
            card.className = "hc-panel hc-preset-card";
            card.append(
                textElement("h2", preset.displayName),
                textElement("p", preset.description),
                preset.attribution ? textElement("p", preset.attribution, "hc-muted") : document.createTextNode(""));
            const facts = document.createElement("dl");
            facts.className = "hc-preset-facts";
            appendFact(facts, "Workflow", presetWorkflow(preset));
            appendFact(facts, "Travel", travelSummary(preset));
            appendFact(facts, "Navigation", navigationSummary(preset));
            appendFact(facts, "Journey", journeySummary(preset));
            card.append(facts);
            const use = button(`Use ${preset.displayName}`, () => void enterPreset(preset.presetKey));
            use.className = "hc-primary-action";
            card.append(use);
            grid.append(card);
        }
        page.append(grid);
        root.replaceChildren(page);
    };

    const renderWorkspace = (): void => {
        if (!draft) {
            root.replaceChildren(loadingPanel("Loading procedure workspace…"));
            return;
        }
        const current = draft;
        const page = basePage(
            current.name,
            sourceProcedureId ? `Campaign procedure · revision ${current.revision}` : "Unsaved campaign procedure");

        const toolbar = document.createElement("div");
        toolbar.className = "hc-view-switcher hc-procedure-mode-toolbar";
        toolbar.setAttribute("aria-label", "Procedure editor mode");
        for (const value of ["compact", "advanced", "json"] as const) {
            const control = button(capitalize(value), () => setMode(value));
            control.classList.toggle("hc-active-view", mode === value);
            control.setAttribute("aria-current", mode === value ? "page" : "false");
            toolbar.append(control);
        }
        if (!sourceProcedureId) {
            toolbar.append(textElement("span", "", "hc-run-toolbar-divider"));
            toolbar.append(button("Change starting point", changeStartingPoint));
        }
        page.append(toolbar, renderSummary(current));

        if (historical()) page.append(historicalNotice(current));
        if (mode === "compact") page.append(renderCompact(current));
        else if (mode === "advanced") page.append(renderAdvanced(current));
        else page.append(renderJson(current));
        root.replaceChildren(page);
    };

    const renderSummary = (current: ProcedureComposer): HTMLElement => {
        const summary = document.createElement("section");
        summary.className = "hc-panel hc-procedure-summary";
        const heading = document.createElement("div");
        heading.className = "hc-area-card-heading";
        heading.append(textElement("h2", "Procedure"), badge(statusLabel(current), statusTone(current)));
        summary.append(heading);
        const metrics = document.createElement("div");
        metrics.className = "hc-summary-metrics";
        metrics.append(
            metric("Areas in use", String(current.modules.length)),
            metric("Pending structure", String(moduleSelections.size)),
            metric("Pending behavior", String(pending.size)),
            metric("Runtime", current.isExecutable ? "Native" : "Assisted / structural"));
        summary.append(metrics);
        const origin = current.origin;
        summary.append(textElement(
            "p",
            origin
                ? `Started from ${origin.presetDisplayName ?? origin.presetKey ?? "a preset"}. The campaign copy is independent of the preset catalog.`
                : "Custom procedure · no named preset origin.",
            "hc-muted"));
        if (current.dependencies.hasErrors) {
            const list = document.createElement("div");
            list.className = "hc-domain-diagnostics";
            for (const issue of current.dependencies.issues.filter(issue => isBlocking(issue.kind))) {
                list.append(domainDiagnostic(dependencyHeading(issue.kind), friendlyDiagnostic(issue.message), true));
            }
            summary.append(list);
        }

        const row = document.createElement("div");
        row.className = "hc-button-row";
        if (sourceProcedureId && revisions.length > 1) {
            const select = document.createElement("select");
            select.setAttribute("aria-label", "Procedure revision");
            for (const revision of [...revisions].sort((a, b) => b.revision - a.revision)) {
                const option = document.createElement("option");
                option.value = String(revision.revision);
                option.textContent = `Revision ${revision.revision}`;
                select.append(option);
            }
            select.value = String(current.revision);
            select.addEventListener("change", () => {
                const selected = Number(select.value);
                viewedRevision = latestRevision !== null && selected === latestRevision ? null : selected;
                pending.clear();
                moduleSelections.clear();
                void loadWorkspace();
            });
            row.append(select);
        }
        if (!historical() && mode !== "json") {
            const save = button(sourceProcedureId ? "Save new revision" : "Save procedure", () => void saveStructured());
            save.className = "hc-primary-action";
            save.dataset.composerControl = "";
            save.disabled = savePending || structuredSaveBlocked(current)
                || (sourceProcedureId !== null && pending.size === 0 && moduleSelections.size === 0);
            row.append(save);
        }
        summary.append(row);
        return summary;
    };

    const renderCompact = (current: ProcedureComposer): HTMLElement => {
        const wrapper = document.createElement("section");
        wrapper.className = "hc-compact-procedure";
        wrapper.append(
            textElement("h2", "Procedure structure"),
            textElement("p", "Choose the expedition concepts this procedure actually uses. Areas you omit are absent from the materialized CampaignProcedure; they are not simulated with disabled mechanics.", "hc-muted"));
        const structure = document.createElement("div");
        structure.className = "hc-procedure-area-grid";
        for (const area of structureAreas) structure.append(renderStructureArea(current, area));
        wrapper.append(structure);

        if (current.modules.length === 0) {
            wrapper.append(domainDiagnostic("Choose at least one procedure area", "A saved CampaignProcedure must contain at least one generic procedure module.", true));
            return wrapper;
        }

        wrapper.append(textElement("h2", "Current procedure areas"));
        const areas = document.createElement("div");
        areas.className = "hc-procedure-area-grid";
        for (const group of groupComposerModules(current.modules)) {
            const card = document.createElement("article");
            card.className = "hc-panel hc-area-card";
            const heading = document.createElement("div");
            heading.className = "hc-area-card-heading";
            heading.append(textElement("h3", group.section), badge(`${group.modules.length} part${group.modules.length === 1 ? "" : "s"}`, "neutral"));
            card.append(heading, textElement("p", compactBehaviorSummary(group.modules), "hc-muted"));
            const open = button("Edit area", () => openCompactArea(group.section, group.modules, open));
            open.dataset.compactArea = group.section;
            open.disabled = historical();
            card.append(open);
            areas.append(card);
        }
        wrapper.append(areas);
        return wrapper;
    };

    const renderStructureArea = (current: ProcedureComposer, area: StructureArea): HTMLElement => {
        const present = new Set(current.modules.map(module => module.moduleKey));
        const count = area.moduleKeys.filter(key => present.has(key)).length;
        const card = document.createElement("article");
        card.className = "hc-panel hc-area-card hc-structure-card";
        card.append(textElement("h3", area.title), textElement("p", area.question), textElement("p", area.detail, "hc-muted"));
        const state = count === 0 ? "Not used" : count === area.moduleKeys.length ? "Used" : `${count} of ${area.moduleKeys.length} parts used`;
        card.append(badge(state, count === 0 ? "neutral" : "info"));
        const row = document.createElement("div");
        row.className = "hc-button-row";
        const include = button(area.usedLabel, () => void applyStructureArea(area, true));
        const omit = button(area.omittedLabel, () => void applyStructureArea(area, false));
        include.dataset.composerControl = "";
        omit.dataset.composerControl = "";
        include.disabled = historical() || count === area.moduleKeys.length;
        omit.disabled = historical() || count === 0;
        row.append(include, omit);
        card.append(row);
        return card;
    };

    const applyStructureArea = async (area: StructureArea, included: boolean): Promise<void> => {
        if (historical()) return;
        for (const moduleKey of area.moduleKeys) {
            moduleSelections.set(moduleKey, included);
            if (!included) pending.delete(moduleKey);
        }
        await refreshDraft();
    };

    const openCompactArea = (
        title: string,
        modules: ProcedureModuleComposer[],
        returnFocus: HTMLElement): void => {
        closeActiveDrawerForRender();
        activeCompactArea = title;
        activeDrawer = openWorkspaceDrawer(root, title, body => {
            populateCompactArea(body, modules);
        }, returnFocus, () => {
            if (closingDrawerForRender) return;
            const area = activeCompactArea;
            activeDrawer = null;
            activeCompactArea = null;
            if (disposed) return;
            render();
            if (area) {
                queueMicrotask(() => {
                    const replacement = [...root.querySelectorAll<HTMLButtonElement>("[data-compact-area]")]
                        .find(control => control.dataset.compactArea === area);
                    replacement?.focus();
                });
            }
        });
    };

    const populateCompactArea = (body: HTMLElement, modules: ProcedureModuleComposer[]): void => {
        const contents: Node[] = [
            textElement("p", "Normal Compact editing uses tabletop concepts rather than mechanic IDs or dependency keys.", "hc-muted")
        ];
        for (const module of modules) contents.push(renderCompactModule(module));
        body.replaceChildren(...contents);
    };

    const refreshCompactDrawer = (current: ProcedureComposer): boolean => {
        if (!activeDrawer?.element.isConnected || !activeCompactArea) return false;
        const group = groupComposerModules(current.modules)
            .find(candidate => candidate.section === activeCompactArea);
        const body = activeDrawer.element.querySelector<HTMLElement>(".hc-focus-workspace-body");
        if (!group || !body) return false;

        const focused = document.activeElement instanceof HTMLElement
            && activeDrawer.element.contains(document.activeElement)
            ? {
                moduleKey: document.activeElement.dataset.compactModule,
                fieldKey: document.activeElement.dataset.compactField
            }
            : null;

        populateCompactArea(body, group.modules);
        if (focused?.moduleKey && focused.fieldKey) {
            queueMicrotask(() => {
                const replacement = [...body.querySelectorAll<HTMLElement>("[data-compact-module]")]
                    .find(control =>
                        control.dataset.compactModule === focused.moduleKey
                        && control.dataset.compactField === focused.fieldKey);
                replacement?.focus();
            });
        }
        return true;
    };

    const renderCompactModule = (module: ProcedureModuleComposer): HTMLElement => {
        const section = document.createElement("section");
        section.className = "hc-compact-module";
        section.append(textElement("h3", module.displayName), textElement("p", module.purpose, "hc-muted"));
        const alternatives = uniqueMechanics(module);
        if (alternatives.length > 1) {
            const label = document.createElement("label");
            label.className = "hc-compact-field";
            label.append(textElement("span", behaviorQuestion(module)));
            const select = document.createElement("select");
            select.dataset.composerControl = "";
            select.dataset.compactModule = module.moduleKey;
            select.dataset.compactField = "mechanic";
            for (const mechanic of alternatives) {
                const option = document.createElement("option");
                option.value = `${mechanic.key}@${mechanic.version}`;
                option.textContent = mechanic.displayName;
                select.append(option);
            }
            select.value = `${module.mechanic.key}@${module.mechanic.version}`;
            select.disabled = historical();
            select.addEventListener("change", () => {
                const selected = alternatives.find(item => `${item.key}@${item.version}` === select.value);
                if (!selected) return;
                pending.set(module.moduleKey, withBehavior(module, pending.get(module.moduleKey), selected.key, selected.version));
                void refreshDraft();
            });
            label.append(select);
            section.append(label);
        }
        const fields = document.createElement("div");
        fields.className = "hc-compact-field-grid";
        for (const [key, definition] of parameterDefinitions(module)) {
            fields.append(renderParameter(module, key, definition, false));
        }
        if (fields.childElementCount > 0) section.append(fields);
        for (const issue of module.dependencyIssues.filter(issue => issue.kind !== "ProducedButUnused")) {
            section.append(domainDiagnostic(dependencyHeading(issue.kind), compactDependencyMessage(issue), isBlocking(issue.kind)));
        }
        return section;
    };

    const renderParameter = (
        module: ProcedureModuleComposer,
        key: string,
        definition: ProcedureParameterDefinition,
        rawLabel: boolean): HTMLElement => {
        const label = document.createElement("label");
        label.className = "hc-compact-field";
        const display = rawLabel
            ? key
            : module.presentationMetadata[`parameter.${key}.label`]
                ?? module.presentationMetadata[`label.${key}`]
                ?? humanizeIdentifier(key);
        label.append(textElement("span", `${display}${definition.required ? " *" : ""}`));
        const value = module.parameters[key] ?? definition.defaultValue ?? "";
        const control = parameterControl(definition, value, next => {
            pending.set(module.moduleKey, withParameter(module, pending.get(module.moduleKey), key, next));
            void refreshDraft();
        }, historical());
        control.dataset.compactModule = module.moduleKey;
        control.dataset.compactField = key;
        label.append(control);
        if (definition.description) label.append(textElement("span", definition.description, "hc-muted"));
        return label;
    };

    const renderAdvanced = (current: ProcedureComposer): HTMLElement => {
        const wrapper = document.createElement("section");
        wrapper.className = "hc-advanced-layout";
        const index = document.createElement("aside");
        index.className = "hc-panel hc-advanced-index";
        index.append(textElement("h2", "Module composition"), textElement("p", "Advanced exposes exact generic module keys, mechanics, versions, parameters, and contracts.", "hc-muted"));
        const allKeys = [...new Set(structureAreas.flatMap(area => area.moduleKeys).concat(["procedure.helpers"]))];
        for (const moduleKey of allKeys) {
            const row = document.createElement("label");
            row.className = "hc-advanced-module-toggle";
            const checkbox = document.createElement("input");
            checkbox.type = "checkbox";
            checkbox.checked = current.modules.some(module => module.moduleKey === moduleKey);
            checkbox.disabled = historical();
            checkbox.dataset.composerControl = "";
            checkbox.addEventListener("change", () => {
                moduleSelections.set(moduleKey, checkbox.checked);
                if (!checkbox.checked) pending.delete(moduleKey);
                void refreshDraft();
            });
            row.append(checkbox, document.createTextNode(` ${moduleLabels.get(moduleKey) ?? humanizeIdentifier(moduleKey)} · ${moduleKey}`));
            index.append(row);
        }

        const main = document.createElement("main");
        for (const group of groupComposerModules(current.modules)) {
            const section = document.createElement("section");
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
        card.append(textElement("h3", `${module.displayName} · ${module.moduleKey}`), textElement("p", module.purpose, "hc-muted"));
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
            const selected = alternatives.find(item => `${item.key}@${item.version}` === select.value);
            if (!selected) return;
            pending.set(module.moduleKey, withBehavior(module, pending.get(module.moduleKey), selected.key, selected.version));
            void refreshDraft();
        });
        behavior.append(select);
        card.append(behavior, textElement("p", executionSummary(module), "hc-muted"));
        const fields = document.createElement("div");
        fields.className = "hc-compact-field-grid";
        for (const [key, definition] of parameterDefinitions(module)) fields.append(renderParameter(module, key, definition, true));
        card.append(fields);
        const contracts = document.createElement("details");
        const summary = document.createElement("summary");
        summary.textContent = "Inputs, outputs, dependencies, and diagnostics";
        contracts.append(summary);
        contracts.append(textElement("p", `Reads: ${module.reads.join(", ") || "none"}`));
        contracts.append(textElement("p", `Produces: ${module.outputs.join(", ") || "none"}`));
        contracts.append(textElement("p", `Required modules: ${module.requiredDependencies.join(", ") || "none"}`));
        for (const issue of module.dependencyIssues) contracts.append(domainDiagnostic(issue.kind, issue.message, isBlocking(issue.kind)));
        card.append(contracts);
        return card;
    };

    const renderJson = (_current: ProcedureComposer): HTMLElement => {
        const wrapper = document.createElement("section");
        wrapper.className = "hc-panel hc-json-editor";
        wrapper.append(textElement("h2", "Canonical procedure JSON"), textElement("p", "This is the exact same CampaignProcedure produced by Compact structural choices and Advanced edits.", "hc-muted"));
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
                } catch (value) {
                    jsonValidation = { isValid: false, procedureId: null, revision: null, error: value instanceof Error ? value.message : String(value), lineNumber: null, bytePositionInLine: null };
                }
                updateJsonStatus(wrapper);
            }),
            button("Validate", () => void validateJson()),
            button("Reload authoritative draft", () => {
                if (jsonText !== jsonBaseline && !globalThis.confirm("Discard unsaved JSON changes?")) return;
                invalidateJson();
                render();
                void ensureJsonLoaded();
            }));
        const save = button(sourceProcedureId ? "Save canonical revision" : "Save canonical procedure", () => void saveJsonProcedure());
        save.className = "hc-primary-action";
        save.dataset.jsonSave = "";
        save.disabled = historical() || jsonBusy || jsonText === jsonBaseline;
        actions.append(save);
        wrapper.append(actions);
        const status = document.createElement("p");
        status.dataset.jsonStatus = "";
        status.className = "hc-json-status";
        wrapper.append(status);
        updateJsonStatus(wrapper);
        return wrapper;
    };

    const ensureJsonLoaded = async (): Promise<void> => {
        if (jsonBusy) return;
        const key = JSON.stringify(currentInput());
        if (jsonLoadedFor === key) return;
        jsonBusy = true;
        let failure: unknown | null = null;
        try {
            const canonical = await composerApi.composeCanonicalDraft(currentInput());
            if (disposed) return;
            jsonText = canonical.canonicalJson;
            jsonBaseline = canonical.canonicalJson;
            jsonLoadedFor = key;
            jsonValidation = null;
        } catch (value) {
            failure = value;
        } finally {
            jsonBusy = false;
            if (!disposed && mode === "json") {
                if (failure === null) {
                    render();
                } else {
                    const error = root.querySelector<HTMLElement>("[data-error]");
                    if (error) showUiError(error, failure);
                    const editor = root.querySelector<HTMLElement>(".hc-json-editor");
                    if (editor && !editor.querySelector("[data-json-retry]")) {
                        const retry = button("Retry canonical JSON", () => {
                            const currentError = root.querySelector<HTMLElement>("[data-error]");
                            if (currentError) clearUiError(currentError);
                            render();
                        });
                        retry.dataset.jsonRetry = "";
                        editor.append(retry);
                    }
                }
            }
        }
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
            status.textContent = jsonValidation.error;
        } else {
            status.className = "hc-json-status";
            status.textContent = dirty ? "Unsaved canonical changes." : "Matches the composed CampaignProcedure.";
        }
        const save = host.querySelector<HTMLButtonElement>("[data-json-save]");
        if (save) save.disabled = historical() || jsonBusy || !dirty;
    };

    const validateJson = async (): Promise<boolean> => {
        if (jsonBusy) return false;
        const validating = jsonText;
        jsonBusy = true;
        updateJsonStatus(root);
        try {
            const validation = await composerApi.validateCanonical(validating);
            if (jsonText !== validating) return false;
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

    const saveStructured = async (): Promise<void> => {
        if (!draft || savePending || historical()) return;
        const error = root.querySelector<HTMLElement>("[data-error]");
        if (error) clearUiError(error);
        savePending = true;
        render();
        const selections: ProcedureComposerModuleSelectionInput[] = [...moduleSelections.entries()]
            .map(([moduleKey, included]) => ({ moduleKey, included }));
        let failure: unknown | null = null;
        try {
            const saved = sourceProcedureId
                ? await composerApi.createRevision(sourceProcedureId, {
                    expectedRevision: draft.revision,
                    moduleSelections: selections,
                    overrides: [...pending.values()]
                })
                : await composerApi.createProcedure({
                    presetKey: sourcePresetKey,
                    moduleSelections: selections,
                    overrides: [...pending.values()]
                });
            pending.clear();
            moduleSelections.clear();
            sourceProcedureId = saved.procedureId;
            sourcePresetKey = null;
            viewedRevision = null;
            latestRevision = saved.revision;
            draft = saved;
            revisions = await composerApi.listRevisions(saved.procedureId);
            invalidateJson();
            if (!disposed) navigate(`/procedures/${encodeURIComponent(saved.procedureId)}`, true);
        } catch (value) {
            failure = value;
        } finally {
            savePending = false;
            if (!disposed) {
                render();
                if (failure !== null) {
                    const current = root.querySelector<HTMLElement>("[data-error]");
                    if (current) showUiError(current, failure);
                }
            }
        }
    };

    const saveJsonProcedure = async (): Promise<void> => {
        if (!draft || jsonBusy || historical()) return;
        const error = root.querySelector<HTMLElement>("[data-error]");
        if (error) clearUiError(error);
        if (!await validateJson()) return;
        jsonBusy = true;
        render();
        let failure: unknown | null = null;
        try {
            const saved = sourceProcedureId
                ? await composerApi.createCanonicalRevision(sourceProcedureId, { expectedRevision: draft.revision, canonicalJson: jsonText })
                : await composerApi.createCanonicalProcedure({ canonicalJson: jsonText, presetKey: sourcePresetKey });
            pending.clear();
            moduleSelections.clear();
            sourceProcedureId = saved.procedureId;
            sourcePresetKey = null;
            viewedRevision = null;
            latestRevision = saved.revision;
            draft = saved;
            revisions = await composerApi.listRevisions(saved.procedureId);
            invalidateJson();
            if (!disposed) navigate(`/procedures/${encodeURIComponent(saved.procedureId)}`, true);
        } catch (value) {
            failure = value;
        } finally {
            jsonBusy = false;
            if (!disposed) {
                render();
                if (failure !== null) {
                    const current = root.querySelector<HTMLElement>("[data-error]");
                    if (current) showUiError(current, failure);
                }
            }
        }
    };

    try {
        if (sourceProcedureId) await loadWorkspace();
        else render();
    } catch (value) {
        const page = basePage("Exploration procedure", "Procedure workspace could not be loaded.");
        root.replaceChildren(page);
        const error = page.querySelector<HTMLElement>("[data-error]");
        if (error) showUiError(error, value);
    }

    return () => {
        disposed = true;
        refreshSerial += 1;
        activeDrawer?.close();
        root.classList.remove("hc-phase15");
    };

    function setControlsDisabled(disabled: boolean): void {
        root.querySelectorAll<HTMLInputElement | HTMLSelectElement | HTMLTextAreaElement | HTMLButtonElement>("[data-composer-control]")
            .forEach(control => { control.disabled = disabled; });
    }
}

function readMode(): ProcedureAuthoringMode {
    try {
        const value = localStorage.getItem(modeStorageKey);
        if (value === "compact" || value === "advanced" || value === "json") return value;
    } catch { /* preference storage is optional */ }
    return "compact";
}

function entryCard(title: string, detail: string, actionLabel: string, action: () => void): HTMLElement {
    const card = document.createElement("article");
    card.className = "hc-panel hc-preset-card hc-procedure-entry-card";
    card.append(textElement("h2", title), textElement("p", detail));
    const control = button(actionLabel, action);
    control.className = "hc-primary-action";
    card.append(control);
    return card;
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
    panel.append(textElement("span", message));
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
        textarea.value = value.split(";").filter(Boolean).join("\n");
        textarea.disabled = disabled;
        textarea.dataset.composerControl = "";
        textarea.addEventListener("change", () => commit(textarea.value.split(/\r?\n/).map(value => value.trim()).filter(Boolean).join(";")));
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
    const distinct = [...new Set(modules.map(module => module.mechanic.displayName).filter(Boolean))];
    if (distinct.length === 0) return "No behavior selected.";
    if (distinct.length <= 2) return distinct.join(" · ");
    return `${distinct.slice(0, 2).join(" · ")} · ${distinct.length - 2} more`;
}

function structuredSaveBlocked(current: ProcedureComposer): boolean {
    return current.modules.length === 0
        || current.dependencies.hasErrors
        || current.modules.some(saveBlocked);
}

function statusLabel(current: ProcedureComposer): string {
    if (current.modules.length === 0) return "Incomplete";
    if (current.dependencies.hasErrors || current.modules.some(saveBlocked)) return "Needs attention";
    return "Ready to save";
}

function statusTone(current: ProcedureComposer): "neutral" | "good" | "warning" | "danger" | "info" {
    if (current.modules.length === 0) return "warning";
    if (current.dependencies.hasErrors || current.modules.some(saveBlocked)) return "warning";
    return "good";
}

function historicalNotice(current: ProcedureComposer): HTMLElement {
    const notice = document.createElement("section");
    notice.className = "hc-panel";
    notice.append(textElement("strong", `Revision ${current.revision} is historical.`), textElement("p", "Historical procedure snapshots are read-only. Select the latest revision to continue editing."));
    return notice;
}

function behaviorQuestion(module: ProcedureModuleComposer): string {
    const key = module.moduleKey;
    if (key.includes("navigation")) return "How does navigation work?";
    if (key.includes("encounter")) return "How are encounters handled?";
    if (key.includes("movement")) return "How is travel progress resolved?";
    if (key.includes("activities")) return "How are party activities assigned?";
    if (key.includes("journey")) return "How does the journey process work?";
    if (key.includes("resource") || key.includes("camp") || key.includes("forag")) return "How is this survival procedure resolved?";
    return "How does this part work?";
}

function isBlocking(kind: string): boolean {
    return kind === "MissingRequiredModule" || kind === "MissingRequiredProducer" || kind === "IncompatibleMechanic";
}

function dependencyHeading(kind: string): string {
    switch (kind) {
        case "UnresolvedInput": return "A result still needs a source";
        case "MissingRequiredProducer": return "Required result is missing";
        case "MissingRequiredModule": return "Required procedure part is missing";
        case "IncompatibleMechanic": return "Selected behavior does not fit this area";
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
    const strong = document.createElement("strong");
    strong.textContent = `${title}: `;
    element.append(strong, document.createTextNode(message));
    if (blocking) element.dataset.blocking = "";
    return element;
}

function humanizeIdentifier(value: string): string {
    return value
        .replace(/[._-]+/g, " ")
        .replace(/([a-z])([A-Z])/g, "$1 $2")
        .replace(/\b\w/g, match => match.toUpperCase());
}

function presetWorkflow(preset: ProcedurePreset): string {
    const names = preset.procedure.modules.map(module => module.moduleName.toLowerCase());
    const hasJourney = names.some(name => name.includes("journey") || name.includes("stage"));
    if (hasJourney && !preset.procedure.runtime) return "Journey process";
    if (hasJourney) return "Travel with journey processes";
    return preset.procedure.runtime ? "Spatial / interval travel" : "Procedure-led exploration";
}

function travelSummary(preset: ProcedurePreset): string {
    const runtime = preset.procedure.runtime;
    if (!runtime) return preset.procedure.modules.some(module => /journey|stage|progress/i.test(module.moduleName)) ? "Journey / progress based" : "Procedure-defined";
    return `${runtime.intervalHours >= 24 ? `${runtime.intervalHours / 24} day` : `${runtime.intervalHours} hour`} interval · ${humanizeIdentifier(runtime.travelResolution).toLowerCase()}`;
}

function navigationSummary(preset: ProcedurePreset): string {
    if (!preset.procedure.runtime) return preset.procedure.modules.some(module => /navigation|route|guide/i.test(module.moduleName)) ? "Procedure-defined" : "Not emphasized";
    return preset.procedure.runtime.usesNavigationChecks
        ? preset.procedure.runtime.usesPersistentVeer ? "Checks with persistent off-course state" : "Navigation checks"
        : "No routine navigation checks";
}

function journeySummary(preset: ProcedurePreset): string {
    const count = preset.procedure.modules.filter(module => /journey|stage|event|progress/i.test(module.moduleName)).length;
    return count === 0 ? "Not used" : count <= 2 ? "Available" : "Core workflow";
}

function capitalize(value: string): string {
    return value.charAt(0).toUpperCase() + value.slice(1);
}
