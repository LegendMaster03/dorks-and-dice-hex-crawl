#!/usr/bin/env bash
set -euo pipefail

cd "$(dirname "$0")/.."
out_dir="$PWD/visual-review/out"
rm -rf "$out_dir"
mkdir -p "$out_dir"

chrome_bin="$(command -v google-chrome || command -v google-chrome-stable || command -v chromium || command -v chromium-browser || true)"
if [[ -z "$chrome_bin" ]]; then
  echo "A Chromium-family browser is required for Phase 15 rendered review." >&2
  exit 1
fi
"$chrome_bin" --version | tee "$out_dir/browser-version.txt"

npx vite --host 127.0.0.1 --port 4173 >"$out_dir/vite.log" 2>&1 &
vite_pid=$!
trap 'kill "$vite_pid" 2>/dev/null || true' EXIT

preview_url="http://127.0.0.1:4173/visual-review/phase15.html"
preview_ready=false
for _ in {1..40}; do
  if ! kill -0 "$vite_pid" 2>/dev/null; then
    echo "Phase 15 preview server exited before becoming ready." >&2
    cat "$out_dir/vite.log" >&2
    exit 1
  fi
  if curl -fsS --max-time 1 "$preview_url" >/dev/null 2>&1; then
    preview_ready=true
    break
  fi
  sleep 0.25
done
if [[ "$preview_ready" != "true" ]]; then
  echo "Phase 15 preview server did not become ready on 127.0.0.1:4173." >&2
  cat "$out_dir/vite.log" >&2
  exit 1
fi
echo "Phase 15 preview server ready."

cases=(
  "01-home-desktop|home|light|1440|1000"
  "02-home-narrow|home|light|500|900|390"
  "03-procedure-home|procedure-home|light|1366|1000"
  "03a-procedure-home-container-390|procedure-home|dark|500|900|390"
  "03b-procedure-catalog|procedure-catalog|light|1366|1000"
  "03c-procedure-catalog-container-390|procedure-catalog|dark|500|900|390"
  "04-procedure-compact|procedure-compact|light|1366|1000"
  "04a-procedure-compact-container-390|procedure-compact|dark|390|844|390"
  "04b-procedure-reference|procedure-reference|light|1366|1000"
  "05-procedure-advanced|procedure-advanced|light|1366|1000"
  "06-procedure-json|procedure-json|light|1366|1000"
  "07-spatial-no-course|no-course|light|1600|1000"
  "08-spatial-selected-edge|selected-edge|light|1366|900"
  "09-spatial-persisted-course|persisted-course|light|1366|900"
  "10-spatial-movement|movement-input-pending|light|1366|900"
  "10d-spatial-movement-validation-desktop|movement-validation-retry|light|1366|900"
  "10e-spatial-movement-validation-narrow|movement-validation-retry|dark|500|844|390"
  "10a-spatial-movement-composition|movement-composition|light|1366|900"
  "10b-spatial-movement-composition-mobile|movement-composition|dark|500|844|390"
  "10c-nonspatial-movement-composition|nonspatial-movement-composition|light|1366|900"
  "11-spatial-navigation|navigation-pending|light|1366|900"
  "12-spatial-boundary|boundary-pending|light|1366|900"
  "13-spatial-encounter|encounter-pending|dark|1366|900"
  "13a-encounter-schedule|encounter-schedule|light|1366|900"
  "13b-encounter-contextual-schedule|encounter-contextual-schedule|light|1366|900"
  "13c-encounter-schedule-day|encounter-schedule-day|dark|1366|900"
  "14-spatial-forced-travel|forced-travel-pending|light|1366|900"
  "14a-spatial-forced-travel-one|forced-travel-one-participant|light|1366|900"
  "14b-spatial-forced-travel-zero|forced-travel-zero-participants|dark|500|844|390"
  "14c-spatial-forced-travel-party|forced-travel-party-scope|light|1366|900"
  "14d-spatial-forced-travel-mount|forced-travel-mount|light|1366|900"
  "14e-spatial-forced-travel-vehicle|forced-travel-vehicle|dark|1366|900"
  "15-spatial-more-options|more-options-open|dark|1366|900"
  "16-spatial-nonadjacent-inspect|map-nonadjacent|light|1366|900"
  "17-spatial-teleport-workspace|teleport-workspace|light|1366|900"
  "18-journey-normal|journey-normal|light|1366|900"
  "19-journey-pending|journey-pending|light|1366|900"
  "20-journey-consequence|journey-consequence|light|1366|900"
  "20a-journey-consequence-multiple|journey-consequence-multiple|light|1366|900"
  "20b-journey-consequence-resolved|journey-consequence-resolved|dark|1366|900"
  "20c-effects-journey-source|effects-journey-source|light|1366|900"
  "20d-abstract-spatial-course|abstract-spatial-course|light|1366|900"
  "21-responsive-laptop|selected-edge|light|1366|900"
  "22-responsive-embedded|selected-edge|light|1120|820|900"
  "23-responsive-tablet|selected-edge|light|820|980"
  "24-responsive-narrow|selected-edge|light|500|844|390"
  "25-theme-dark-spatial|selected-edge|dark|1366|900"
  "26-theme-light-spatial|selected-edge|light|1366|900"
  "27-theme-dark-nonspatial|journey-normal|dark|1366|900"
  "28-theme-light-nonspatial|journey-normal|light|1366|900"
  "29-spatial-fixed-pace|fixed-pace|light|1366|900"
  "30-spatial-empty-party|empty-party|light|1366|900"
  "31-effects-workspace-wide|effects-workspace|light|1366|900"
  "32-effects-workspace-mobile|effects-workspace|dark|500|844|390"
  "32a-readability-workspace-wide|readability-workspace|light|1366|1000"
  "32b-readability-workspace-container-390|readability-workspace|dark|390|844|390"
  "32c-guided-pending-consequence|guided-pending-consequence|light|1366|900"
  "33-history-workspace-wide|history-workspace|light|1366|900"
  "34-history-workspace-mobile|history-workspace|dark|500|844|390"
  "35-encounter-mobile|encounter-pending|light|500|844|390"
  "36-journey-pending-mobile|journey-pending|dark|500|844|390"
  "37-partial-progress-mobile|partial-progress|dark|500|844|390"
  "38-focus-return-wide|focus-return|light|1366|900"
  "39-focus-return-mobile|focus-return|dark|500|844|390"
  "40-world-selected-cell-wide|world-selected-cell|light|1366|900"
  "41-world-selected-cell-dark|world-selected-cell|dark|1366|900"
  "42-world-selected-cell-narrow|world-selected-cell|light|500|844|390"
  "43-course-change-reload|course-change-reload|light|1366|900"
  "44-course-clear-reload|course-clear-reload|dark|820|980"
)

failures=()
for spec in "${cases[@]}"; do
  IFS='|' read -r name state theme width height container_width <<<"$spec"
  echo "Rendering $name ($state, $theme, ${width}x${height})..."
  url="http://127.0.0.1:4173/visual-review/phase15.html?state=$state&theme=$theme"
  if [[ -n "${container_width:-}" ]]; then
    url="$url&containerWidth=$container_width"
  fi
  profile="$(mktemp -d)"
  common=(
    --headless=new
    --no-sandbox
    --disable-dev-shm-usage
    --hide-scrollbars
    --force-device-scale-factor=1
    --run-all-compositor-stages-before-draw
    --virtual-time-budget=5000
    --window-size="$width,$height"
    --user-data-dir="$profile"
  )

  "$chrome_bin" "${common[@]}" --screenshot="$out_dir/$name.png" "$url" >/dev/null 2>"$out_dir/$name.chrome.log"
  dom="$("$chrome_bin" "${common[@]}" --dump-dom "$url" 2>>"$out_dir/$name.chrome.log")"
  rm -rf "$profile"

  printf "%s" "$dom" >"$out_dir/$name.html"
  if python3 - "$out_dir/$name.html" "$out_dir/$name.json" "$name" "$width" "${container_width:-}" <<'PY'
import html, json, re, sys
source = open(sys.argv[1], encoding="utf-8").read()
match = re.search(r'<pre id="review-metrics"[^>]*>(.*?)</pre>', source, re.S)
if not match:
    raise SystemExit("visual review metrics were not emitted")
raw = html.unescape(match.group(1)).strip()
if not raw:
    raise SystemExit("visual review metrics were empty before the fixture finished")
metrics = json.loads(raw)
if "fixtureError" in metrics:
    raise SystemExit(f'Visual-review fixture initialization failed: {metrics["fixtureError"]}')
case_name = sys.argv[3]
requested_window_width = int(sys.argv[4])
requested_container_width = int(sys.argv[5]) if sys.argv[5] else None
metrics["captureCase"] = case_name
metrics["requestedWindowWidth"] = requested_window_width
metrics["requestedContainerWidth"] = requested_container_width
metrics["measuredViewportMatchesRequestedWindow"] = metrics["viewportWidth"] == requested_window_width
if requested_container_width is not None and abs(metrics["reviewWidth"] - requested_container_width) > 2:
    raise SystemExit(f'requested review container width was not established: {metrics}')
with open(sys.argv[2], "w", encoding="utf-8") as handle:
    json.dump(metrics, handle, indent=2, sort_keys=True)

if metrics["scrollWidth"] > metrics["viewportWidth"] + 2:
    raise SystemExit(f'horizontal viewport overflow: {metrics}')
if metrics["reviewScrollWidth"] > metrics["reviewWidth"] + 2:
    raise SystemExit(f'horizontal tool overflow: {metrics}')

state = metrics["state"]
surface = metrics["surface"]

if surface == "home":
    if metrics["pageTitle"] != "Hex Crawl":
        raise SystemExit(f'wrong home title: {metrics}')
    if metrics["openExpeditionButtons"] < 1:
        raise SystemExit(f'home does not expose Open expedition: {metrics}')
    if not all((metrics["startExpeditionVisible"], metrics["manageRulesetsVisible"], metrics["manageWorldsVisible"], metrics["gmUtilitiesVisible"])):
        raise SystemExit(f'home hierarchy is incomplete: {metrics}')
    if not metrics["guidedSetupVisible"] or not metrics["guidanceToggleVisible"] or metrics["guidedFieldHelpCount"] < 4:
        raise SystemExit(f'Guided home onboarding is incomplete: {metrics}')
    if metrics["guidedSetupStepCount"] != 3 or metrics["visibleGuidedSetupSteps"] != 1:
        raise SystemExit(f'Guided setup did not return to one clear current step: {metrics}')
    if not metrics["guidedSetupPreservesInputs"] or not metrics["guidedSetupDirectShowsAll"] or not metrics["guidedSetupReenablePreservesStep"]:
        raise SystemExit(f'Guided setup lost state across back/direct/re-enable transitions: {metrics}')
    if not metrics["guidedSetupTransitioned"] or not metrics["guidedSetupAllContexts"]:
        raise SystemExit(f'Guided setup did not traverse mapped, mapless and nonspatial paths: {metrics}')
    if metrics["guidedSetupCreateCalls"] != 0:
        raise SystemExit(f'Guided navigation created an expedition before explicit submit: {metrics}')
    if metrics["mapScaleFieldCount"] < 1:
        raise SystemExit(f'Map scale is not presented as a grouped value/unit field: {metrics}')
elif surface == "procedure":
    if state == "procedure-home" and not metrics["procedureHomeVisible"]:
        raise SystemExit(f'procedure home is incomplete: {metrics}')
    if state == "procedure-home" and metrics["presetDecisionFactGroups"] < 1:
        raise SystemExit(f'Guided preset discovery is missing: {metrics}')
    if state == "procedure-home" and metrics["presetViewDetailsButtons"] < 1:
        raise SystemExit(f'Preset details are not progressively disclosed: {metrics}')
    if state in ("procedure-home", "procedure-catalog"):
        if metrics["presetDecisionFactGroups"] < 3 or metrics["presetCautionCount"] < 2:
            raise SystemExit(f'Representative catalog facts or partial-support warnings are missing: {metrics}')
        if not {"B/X", "Old-School Essentials Classic Fantasy"}.issubset(set(metrics["representativePresetTitles"])):
            raise SystemExit(f'Distinct shared-behavior sources are missing: {metrics}')
    if state == "procedure-catalog" and "Simple Fixed Distance" not in metrics["representativePresetTitles"]:
        raise SystemExit(f'Simple disabled-navigation/encounter case is missing from catalog: {metrics}')
    if state == "procedure-home" and not metrics["guidanceToggleVisible"]:
        raise SystemExit(f'Guided procedure preference control is missing: {metrics}')
    expected = {
        "procedure-compact": "compactVisible",
        "procedure-advanced": "advancedVisible",
        "procedure-json": "jsonVisible"
    }
    if state in expected and not metrics[expected[state]]:
        raise SystemExit(f'wrong procedure authoring surface: {metrics}')
    expected_modules = metrics["expectedProcedureModuleCount"]
    if expected_modules < 1:
        raise SystemExit(f'procedure fixture has no semantic modules: {metrics}')
    if state == "procedure-compact":
        if metrics["procedureModuleCards"] != expected_modules or metrics["procedureFactGroups"] < expected_modules:
            raise SystemExit(f'feature-rich compact procedure did not render all semantic modules: {metrics}')
        if not metrics["compactGuidanceVisible"] or metrics["guidedRuleWhyCount"] != expected_modules:
            raise SystemExit(f'Guided Compact explanations are incomplete: {metrics}')
    if state == "procedure-reference":
        if not metrics["referenceVisible"] or metrics["referenceModuleCount"] != expected_modules:
            raise SystemExit(f'feature-rich procedure reference is incomplete: {metrics}')
        if metrics["referenceFactGroups"] < expected_modules or metrics["exactParameterDisclosures"] != expected_modules:
            raise SystemExit(f'procedure reference lost semantic or exact parameter evidence: {metrics}')
elif surface == "world":
    if not metrics["guidanceToggleVisible"]:
        raise SystemExit(f'World editor lost its guidance preference control: {metrics}')
    # The selected world is a periodic polygon fixture, not an axial-grid editor.
    summary = metrics["selectedCellSummary"] or ""
    if not re.search(r"Center:.*[3-9][0-9]* sides", summary):
        raise SystemExit(f'qualified polygon context did not render: {metrics}')
    if metrics["selectedCellTerrain"] != "forest":
        raise SystemExit(f'explicit cell-scoped terrain did not populate the editor: {metrics}')
    if not metrics["selectedCellHiddenPoiVisible"]:
        raise SystemExit(f'authored hidden POI is not visible to the DM cell editor: {metrics}')
    if not metrics["selectedCellRoadVisible"]:
        raise SystemExit(f'intersecting route is not visible in selected-cell context: {metrics}')
    if not metrics["selectedCellRouteBehaviorVisible"]:
        raise SystemExit(f'feature-scoped route behavior is not visible in selected-cell context: {metrics}')
    if metrics["selectedCellAdvancedOpen"]:
        raise SystemExit(f'advanced cell rules are open by default: {metrics}')
    if metrics["mapHeight"] < 250:
        raise SystemExit(f'world authoring map is too short to remain usable: {metrics}')
else:
    if not metrics["guidedNextActionWhyVisible"] or not metrics["guidedActionGuideVisible"]:
        raise SystemExit(f'Guided next-action explanation is missing: {metrics}')
    nonspatial = state.startswith("journey-") or state in {"history-workspace", "nonspatial-movement-composition"}
    abstract_spatial = state == "abstract-spatial-course"
    if nonspatial:
        if metrics["navigatorButtons"] != 0 or metrics["mapHeight"] != 0:
            raise SystemExit(f'nonspatial fixture fabricated a map or navigator: {metrics}')
        if metrics["currentTravelVisible"] or metrics["fakeSpatialStateVisible"]:
            raise SystemExit(f'nonspatial fixture fabricated spatial travel state: {metrics}')
        if state == "nonspatial-movement-composition":
            if not metrics["movementStatusVisible"] or not metrics["movementLedgerVisible"] or metrics["movementContributorRows"] < 2:
                raise SystemExit(f'nonspatial movement composition is not presented in the party workspace: {metrics}')
        else:
            if metrics["movementStatusVisible"]:
                raise SystemExit(f'nonspatial journey without movement capability rendered movement state: {metrics}')
            if not metrics["journeyVisible"] or not metrics["railVisible"]:
                raise SystemExit(f'journey-first primary state is incomplete: {metrics}')
        if state == "journey-pending" and metrics["primaryAction"] != "Resolve journey event":
            raise SystemExit(f'journey event is not the actual next action: {metrics}')
        if state == "journey-consequence" and not metrics["journeyConsequenceVisible"]:
            raise SystemExit(f'journey consequence state is not visible on the primary surface: {metrics}')
        if state == "journey-consequence" and not metrics["journeyPendingConsequenceVisible"]:
            raise SystemExit(f'journey summary disagrees with authoritative pending Effects state: {metrics}')
        if state == "journey-consequence-multiple" and not metrics["journeyMultiplePendingConsequenceVisible"]:
            raise SystemExit(f'journey summary did not report multiple authoritative pending consequences: {metrics}')
        if state == "journey-consequence-resolved":
            if metrics["journeyPendingConsequenceVisible"] or metrics["journeyMultiplePendingConsequenceVisible"]:
                raise SystemExit(f'resolved journey consequence remained pending in the summary: {metrics}')
            if not metrics["journeyStageStateVisible"]:
                raise SystemExit(f'stage-local failures/complications were lost after consequence resolution: {metrics}')
        if state == "journey-normal":
            if not metrics["journeyNoPendingConsequencesVisible"]:
                raise SystemExit(f'clean journey does not report an authoritative no-pending state: {metrics}')
            if metrics["journeyStageCount"] != 3:
                raise SystemExit(f'multi-stage journey sequence is incomplete: {metrics}')
            if metrics["journeyStageLabels"] != ["Reach the pass", "Cross the pass", "Reach the high country"]:
                raise SystemExit(f'journey stage sequence ignored authoritative stageOrder: {metrics}')
        if state == "journey-pending" and metrics["journeyPendingRawInternalsVisible"]:
            raise SystemExit(f'normal pending journey presentation exposes raw event internals: {metrics}')
    elif abstract_spatial:
        if metrics["navigatorButtons"] < 3 or metrics["mapHeight"] < 250:
            raise SystemExit(f'mapless periodic geometry did not render its cell navigator: {metrics}')
        if not metrics["currentTravelVisible"] or not metrics["abstractSpatialCourseReused"]:
            raise SystemExit(f'mapless spatial course was not reused through watch resolution: {metrics}')
    else:
        if metrics["navigatorButtons"] != 6:
            raise SystemExit(f'navigator edge count: {metrics}')
        if metrics["mapHeight"] < 250:
            raise SystemExit(f'map too short to remain usable: {metrics}')
        if not metrics["railVisible"]:
            raise SystemExit(f'At the Table rail is not visible: {metrics}')
        if metrics["currentTravelCount"] != 1 or not metrics["currentTravelInRail"]:
            raise SystemExit(f'current travel is not using the table rail exactly once: {metrics}')
        if metrics["partyActivitiesRailButtons"] != 0:
            raise SystemExit(f'rail duplicated the top Party summary: {metrics}')
        if metrics["continueButtons"] > 1:
            raise SystemExit(f'duplicate Continue travel actions: {metrics}')
        if metrics["travelControlsButtons"] != 0:
            raise SystemExit(f'legacy Travel controls action returned: {metrics}')

    if state == "guided-pending-consequence" and metrics["guidedConsequenceHelpCount"] < 1:
        raise SystemExit(f'Guided pending-consequence explanation is missing: {metrics}')

    if not metrics["compassInMapFrame"] or metrics["compassInSidebar"]:
        raise SystemExit(f'spatial compass is not hosted by the map frame: {metrics}')
    offset = metrics["compassOffset"]
    if offset is None or not (0 <= offset["left"] <= 32 and 0 <= offset["top"] <= 32):
        raise SystemExit(f'spatial compass is not in the map upper-left corner: {metrics}')

    if state == "no-course":
        if metrics["selectedEdges"] != 0 or metrics["primaryAction"] != "Choose travel direction":
            raise SystemExit(f'no-course state is inconsistent: {metrics}')
    if state in {"selected-edge", "persisted-course"} and metrics["selectedEdges"] != 1:
        raise SystemExit(f'expected exactly one selected edge: {metrics}')
    if state == "persisted-course":
        if metrics["primaryAction"] != "Continue travel" or not metrics["currentTravelVisible"]:
            raise SystemExit(f'persistent qualified-cell course failed to resume: {metrics}')
        if not (metrics["selectedInterfaceIndex"] or "").endswith(":2") or metrics["runtimeIntendedHeading"] is None:
            raise SystemExit(f'persisted course differs from authoritative cell interface: {metrics}')
    if state == "course-change-reload":
        if not (metrics["selectedInterfaceIndex"] or "").endswith(":5") or metrics["runtimeIntendedHeading"] is None:
            raise SystemExit(f'changed qualified-cell course was not persisted across a fresh client: {metrics}')
        if metrics["runtimeIntendedHeading"] == metrics["runtimeActualHeading"]:
            raise SystemExit(f'changing intended course incorrectly replaced actual travel heading: {metrics}')
        if "boundary 6" not in (metrics["currentTravelCourseText"] or ""):
            raise SystemExit(f'current travel disagrees with the stored cell interface: {metrics}')
        if metrics["drawerCount"] != 1 or metrics["focusedTitle"] != "Travel / watch":
            raise SystemExit(f'fresh client did not expose the current travel workflow: {metrics}')
    if state == "course-clear-reload":
        if metrics["selectedEdges"] != 1 or not (metrics["selectedInterfaceIndex"] or "").endswith(":1"):
            raise SystemExit(f'fresh client did not restore saved course after discarding a local draft: {metrics}')
        if metrics["runtimeIntendedHeading"] is None or metrics["primaryAction"] != "Continue travel":
            raise SystemExit(f'reloading a discarded course proposal changed authoritative travel intent: {metrics}')
    if state == "map-nonadjacent" and not metrics["teleportContextVisible"]:
        raise SystemExit(f'distant cell inspection did not expose an explicit GM teleport action: {metrics}')
    if state == "teleport-workspace":
        if metrics["drawerCount"] != 1 or metrics["focusedTitle"] != "Teleport party":
            raise SystemExit(f'GM teleport did not open with a qualified cell target: {metrics}')
    if state == "movement-validation-retry":
        if not metrics["movementUnitVisible"] or not metrics["movementValidationVerified"] or not metrics["movementRetryVerified"] or metrics["movementRetryCalls"] != 1:
            raise SystemExit(f'rendered movement validation/retry failed: {metrics}')
    if state == "movement-input-pending" and not metrics["movementUnitVisible"]:
        raise SystemExit(f'movement resolution omitted its authoritative unit: {metrics}')
    if state == "movement-composition":
        if not metrics["movementLedgerVisible"] or metrics["movementContributorRows"] < 9:
            raise SystemExit(f'movement composition ledger did not expose all contributor kinds: {metrics}')
    if state == "encounter-schedule":
        if not metrics["encounterScheduleVisible"] or not metrics["encounterPerWatchVisible"]:
            raise SystemExit(f'per-watch encounter cadence is not visible before an encounter is pending: {metrics}')
    if state == "encounter-schedule-day":
        if not metrics["encounterScheduleVisible"] or not metrics["encounterPerDayVisible"]:
            raise SystemExit(f'per-day encounter cadence is not visible before an encounter is pending: {metrics}')
    if state == "encounter-contextual-schedule":
        if not metrics["encounterScheduleVisible"] or not metrics["contextualEncounterScheduleVisible"]:
            raise SystemExit(f'contextual-only encounter schedule is not visible before an encounter is pending: {metrics}')
    if state == "readability-workspace":
        if metrics["resourceLedgerRows"] < 5 or metrics["effectLedgerRows"] < 2:
            raise SystemExit(f'resource/effect comparison ledgers are incomplete: {metrics}')
        if not all((metrics["readableEnvironmentVisible"], metrics["exposureLedgerVisible"], metrics["foragingSummaryVisible"], metrics["campingSummaryVisible"])):
            raise SystemExit(f'survival readability summaries are incomplete: {metrics}')
        if not metrics["readableLedgerShortCellsSingleLine"] or not metrics["longReadableTargetVisible"]:
            raise SystemExit(f'narrow comparison ledger fragmented core labels or lost the longer named target: {metrics}')
    if state in {"forced-travel-pending", "forced-travel-one-participant", "forced-travel-zero-participants", "forced-travel-party-scope", "forced-travel-mount", "forced-travel-vehicle"}:
        if not metrics["forcedTravelPrimaryDomainFacing"]:
            raise SystemExit(f'forced-travel primary workflow is not domain-facing: {metrics}')
        if metrics["forcedTravelTechnicalExpanded"]:
            raise SystemExit(f'forced-travel advanced consequence details opened by default: {metrics}')
    if state in {"forced-travel-pending", "forced-travel-one-participant", "forced-travel-zero-participants"}:
        if not metrics["forcedTravelNamedTargetVisible"]:
            raise SystemExit(f'forced-travel normal workflow does not expose named participant targeting: {metrics}')
    if state == "forced-travel-pending":
        if metrics["forcedTravelTargetOptionCount"] < 3 or metrics["forcedTravelTargetValue"] != "":
            raise SystemExit(f'multiple participant forced-travel target was silently selected: {metrics}')
        if not metrics["forcedTravelTargetRequired"]:
            raise SystemExit(f'multiple participant forced-travel target can submit without an explicit named choice: {metrics}')
    if state == "forced-travel-one-participant":
        if metrics["forcedTravelTargetOptionCount"] != 1 or metrics["forcedTravelTargetValue"] != "member-1":
            raise SystemExit(f'single participant forced-travel target was not visibly preselected: {metrics}')
    if state == "forced-travel-zero-participants" and not metrics["forcedTravelMissingTargetBlocked"]:
        raise SystemExit(f'zero-participant forced-travel failure did not expose blocked setup state: {metrics}')
    if state == "forced-travel-party-scope":
        if metrics["forcedTravelTargetVisible"] or metrics["forcedTravelTargetRequired"]:
            raise SystemExit(f'party-scope forced travel incorrectly requires an entity target: {metrics}')
    if state == "forced-travel-mount":
        if metrics["forcedTravelTargetOptionCount"] != 1 or metrics["forcedTravelTargetValue"] != "mount-1" or "Pack Mule" not in metrics["forcedTravelTargetLabels"]:
            raise SystemExit(f'mount-scope forced travel did not expose the named authoritative mount: {metrics}')
    if state == "forced-travel-vehicle":
        if metrics["forcedTravelTargetOptionCount"] != 1 or metrics["forcedTravelTargetValue"] != "vehicle-1" or "River Skiff" not in metrics["forcedTravelTargetLabels"]:
            raise SystemExit(f'vehicle-scope forced travel did not expose the named authoritative vehicle: {metrics}')
    if state in {"navigation-pending", "abstract-spatial-course"} and not metrics["navigationCourseReadOnly"]:
        raise SystemExit(f'navigation repeated the already-selected intended course input: {metrics}')
    if state == "effects-journey-source":
        if not metrics["journeyEffectSourceHumanized"]:
            raise SystemExit(f'normal journey-generated effect provenance exposes raw internals: {metrics}')
        if not metrics["journeyEffectTechnicalRetained"]:
            raise SystemExit(f'journey-generated effect diagnostics were lost from Technical details: {metrics}')
    if state == "effects-workspace":
        if not metrics["activeEffectVisible"] or not metrics["effectRecoveryVisible"] or metrics["effectLedgerRows"] < 1:
            raise SystemExit(f'shared gameplay panel lost active effects or recovery controls: {metrics}')
    if state == "history-workspace":
        if metrics["drawerCount"] != 1 or metrics["focusedTitle"] != "Expedition history":
            raise SystemExit(f'history workspace did not open correctly: {metrics}')
        if not metrics["unifiedHistoryVisible"]:
            raise SystemExit(f'unified expedition history is incomplete: {metrics}')
        if metrics["normalHistoryRawInternalsVisible"]:
            raise SystemExit(f'normal expedition history exposes raw journey identity: {metrics}')
        if not metrics["technicalHistoryRetained"]:
            raise SystemExit(f'advanced expedition history lost raw diagnostic identity: {metrics}')
    if state == "partial-progress" and not metrics["partialWatchPositionVisible"]:
        raise SystemExit(f'partial spatial progress is not visible: {metrics}')
    if state == "focus-return" and not metrics["focusReturnedToOpener"]:
        raise SystemExit(f'drawer focus did not return to the opener: {metrics}')
    if state == "encounter-pending" and not metrics["encounterResolutionVisible"]:
        raise SystemExit(f'encounter drawer does not expose authoritative resolution: {metrics}')
    if state == "fixed-pace":
        if metrics["paceOptionCount"] != 1 or metrics["paceTextInputs"] != 0:
            raise SystemExit(f'fixed pace did not stay restricted to one server-provided option: {metrics}')
    if state == "selected-edge":
        if metrics["paceOptionCount"] < 2:
            raise SystemExit(f'finite travel modes did not offer bounded named pace options: {metrics}')
    if state == "more-options-open" and not metrics["gmToolsOpen"]:
        raise SystemExit(f'GM tools disclosure did not expose advanced operations: {metrics}')
    if state == "empty-party" and not metrics["partySetupVisible"]:
        raise SystemExit(f'empty party did not expose a useful setup action: {metrics}')

    # The only spatial watch executor has one travel/watch drawer. Gameplay
    # and GM utilities are shared panels, not separate prototype drawers.
    expected_titles = {
        "navigation-pending": "Travel / watch",
        "movement-input-pending": "Travel / watch",
        "encounter-pending": "Travel / watch",
        "journey-pending": "Journey / challenge",
        "abstract-spatial-course": "Travel / watch",
    }
    if state in expected_titles:
        if metrics["drawerCount"] != 1 or metrics["focusedTitle"] != expected_titles[state]:
            raise SystemExit(f'unified workspace did not open the expected action: {metrics}')
    if state == "selected-edge" and metrics["reviewWidth"] <= 392:
        if metrics["mapTop"] is None or metrics["mapTop"] >= metrics["viewportHeight"]:
            raise SystemExit(f'narrow map does not begin in the initial viewport: {metrics}')
PY
  then
    :
  else
    failures+=("$name")
  fi
done

python3 - "$out_dir" <<'PY'
import json, pathlib, sys
root = pathlib.Path(sys.argv[1])
metrics = [json.loads(path.read_text()) for path in sorted(root.glob("*.json"))]
(root / "summary.json").write_text(json.dumps(metrics, indent=2, sort_keys=True))
print(f"Captured {len(metrics)} completed Phase 15 metrics records.")
PY
if (( ${#failures[@]} > 0 )); then
  printf 'Phase 15 rendered validation failures (%s): %s\\n' "${#failures[@]}" "${failures[*]}" >&2
  exit 1
fi
