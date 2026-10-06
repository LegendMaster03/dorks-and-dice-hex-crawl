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

for _ in {1..40}; do
  if curl -fsS http://127.0.0.1:4173/visual-review/phase15.html >/dev/null; then
    break
  fi
  sleep 0.25
done
curl -fsS http://127.0.0.1:4173/visual-review/phase15.html >/dev/null

cases=(
  "01-home-desktop|home|light|1440|1000"
  "02-home-narrow|home|light|500|900|390"
  "03-procedure-home|procedure-home|light|1366|1000"
  "04-procedure-compact|procedure-compact|light|1366|1000"
  "05-procedure-advanced|procedure-advanced|light|1366|1000"
  "06-procedure-json|procedure-json|light|1366|1000"
  "07-spatial-no-course|no-course|light|1600|1000"
  "08-spatial-selected-edge|selected-edge|light|1366|900"
  "09-spatial-persisted-course|persisted-course|light|1366|900"
  "10-spatial-movement|movement-input-pending|light|1366|900"
  "11-spatial-navigation|navigation-pending|light|1366|900"
  "12-spatial-boundary|boundary-pending|light|1366|900"
  "13-spatial-encounter|encounter-pending|dark|1366|900"
  "14-spatial-forced-travel|forced-travel-pending|light|1366|900"
  "15-spatial-more-options|more-options-open|dark|1366|900"
  "16-spatial-nonadjacent-inspect|map-nonadjacent|light|1366|900"
  "17-spatial-teleport-workspace|teleport-workspace|light|1366|900"
  "18-journey-normal|journey-normal|light|1366|900"
  "19-journey-pending|journey-pending|light|1366|900"
  "20-journey-consequence|journey-consequence|light|1366|900"
  "21-responsive-laptop|selected-edge|light|1366|900"
  "22-responsive-embedded|selected-edge|light|1120|820|900"
  "23-responsive-tablet|selected-edge|light|820|980"
  "24-responsive-narrow|selected-edge|light|500|844|390"
  "25-theme-dark-spatial|selected-edge|dark|1366|900"
  "26-theme-light-spatial|selected-edge|light|1366|900"
  "27-theme-dark-nonspatial|journey-normal|dark|1366|900"
  "28-theme-light-nonspatial|journey-normal|light|1366|900"
)

for spec in "${cases[@]}"; do
  IFS='|' read -r name state theme width height container_width <<<"$spec"
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
  python3 - "$out_dir/$name.html" "$out_dir/$name.json" <<'PY'
import html, json, re, sys
source = open(sys.argv[1], encoding="utf-8").read()
match = re.search(r'<pre id="review-metrics"[^>]*>(.*?)</pre>', source, re.S)
if not match:
    raise SystemExit("visual review metrics were not emitted")
raw = html.unescape(match.group(1)).strip()
if not raw:
    raise SystemExit("visual review metrics were empty before the fixture finished")
metrics = json.loads(raw)
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
    if not all((metrics["startExpeditionVisible"], metrics["manageProceduresVisible"], metrics["manageWorldsVisible"], metrics["gmUtilitiesVisible"])):
        raise SystemExit(f'home hierarchy is incomplete: {metrics}')
elif surface == "procedure":
    if state == "procedure-home" and not metrics["procedureHomeVisible"]:
        raise SystemExit(f'procedure home is incomplete: {metrics}')
    expected = {
        "procedure-compact": "compactVisible",
        "procedure-advanced": "advancedVisible",
        "procedure-json": "jsonVisible"
    }
    if state in expected and not metrics[expected[state]]:
        raise SystemExit(f'wrong procedure authoring surface: {metrics}')
else:
    nonspatial = state.startswith("journey-")
    if nonspatial:
        if metrics["navigatorButtons"] != 0 or metrics["mapHeight"] != 0:
            raise SystemExit(f'nonspatial fixture fabricated a map or navigator: {metrics}')
        if metrics["currentTravelVisible"] or metrics["fakeSpatialStateVisible"]:
            raise SystemExit(f'nonspatial fixture fabricated spatial travel state: {metrics}')
        if metrics["movementStatusVisible"]:
            raise SystemExit(f'nonspatial journey without movement capability rendered movement state: {metrics}')
        if not metrics["journeyVisible"] or not metrics["railVisible"]:
            raise SystemExit(f'journey-first primary state is incomplete: {metrics}')
        if state == "journey-pending" and metrics["primaryAction"] != "Resolve journey event":
            raise SystemExit(f'journey event is not the actual next action: {metrics}')
        if state == "journey-consequence" and not metrics["journeyConsequenceVisible"]:
            raise SystemExit(f'journey consequence state is not visible on the primary surface: {metrics}')
    else:
        if metrics["navigatorButtons"] != 6:
            raise SystemExit(f'navigator edge count: {metrics}')
        if metrics["mapHeight"] < 250:
            raise SystemExit(f'map too short to remain usable: {metrics}')
        if not metrics["railVisible"]:
            raise SystemExit(f'At the Table rail is not visible: {metrics}')
        if metrics["continueButtons"] > 1:
            raise SystemExit(f'duplicate Continue travel actions: {metrics}')
        if metrics["travelControlsButtons"] != 0:
            raise SystemExit(f'legacy Travel controls action returned: {metrics}')

    if state == "no-course":
        if metrics["selectedEdges"] != 0 or metrics["primaryAction"] != "Choose course":
            raise SystemExit(f'no-course state is inconsistent: {metrics}')
    if state in {"selected-edge", "persisted-course"} and metrics["selectedEdges"] != 1:
        raise SystemExit(f'expected exactly one selected edge: {metrics}')
    if state == "persisted-course":
        if metrics["primaryAction"] != "Continue travel" or not metrics["currentTravelVisible"]:
            raise SystemExit(f'persisted course did not restore the shared travel projection: {metrics}')
    if state == "map-nonadjacent" and not metrics["teleportContextVisible"]:
        raise SystemExit(f'non-adjacent inspection did not expose deliberate teleport authority: {metrics}')
    if state == "movement-input-pending" and not metrics["movementUnitVisible"]:
        raise SystemExit(f'movement resolution omitted its authoritative unit: {metrics}')
    if state == "forced-travel-pending":
        if not metrics["forcedTravelPrimaryDomainFacing"]:
            raise SystemExit(f'forced-travel primary workflow is not domain-facing: {metrics}')
        if metrics["forcedTravelTechnicalExpanded"]:
            raise SystemExit(f'forced-travel advanced consequence details opened by default: {metrics}')

    expected_titles = {
        "navigation-pending": "Navigation",
        "movement-input-pending": "Movement resolution",
        "boundary-pending": "Boundary crossing",
        "encounter-pending": "Encounter",
        "forced-travel-pending": "Forced travel",
        "more-options-open": "Advanced travel controls",
        "teleport-workspace": "Teleport party"
    }
    if state in expected_titles:
        if metrics["drawerCount"] != 1 or metrics["focusedTitle"] != expected_titles[state]:
            raise SystemExit(f'wrong focused workflow: {metrics}')

    if state == "selected-edge" and metrics["reviewWidth"] <= 392:
        if metrics["mapTop"] is None or metrics["mapTop"] >= metrics["viewportHeight"]:
            raise SystemExit(f'narrow map does not begin in the initial viewport: {metrics}')
PY
done

python3 - "$out_dir" <<'PY'
import json, pathlib, sys
root = pathlib.Path(sys.argv[1])
metrics = [json.loads(path.read_text()) for path in sorted(root.glob("*.json"))]
(root / "summary.json").write_text(json.dumps(metrics, indent=2, sort_keys=True))
print(json.dumps(metrics, indent=2, sort_keys=True))
PY
