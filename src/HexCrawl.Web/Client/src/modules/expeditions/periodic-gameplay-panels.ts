import type { HexCrawlApi } from "../../api";
import { ExpeditionEffectsApi } from "../../effect-api";
import { JourneyApi } from "../../journey-api";
import { SurvivalResourcesApi } from "../../survival-api";
import type { ExpeditionDetail, ToolHostContext } from "../../types";
import type { ExpeditionJourneyState } from "../../journey-types";
import type { ExpeditionEffectState } from "../../effect-types";
import type { SurvivalResources } from "../../survival-types";
import { ExpeditionEffectsPanel } from "./effects-panel";
import { ExpeditionEnvironmentPanel } from "./environment-panel";
import { ExpeditionJourneyPanel } from "./journey-panel";
import { ExpeditionPartySheetController } from "./expedition-party-sheet";
import { ExpeditionSurvivalResourcesPanel } from "./survival-resources-panel";

type Mutate = (control: HTMLButtonElement | null, action: () => Promise<void>) => Promise<void>;

/**
 * Persistent expedition gameplay aggregates use one set of party,
 * environment, journey, effects, and survival editors. Each editor mounts
 * within the explicitly provided gameplay host, independently of tile shape.
 */
export function mountPeriodicGameplayPanels(
    host: HTMLElement,
    api: HexCrawlApi,
    expeditionId: string,
    toolContext: ToolHostContext | null,
    getRuntime: () => ExpeditionDetail,
    applyRuntime: (runtime: ExpeditionDetail) => void,
    mutate: Mutate,
    reportError: (error: unknown) => void,
    onAttention: (journey: ExpeditionJourneyState | null,
        survival: SurvivalResources | null, effects: ExpeditionEffectState | null,
        failed: boolean) => void
): { sync: () => void; dispose: () => void } {
    const partyPanel = document.createElement("details");
    partyPanel.className = "hc-party-editor-panel";
    partyPanel.dataset.periodicParty = "";
    const summary = document.createElement("summary");
    summary.textContent = "Party & travel order";
    const partySummary = document.createElement("div");
    partySummary.dataset.partySummary = "";
    const partyEditor = document.createElement("div");
    partyEditor.dataset.partyEditor = "";
    partyPanel.append(summary, partySummary, partyEditor);
    host.append(partyPanel);

    const party = new ExpeditionPartySheetController(
        host, api, getRuntime, applyRuntime, mutate);
    const environment = new ExpeditionEnvironmentPanel(
        host, api, getRuntime, applyRuntime, mutate);
    const journey = new ExpeditionJourneyPanel(
        host, new JourneyApi(toolContext), expeditionId, getRuntime, mutate);
    const effects = new ExpeditionEffectsPanel(
        host, new ExpeditionEffectsApi(toolContext), expeditionId, getRuntime, mutate);
    const survival = new ExpeditionSurvivalResourcesPanel(
        host, new SurvivalResourcesApi(toolContext), expeditionId, getRuntime, mutate);

    let disposed = false;
    let syncGeneration = 0;
    const sync = (): void => {
        if (disposed) return;
        party.sync(getRuntime());
        environment.sync();
        const generation = ++syncGeneration;
        // A panel's API is independently scoped to the signed-in expedition;
        // failures must be visible without disabling unrelated gameplay editors.
        void Promise.allSettled([
            journey.sync(), effects.sync(), survival.sync()
        ]).then(results => {
            if (disposed || generation !== syncGeneration) return;
            for (const result of results) {
                if (result.status === "rejected") reportError(result.reason);
            }
            onAttention(results[0].status === "fulfilled" ? results[0].value : null,
                results[2].status === "fulfilled" ? results[2].value : null,
                results[1].status === "fulfilled" ? results[1].value : null,
                results.some(result => result.status === "rejected"));
        });
    };
    const dispose = (): void => {
        disposed = true;
        syncGeneration++;
        environment.dispose();
        journey.dispose();
        effects.dispose();
        survival.dispose();
        partyPanel.remove();
    };
    return { sync, dispose };
}
