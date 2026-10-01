import assert from "node:assert/strict";
import fs from "node:fs";
import path from "node:path";
import test from "node:test";
import { fileURLToPath } from "node:url";

const here = path.dirname(fileURLToPath(import.meta.url));
const clientRoot = path.resolve(here, "..");
const types = fs.readFileSync(path.join(clientRoot, "src/types.ts"), "utf8");
const focusedPolicy = fs.readFileSync(
    path.join(clientRoot, "src/modules/expeditions/focused-interval-policy.ts"),
    "utf8");
const serverContract = fs.readFileSync(
    path.join(clientRoot, "../Modules/ReferenceData/ProcedureContracts.cs"),
    "utf8");

test("CampaignProcedure canonically includes the mandatory focused interval projection", () => {
    assert.match(types, /export type FocusedIntervalPolicySupport = "None" \| "Supported" \| "Unsupported";/);
    assert.match(types, /export type FocusedIntervalPolicy = \{[\s\S]*support: FocusedIntervalPolicySupport;[\s\S]*intervalHours: number \| null;[\s\S]*mechanicKey: string \| null;[\s\S]*mechanicVersion: number \| null;[\s\S]*executionHandler: string \| null;[\s\S]*unsupportedReason: string \| null;[\s\S]*\};/);
    assert.match(types, /export type CampaignProcedure = \{[\s\S]*runtime: ProcedureRuntime \| null;[\s\S]*focusedIntervalPolicy: FocusedIntervalPolicy;[\s\S]*modules: ProcedureModule\[\];[\s\S]*\};/);
    assert.doesNotMatch(types, /focusedIntervalPolicy\?:/);
});

test("focused interval client code uses the canonical CampaignProcedure field without a shadow contract or cast", () => {
    assert.match(focusedPolicy, /import type \{ ExpeditionDetail, FocusedIntervalPolicy \} from "\.\.\/\.\.\/types";/);
    assert.match(focusedPolicy, /function focusedIntervalPolicy\(runtime: ExpeditionDetail\): FocusedIntervalPolicy \{\s*return runtime\.procedure\.focusedIntervalPolicy;\s*\}/s);
    assert.doesNotMatch(focusedPolicy, /ProcedureWithFocusedIntervalPolicy/);
    assert.doesNotMatch(focusedPolicy, /runtime\.procedure as/);
});

test("server and client focused interval contract field names remain aligned", () => {
    const serverFields = [
        "Support",
        "IntervalHours",
        "MechanicKey",
        "MechanicVersion",
        "ExecutionHandler",
        "UnsupportedReason"
    ];
    const clientFields = [
        "support",
        "intervalHours",
        "mechanicKey",
        "mechanicVersion",
        "executionHandler",
        "unsupportedReason"
    ];

    assert.match(serverContract, /public sealed record CampaignProcedureContract\([\s\S]*FocusedIntervalPolicyContract FocusedIntervalPolicy,[\s\S]*IReadOnlyList<ProcedureModuleContract> Modules\)/);
    assert.match(serverContract, /public sealed record FocusedIntervalPolicyContract\([\s\S]*\)/);
    for (const field of serverFields) assert.match(serverContract, new RegExp(`\\b${field}\\b`));
    for (const field of clientFields) assert.match(types, new RegExp(`\\b${field}:`));
});
