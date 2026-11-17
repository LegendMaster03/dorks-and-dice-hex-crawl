import type { TilingCatalogEntry } from "../../procedure-composer-types";
import { tilingPreview } from "./tiling-preview";
import { resolveTilingCatalogChoice, tilingCategoryTree, type TilingCategoryNode } from "./procedure-composer-model";

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
    // Distinct metrics can share a D-symbol. A stored procedure does not
    // retain the catalog preset ID, so do not mark multiple examples pressed.
    let selected = resolveTilingCatalogChoice(entries, selectedSymbol, selectedPresetId);
    const isSelected = (entry: TilingCatalogEntry): boolean => entry === selected;
    const containsSelection = (node: TilingCategoryNode): boolean =>
        node.entries.some(entry => isSelected(entry)
            || (selected === null && selectedPresetId === null
                && selectedSymbol !== null && entry.dsSymbol === selectedSymbol))
        || node.categories.some(containsSelection);
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
            const preview = tilingPreview(entry);
            // The button text already names this choice. A second accessible
            // name on its decorative preview would announce the name twice.
            preview.setAttribute("aria-hidden", "true");
            option.append(document.createTextNode(entry.displayName), preview);
            option.setAttribute("aria-pressed", String(isSelected(entry)));
            option.disabled = disabled;
            option.addEventListener("click", () => {
                // Preserve the current focus and expanded categories. A caller
                // may replace the whole menu, but it need not do so merely to
                // change the highlighted geometry in a world-creation form.
                selected = entry;
                root.querySelectorAll<HTMLButtonElement>(".hc-tiling-category-choice")
                    .forEach(button => button.setAttribute("aria-pressed", String(button === option)));
                onSelect(entry);
            });
            parent.append(option);
        }
    };
    appendNode(tree, root);
    return root;
}
