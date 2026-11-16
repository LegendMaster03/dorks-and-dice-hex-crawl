import type { TilingCatalogEntry } from "../../procedure-composer-types";
import { tilingPreview } from "./tiling-preview";

export type TilingCategoryNode = {
    label: string;
    categories: TilingCategoryNode[];
    entries: TilingCatalogEntry[];
};

/**
 * Category paths are presentation metadata, not part of a D-symbol or a
 * ruleset. A valid unlisted tiling never depends on membership in this tree.
 */
export function tilingCategoryTree(entries: readonly TilingCatalogEntry[]): TilingCategoryNode {
    const root: TilingCategoryNode = { label: "", categories: [], entries: [] };
    for (const entry of entries) {
        const path = entry.categoryPath?.filter(part => part.trim().length > 0) ?? [];
        let node = root;
        for (const label of path.length > 0 ? path : ["Other tilings"]) {
            let child = node.categories.find(category => category.label === label);
            if (!child) {
                child = { label, categories: [], entries: [] };
                node.categories.push(child);
            }
            node = child;
        }
        node.entries.push(entry);
    }
    const sort = (node: TilingCategoryNode): void => {
        node.categories.sort((a, b) => a.label.localeCompare(b.label));
        node.entries.sort((a, b) => a.displayName.localeCompare(b.displayName)
            || (a.presetId ?? a.dsSymbol).localeCompare(b.presetId ?? b.dsSymbol));
        for (const child of node.categories) sort(child);
    };
    sort(root);
    return root;
}

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
            appendNode(category, content);
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
