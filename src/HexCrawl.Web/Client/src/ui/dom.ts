import type { ResolutionSource } from "../types";

export function statusCell(label: string, value: string): HTMLElement {
    const cell = document.createElement("div");
    const strong = document.createElement("strong");
    strong.textContent = label;
    const span = document.createElement("span");
    span.textContent = value;
    cell.append(strong, span);
    return cell;
}

export function option(value: string, label: string): HTMLOptionElement {
    const result = document.createElement("option");
    result.value = value;
    result.textContent = label;
    return result;
}

export function sourceLabel(source: ResolutionSource): string {
    switch (source) {
        case "ProcedureDefault": return "Procedure / default";
        case "AutomaticRoll": return "Automatic helper roll";
        case "ManualRoll": return "Manual roll / result";
        case "ExternalSystem": return "External system";
        case "DmOverride": return "DM override";
    }
}

export function prettyEnum(value: string): string {
    return value.replace(/([a-z])([A-Z])/g, "$1 $2").replace(/_/g, " ").toLowerCase();
}

export function required<T extends Element>(root: ParentNode, selector: string): T {
    const value = root.querySelector<T>(selector);
    if (!value) throw new Error(`Missing ${selector}`);
    return value;
}

export function input(root: ParentNode, name: string): HTMLInputElement {
    const value = root.querySelector<HTMLInputElement>(`input[name="${name}"]`);
    if (!value) throw new Error(`Missing input ${name}`);
    return value;
}

export function checkbox(root: ParentNode, name: string): HTMLInputElement {
    return input(root, name);
}

export function select(root: ParentNode, name: string): HTMLSelectElement {
    const value = root.querySelector<HTMLSelectElement>(`select[name="${name}"]`);
    if (!value) throw new Error(`Missing select ${name}`);
    return value;
}

export function numeric(element: HTMLInputElement | HTMLSelectElement): number {
    const value = Number(element.value);
    if (!Number.isFinite(value)) {
        throw new Error(`${element.getAttribute("name") ?? "value"} must be a finite number.`);
    }
    return value;
}

export function integer(element: HTMLInputElement | HTMLSelectElement): number {
    const value = Number(element.value);
    if (!Number.isInteger(value)) {
        throw new Error(`${element.getAttribute("name") ?? "value"} must be an integer.`);
    }
    return value;
}

export function nonZeroInteger(element: HTMLInputElement): number {
    const value = integer(element);
    if (value === 0) throw new Error(`${element.name} must be a non-zero integer.`);
    return value;
}

export function optionalText(element: HTMLInputElement): string | undefined {
    const value = element.value.trim();
    return value || undefined;
}
