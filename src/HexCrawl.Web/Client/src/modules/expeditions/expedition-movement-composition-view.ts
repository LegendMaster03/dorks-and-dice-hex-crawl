import type { ExpeditionDetail, MovementAppliedContributor } from "../../types";
import { statusCell } from "../../ui/dom";

export function movementCompositionStatusCells(runtime: ExpeditionDetail): HTMLElement[] {
    const composition = runtime.movementComposition;
    const cells = [
        statusCell("Effective movement", compositionSummary(runtime)),
        statusCell("Movement limiter", limiterSummary(runtime)),
        statusCell("Movement inputs", contributorSummary(runtime))
    ];

    if (composition.provenance.length > 0) {
        cells.push(statusCell("Movement provenance", composition.provenance.join(" · ")));
    }

    const diagnostic = diagnosticSummary(runtime);
    if (diagnostic) {
        cells.push(statusCell("Movement state", diagnostic));
    }

    return cells;
}

function compositionSummary(runtime: ExpeditionDetail): string {
    const composition = runtime.movementComposition;
    const effective = composition.effectiveValue === null
        ? "No resolved quantity"
        : `${formatNumber(composition.effectiveValue)} ${composition.effectiveUnit ?? "unit"}${composition.effectivePerUnit ? `/${composition.effectivePerUnit}` : ""}`;
    const reference = composition.referenceUse === "None"
        ? ""
        : ` · reference ${humanizeKey(composition.referenceUse).toLowerCase()}`;
    const override = composition.preOverrideValue === null ? "" : " · DM override applied";
    return `${humanizeKey(composition.status)} · ${effective}${reference}${override}`;
}

function limiterSummary(runtime: ExpeditionDetail): string {
    const composition = runtime.movementComposition;
    if (composition.limitingParticipantId) {
        const member = runtime.party.members.find(value => value.id === composition.limitingParticipantId);
        if (member) return member.name;
    }
    return composition.limitingContributorKey ?? "—";
}

function contributorSummary(runtime: ExpeditionDetail): string {
    const contributors = runtime.movementComposition.contributors;
    if (contributors.length === 0) return "No composed contributors";
    return contributors.map(contributorLabel).join(" · ");
}

function contributorLabel(contributor: MovementAppliedContributor): string {
    const value = contributor.value === null
        ? contributor.symbolicValue
        : `${formatNumber(contributor.value)}${contributor.unit ? ` ${contributor.unit}` : ""}${contributor.perUnit ? `/${contributor.perUnit}` : ""}`;
    const operation = humanizeKey(contributor.operation).toLowerCase();
    const state = contributor.applied ? "applied" : "retained/not applied";
    return `${humanizeKey(contributor.kind ?? "Reference")} ${humanizeKey(contributor.key)} · ${operation}${value ? ` ${value}` : ""} · ${state}`;
}

function diagnosticSummary(runtime: ExpeditionDetail): string | null {
    const composition = runtime.movementComposition;
    const details = [
        composition.missingInputs.length > 0
            ? `Missing: ${composition.missingInputs.join(", ")}`
            : null,
        ...composition.diagnostics
    ].filter((value): value is string => value !== null && value.length > 0);
    return details.length > 0 ? details.join(" · ") : null;
}

function formatNumber(value: number): string {
    return Number.isInteger(value) ? String(value) : String(Number(value.toFixed(4)));
}

function humanizeKey(value: string): string {
    const text = value.replace(/[-_.]+/g, " ").replace(/([a-z])([A-Z])/g, "$1 $2").trim();
    return text ? text[0].toUpperCase() + text.slice(1).toLowerCase() : value;
}
