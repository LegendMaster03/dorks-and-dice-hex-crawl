import type {
    ProcedureComposerOverrideInput,
    ProcedureModuleComposer,
    ProcedureParameterDefinition
} from "../../procedure-composer-types";

export const composerSectionOrder = [
    "Time",
    "Movement",
    "Party Organization",
    "Navigation",
    "Exploration",
    "Encounters",
    "Survival",
    "Journey Processes",
    "Procedure Support"
] as const;

export function composerSection(category: string): string {
    switch (category.trim().toLowerCase()) {
        case "time":
            return "Time";
        case "movement":
            return "Movement";
        case "party procedure":
            return "Party Organization";
        case "navigation":
            return "Navigation";
        case "exploration":
            return "Exploration";
        case "encounters":
            return "Encounters";
        case "survival/resources":
        case "environment/effects":
            return "Survival";
        case "journey processes":
            return "Journey Processes";
        case "procedure":
            return "Procedure Support";
        default:
            return category || "Procedure Support";
    }
}

export function groupComposerModules(
    modules: ProcedureModuleComposer[]): Array<{ section: string; modules: ProcedureModuleComposer[] }> {
    const grouped = new Map<string, ProcedureModuleComposer[]>();
    for (const module of modules) {
        const section = composerSection(module.category);
        const values = grouped.get(section) ?? [];
        values.push(module);
        grouped.set(section, values);
    }

    const order = new Map<string, number>(
        composerSectionOrder.map((section, index) => [section, index]));
    return [...grouped.entries()]
        .sort(([left], [right]) =>
            (order.get(left) ?? Number.MAX_SAFE_INTEGER) - (order.get(right) ?? Number.MAX_SAFE_INTEGER)
            || left.localeCompare(right))
        .map(([section, values]) => ({
            section,
            modules: values
        }));
}

export function parameterDefinitions(
    module: ProcedureModuleComposer): Array<[string, ProcedureParameterDefinition]> {
    const definitions = new Map<string, ProcedureParameterDefinition>();
    for (const [key, value] of Object.entries(module.configurationSchema)) {
        definitions.set(key, value);
    }
    for (const [key, value] of Object.entries(module.mechanic.parameterSchema)) {
        definitions.set(key, value);
    }
    for (const key of Object.keys(module.parameters)) {
        if (!definitions.has(key)) {
            definitions.set(key, {
                type: "string",
                required: false,
                description: "Stored parameter not described by the current catalog. It is preserved losslessly.",
                defaultValue: null
            });
        }
    }
    return [...definitions.entries()];
}

export function createPendingOverride(
    module: ProcedureModuleComposer,
    existing?: ProcedureComposerOverrideInput): ProcedureComposerOverrideInput {
    return existing ?? {
        overrideId: `composer-${module.moduleKey.replace(/[^a-zA-Z0-9_.-]/g, "-")}`,
        moduleKey: module.moduleKey,
        replacementMechanicKey: null,
        replacementMechanicVersion: null,
        parameters: {},
        note: null
    };
}

export function withBehavior(
    module: ProcedureModuleComposer,
    existing: ProcedureComposerOverrideInput | undefined,
    mechanicKey: string,
    mechanicVersion: number): ProcedureComposerOverrideInput {
    return {
        ...createPendingOverride(module, existing),
        replacementMechanicKey: mechanicKey,
        replacementMechanicVersion: mechanicVersion
    };
}

export function withParameter(
    module: ProcedureModuleComposer,
    existing: ProcedureComposerOverrideInput | undefined,
    key: string,
    value: string): ProcedureComposerOverrideInput {
    const pending = createPendingOverride(module, existing);
    return {
        ...pending,
        parameters: {
            ...pending.parameters,
            [key]: value
        }
    };
}

export function parseMapParameter(value: string): Array<{ key: string; value: string }> {
    if (!value.trim()) return [];
    return value
        .split(";")
        .map(part => part.trim())
        .filter(Boolean)
        .map(part => {
            const separator = part.indexOf("=");
            return separator < 0
                ? { key: part, value: "" }
                : {
                    key: part.slice(0, separator).trim(),
                    value: part.slice(separator + 1).trim()
                };
        });
}

export function serializeMapParameter(entries: Array<{ key: string; value: string }>): string {
    return entries
        .filter(entry => entry.key.trim() || entry.value.trim())
        .map(entry => `${entry.key.trim()}=${entry.value.trim()}`)
        .join(";");
}

export function parseKeyListParameter(value: string): string[] {
    return value
        .split(";")
        .map(item => item.trim())
        .filter(Boolean);
}

export function serializeKeyListParameter(values: string[]): string {
    return values.map(value => value.trim()).filter(Boolean).join(";");
}

export function inputSourceLabel(source: string): string {
    switch (source) {
        case "SelectedModule":
            return "selected module";
        case "Dm":
            return "DM / manual input";
        case "OptionalProvider":
            return "optional provider";
        case "ExternalState":
            return "external / runtime state";
        default:
            return source;
    }
}

export function executionSummary(module: ProcedureModuleComposer): string {
    if (module.mechanic.executionSupport === "Unsupported") {
        return "Unsupported handler/version. Stored data is preserved and is not reinterpreted.";
    }
    if (module.mechanic.executionSupport === "Declarative") {
        return `${module.mechanic.automationLevel} structural/declarative mechanic. Hex Crawl records the contract without claiming native execution.`;
    }
    return `${module.mechanic.automationLevel} native mechanic.`;
}

export function saveBlocked(module: ProcedureModuleComposer): boolean {
    return module.validationIssues.length > 0
        || module.dependencyIssues.some(issue =>
            issue.kind === "MissingRequiredModule"
            || issue.kind === "MissingRequiredProducer"
            || issue.kind === "IncompatibleMechanic");
}
