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
import { applyGuidedExperience, guidedCallout, guidedDisclosure, guidancePreferenceButton } from "../../ui/guidance";
import { executionSummary, inputSourceLabel, saveBlocked, withBehavior, withParameter } from "./procedure-composer-model";
import { presetGuidance } from "./preset-guidance";
import {
    compactParameter,
    compactRule,
    compactRuleCatalog,
    durationToTicks,
    formatDurationTicks,
    friendlyStoredValue,
    parameterDefinitions,
    procedurePresentationSections,
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
    applyGuidedExperience(root);

    let disposed = false;
    let entry: EntryState = procedureId ? "workspace" : "landing";
    let mode = readMode();
    let sourcePresetKey: string | null = null;
    let sourceProcedureId = procedureId;
    let viewedRevision = requestedRevision;
    let latestRevision: number | null = null;
    let baselineName: string | null = null;
    let nameOverride: string | null = null;
    let draft: ProcedureComposer | null = null;
    let presets: ProcedurePreset[] = [];
    let savedProcedures: SavedProcedureSummary[] = [];
    let revisions: ProcedureRevisionSummary[] = [];
    let savePending = false;
    let refreshSerial = 0;
    let activeDrawer: WorkspaceDrawer | null = null;
    let advancedSelectedModuleKey: string | null = null;
    const pending = new Map<string, ProcedureComposerOverrideInput>();
    const moduleSelections = new Map<string, boolean>();

    let jsonText = "";
    let jsonBaseline = "";
    let jsonBusy = false;
    let jsonLoadFailed = false;
    let jsonValidation: ProcedureCanonicalValidation | null = null;

    root.replaceChildren(loadingPanel("Loading exploration rulesets…"));
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
        name: nameOverride,
        moduleSelections: [...moduleSelections.entries()].map(([moduleKey, included]) => ({ moduleKey, included })),
        overrides: [...pending.values()]
    });

    const resetEdits = (): void => {
        pending.clear();
        moduleSelections.clear();
        nameOverride = null;
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
        baselineName = draft.name;
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

    const setModules = async (values: Array<[string, boolean]>): Promise<void> => {
        for (const [moduleKey, included] of values) {
            moduleSelections.set(moduleKey, included);
            pending.delete(moduleKey);
        }
        closeDrawer();
        await refreshDraft();
    };

    const setModule = async (moduleKey: string, included: boolean): Promise<void> =>
        setModules([[moduleKey, included]]);

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
                    name: nameOverride,
                    moduleSelections: moduleSelectionInputs(moduleSelections),
                    overrides: [...pending.values()]
                })
                : await composerApi.createProcedure({
                    presetKey: sourcePresetKey,
                    name: nameOverride,
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
        if (mode === "json"
            && next !== "json"
            && jsonText.length > 0
            && jsonText !== jsonBaseline) {
            if (!window.confirm("Discard unsaved JSON changes?")) return;
            jsonText = "";
            jsonBaseline = "";
            jsonValidation = null;
            jsonLoadFailed = false;
        }

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

    const hasBlockingStructuredIssues = (): boolean =>
        draft?.dependencies.hasErrors === true
        || (draft?.modules.some(saveBlocked) ?? false);

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
        baselineName = null;
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
        const page = pageShell("Exploration rulesets", "Choose a saved ruleset, build your own, or start from a familiar method.");
        const error = errorBox();
        page.querySelector<HTMLElement>(".hc-page-header")?.append(guidancePreferenceButton(root));
        page.append(error);
        page.append(guidedCallout(
            "New to exploration rulesets?",
            "Rulesets define how your table handles exploration. Start from a preset or build your own; either way, saving creates a campaign copy you can edit."));

        const saved = document.createElement("section");
        saved.className = "hc-procedure-home-section";
        saved.append(sectionHeading("Saved rulesets", "Campaign-owned procedures you can edit and use in an expedition."));
        if (savedProcedures.length === 0) {
            saved.append(emptyState("No saved rulesets yet", "Create one below or start from a familiar ruleset."));
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
        const action = button("Build a custom ruleset", "primary");
        action.addEventListener("click", () => void startCustom());
        build.append(copy, action);
        page.append(build);

        const familiar = document.createElement("section");
        familiar.className = "hc-procedure-home-section";
        const heading = sectionHeading("Start from a known ruleset", "Compare the rules first, then save an editable campaign copy.");
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
        const head = document.createElement("header");
        head.className = "hc-saved-procedure-head";
        const meta = document.createElement("div");
        meta.className = "hc-card-meta";
        meta.append(
            badge(`Revision ${value.revision}`, "info"),
            badge(value.isExecutable ? "Executable" : "Structured", value.isExecutable ? "good" : "warning"));
        head.append(textElement("h3", value.name), meta);
        card.append(head);

        const summary = document.createElement("dl");
        summary.className = "hc-saved-procedure-summary";
        summary.append(
            textElement("dt", "Origin"),
            textElement("dd", value.originPresetDisplayName ? `Started from ${value.originPresetDisplayName}` : "Custom"),
            textElement("dt", "Rules"),
            textElement("dd", `${value.moduleCount} ${value.moduleCount === 1 ? "rule" : "rules"}`));
        card.append(summary);

        const actions = document.createElement("footer");
        actions.className = "hc-saved-procedure-actions";
        const open = button("Open", "secondary");
        open.addEventListener("click", () => navigate(`/procedures/${encodeURIComponent(value.procedureId)}`));
        actions.append(open);
        card.append(actions);
        return card;
    };

    const renderCatalog = (): void => {
        const page = pageShell("Ruleset starting points", "Compare the table experience first. Open details only when you need exact mechanics, automation, or source information.");
        const toolbar = document.createElement("div");
        toolbar.className = "hc-procedure-toolbar";
        const back = button("Back to rulesets", "secondary");
        back.addEventListener("click", () => { entry = "landing"; render(); });
        toolbar.append(back, guidancePreferenceButton(root));
        page.append(toolbar, errorBox());
        page.append(guidedCallout(
            "Choosing a ruleset",
            "Compare the four facts on each card. Use View details for exact rules and source information; partial or manual support is flagged before selection."));

        const familiar = familiarPresets(presets);
        if (familiar.length > 0) page.append(presetSection("Familiar exploration rulesets", familiar));
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
        const guidance = presetGuidance(preset.procedure);
        card.append(head, presetDecisionFacts(guidance));
        if (guidance.caution) {
            card.append(textElement("p", guidance.caution, "hc-preset-caution"));
        }
        const actions = document.createElement("footer");
        actions.className = "hc-preset-actions";
        const inspect = button("View details", "secondary");
        inspect.addEventListener("click", () => inspectPreset(preset));
        const use = button("Use this preset", "primary");
        use.addEventListener("click", () => void selectPreset(preset));
        actions.append(inspect, use);
        card.append(actions);
        return card;
    };

    const inspectPreset = (preset: ProcedurePreset): void => {
        closeDrawer();
        activeDrawer = openWorkspaceDrawer(root, {
            title: preset.displayName,
            description: "Inspect the ruleset before using it as the starting point for your table.",
            onClose: () => { activeDrawer = null; }
        });
        activeDrawer.body.append(presetFacts(preset), presetGuidanceCard(preset));
        const behavior = document.createElement("section");
        behavior.className = "hc-focus-workspace-module hc-inspect-rules";
        behavior.append(textElement("h3", "Ruleset rules"));
        for (const module of preset.procedure.modules) {
            const row = document.createElement("article");
            row.className = "hc-inspect-rule";
            const descriptor = compactRule(module.moduleKey);
            row.append(textElement("h4", descriptor?.label ?? module.moduleName));
            if (descriptor?.description) row.append(textElement("p", descriptor.description));
            const facts = renderPresentationFactGroups(module.moduleKey, module.parameters, "hc-inspect-rule-facts");
            if (facts.childElementCount > 0) row.append(facts);
            else row.append(textElement("p", "No additional table-facing values are configured."));
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
        const use = button("Use this preset", "primary");
        use.addEventListener("click", () => void selectPreset(preset));
        activeDrawer.body.append(use);
    };

    const displayedName = (): string =>
        nameOverride ?? baselineName ?? draft?.name ?? "Exploration procedure";

    const renderWorkspace = (): void => {
        if (!draft) {
            root.replaceChildren(loadingPanel("Loading procedure…"));
            return;
        }
        const page = pageShell(displayedName(), "Edit the rules the DM will actually run. Compact, Advanced, and JSON all edit the same saved ruleset.");
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
        left.append(guidancePreferenceButton(root));
        const right = document.createElement("div");
        right.className = "hc-preset-actions";
        const home = button("Ruleset home", "secondary");
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

        const save = button(savePending ? "Saving…" : sourceProcedureId ? "Save new revision" : "Save ruleset", "primary");
        save.dataset.procedureSave = "";
        save.disabled = savePending
            || historical()
            || !hasSaveableChanges()
            || (mode !== "json" && hasBlockingStructuredIssues());
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

        if (mode !== "json") {
            const identity = document.createElement("section");
            identity.className = "hc-panel hc-procedure-identity";
            const nameField = document.createElement("label");
            nameField.className = "hc-compact-field";
            nameField.append(textElement("span", "Ruleset name"));
            const nameInput = document.createElement("input");
            nameInput.type = "text";
            nameInput.name = "procedureName";
            nameInput.value = displayedName();
            nameInput.disabled = savePending || historical();
            nameInput.addEventListener("change", () => {
                const next = nameInput.value.trim();
                const error = root.querySelector<HTMLElement>("[data-error]");
                if (!next) {
                    nameInput.value = displayedName();
                    if (error) showUiError(error, new Error("Ruleset name is required."));
                    return;
                }
                if (error) clearUiError(error);
                nameOverride = next === baselineName ? null : next;
                render();
            });
            nameField.append(nameInput);
            identity.append(nameField);
            page.append(identity);
        }

        if (historical()) {
            page.append(notice("You are viewing a historical revision. Choose the latest revision above before saving further changes."));
        }
        if (mode !== "json" && hasBlockingStructuredIssues()) {
            const blocking = draft.dependencies.issues.filter(issue =>
                issue.kind === "MissingRequiredModule"
                || issue.kind === "MissingRequiredProducer"
                || issue.kind === "IncompatibleMechanic");
            const affected = [...new Set(blocking
                .map(issue => compactRule(issue.moduleKey)?.label)
                .filter((label): label is string => Boolean(label)))];
            const fix = draft.dependencyFixes[0] ?? null;
            const required = fix?.moduleKeys
                .map(moduleKey => compactRule(moduleKey)?.label ?? moduleKey)
                ?? [];
            const subject = affected.length > 0 ? affected.join(", ") : "The selected rules";
            const warning = notice(required.length > 0
                ? `${subject} need ${required.join(", ")} before this procedure can be saved.`
                : `${subject} still need a compatible companion rule or input before this procedure can be saved.`);
            if (fix && required.length > 0) {
                const repairLabel = required.length <= 2
                    ? `Add ${required.join(" + ")}`
                    : "Add required companion rules";
                const repair = button(repairLabel, "secondary");
                repair.title = `Add required rules: ${required.join(", ")}`;
                repair.addEventListener("click", () => void setModules(
                    fix.moduleKeys.map(moduleKey => [moduleKey, true] as [string, boolean])));
                warning.append(document.createTextNode(" "), repair);
            }
            page.append(warning);
        }

        if (mode === "compact") page.append(renderCompact());
        else if (mode === "advanced") page.append(renderAdvanced());
        else page.append(renderJson());
        root.replaceChildren(page);
    };

    const hasStructuredChanges = (): boolean =>
        pending.size > 0
        || moduleSelections.size > 0
        || nameOverride !== null
        || (sourceProcedureId === null && (sourcePresetKey !== null || (draft?.modules.length ?? 0) > 0));

    const renderCompact = (): HTMLElement => {
        const shell = document.createElement("div");
        shell.className = "hc-procedure-shell hc-compact-procedure";
        if (!draft) return shell;
        shell.append(textElement("p", "These are the rules the DM runs. Add optional rules only when the table uses them."));
        shell.append(guidedCallout(
            "How to use Compact",
            "Compact shows the procedure in tabletop terms. Each card is one rule your table may apply during exploration; you do not need to understand mechanic IDs or dependency keys.",
            [
                "Read At the table first for the ordinary travel loop.",
                "Open a rule only when you need to change how it behaves.",
                "Add optional survival, journey, or automation rules only when your table actually uses them."
            ]));

        if (draft.modules.length === 0) {
            const neutral = document.createElement("section");
            neutral.className = "hc-panel hc-neutral-procedure";
            neutral.append(
                textElement("h2", "Choose a starting rule"),
                textElement("p", "Nothing is assumed yet. Start with a repeating travel period or spatial movement, or browse the grouped rule library below for a journey-first or nonspatial procedure."));
            const choices = document.createElement("div");
            choices.className = "hc-add-rule-grid";
            for (const rule of compactRuleCatalog().filter(value =>
                value.moduleKey === "time.interval" || value.moduleKey === "movement.resolution")) {
                choices.append(addRuleCard(rule.moduleKey));
            }
            neutral.append(choices);
            shell.append(neutral);
        }

        const travel = activeRules("Travel flow");
        if (travel.length > 0) {
            const section = document.createElement("section");
            section.className = "hc-panel hc-table-procedure";
            section.append(
                textElement("h2", "At the table"),
                textElement("p", "These travel rules are grouped for quick reference. Apply each when its configured trigger or dependency becomes relevant."));
            const list = document.createElement("div");
            list.className = "hc-procedure-step-list";
            for (const module of travel) list.append(compactRuleCard(module, false));
            section.append(list);
            shell.append(section);
        }

        const supportingGroups = travel.length === 0 && activeRules("Journey & events").length > 0
            ? ["Journey & events", "Survival & resources", "Automation"]
            : ["Survival & resources", "Journey & events", "Automation"];
        for (const group of supportingGroups) {
            const active = activeRules(group);
            if (active.length === 0) continue;
            const available = missingRules(group);
            const section = document.createElement("section");
            section.className = "hc-panel hc-rule-group";
            section.append(textElement("h2", group));
            if (group === "Survival & resources") {
                section.append(textElement("p", "Apply these rules when their resource, rest, forced-travel, exposure, or persistent-effect trigger occurs."));
            } else if (group === "Journey & events") {
                section.append(textElement("p", "Run the configured stages and event triggers as one journey procedure."));
            } else {
                section.append(textElement("p", "Optional automatic generation supplements the procedure rules above; it does not replace them."));
            }
            const grid = document.createElement("div");
            grid.className = "hc-procedure-area-grid";
            for (const module of active) grid.append(compactRuleCard(module, false));
            section.append(grid);
            if (available.length > 0) {
                const add = document.createElement("div");
                add.className = "hc-add-rule-strip";
                add.append(textElement("strong", "Related rules"));
                for (const rule of available) {
                    const control = button(rule.label, "secondary");
                    control.addEventListener("click", () => void setModule(rule.moduleKey, true));
                    add.append(control);
                }
                section.append(add);
            }
            shell.append(section);
        }

        const library = renderRuleLibrary();
        if (library) shell.append(library);
        return shell;
    };

    const renderRuleLibrary = (): HTMLElement | null => {
        if (!draft) return null;
        const missing = new Set(compactRuleCatalog()
            .filter(rule => !draft!.modules.some(module => module.moduleKey === rule.moduleKey))
            .map(rule => rule.moduleKey));
        if (missing.size === 0) return null;

        const details = document.createElement("details");
        details.className = "hc-panel hc-rule-library";
        details.open = draft.modules.length === 0;
        const summary = document.createElement("summary");
        summary.textContent = "Add exploration rules";
        details.append(summary, textElement("p", "Browse by what you want the ruleset to handle. Required supporting rules are explained when you select an option."));

        const groups: Array<{ title: string; description: string; keys: string[] }> = [
            {
                title: "Travel, course, and pace",
                description: "Set travel periods, activities and roles, movement budget or pace, terrain and routes, movement resolution, and partial cell progress.",
                keys: ["time.interval", "party.activities", "movement.budget", "movement.terrain", "movement.resolution", "movement.hex-progress"]
            },
            {
                title: "Navigation",
                description: "Add route checks and the rules for becoming lost, recognizing the error, and recovering the course.",
                keys: ["navigation.check", "navigation.outcome"]
            },
            {
                title: "Encounters",
                description: "Set a regular encounter cadence and, when needed, a contextual encounter schedule.",
                keys: ["encounters.cadence", "encounters.schedule"]
            },
            {
                title: "Survival and recovery",
                description: "Track supplies, foraging, camping, forced travel, environmental exposure, and persistent effects.",
                keys: ["survival.resources", "exploration.foraging", "survival.camping", "time.forced-travel", "survival.exposure", "effects.expedition"]
            },
            {
                title: "Journeys and events",
                description: "Create a multi-stage journey, journey events, or both without requiring a spatial travel loop.",
                keys: ["journey.process", "journey.events"]
            },
            {
                title: "Automatic resolution",
                description: "Let Hex Crawl generate supported travel, navigation, and encounter results.",
                keys: ["procedure.helpers"]
            }
        ];

        const grid = document.createElement("div");
        grid.className = "hc-rule-library-groups";
        const hasSpatialMovement = draft.modules.some(module =>
            module.moduleKey === "movement.resolution"
            || module.moduleKey === "movement.budget"
            || module.moduleKey === "movement.terrain"
            || module.moduleKey === "movement.hex-progress");

        for (const group of groups) {
            const rules = group.keys
                .filter(key => missing.has(key))
                .map(key => compactRule(key))
                .filter((rule): rule is NonNullable<ReturnType<typeof compactRule>> => rule !== null);
            if (rules.length === 0) continue;
            const section = document.createElement("section");
            section.className = "hc-rule-library-group";
            section.append(textElement("h3", group.title), textElement("p", group.description));

            if (group.title === "Navigation" && !hasSpatialMovement) {
                section.append(notice("Navigation needs spatial movement when it is used for map travel."));
                const combined = button("Add movement and navigation", "secondary");
                combined.addEventListener("click", () => void setModules([
                    ["movement.resolution", true],
                    ["navigation.check", true]
                ]));
                section.append(combined);
            }

            const controls = document.createElement("div");
            controls.className = "hc-add-rule-strip";
            for (const rule of rules) {
                const control = button(rule.label, "secondary");
                control.addEventListener("click", () => void setModule(rule.moduleKey, true));
                controls.append(control);
            }
            section.append(controls);
            grid.append(section);
        }
        details.append(grid);
        return details;
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
        card.append(heading, textElement("p", descriptor?.description ?? module.purpose));
        const facts = compactFacts(module);
        if (facts.childElementCount > 0) card.append(facts);
        card.append(guidedDisclosure("Why?", descriptor?.label ?? module.displayName, guidedRuleExplanation(module.moduleKey)));
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

    const guidedRuleExplanation = (moduleKey: string): string => {
        const group = compactRule(moduleKey)?.group;
        if (group === "Travel flow") return "This rule participates in the ordinary travel loop. It affects when or how the party spends time, chooses travel intent, or makes spatial progress.";
        if (group === "Survival & resources") return "This rule adds resource, recovery, forced-travel, exposure, or persistent-effect consequences when its trigger occurs. Leave it out if your table does not track that concern.";
        if (group === "Journey & events") return "This rule handles journey stages or events as a process. It is useful for travel that is better represented as a sequence of challenges than as repeated hex movement.";
        if (group === "Automation") return "This rule can generate supported results for the procedure. Automation supplements the same authoritative procedure; it does not replace or bypass it.";
        return "This rule is part of the saved ruleset. Add or change it only when your table needs that behavior.";
    };

    const compactFacts = (module: ProcedureModuleComposer): HTMLElement => {
        const values = Object.fromEntries(parameterDefinitions(module)
            .map(([key, definition]) => [key, module.parameters[key] ?? definition.defaultValue ?? ""])
            .filter(([, value]) => value !== ""));
        return renderPresentationFactGroups(module.moduleKey, values);
    };

    const renderPresentationFactGroups = (
        moduleKey: string,
        values: Record<string, string>,
        extraClass = ""): HTMLElement => {
        const groups = document.createElement("div");
        groups.className = `hc-rule-fact-groups${extraClass ? ` ${extraClass}` : ""}`;
        for (const section of procedurePresentationSections(moduleKey, values)) {
            const group = document.createElement("section");
            group.className = "hc-rule-fact-group";
            group.append(textElement("h4", section.label));
            const facts = document.createElement("dl");
            facts.className = "hc-rule-facts";
            for (const fact of section.facts) {
                const term = textElement("dt", fact.label);
                const value = document.createElement("dd");
                if (fact.entries && fact.entries.length > 0) {
                    const table = document.createElement("table");
                    table.className = "hc-rule-map";
                    const body = document.createElement("tbody");
                    for (const entry of fact.entries) {
                        const row = document.createElement("tr");
                        row.append(textElement("th", entry.label), textElement("td", entry.value));
                        body.append(row);
                    }
                    table.append(body);
                    value.append(table);
                } else if (fact.items && fact.items.length > 0) {
                    const list = document.createElement("ul");
                    list.className = "hc-rule-value-list";
                    for (const item of fact.items) list.append(textElement("li", item));
                    value.append(list);
                } else {
                    value.textContent = fact.value;
                }
                facts.append(term, value);
            }
            group.append(facts);
            groups.append(group);
        }
        return groups;
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
        if (save) save.disabled = savePending
            || historical()
            || !hasStructuredChanges()
            || hasBlockingStructuredIssues();
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
            output.textContent = friendlyStoredValue(key, value, module.moduleKey);
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
                choices.unshift({ value, label: friendlyStoredValue(key, value, module.moduleKey) });
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
        index.append(
            textElement("h2", "Procedure structure"),
            textElement("p", "Select one module to inspect its exact generic structure. Inclusion is controlled independently."));

        const catalog = compactRuleCatalog();
        const includedKeys = new Set((draft?.modules ?? []).map(module => module.moduleKey));
        const selectedKey = advancedSelectedModuleKey
            ?? draft?.modules[0]?.moduleKey
            ?? catalog[0]?.moduleKey
            ?? null;
        advancedSelectedModuleKey = selectedKey;

        for (const group of [...new Set(catalog.map(rule => rule.group))]) {
            const groupSection = document.createElement("section");
            groupSection.className = "hc-advanced-index-group";
            groupSection.append(textElement("h3", group));
            for (const rule of catalog.filter(value => value.group === group)) {
                const row = document.createElement("div");
                row.className = `hc-advanced-module-row${selectedKey === rule.moduleKey ? " is-selected" : ""}${includedKeys.has(rule.moduleKey) ? " is-included" : ""}`;

                const include = document.createElement("input");
                include.type = "checkbox";
                include.checked = includedKeys.has(rule.moduleKey);
                include.setAttribute("aria-label", `Include ${rule.label}`);
                include.addEventListener("change", () => {
                    if (include.checked) advancedSelectedModuleKey = rule.moduleKey;
                    void setModule(rule.moduleKey, include.checked);
                });

                const select = button(rule.label, "secondary");
                select.classList.add("hc-advanced-module-select");
                select.dataset.moduleKey = rule.moduleKey;
                select.setAttribute("aria-current", selectedKey === rule.moduleKey ? "true" : "false");
                select.replaceChildren(
                    textElement("span", rule.label),
                    code(rule.moduleKey));
                select.addEventListener("click", () => {
                    advancedSelectedModuleKey = rule.moduleKey;
                    render();
                });
                row.append(include, select);
                groupSection.append(row);
            }
            index.append(groupSection);
        }

        const navigation = [...index.querySelectorAll<HTMLButtonElement>(".hc-advanced-module-select")];
        navigation.forEach((control, position) => control.addEventListener("keydown", event => {
            const next = event.key === "ArrowDown" ? position + 1
                : event.key === "ArrowUp" ? position - 1
                    : event.key === "Home" ? 0
                        : event.key === "End" ? navigation.length - 1
                            : -1;
            if (next < 0 && !["ArrowUp"].includes(event.key)) return;
            if (!["ArrowDown", "ArrowUp", "Home", "End"].includes(event.key)) return;
            event.preventDefault();
            navigation[Math.max(0, Math.min(navigation.length - 1, next))]?.focus();
        }));
        layout.append(index);

        const content = document.createElement("main");
        content.className = "hc-procedure-shell hc-advanced-content";
        const selectedModule = selectedKey
            ? draft?.modules.find(module => module.moduleKey === selectedKey)
            : null;
        if (selectedModule) {
            content.append(advancedModule(selectedModule));
        } else if (selectedKey) {
            const rule = compactRule(selectedKey);
            const empty = document.createElement("section");
            empty.className = "hc-panel hc-advanced-module";
            empty.append(
                textElement("h2", rule?.label ?? selectedKey),
                code(selectedKey),
                textElement("p", rule?.description ?? "This module is not included in the procedure."));
            const add = button("Include this module", "primary");
            add.addEventListener("click", () => void setModule(selectedKey, true));
            empty.append(add);
            content.append(empty);
        } else {
            content.append(emptyState("No modules available", "No procedure modules are available to inspect."));
        }
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
        const mechanicOptions = [
            module.mechanic,
            ...module.alternatives.filter(option =>
                option.key !== module.mechanic.key || option.version !== module.mechanic.version)
        ];
        for (const option of mechanicOptions) {
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
        mechanic.append(select);

        const mechanicFacts = document.createElement("dl");
        mechanicFacts.className = "hc-advanced-facts";
        mechanicFacts.append(
            textElement("dt", "Mechanic"),
            codeValue(`${module.mechanic.key} · v${module.mechanic.version}`),
            textElement("dt", "Automation"),
            textElement("dd", module.mechanic.automationLevel),
            textElement("dt", "Execution handler"),
            codeValue(module.mechanic.executionHandler),
            textElement("dt", "Execution support"),
            textElement("dd", module.mechanic.executionSupport));
        mechanic.append(mechanicFacts, textElement("p", executionSummary(module)));
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
        contracts.append(
            contractBlock("Inputs", module.requiredInputs.map(input =>
                `${input.inputKey} — ${input.allowedSources.map(inputSourceLabel).join(", ")}`)),
            contractBlock("Produces", module.outputs),
            contractBlock("Required dependencies", module.requiredDependencies),
            contractBlock("Optional dependencies", module.optionalDependencies),
            contractBlock("Diagnostics", [
                ...module.validationIssues,
                ...module.dependencyIssues.map(issue => issue.message)
            ]));
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

function presetGuidanceCard(preset: ProcedurePreset): HTMLElement {
    const guidance = presetGuidance(preset.procedure);
    const section = document.createElement("section");
    section.className = "hc-guided-preset-advice";
    section.append(
        textElement("strong", "Best fit"),
        textElement("p", guidance.bestFor),
        textElement("strong", "Table work"),
        textElement("p", guidance.manage),
        textElement("strong", "Ruleset breadth"),
        textElement("p", `${guidance.breadth} · ${guidance.areaCount} ${guidance.areaCount === 1 ? "rule area" : "rule areas"}`));
    return section;
}

function presetDecisionFacts(guidance: ReturnType<typeof presetGuidance>): HTMLElement {
    const facts = document.createElement("dl");
    facts.className = "hc-preset-facts hc-preset-decision-facts";
    for (const fact of guidance.summaryFacts) {
        facts.append(textElement("dt", fact.label), textElement("dd", fact.value));
    }
    return facts;
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
    const storedTicks = parameter("time.interval", "durationTicks");
    const time = interval == null
        ? storedTicks ? formatDurationTicks(storedTicks) : "No fixed interval"
        : interval === 24 ? "Daily" : interval === 1 ? "Hourly" : `${formatNumber(interval)} hours`;
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
        Workflow: modules.has("journey.process")
            ? "Journey / staged process"
            : modules.has("movement.resolution") && modules.has("time.interval")
                ? "Interval travel"
                : modules.has("time.interval") ? "Repeating interval" : "Procedure-driven",
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
    return preset.description;
}

function familiarPresets(values: ProcedurePreset[]): ProcedurePreset[] {
    return values.filter(value => value.category !== "Generic starting points");
}

function genericPresets(values: ProcedurePreset[]): ProcedurePreset[] {
    return values.filter(value => value.category === "Generic starting points");
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

function codeValue(value: string): HTMLElement {
    const output = document.createElement("dd");
    output.append(code(value));
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
