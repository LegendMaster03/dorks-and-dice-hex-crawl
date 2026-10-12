import { badge, textElement } from "../../ui/workspace";
import { guidedDisclosure } from "../../ui/guidance";
import type { ExpeditionWorkspaceAction } from "./expedition-workspace-model";

/**
 * The existing Phase 15 "Next action" chrome, shared by nonspatial and V2
 * spatial sessions. The engine owns every action result; this module owns only
 * presentation and dispatch, without assuming a hex direction or q/r address.
 */
export function renderPhase15NextAction(
    action: ExpeditionWorkspaceAction,
    copy: { label: string; detail: string },
    activate: () => void,
    guidedDetails: HTMLElement,
    guidedWhy: string
): HTMLElement {
    const section = document.createElement("section");
    section.className = "hc-current-action";
    const header = document.createElement("header");
    header.append(textElement("h2", "Next action"),
        badge(action.urgent ? "Needs resolution" : "Ready", action.urgent ? "warning" : "good"));
    const actionLabel = textElement("p", copy.label, "hc-current-action-primary");
    actionLabel.dataset.currentActionLabel = "";
    const actionDetail = textElement("p", copy.detail, "hc-current-action-detail");
    actionDetail.dataset.currentActionDetail = "";
    section.append(header, actionLabel, actionDetail);
    const row = document.createElement("div");
    row.className = "hc-button-row";
    const primary = document.createElement("button");
    primary.type = "button";
    primary.textContent = copy.label;
    primary.className = "hc-primary-action";
    primary.dataset.currentActionButton = "";
    primary.addEventListener("click", activate);
    row.append(primary);
    section.append(row, guidedDetails);
    section.append(guidedDisclosure("Why is this next?", copy.label, guidedWhy));
    return section;
}
