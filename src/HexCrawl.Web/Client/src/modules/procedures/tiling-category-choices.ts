import type { TilingCatalogEntry } from "../../procedure-composer-types";
import { tilingPreview } from "./tiling-preview";
import { tilingCategoryTree, type TilingCategoryNode } from "./procedure-composer-model";

/** Reusable, arbitrary-depth category menu using the existing polygon preview. */
export function tilingCategoryChoices(
    entries: readonly TilingCatalogEntry[],
    selectedSymbol: string | null,
    onSelect: (entry: TilingCatalogEntry) => void,
    disabled = false,
    selectedPresetId: string | null = null
): HTMLElement {
    const root = document.createElement("div");
    root.className = "hc-tiling-category-choices";
    const tree = tilingCategoryTree(entries);
    const isSelected = (entry: TilingCatalogEntry): boolean =>
        selectedPresetId !== null && entry.presetId !== null
            ? entry.presetId === selectedPresetId
            : selectedPresetId === null && entry.dsSymbol === selectedSymbol;
    const containsSelection = (node: TilingCategoryNode): boolean =>
        node.entries.some(isSelected) || node.categories.some(containsSelection);
    const appendNode = (node: TilingCategoryNode, parent: HTMLElement): void => {
        for (const category of node.categories) {
            const group = document.createElement("details");
            group.className = "hc-tiling-category-group";
            group.open = containsSelection(category);
            const label = document.createElement("summary");
            label.textContent = category.label;
            group.append(label);
            const content = document.createElement("div");
            content.className = "hc-tiling-category-content";
            // Closed categories do not create or paint potentially expensive motifs.
            let populated = false;
            const populate = (): void => {
                if (populated) return;
                populated = true;
                appendNode(category, content);
            };
            if (group.open) populate();
            group.addEventListener("toggle", () => { if (group.open) populate(); });
            group.append(content);
            parent.append(group);
        }
        for (const entry of node.entries) {
            const option = document.createElement("button");
            option.type = "button";
            option.className = "hc-panel hc-tiling-category-choice";
            option.append(document.createTextNode(entry.displayName), tilingPreview(entry));
            option.setAttribute("aria-pressed", String(isSelected(entry)));
            option.disabled = disabled;
            option.addEventListener("click", () => onSelect(entry));
            parent.append(option);
        }
    };
    appendNode(tree, root);
    return root;
}
