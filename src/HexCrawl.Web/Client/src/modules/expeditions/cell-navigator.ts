import {
    type CurrentCellAdjacency,
    type SpatialAdjacencyInterface
} from "./spatial-adjacency";

export type CurrentCellNavigatorOptions<TCell, TIntent> = {
    selectable: boolean;
    actualIntent: TIntent | null;
    onToggle: (adjacency: SpatialAdjacencyInterface<TCell, TIntent>) => void;
};

export function renderCurrentCellNavigator<TCell, TIntent>(
    adjacency: CurrentCellAdjacency<TCell, TIntent> | null,
    options: CurrentCellNavigatorOptions<TCell, TIntent>): HTMLElement {
    const navigator = document.createElement("div");
    navigator.className = "hc-adjacency-navigator";
    navigator.setAttribute("role", "group");
    navigator.setAttribute("aria-label", "Current-cell adjacent travel");
    if (!adjacency) return navigator;

    const svg = document.createElementNS("http://www.w3.org/2000/svg", "svg");
    svg.setAttribute("viewBox", "0 0 100 100");
    svg.setAttribute("aria-hidden", "true");
    svg.classList.add("hc-adjacency-cell");
    const polygon = document.createElementNS("http://www.w3.org/2000/svg", "polygon");
    polygon.setAttribute("points", adjacency.boundary
        .map(point => `${point.x * 100},${point.y * 100}`)
        .join(" "));
    svg.append(polygon);
    navigator.append(svg);

    for (const candidate of adjacency.adjacencies) {
        const vector = candidate.outwardVector;
        const angleDegrees = Math.atan2(vector.y, vector.x) * 180 / Math.PI;
        const control = document.createElement("button");
        control.type = "button";
        control.className = "hc-adjacency-interface";
        control.dataset.adjacencyInterfaceId = candidate.id;
        control.style.setProperty("--hc-adjacency-x", `${(candidate.anchor.x + vector.x * 0.045) * 100}%`);
        control.style.setProperty("--hc-adjacency-y", `${(candidate.anchor.y + vector.y * 0.045) * 100}%`);
        control.style.setProperty("--hc-adjacency-feedback-x", `${vector.x * 0.2}rem`);
        control.style.setProperty("--hc-adjacency-feedback-y", `${vector.y * 0.2}rem`);
        control.style.setProperty("--hc-adjacency-angle", `${angleDegrees}deg`);

        const arrow = document.createElementNS("http://www.w3.org/2000/svg", "svg");
        arrow.classList.add("hc-adjacency-arrow-shape");
        arrow.setAttribute("viewBox", "0 0 64 40");
        arrow.setAttribute("aria-hidden", "true");
        const arrowPath = document.createElementNS("http://www.w3.org/2000/svg", "path");
        arrowPath.setAttribute("d", "M2 15 H38 V4 L62 20 L38 36 V25 H2 Z");
        arrow.append(arrowPath);
        control.append(arrow);

        const selected = adjacency.selectedAdjacencyId === candidate.id;
        const actualCourse = options.actualIntent !== null
            && Object.is(options.actualIntent, candidate.intentValue)
            && !selected;
        control.setAttribute(
            "aria-label",
            actualCourse
                ? `Travel through ${candidate.label} to ${candidate.targetLabel}; this is the current actual resolved course`
                : `Travel through ${candidate.label} to ${candidate.targetLabel}`);
        control.setAttribute("aria-pressed", String(selected));
        control.title = actualCourse
            ? `${candidate.label} to ${candidate.targetLabel} · actual course`
            : `${candidate.label} to ${candidate.targetLabel}`;
        control.disabled = !options.selectable || !candidate.traversable;
        if (selected) control.classList.add("is-selected");
        if (actualCourse) control.classList.add("is-actual-course");
        control.addEventListener("click", () => options.onToggle(candidate));
        navigator.append(control);
    }
    return navigator;
}
