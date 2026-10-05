import test from "node:test";
import assert from "node:assert/strict";
import fs from "node:fs";
import {
    authoritativeFixedWatchDistance,
    convertDistanceValue,
    movementContributorsAfterMemberRemoval,
    suggestedWatchDistance
} from "../.test-dist/modules/expeditions/expedition-party-movement.js";

const mile = { kind: "Mile", symbol: "mi", metersPerUnit: 1609.344 };
const kilometer = { kind: "Kilometer", symbol: "km", metersPerUnit: 1000 };

function runtime({
    suggestion = null,
    targetUnit = mile,
    baseMovement = null,
    missingInputs = [],
    compositionStatus = "Resolved",
    travelResolution = "ContinuousDistance",
    actualDistanceResolution = "Fixed",
    isSpatial = true
} = {}) {
    return {
        movementComposition: {
            suggestedExpectedDistance: suggestion,
            missingInputs,
            status: compositionStatus
        },
        party: { baseMovement },
        procedure: {
            runtime: {
                travelResolution,
                actualDistanceResolution
            }
        },
        expedition: {
            isSpatial,
            distanceTraveled: { value: 0, unit: targetUnit }
        }
    };
}

test("distance conversion uses explicit physical unit metadata", () => {
    assert.equal(convertDistanceValue({ value: 12, unit: mile }, mile), 12);
    assert.equal(
        convertDistanceValue({ value: 12, unit: mile }, kilometer),
        19.312128);
    assert.equal(
        convertDistanceValue(
            { value: 3, unit: { kind: "Custom", symbol: "hex", metersPerUnit: null } },
            mile),
        null);
});

test("watch suggestion presents the server-derived movement composition", () => {
    assert.equal(suggestedWatchDistance(runtime({
        suggestion: { value: 6, unit: mile }
    })), 6);
});

test("watch suggestion performs only presentational unit conversion", () => {
    assert.equal(suggestedWatchDistance(runtime({
        suggestion: { value: 1, unit: kilometer },
        targetUnit: mile
    })), 0.621371192237334);
});

test("fixed travel defaults consume deterministic resolved and reference-fallback suggestions", () => {
    assert.equal(authoritativeFixedWatchDistance(runtime({
        suggestion: { value: 6, unit: mile }
    })), 6);
    assert.equal(authoritativeFixedWatchDistance(runtime({
        suggestion: { value: 4, unit: mile },
        compositionStatus: "ReferenceFallback",
        missingInputs: ["automatic movement capability"]
    })), 4);
});

test("fixed travel defaults reject non-resolved composition states even if malformed input carries a suggestion", () => {
    for (const compositionStatus of [
        "InputRequired",
        "RequiresAdjudication",
        "Unsupported",
        "Unavailable",
        "Failed"
    ]) {
        assert.equal(authoritativeFixedWatchDistance(runtime({
            suggestion: { value: 6, unit: mile },
            compositionStatus
        })), null);
    }
});

test("variable and step travel still require procedure resolution rather than inventing a fixed default", () => {
    assert.equal(authoritativeFixedWatchDistance(runtime({
        suggestion: { value: 6, unit: mile },
        actualDistanceResolution: "VariableResolved"
    })), null);
    assert.equal(authoritativeFixedWatchDistance(runtime({
        suggestion: { value: 6, unit: mile },
        travelResolution: "HexSteps"
    })), null);
    assert.equal(authoritativeFixedWatchDistance(runtime({
        suggestion: { value: 6, unit: mile },
        isSpatial: false
    })), null);
});

test("client does not reconstruct movement from party reference state", () => {
    const result = suggestedWatchDistance(runtime({
        baseMovement: {
            perHour: { value: 3, unit: mile },
            perWatch: { value: 12, unit: mile },
            perMarch: null,
            limitingMemberId: null,
            note: null
        }
    }));

    assert.equal(result, null);
});

test("server movement suggestion is optional", () => {
    assert.equal(suggestedWatchDistance(runtime()), null);
});

test("member removal drops direct movement references and prunes conveyance assignments", () => {
    const bob = "00000000-0000-0000-0000-000000000001";
    const cara = "00000000-0000-0000-0000-000000000002";
    const contributors = [
        {
            id: "10000000-0000-0000-0000-000000000001",
            kind: "Participant",
            key: "bob-walk",
            operation: "Base",
            scope: "Participant",
            value: 3,
            unit: "mi",
            perUnit: "hour",
            distanceUnit: mile,
            symbolicValue: null,
            participantId: bob,
            movementUnitKey: null,
            replacesParticipantIds: [],
            provenance: null,
            note: null,
            enabled: true
        },
        {
            id: "10000000-0000-0000-0000-000000000002",
            kind: "Vehicle",
            key: "wagon",
            operation: "Base",
            scope: "MovementUnit",
            value: 4,
            unit: "mi",
            perUnit: "hour",
            distanceUnit: mile,
            symbolicValue: null,
            participantId: null,
            movementUnitKey: null,
            replacesParticipantIds: [bob, cara],
            provenance: null,
            note: null,
            enabled: true
        },
        {
            id: "10000000-0000-0000-0000-000000000003",
            kind: "Vehicle",
            key: "cart",
            operation: "Base",
            scope: "MovementUnit",
            value: 4,
            unit: "mi",
            perUnit: "hour",
            distanceUnit: mile,
            symbolicValue: null,
            participantId: null,
            movementUnitKey: null,
            replacesParticipantIds: [bob],
            provenance: null,
            note: null,
            enabled: true
        },
        {
            id: "10000000-0000-0000-0000-000000000004",
            kind: "PersistentEffect",
            key: "unrelated-effect",
            operation: "Multiply",
            scope: "Party",
            value: 0.75,
            unit: "factor",
            perUnit: null,
            distanceUnit: null,
            symbolicValue: null,
            participantId: null,
            movementUnitKey: null,
            replacesParticipantIds: [],
            provenance: null,
            note: null,
            enabled: true
        }
    ];

    const cleaned = movementContributorsAfterMemberRemoval(contributors, bob);

    assert.ok(cleaned);
    assert.equal(cleaned.length, 3);
    assert.equal(cleaned.some(contributor => contributor.participantId === bob), false);
    assert.deepEqual(cleaned.find(contributor => contributor.key === "wagon").replacesParticipantIds, [cara]);
    assert.deepEqual(cleaned.find(contributor => contributor.key === "cart").replacesParticipantIds, []);
    assert.ok(cleaned.some(contributor => contributor.key === "unrelated-effect"));
});

test("omitted contributor state remains omitted until member removal deliberately materializes it", () => {
    assert.equal(movementContributorsAfterMemberRemoval(undefined, "member"), undefined);

    const source = fs.readFileSync(
        new URL("../src/modules/expeditions/expedition-party-sheet.ts", import.meta.url),
        "utf8");
    assert.match(source, /movementContributorsAfterMemberRemoval/);
    assert.match(source, /cloneParty\(draft, draft\.movementContributors !== undefined\)/);
    assert.match(source, /includeMovementContributors = false/);
});

test("movement suggestion source uses the canonical projection without watch or party-reference math", () => {
    const source = fs.readFileSync(new URL("../src/modules/expeditions/expedition-party-movement.ts", import.meta.url), "utf8");
    assert.match(source, /runtime\.movementComposition\.suggestedExpectedDistance/);
    assert.doesNotMatch(source, /MovementCompositionProjection/);
    assert.doesNotMatch(source, /as\s+MovementComposition/);
    assert.doesNotMatch(source, /baseMovement/);
    assert.doesNotMatch(source, /activeWatchRemainingHours/);
    assert.doesNotMatch(source, /intervalHours/);
});

test("HTTP and TypeScript expedition detail both expose canonical movement composition", () => {
    const types = fs.readFileSync(new URL("../src/types.ts", import.meta.url), "utf8");
    const contract = fs.readFileSync(new URL("../../Modules/Expeditions/ExpeditionApiContracts.cs", import.meta.url), "utf8");

    assert.match(types, /movementComposition:\s*MovementCapabilityComposition;/);
    assert.match(contract, /MovementCapabilityCompositionContract\s+MovementComposition/);
    assert.match(contract, /MovementCapabilityComposer\.Compose\(expedition\)/);
});

test("movement composition UI exposes limiter provenance and unresolved diagnostics", () => {
    const view = fs.readFileSync(new URL("../src/modules/expeditions/expedition-movement-composition-view.ts", import.meta.url), "utf8");

    assert.match(view, /Movement limiter/);
    assert.match(view, /Movement provenance/);
    assert.match(view, /Missing:/);
    assert.match(view, /composition\.diagnostics/);
});