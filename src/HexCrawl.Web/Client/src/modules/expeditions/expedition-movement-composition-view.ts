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

export function movementCompositionLedger(runtime: ExpeditionDetail): HTMLElement {
    const composition = runtime.movementComposition;
    const section = document.createElement("section");
    section.className = "hc-movement-composition-ledger";
    section.dataset.movementCompositionLedger = "";

    const heading = document.createElement("header");
    const title = document.createElement("div");
    const kicker = document.createElement("span");
    kicker.className = "hc-ledger-kicker";
    kicker.textContent = "Effective movement";
    const value = document.createElement("strong");
    value.className = "hc-ledger-effective";
    value.textContent = composition.effectiveValue === null
        ? "Unresolved"
        : `${formatNumber(composition.effectiveValue)} ${composition.effectiveUnit ?? "unit"}${composition.effectivePerUnit ? `/${composition.effectivePerUnit}` : ""}`;
    title.append(kicker, value);
    const status = document.createElement("span");
    status.className = "hc-ledger-status";
    status.textContent = humanizeKey(composition.status);
    heading.append(title, status);
    section.append(heading);

    const summary = document.createElement("dl");
    summary.className = "hc-movement-ledger-summary";
    appendDefinition(summary, "Limiter", limiterSummary(runtime));
    appendDefinition(summary, "Reference basis", referenceUseLabel(composition.referenceUse));
    const appliedOverride = composition.contributors.some(contributor =>
        contributor.kind === "DmOverride" && contributor.applied);
    if (appliedOverride && composition.preOverrideValue !== null) {
        appendDefinition(
            summary,
            "Before DM override",
            `${formatNumber(composition.preOverrideValue)} ${composition.effectiveUnit ?? "unit"}${composition.effectivePerUnit ? `/${composition.effectivePerUnit}` : ""}`);
    }
    section.append(summary);

    if (composition.contributors.length === 0) {
        const empty = document.createElement("p");
        empty.className = "hc-muted";
        empty.textContent = composition.missingInputs.length > 0
            ? "No movement contributors are currently resolved."
            : "This ruleset has no calculated party movement value. Supply the distance resolved for the current travel period.";
        section.append(empty);
    } else {
        const table = document.createElement("table");
        table.className = "hc-movement-ledger-table";
        const caption = document.createElement("caption");
        caption.textContent = "Movement composition contributors";
        table.append(caption);
        const head = document.createElement("thead");
        const headerRow = document.createElement("tr");
        for (const label of ["Source", "Adjustment", "Value", "State"]) {
            const cell = document.createElement("th");
            cell.scope = "col";
            cell.textContent = label;
            headerRow.append(cell);
        }
        head.append(headerRow);
        const body = document.createElement("tbody");
        for (const contributor of composition.contributors) {
            const row = document.createElement("tr");
            const source = document.createElement("th");
            source.scope = "row";
            source.textContent = contributorSource(runtime, contributor);
            if (contributor.detail || contributor.provenance) {
                const note = document.createElement("small");
                note.textContent = [contributor.detail, contributor.provenance].filter(Boolean).join(" · ");
                source.append(document.createElement("br"), note);
            }
            const operation = document.createElement("td");
            operation.textContent = humanizeKey(contributor.operation);
            const amount = document.createElement("td");
            amount.textContent = contributorValue(contributor) || "Symbolic / unresolved";
            const state = document.createElement("td");
            state.textContent = contributor.applied ? "Applied" : "Retained, not applied";
            row.append(source, operation, amount, state);
            body.append(row);
        }
        table.append(head, body);
        section.append(table);
    }

    const diagnostics = diagnosticSummary(runtime);
    if (diagnostics) {
        const technical = document.createElement("details");
        technical.className = "hc-optional-reference";
        const title = document.createElement("summary");
        title.textContent = "Technical movement diagnostics";
        technical.append(title);
        const note = document.createElement("p");
        note.className = "hc-movement-ledger-diagnostic";
        note.textContent = diagnostics;
        technical.append(note);
        section.append(technical);
    }
    return section;
}

function appendDefinition(list: HTMLDListElement, label: string, value: string): void {
    const term = document.createElement("dt");
    term.textContent = label;
    const detail = document.createElement("dd");
    detail.textContent = value;
    list.append(term, detail);
}

function contributorSource(runtime: ExpeditionDetail, contributor: MovementAppliedContributor): string {
    const kind = humanizeKey(contributor.kind ?? "Reference");
    const participant = contributor.participantId
        ? runtime.party.members.find(member => member.id === contributor.participantId)?.name ?? contributor.participantId
        : null;
    return `${kind} · ${humanizeKey(contributor.key)}${participant ? ` · ${participant}` : ""}`;
}

function contributorValue(contributor: MovementAppliedContributor): string {
    if (contributor.value !== null) {
        return `${formatNumber(contributor.value)}${contributor.unit ? ` ${contributor.unit}` : ""}${contributor.perUnit ? `/${contributor.perUnit}` : ""}`;
    }
    return contributor.symbolicValue ?? "";
}

function referenceUseLabel(value: ExpeditionDetail["movementComposition"]["referenceUse"]): string {
    switch (value) {
        case "AuthoritativeBase": return "Authoritative base";
        case "Fallback": return "Reference fallback";
        case "InformationalOnly": return "Informational only";
        default: return "No reference value used";
    }
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
    return composition.limitingContributorKey === "procedure-base-budget"
        ? "Ruleset movement budget"
        : composition.limitingContributorKey ? humanizeKey(composition.limitingContributorKey) : "—";
}

function contributorSummary(runtime: ExpeditionDetail): string {
    const contributors = runtime.movementComposition.contributors;
    if (contributors.length === 0) return "No calculated movement inputs";
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
