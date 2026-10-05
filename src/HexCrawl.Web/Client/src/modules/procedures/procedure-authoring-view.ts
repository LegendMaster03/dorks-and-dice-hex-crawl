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
    ProcedureRevisionSummary,
    SavedProcedureSummary
} from "../../procedure-composer-types";
import type { ProcedurePreset } from "../../types";
import { clearUiError, showUiError } from "../../ui-error";
import { badge, openWorkspaceDrawer, textElement, type WorkspaceDrawer } from "../../ui/workspace";
import { executionSummary, inputSourceLabel, saveBlocked, withBehavior, withParameter } from "./procedure-composer-model";
import {
    compactModuleSummary,
    compactParameter,
    compactRule,
    compactRuleCatalog,
    durationToTicks,
    formatDurationTicks,
    friendlyStoredValue,
    parameterDefinitions,
    ticksToDuration
} from "./procedure-presentation";

export type ProcedureAuthoringMode = "compact" | "advanced" | "json";
type EntryState = "landing" | "presets" | "workspace";

const modeStorageKey = "hex-crawl.procedure-authoring.mode";

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
    let savedProcedures: SavedProcedureSummary[] = [];
    let revisions: ProcedureRevisionSummary[] = [];
    let savePending = false;
    let refreshSerial = 0;
    let activeDrawer: WorkspaceDrawer | null = null;
    const pending = new Map<string, ProcedureComposerOverrideInput>();
    const moduleSelections = new Map<string, boolean>();

    let jsonText = "";
    let jsonBaseline = "";
    let jsonBusy = false;
    let jsonLoadFailed = false;
    let jsonValidation: ProcedureCanonicalValidation | null = null;

    root.replaceChildren(loadingPanel("Loading exploration procedures…"));
    const composerApi = await ProcedureComposerApi.create(root);
    [presets, savedProcedures] = await Promise.all([
        api.getProcedurePresets(),
        composerApi.listProcedures()
    ]);
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

    const resetEdits = (): void => {
        pending.clear();
        moduleSelections.clear();
        jsonText = "";
        jsonBaseline = "";
        jsonLoadFailed = false;
        jsonValidation = null;
    };

    const closeDrawer = (): void => {
        const active = activeDrawer;
        activeDrawer = null;
        active?.close();
    };

    const loadWorkspace = async (): Promise<void> => {
        if (sourceProcedureId) {
            revisions = await composerApi.listRevisions(sourceProcedureId);
            latestRevision = revisions.reduce((max, value) => Math.max(max, value.revision), 0) || null;
        } else {
            revisions = [];
            latestRevision = null;
        }
        draft = await composerApi.composeDraft(currentInput());
        jsonText = "";
        jsonBaseline = "";
        jsonLoadFailed = false;
        jsonValidation = null;
        if (mode === "json") {
            await loadJson();
            if (jsonLoadFailed) return;
        }
        if (!disposed) render();
    };

    const refreshDraft = async (): Promise<void> => {
        const serial = ++refreshSerial;
        try {
            const next = await composerApi.composeDraft(currentInput());
            if (disposed || serial !== refreshSerial) return;
            draft = next;
            jsonText = "";
            jsonBaseline = "";
            jsonLoadFailed = false;
            jsonValidation = null;
            if (!refreshCompactDrawer(next)) render();
        } catch (error) {
            const target = root.querySelector<HTMLElement>("[data-error]");
            if (target) showUiError(target, error);
        }
    };

    const setModule = async (moduleKey: string, included: boolean): Promise<void> => {
        moduleSelections.set(moduleKey, included);
        pending.delete(moduleKey);
        closeDrawer();
        await refreshDraft();
    };

    const setOverride = async (value: ProcedureComposerOverrideInput): Promise<void> => {
        pending.set(value.moduleKey, value);
        await refreshDraft();
    };

    const loadJson = async (): Promise<void> => {
        if (!draft || jsonBusy || jsonText) return;
        let failure: unknown | null = null;
        jsonBusy = true;
        jsonLoadFailed = false;
        try {
            const canonical = await composerApi.composeCanonicalDraft(currentInput());
            if (disposed) return;
            jsonText = canonical.canonicalJson;
            jsonBaseline = canonical.canonicalJson;
            jsonValidation = await composerApi.validateCanonical(jsonText);
        } catch (value) {
            failure = value;
            jsonLoadFailed = true;
        } finally {
            jsonBusy = false;
            if (!disposed) {
                render();
                if (failure !== null) {
                    const target = root.querySelector<HTMLElement>("[data-error]");
                    if (target) showUiError(target, failure);
                }
            }
        }
    };

    const selectPreset = async (preset: ProcedurePreset): Promise<void> => {
        resetEdits();
        sourcePresetKey = preset.presetKey;
        sourceProcedureId = null;
        viewedRevision = null;
        entry = "workspace";
        await loadWorkspace();
    };

    const startCustom = async (): Promise<void> => {
        resetEdits();
        sourcePresetKey = null;
        sourceProcedureId = null;
        viewedRevision = null;
        entry = "workspace";
        await loadWorkspace();
    };

    const saveStructured = async (): Promise<void> => {
        if (!draft || savePending || historical()) return;
        const error = root.querySelector<HTMLElement>("[data-error]");
        if (error) clearUiError(error);
        let failure: unknown | null = null;
        savePending = true;
        render();
        try {
            const saved = sourceProcedureId
                ? await composerApi.createRevision(sourceProcedureId, {
                    expectedRevision: latestRevision ?? draft.revision,
                    moduleSelections: moduleSelectionInputs(moduleSelections),
                    overrides: [...pending.values()]
                })
                : await composerApi.createProcedure({
                    presetKey: sourcePresetKey,
                    moduleSelections: moduleSelectionInputs(moduleSelections),
                    overrides: [...pending.values()]
                });
            sourceProcedureId = saved.procedureId;
            sourcePresetKey = null;
            viewedRevision = null;
            resetEdits();
            savedProcedures = await composerApi.listProcedures();
            navigate(`/procedures/${encodeURIComponent(saved.procedureId)}`, true);
            await loadWorkspace();
        } catch (errorValue) {
            failure = errorValue;
        } finally {
            savePending = false;
            if (!disposed) {
                render();
                if (failure !== null) {
                    const target = root.querySelector<HTMLElement>("[data-error]");
                    if (target) showUiError(target, failure);
                }
            }
        }
    };

    const saveCanonical = async (): Promise<void> => {
        if (!draft || !jsonText || savePending || historical()) return;
        let failure: unknown | null = null;
        savePending = true;
        render();
        try {
            const validation = await composerApi.validateCanonical(jsonText);
            jsonValidation = validation;
            if (!validation.isValid) return;

            const saved = sourceProcedureId
                ? await composerApi.createCanonicalRevision(sourceProcedureId, {
                    expectedRevision: latestRevision ?? draft.revision,
                    canonicalJson: jsonText
                })
                : await composerApi.createCanonicalProcedure({
                    canonicalJson: jsonText,
                    presetKey: sourcePresetKey
                });
            sourceProcedureId = saved.procedureId;
            sourcePresetKey = null;
            viewedRevision = null;
            resetEdits();
            savedProcedures = await composerApi.listProcedures();
            navigate(`/procedures/${encodeURIComponent(saved.procedureId)}`, true);
            await loadWorkspace();
        } catch (errorValue) {
            failure = errorValue;
        } finally {
            savePending = false;
            if (!disposed) {
                render();
                if (failure !== null) {
                    const target = root.querySelector<HTMLElement>("[data-error]");
                    if (target) showUiError(target, failure);
                }
            }
        }
    };

    const changeMode = async (next: ProcedureAuthoringMode): Promise<void> => {
        mode = next;
        localStorage.setItem(modeStorageKey, next);
        closeDrawer();
        if (mode === "json") {
            await loadJson();
            if (jsonLoadFailed) return;
        }
        if (!disposed) render();
    };

    const hasUnsavedChanges = (): boolean =>
        hasStructuredChanges()
        || (jsonText.length > 0 && jsonText !== jsonBaseline);

    const hasSaveableChanges = (): boolean =>
        mode === "json"
            ? sourceProcedureId === null
                ? hasStructuredChanges() || (jsonText.length > 0 && jsonText !== jsonBaseline)
                : jsonText.length > 0 && jsonText !== jsonBaseline
            : hasStructuredChanges();

    const confirmDiscardChanges = (): boolean =>
        !hasUnsavedChanges()
        || window.confirm("Discard unsaved procedure changes?");

    const returnToProcedureHome = (): void => {
        if (!confirmDiscardChanges()) return;
        resetEdits();
        sourcePresetKey = null;
        sourceProcedureId = null;
        viewedRevision = null;
        latestRevision = null;
        revisions = [];
        draft = null;
        entry = "landing";
        render();
    };

    const onBeforeUnload = (event: BeforeUnloadEvent): void => {
        if (!hasUnsavedChanges()) return;
        event.preventDefault();
        event.returnValue = "";
    };
    window.addEventListener("beforeunload", onBeforeUnload);

    const render = (): void => {
        closeDrawer();
        if (entry === "landing") {
            renderHome();
            return;
        }
        if (entry === "presets") {
            renderCatalog();
            return;
        }
        renderWorkspace();
    };

    const renderHome = (): void => {
        const page = pageShell("Exploration Procedures", "Choose an existing procedure, build your own, or start from a familiar method.");
        const error = errorBox();
        page.append(error);

        const saved = document.createElement("section");
        saved.className = "hc-procedure-home-section";
        saved.append(sectionHeading("Saved procedures", "Campaign-owned procedures you can edit and use in an expedition."));
        if (savedProcedures.length === 0) {
            saved.append(emptyState("No saved procedures yet", "Create one below or start from a familiar procedure."));
        } else {
            const grid = document.createElement("div");
            grid.className = "hc-saved-procedure-grid";
            for (const value of savedProcedures) grid.append(savedProcedureCard(value));
            saved.append(grid);
        }
        page.append(saved);

        const build = document.createElement("section");
        build.className = "hc-procedure-home-section hc-build-custom";
        const copy = document.createElement("div");
        copy.append(
            textElement("h2", "Build my own"),
            textElement("p", "Start without hidden timing or movement assumptions. Add only the rules your table uses."));
        const action = button("Build a custom procedure", "primary");
        action.addEventListener("click", () => void startCustom());
        build.append(copy, action);
        page.append(build);

        const familiar = document.createElement("section");
        familiar.className = "hc-procedure-home-section";
        const heading = sectionHeading("Start from a known procedure", "Inspect the rules first, then materialize an editable campaign-owned copy.");
        const browse = button("Browse all presets", "secondary");
        browse.addEventListener("click", () => { entry = "presets"; render(); });
        heading.append(browse);
        familiar.append(heading);
        const grid = document.createElement("div");
        grid.className = "hc-preset-grid hc-preset-grid-featured";
        for (const preset of familiarPresets(presets).slice(0, 4)) grid.append(presetCard(preset));
        familiar.append(grid);
        page.append(familiar);
        root.replaceChildren(page);
    };

    const savedProcedureCard = (value: SavedProcedureSummary): HTMLElement => {
        const card = document.createElement("article");
        card.className = "hc-panel hc-saved-procedure-card";
        const meta = document.createElement("div");
        meta.className = "hc-card-meta";
        meta.append(
            badge(`Revision ${value.revision}`, "info"),
            badge(value.isExecutable ? "Executable" : "Structured", value.isExecutable ? "good" : "warning"));
        card.append(textElement("h3", value.name), meta);
        const origin = value.originPresetDisplayName
            ? `Started from ${value.originPresetDisplayName}`
            : "Built as a custom procedure";
        card.append(textElement("p", `${origin} · ${value.moduleCount} ${value.moduleCount === 1 ? "rule" : "rules"}`));
        const open = button("Open procedure", "secondary");
        open.addEventListener("click", () => navigate(`/procedures/${encodeURIComponent(value.procedureId)}`));
        card.append(open);
        return card;
    };

    const renderCatalog = (): void => {
        const page = pageShell("Procedure starting points", "Compare behavior using the same fields, inspect details, then choose a starting point.");
        const toolbar = document.createElement("div");
        toolbar.className = "hc-procedure-toolbar";
        const back = button("Back to procedures", "secondary");
        back.addEventListener("click", () => { entry = "landing"; render(); });
        toolbar.append(back);
        page.append(toolbar, errorBox());

        const familiar = familiarPresets(presets);
        if (familiar.length > 0) page.append(presetSection("Familiar procedures", familiar));
        const generic = genericPresets(presets);
        if (generic.length > 0) page.append(presetSection("Generic starting points", generic,
            "Small generic procedures useful when you want a minimal foundation rather than a named methodology."));
        root.replaceChildren(page);
    };

    const presetSection = (title: string, values: ProcedurePreset[], description?: string): HTMLElement => {
        const section = document.createElement("section");
        section.className = "hc-procedure-home-section";
        section.append(sectionHeading(title, description ?? "Use a starting point, then edit and save the procedure for your table."));
        const grid = document.createElement("div");
        grid.className = "hc-preset-grid";
        for (const preset of values) grid.append(presetCard(preset));
        section.append(grid);
        return section;
    };

    const presetCard = (preset: ProcedurePreset): HTMLElement => {
        const card = document.createElement("article");
        card.className = "hc-panel hc-preset-card";
        const head = document.createElement("div");
        head.className = "hc-preset-card-head";
        head.append(textElement("h3", preset.displayName), textElement("p", presetTagline(preset)));
        card.append(head, presetFacts(preset));
        const provenance = document.createElement("details");
        provenance.className = "hc-ux-disclosure hc-preset-provenance";
        provenance.innerHTML = `<summary>Source and provenance</summary>`;
        provenance.append(textElement("p", preset.attribution ?? "Generic Dorks & Dice procedure starting point."));
        if (preset.disclaimer) provenance.append(textElement("p", preset.disclaimer));
        card.append(provenance);
        const actions = document.createElement("footer");
        actions.className = "hc-preset-actions";
        const inspect = button("Inspect", "secondary");
        inspect.addEventListener("click", () => inspectPreset(preset));
        const use = button("Use as starting point", "primary");
        use.addEventListener("click", () => void selectPreset(preset));
        actions.append(inspect, use);
        card.append(actions);
        return card;
    };

    const inspectPreset = (preset: ProcedurePreset): void => {
        closeDrawer();
        activeDrawer = openWorkspaceDrawer(root, {
            title: preset.displayName,
            description: "Inspect the procedure before using it as the starting point for your table.",
            onClose: () => { activeDrawer = null; }
        });
        activeDrawer.body.append(presetFacts(preset));
        const behavior = document.createElement("section");
        behavior.className = "hc-focus-workspace-module";
        behavior.append(textElement("h3", "Procedure rules"));
        for (const module of preset.procedure.modules) {
            const row = document.createElement("div");
            row.className = "hc-inspect-rule";
            row.append(textElement("strong", module.moduleName), textElement("span", presetModuleSummary(module)));
            behavior.append(row);
        }
        activeDrawer.body.append(behavior);
        if (preset.attribution || preset.disclaimer) {
            const source = document.createElement("section");
            source.className = "hc-focus-workspace-module";
            source.append(textElement("h3", "Source and provenance"));
            if (preset.attribution) source.append(textElement("p", preset.attribution));
            if (preset.disclaimer) source.append(textElement("p", preset.disclaimer));
            activeDrawer.body.append(source);
        }
        const use = button("Use as starting point", "primary");
        use.addEventListener("click", () => void selectPreset(preset));
        activeDrawer.body.append(use);
    };

    const renderWorkspace = (): void => {
        if (!draft) {
            root.replaceChildren(loadingPanel("Loading procedure…"));
            return;
        }
        const page = pageShell(draft.name, "Edit the rules the DM will actually run. Compact, Advanced, and JSON edit the same campaign procedure.");
        const toolbar = document.createElement("section");
        toolbar.className = "hc-panel hc-procedure-toolbar";
        const left = document.createElement("div");
        left.className = "hc-mode-switcher";
        for (const value of ["compact", "advanced", "json"] as ProcedureAuthoringMode[]) {
            const control = button(value === "compact" ? "Compact" : value === "advanced" ? "Advanced" : "JSON", "secondary");
            control.setAttribute("aria-pressed", String(mode === value));
            control.addEventListener("click", () => void changeMode(value));
            left.append(control);
        }
        const right = document.createElement("div");
        right.className = "hc-preset-actions";
        const home = button("Procedure home", "secondary");
        home.addEventListener("click", returnToProcedureHome);

        if (sourceProcedureId && revisions.length > 0) {
            const procedureId = sourceProcedureId;
            const currentRevision = viewedRevision ?? latestRevision ?? draft.revision;
            const revisionField = document.createElement("label");
            revisionField.className = "hc-revision-picker";
            revisionField.append(textElement("span", "Revision"));
            const revisionSelect = document.createElement("select");
            revisionSelect.dataset.procedureRevision = "";
            for (const revision of [...revisions].sort((left, right) => right.revision - left.revision)) {
                const option = document.createElement("option");
                option.value = String(revision.revision);
                option.textContent = revision.revision === latestRevision
                    ? `Revision ${revision.revision} · latest`
                    : `Revision ${revision.revision}`;
                revisionSelect.append(option);
            }
            revisionSelect.value = String(currentRevision);
            revisionSelect.addEventListener("change", () => {
                const nextRevision = Number(revisionSelect.value);
                if (nextRevision === currentRevision) return;
                if (!confirmDiscardChanges()) {
                    revisionSelect.value = String(currentRevision);
                    return;
                }
                resetEdits();
                const base = `/procedures/${encodeURIComponent(procedureId)}`;
                navigate(nextRevision === latestRevision
                    ? base
                    : `${base}/revisions/${encodeURIComponent(String(nextRevision))}`);
            });
            revisionField.append(revisionSelect);
            left.append(revisionField);
        }

        const save = button(savePending ? "Saving…" : sourceProcedureId ? "Save new revision" : "Save procedure", "primary");
        save.dataset.procedureSave = "";
        save.disabled = savePending || historical() || !hasSaveableChanges();
        save.addEventListener("click", () => void (mode === "json" ? saveCanonical() : saveStructured()));
        right.append(home, save);
        toolbar.append(left, right);
        page.append(toolbar, errorBox());

        const strip = document.createElement("div");
        strip.className = "hc-procedure-summary-strip";
        strip.append(
            metric("Revision", historical() ? `${draft.revision} of ${latestRevision}` : String(draft.revision)),
            metric("Rules", String(draft.modules.length)),
            metric("Status", draft.isExecutable ? "Executable" : draft.modules.length === 0 ? "Choose structure" : "Structured / assisted"),
            metric("Origin", draft.origin?.presetDisplayName ?? "Custom"));
        page.append(strip);
        if (historical()) {
            page.append(notice("You are viewing a historical revision. Choose the latest revision above before saving further changes."));
        }

        if (mode === "compact") page.append(renderCompact());
        else if (mode === "advanced") page.append(renderAdvanced());
        else page.append(renderJson());
        root.replaceChildren(page);
    };

    const hasStructuredChanges = (): boolean =>
        pending.size > 0
        || moduleSelections.size > 0
        || (sourceProcedureId === null && (sourcePresetKey !== null || (draft?.modules.length ?? 0) > 0));

    const renderCompact = (): HTMLElement => {
        const shell = document.createElement("div");
        shell.className = "hc-procedure-shell hc-compact-procedure";
        if (!draft) return shell;
        shell.append(textElement("p", "Only the rules shown here are part of this procedure. Add optional rules when your table uses them."));

        if (draft.modules.length === 0) {
            const neutral = document.createElement("section");
            neutral.className = "hc-panel hc-neutral-procedure";
            neutral.append(
                textElement("h2", "Choose how expedition play is structured"),
                textElement("p", "Nothing is assumed yet. Add a repeating travel period, a journey process, movement rules, or any combination your table actually uses."));
            const choices = document.createElement("div");
            choices.className = "hc-add-rule-grid";
            for (const rule of compactRuleCatalog().filter(value => value.moduleKey === "time.interval" || value.moduleKey === "journey.process" || value.moduleKey === "movement.resolution")) {
                choices.append(addRuleCard(rule.moduleKey));
            }
            neutral.append(choices);
            shell.append(neutral);
        }

        const travel = activeRules("Travel flow");
        if (travel.length > 0) {
            const section = document.createElement("section");
            section.className = "hc-panel hc-table-procedure";
            section.append(textElement("h2", "At the table"), textElement("p", "Run these procedure steps in order when they apply."));
            const list = document.createElement("ol");
            list.className = "hc-procedure-step-list";
            for (const module of travel) list.append(compactRuleCard(module, true));
            section.append(list);
            shell.append(section);
        }

        for (const group of ["Survival & resources", "Journey process", "Procedure support"]) {
            const active = activeRules(group);
            const available = missingRules(group);
            if (active.length === 0 && available.length === 0) continue;
            const section = document.createElement("section");
            section.className = "hc-panel hc-rule-group";
            section.append(textElement("h2", group));
            if (group === "Survival & resources") {
                section.append(textElement("p", "Resource tracking, foraging, camping, forced travel, and persistent effects are independent rules. Use only the ones your table needs."));
            }
            const grid = document.createElement("div");
            grid.className = "hc-procedure-area-grid";
            for (const module of active) grid.append(compactRuleCard(module, false));
            section.append(grid);
            if (available.length > 0) {
                const add = document.createElement("div");
                add.className = "hc-add-rule-strip";
                add.append(textElement("strong", "Add rule"));
                for (const rule of available) {
                    const control = button(rule.label, "secondary");
                    control.addEventListener("click", () => void setModule(rule.moduleKey, true));
                    add.append(control);
                }
                section.append(add);
            }
            shell.append(section);
        }

        const remaining = compactRuleCatalog().filter(rule =>
            !draft!.modules.some(module => module.moduleKey === rule.moduleKey)
            && !["Survival & resources", "Journey process", "Procedure support"].includes(rule.group));
        if (remaining.length > 0 && draft.modules.length > 0) {
            const section = document.createElement("section");
            section.className = "hc-panel hc-additional-rules";
            section.append(textElement("h2", "Add another rule"));
            const strip = document.createElement("div");
            strip.className = "hc-add-rule-strip";
            for (const rule of remaining) {
                const control = button(rule.label, "secondary");
                control.addEventListener("click", () => void setModule(rule.moduleKey, true));
                strip.append(control);
            }
            section.append(strip);
            shell.append(section);
        }
        return shell;
    };

    const activeRules = (group: string): ProcedureModuleComposer[] =>
        (draft?.modules ?? [])
            .filter(module => compactRule(module.moduleKey)?.group === group)
            .sort((left, right) => (compactRule(left.moduleKey)?.order ?? 999) - (compactRule(right.moduleKey)?.order ?? 999));

    const missingRules = (group: string) => compactRuleCatalog().filter(rule =>
        rule.group === group && !draft?.modules.some(module => module.moduleKey === rule.moduleKey));

    const addRuleCard = (moduleKey: string): HTMLElement => {
        const rule = compactRule(moduleKey)!;
        const card = document.createElement("article");
        card.className = "hc-area-card hc-add-rule-card";
        card.append(textElement("h3", rule.label), textElement("p", rule.description));
        const action = button(`Add ${rule.label.toLowerCase()}`, "primary");
        action.addEventListener("click", () => void setModule(moduleKey, true));
        card.append(action);
        return card;
    };

    const compactRuleCard = (module: ProcedureModuleComposer, listItem: boolean): HTMLElement => {
        const descriptor = compactRule(module.moduleKey);
        const card = document.createElement(listItem ? "li" : "article");
        card.className = "hc-area-card hc-rule-card";
        const heading = document.createElement("div");
        heading.className = "hc-area-card-heading";
        heading.append(textElement("h3", descriptor?.label ?? module.displayName));
        if (module.isModified) heading.append(badge("Edited", "info"));
        card.append(heading, textElement("p", compactModuleSummary(module)));
        const facts = compactFacts(module);
        if (facts.childElementCount > 0) card.append(facts);
        const actions = document.createElement("div");
        actions.className = "hc-area-actions";
        const edit = button(`Edit ${descriptor?.label?.toLowerCase() ?? "rule"}`, "secondary");
        edit.addEventListener("click", () => editCompactModule(module));
        const remove = button("Remove", "secondary");
        remove.addEventListener("click", () => void setModule(module.moduleKey, false));
        actions.append(edit, remove);
        card.append(actions);
        return card;
    };

    const compactFacts = (module: ProcedureModuleComposer): HTMLElement => {
        const facts = document.createElement("dl");
        facts.className = "hc-rule-facts";
        let count = 0;
        for (const [key, definition] of parameterDefinitions(module)) {
            const presentation = compactParameter(key, definition, module.moduleKey);
            if (!presentation) continue;
            const value = module.parameters[key] ?? definition.defaultValue;
            if (value == null || value === "") continue;
            if (presentation.control === "boolean" && value === "false") continue;
            facts.append(textElement("dt", presentation.label), textElement("dd", key === "durationTicks" ? formatDurationTicks(value) : friendlyStoredValue(key, value)));
            count++;
            if (count >= 4) break;
        }
        return facts;
    };

    const compactGroup = (current: ProcedureComposer, section: string) => ({
        section,
        modules: current.modules.filter(module => module.moduleKey === section)
    });

    const populateCompactArea = (body: HTMLElement, modules: ProcedureModuleComposer[]): void => {
        body.replaceChildren();
        const module = modules[0];
        if (!module) {
            body.append(textElement("p", "This rule is no longer part of the procedure."));
            return;
        }

        if (module.alternatives.length > 1) {
            const field = document.createElement("label");
            field.className = "hc-compact-field";
            field.append(textElement("span", "Behavior"));
            const select = document.createElement("select");
            select.dataset.compactModule = module.moduleKey;
            select.dataset.compactField = "behavior";
            for (const option of module.alternatives) {
                const item = document.createElement("option");
                item.value = `${option.key}|${option.version}`;
                item.textContent = option.displayName;
                item.selected = option.key === module.mechanic.key && option.version === module.mechanic.version;
                select.append(item);
            }
            select.addEventListener("change", () => {
                const [key, version] = select.value.split("|");
                void setOverride(withBehavior(module, pending.get(module.moduleKey), key, Number(version)));
            });
            field.append(select);
            body.append(field);
        }

        const grid = document.createElement("div");
        grid.className = "hc-compact-field-grid";
        for (const [key, definition] of parameterDefinitions(module)) {
            const presentation = compactParameter(key, definition, module.moduleKey);
            if (!presentation) continue;
            const value = module.parameters[key] ?? definition.defaultValue ?? "";
            grid.append(compactEditor(module, key, value, presentation));
        }
        body.append(grid);
        const remove = button("Remove this rule", "secondary");
        remove.addEventListener("click", () => void setModule(module.moduleKey, false));
        body.append(remove);
    };

    const refreshCompactDrawer = (current: ProcedureComposer): boolean => {
        if (!activeDrawer || mode !== "compact") return false;
        const body = activeDrawer.body;
        const section = body.dataset.compactArea;
        if (!section) return false;
        const focused = document.activeElement instanceof HTMLElement ? document.activeElement : null;
        const moduleKey = focused?.dataset.compactModule ?? null;
        const fieldKey = focused?.dataset.compactField ?? null;
        const group = compactGroup(current, section);
        if (group.modules.length === 0) {
            closeDrawer();
            return false;
        }
        populateCompactArea(body, group.modules);
        const save = root.querySelector<HTMLButtonElement>("[data-procedure-save]");
        if (save) save.disabled = savePending || historical() || !hasStructuredChanges();
        if (moduleKey && fieldKey) {
            const replacement = [...body.querySelectorAll<HTMLElement>("[data-compact-module][data-compact-field]")]
                .find(control => control.dataset.compactModule === moduleKey && control.dataset.compactField === fieldKey);
            replacement?.focus();
        }
        return true;
    };

    const editCompactModule = (module: ProcedureModuleComposer): void => {
        closeDrawer();
        const descriptor = compactRule(module.moduleKey);
        const group = compactGroup(draft!, module.moduleKey);
        activeDrawer = openWorkspaceDrawer(root, {
            title: descriptor?.label ?? module.displayName,
            description: descriptor?.description ?? module.purpose,
            onClose: () => { activeDrawer = null; }
        });
        const open = activeDrawer.body;
        open.dataset.compactArea = group.section;
        populateCompactArea(open, group.modules);
    };

    const compactEditor = (
        module: ProcedureModuleComposer,
        key: string,
        value: string,
        presentation: ReturnType<typeof compactParameter> extends infer T ? Exclude<T, null> : never): HTMLElement => {
        const field = document.createElement("label");
        field.className = "hc-compact-field";
        field.append(textElement("span", presentation.label));
        const commit = (next: string): void => {
            void setOverride(withParameter(module, pending.get(module.moduleKey), key, next));
        };
        const mark = <T extends HTMLElement>(control: T): T => {
            control.dataset.compactModule = module.moduleKey;
            control.dataset.compactField = key;
            return control;
        };

        if (presentation.control === "summary") {
            const output = document.createElement("div");
            output.className = "hc-readonly-domain-value";
            output.textContent = friendlyStoredValue(key, value);
            field.append(output, textElement("small", "This specialized setting can be edited in Advanced."));
            return field;
        }
        if (presentation.control === "duration") {
            const parsed = ticksToDuration(value) ?? { amount: 1, unit: "hours" as const };
            const row = document.createElement("div");
            row.className = "hc-duration-control";
            const amount = document.createElement("input");
            amount.type = "number";
            amount.min = "0.01";
            amount.step = "any";
            amount.value = String(parsed.amount);
            mark(amount);
            const unit = document.createElement("select");
            mark(unit);
            unit.dataset.compactField = `${key}:unit`;
            for (const name of ["minutes", "hours", "days"] as const) {
                const option = document.createElement("option");
                option.value = name;
                option.textContent = name;
                option.selected = name === parsed.unit;
                unit.append(option);
            }
            const update = () => commit(durationToTicks(Number(amount.value), unit.value as "minutes" | "hours" | "days"));
            amount.addEventListener("change", update);
            unit.addEventListener("change", update);
            row.append(amount, unit);
            field.append(row);
        } else if (presentation.control === "boolean") {
            const input = document.createElement("input");
            input.type = "checkbox";
            input.checked = value.toLowerCase() === "true";
            mark(input);
            input.addEventListener("change", () => commit(String(input.checked)));
            field.append(input);
        } else if (presentation.control === "select") {
            const select = document.createElement("select");
            mark(select);
            const choices = [...(presentation.choices ?? [])];
            if (!choices.some(choice => choice.value === value) && value) {
                choices.unshift({ value, label: friendlyStoredValue(key, value) });
            }
            for (const choice of choices) {
                const option = document.createElement("option");
                option.value = choice.value;
                option.textContent = choice.label;
                option.selected = choice.value === value;
                select.append(option);
            }
            select.addEventListener("change", () => commit(select.value));
            field.append(select);
        } else if (presentation.control === "key-list") {
            const input = document.createElement("textarea");
            input.rows = 3;
            mark(input);
            input.value = value.split(";").join(", ");
            input.addEventListener("change", () => commit(input.value.split(",").map(item => item.trim()).filter(Boolean).join(";")));
            field.append(input);
        } else if (presentation.control === "mapping") {
            const input = document.createElement("textarea");
            input.rows = 4;
            mark(input);
            input.value = value.split(";").map(item => item.replace("=", ": ")).join("\n");
            input.addEventListener("change", () => commit(input.value.split(/\r?\n/).map(line => {
                const separator = line.indexOf(":");
                return separator < 0 ? line.trim() : `${line.slice(0, separator).trim()}=${line.slice(separator + 1).trim()}`;
            }).filter(Boolean).join(";")));
            field.append(input);
        } else {
            const input = document.createElement("input");
            input.type = presentation.control === "number" ? "number" : "text";
            mark(input);
            input.value = value;
            input.addEventListener("change", () => commit(input.value));
            field.append(input);
        }
        if (presentation.help) field.append(textElement("small", presentation.help));
        return field;
    };

    const renderAdvanced = (): HTMLElement => {
        const layout = document.createElement("div");
        layout.className = "hc-advanced-layout";
        const index = document.createElement("aside");
        index.className = "hc-panel hc-advanced-index";
        index.append(textElement("h2", "Procedure structure"), textElement("p", "Advanced exposes exact generic module keys, mechanics, versions, parameters, and contracts."));
        for (const rule of compactRuleCatalog()) {
            const label = document.createElement("label");
            label.className = "hc-advanced-module-toggle";
            const input = document.createElement("input");
            input.type = "checkbox";
            input.checked = draft?.modules.some(module => module.moduleKey === rule.moduleKey) ?? false;
            input.addEventListener("change", () => void setModule(rule.moduleKey, input.checked));
            label.append(input, textElement("span", rule.label), code(rule.moduleKey));
            index.append(label);
        }
        layout.append(index);

        const content = document.createElement("main");
        content.className = "hc-procedure-shell";
        for (const module of draft?.modules ?? []) content.append(advancedModule(module));
        if ((draft?.modules.length ?? 0) === 0) content.append(emptyState("No modules selected", "Select modules in the structure inspector."));
        layout.append(content);
        return layout;
    };

    const advancedModule = (module: ProcedureModuleComposer): HTMLElement => {
        const card = document.createElement("section");
        card.className = "hc-panel hc-advanced-module";
        const heading = document.createElement("header");
        heading.className = "hc-advanced-module-heading";
        const identity = document.createElement("div");
        identity.append(textElement("h2", module.displayName), code(module.moduleKey));
        identity.append(textElement("p", module.purpose));
        heading.append(identity, badge(module.mechanic.executionSupport, module.mechanic.executionSupport === "Unsupported" ? "danger" : "info"));
        card.append(heading);

        const mechanic = document.createElement("section");
        mechanic.className = "hc-advanced-section";
        mechanic.append(textElement("h3", "Mechanic"));
        const select = document.createElement("select");
        for (const option of module.alternatives) {
            const item = document.createElement("option");
            item.value = `${option.key}|${option.version}`;
            item.textContent = `${option.displayName} · ${option.key} · v${option.version}`;
            item.selected = option.key === module.mechanic.key && option.version === module.mechanic.version;
            select.append(item);
        }
        select.addEventListener("change", () => {
            const [key, version] = select.value.split("|");
            void setOverride(withBehavior(module, pending.get(module.moduleKey), key, Number(version)));
        });
        mechanic.append(select, textElement("p", `${module.mechanic.key} · v${module.mechanic.version} · ${module.mechanic.automationLevel}`), textElement("p", executionSummary(module)));
        card.append(mechanic);

        const parameters = document.createElement("section");
        parameters.className = "hc-advanced-section";
        parameters.append(textElement("h3", "Parameters"));
        const grid = document.createElement("div");
        grid.className = "hc-advanced-parameter-grid";
        for (const [key, definition] of parameterDefinitions(module)) {
            const field = document.createElement("label");
            field.className = "hc-compact-field";
            field.append(code(key));
            const input = document.createElement("input");
            input.type = "text";
            input.value = module.parameters[key] ?? definition.defaultValue ?? "";
            input.addEventListener("change", () => void setOverride(withParameter(module, pending.get(module.moduleKey), key, input.value)));
            field.append(input);
            if (definition.description) field.append(textElement("small", definition.description));
            grid.append(field);
        }
        parameters.append(grid);
        card.append(parameters);

        const contracts = document.createElement("div");
        contracts.className = "hc-advanced-contracts";
        contracts.append(contractBlock("Inputs", module.requiredInputs.map(input => `${input.inputKey} — ${input.allowedSources.map(inputSourceLabel).join(", ")}`)),
            contractBlock("Produces", module.outputs),
            contractBlock("Dependencies", [...module.requiredDependencies.map(value => `Required: ${value}`), ...module.optionalDependencies.map(value => `Optional: ${value}`)]),
            contractBlock("Diagnostics", [...module.validationIssues, ...module.dependencyIssues.map(issue => issue.message)]));
        card.append(contracts);
        return card;
    };

    const renderJson = (): HTMLElement => {
        const section = document.createElement("section");
        section.className = "hc-panel hc-json-editor";
        section.append(textElement("h2", "Canonical CampaignProcedure JSON"), textElement("p", "JSON edits the exact same CampaignProcedure produced by Compact structural choices and Advanced edits. Server parsing, domain validation, identity, revisions, and optimistic concurrency remain authoritative."));
        if (jsonBusy || !jsonText) {
            const retry = button(jsonBusy ? "Loading…" : jsonLoadFailed ? "Retry canonical JSON" : "Load canonical JSON", "secondary");
            retry.dataset.jsonRetry = "";
            retry.disabled = jsonBusy;
            retry.addEventListener("click", () => void loadJson());
            section.append(retry);
            return section;
        }
        const textarea = document.createElement("textarea");
        textarea.value = jsonText;
        textarea.spellcheck = false;
        textarea.addEventListener("input", () => { jsonText = textarea.value; jsonValidation = null; });
        section.append(textarea);
        const actions = document.createElement("div");
        actions.className = "hc-json-actions";
        const validate = button("Validate JSON", "secondary");
        validate.addEventListener("click", () => void (async () => {
            const error = root.querySelector<HTMLElement>("[data-error]");
            if (error) clearUiError(error);
            try {
                jsonValidation = await composerApi.validateCanonical(jsonText);
                render();
            } catch (value) {
                if (error) showUiError(error, value);
            }
        })());
        const reset = button("Reset draft", "secondary");
        reset.disabled = jsonText === jsonBaseline;
        reset.addEventListener("click", () => { jsonText = jsonBaseline; jsonValidation = null; render(); });
        actions.append(validate, reset);
        section.append(actions);
        const status = document.createElement("p");
        status.className = `hc-json-status${jsonValidation?.isValid === false ? " is-error" : jsonValidation?.isValid ? " is-valid" : ""}`;
        status.setAttribute("role", "status");
        status.textContent = jsonValidation
            ? jsonValidation.isValid
                ? "Canonical JSON is valid."
                : `${jsonValidation.error ?? "Canonical JSON is invalid."}${jsonValidation.lineNumber != null ? ` (line ${jsonValidation.lineNumber + 1})` : ""}`
            : "Validate after editing before saving.";
        section.append(status);
        return section;
    };

    if (entry === "workspace") await loadWorkspace();
    else render();

    return () => {
        disposed = true;
        window.removeEventListener("beforeunload", onBeforeUnload);
        closeDrawer();
        root.classList.remove("hc-phase15");
    };
}

function presetFacts(preset: ProcedurePreset): HTMLElement {
    const facts = document.createElement("dl");
    facts.className = "hc-preset-facts";
    const summary = presetComparison(preset);
    for (const [label, value] of Object.entries(summary)) {
        facts.append(textElement("dt", label), textElement("dd", value));
    }
    return facts;
}

function presetComparison(preset: ProcedurePreset): Record<string, string> {
    const modules = new Map(preset.procedure.modules.map(module => [module.moduleKey, module]));
    const parameter = (moduleKey: string, key: string): string | null => modules.get(moduleKey)?.parameters[key] ?? null;
    const interval = preset.procedure.runtime?.intervalHours;
    const time = interval == null ? "No fixed interval" : interval === 24 ? "Daily" : interval === 1 ? "Hourly" : `${formatNumber(interval)} hours`;
    const movement = modules.has("movement.terrain")
        ? "Terrain-adjusted"
        : parameter("movement.resolution", "travelResolution") === "HexSteps" ? "Whole cell steps"
            : modules.has("movement.resolution") ? "Distance travel" : "Not used";
    const navigation = modules.has("navigation.outcome")
        ? "Getting lost / recovery"
        : parameter("navigation.check", "usesNavigationChecks") === "true" ? "Route checks" : "Not used";
    const encounters = modules.has("encounters.schedule")
        ? "Scheduled / contextual"
        : parameter("encounters.cadence", "cadence") === "PerWatch" ? "Each travel period"
            : parameter("encounters.cadence", "cadence") === "PerDay" ? "Daily" : "Not used";
    const resourceParts = [
        modules.has("survival.resources") ? "food / water" : null,
        modules.has("exploration.foraging") ? "foraging" : null,
        modules.has("survival.camping") ? "camping" : null,
        modules.has("time.forced-travel") ? "forced travel" : null
    ].filter((value): value is string => value !== null);
    return {
        Workflow: modules.has("journey.process") ? "Journey / staged process" : modules.has("time.interval") ? "Interval travel" : "Procedure-driven",
        Time: time,
        Movement: movement,
        Navigation: navigation,
        Activities: modules.has("party.activities") ? "Party activities / roles" : "Not used",
        Encounters: encounters,
        Resources: resourceParts.length > 0 ? resourceParts.join(", ") : "Not used",
        Journey: modules.has("journey.process") ? "Multi-stage journey" : modules.has("journey.events") ? "Journey events" : "Not used"
    };
}

function presetTagline(preset: ProcedurePreset): string {
    const summary = presetComparison(preset);
    return `${summary.Workflow} · ${summary.Time}`;
}

function presetModuleSummary(module: ProcedurePreset["procedure"]["modules"][number]): string {
    const entries = Object.entries(module.parameters).slice(0, 3).map(([key, value]) => friendlyStoredValue(key, value));
    return entries.length > 0 ? entries.join(" · ") : `${module.automationLevel} behavior`;
}

function familiarPresets(values: ProcedurePreset[]): ProcedurePreset[] {
    return values.filter(value => !value.presetKey.startsWith("simple-"));
}

function genericPresets(values: ProcedurePreset[]): ProcedurePreset[] {
    return values.filter(value => value.presetKey.startsWith("simple-"));
}

function moduleSelectionInputs(values: Map<string, boolean>): ProcedureComposerModuleSelectionInput[] {
    return [...values.entries()].map(([moduleKey, included]) => ({ moduleKey, included }));
}

function pageShell(title: string, description: string): HTMLElement {
    const page = document.createElement("div");
    page.className = "hc-page hc-procedure-shell";
    const header = document.createElement("header");
    header.className = "hc-page-header";
    const copy = document.createElement("div");
    copy.append(textElement("h1", title), textElement("p", description));
    header.append(copy);
    page.append(header);
    return page;
}

function sectionHeading(title: string, description: string): HTMLElement {
    const heading = document.createElement("header");
    heading.className = "hc-preset-browser-header";
    const copy = document.createElement("div");
    copy.append(textElement("h2", title), textElement("p", description));
    heading.append(copy);
    return heading;
}

function errorBox(): HTMLElement {
    const error = document.createElement("div");
    error.className = "hc-error";
    error.dataset.error = "";
    error.hidden = true;
    error.setAttribute("role", "alert");
    return error;
}

function loadingPanel(message: string): HTMLElement {
    const panel = document.createElement("section");
    panel.className = "hc-loading-panel";
    panel.append(textElement("p", message));
    return panel;
}

function emptyState(title: string, detail: string): HTMLElement {
    const state = document.createElement("div");
    state.className = "hc-empty-state";
    state.append(textElement("strong", title), textElement("span", detail));
    return state;
}

function notice(message: string): HTMLElement {
    const value = document.createElement("p");
    value.className = "hc-domain-diagnostic";
    value.textContent = message;
    return value;
}

function metric(label: string, value: string): HTMLElement {
    const item = document.createElement("div");
    item.className = "hc-summary-metric";
    item.append(textElement("span", label), textElement("strong", value));
    return item;
}

function contractBlock(title: string, values: string[]): HTMLElement {
    const block = document.createElement("section");
    block.className = "hc-focus-workspace-module";
    block.append(textElement("h4", title));
    if (values.length === 0) block.append(textElement("p", "None"));
    else {
        const list = document.createElement("ul");
        for (const value of values) list.append(textElement("li", value));
        block.append(list);
    }
    return block;
}

function code(value: string): HTMLElement {
    const output = document.createElement("code");
    output.textContent = value;
    return output;
}

function button(label: string, kind: "primary" | "secondary"): HTMLButtonElement {
    const value = document.createElement("button");
    value.type = "button";
    value.className = kind === "primary" ? "hc-primary" : "hc-secondary";
    value.textContent = label;
    return value;
}

function readMode(): ProcedureAuthoringMode {
    const value = localStorage.getItem(modeStorageKey);
    return value === "advanced" || value === "json" ? value : "compact";
}

function formatNumber(value: number): string {
    return Number.isInteger(value) ? String(value) : value.toFixed(2).replace(/0+$/, "").replace(/\.$/, "");
}
