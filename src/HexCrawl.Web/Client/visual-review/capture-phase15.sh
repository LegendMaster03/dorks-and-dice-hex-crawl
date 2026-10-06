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
  "wide-no-course|no-course|light|1600|1000"
  "laptop-selected-edge|selected-edge|light|1366|900"
  "embedded-map-selected|map-selected|light|1120|820"
  "laptop-partial-progress|partial-progress|dark|1366|900"
  "tablet-navigation|navigation-pending|dark|820|980"
  "narrow-selected-edge|selected-edge|light|500|844|390"
  "narrow-movement|movement-input-pending|light|500|844|390"
  "narrow-encounter|encounter-pending|dark|500|844|390"
  "laptop-forced-travel|forced-travel-pending|light|1366|900"
  "laptop-more-options|more-options-open|dark|1366|900"
  "embedded-realistic-rail|rail-realistic|light|1120|820"
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
if metrics["navigatorButtons"] != 6:
    raise SystemExit(f'navigator edge count: {metrics}')
if metrics["continueButtons"] > 1:
    raise SystemExit(f'duplicate Continue travel actions: {metrics}')
if metrics["travelControlsButtons"] != 0:
    raise SystemExit(f'legacy Travel controls action returned: {metrics}')
if metrics["mapHeight"] < 250:
    raise SystemExit(f'map too short to remain usable: {metrics}')
if not metrics["railVisible"]:
    raise SystemExit(f'At the Table rail is not visible: {metrics}')
if metrics["state"] in {"no-course", "selected-edge", "partial-progress", "map-selected", "rail-realistic"}:
    if not metrics["currentTravelVisible"] or metrics["currentTravelTop"] >= metrics["viewportHeight"]:
        raise SystemExit(f'Current travel is not discoverable in the initial viewport: {metrics}')
if metrics["state"] in {"movement-input-pending", "encounter-pending"} and metrics["reviewWidth"] > 392:
    raise SystemExit(f'narrow host did not render at mobile-like width: {metrics}')
if metrics["state"] == "no-course":
    if metrics["selectedEdges"] != 0:
        raise SystemExit(f'no-course fixture unexpectedly selected an edge: {metrics}')
    if metrics["primaryAction"] != "Choose course":
        raise SystemExit(f'no-course fixture does not identify the unresolved course: {metrics}')
if metrics["state"] in {"selected-edge", "partial-progress", "map-selected", "rail-realistic"} and metrics["selectedEdges"] != 1:
    raise SystemExit(f'expected exactly one selected edge: {metrics}')
if metrics["state"] == "map-selected":
    if not metrics["mapContextVisible"]:
        raise SystemExit(f'map selection did not expose contextual detail: {metrics}')
    if metrics["primaryAction"] != "Continue travel":
        raise SystemExit(f'map selection did not update the shared travel action: {metrics}')
if metrics["state"] == "selected-edge":
    if not metrics["focusedEdge"]:
        raise SystemExit(f'selected edge did not retain visible keyboard focus: {metrics}')
    if metrics["reviewWidth"] <= 392 and (metrics["mapTop"] is None or metrics["mapTop"] >= metrics["viewportHeight"]):
        raise SystemExit(f'narrow map does not begin in the initial viewport: {metrics}')
if metrics["state"] in {"navigation-pending", "movement-input-pending", "encounter-pending", "forced-travel-pending", "more-options-open"} and metrics["drawerCount"] != 1:
    raise SystemExit(f'focused workflow did not open exactly one drawer: {metrics}')
expected_titles = {
    "navigation-pending": "Navigation",
    "movement-input-pending": "Movement resolution",
    "encounter-pending": "Encounter",
    "forced-travel-pending": "Forced travel",
    "more-options-open": "Advanced travel controls"
}
if metrics["state"] in expected_titles and metrics["focusedTitle"] != expected_titles[metrics["state"]]:
    raise SystemExit(f'wrong focused workflow: {metrics}')
PY
done

python3 - "$out_dir" <<'PY'
import json, pathlib, sys
root = pathlib.Path(sys.argv[1])
metrics = [json.loads(path.read_text()) for path in sorted(root.glob("*.json"))]
(root / "summary.json").write_text(json.dumps(metrics, indent=2, sort_keys=True))
print(json.dumps(metrics, indent=2, sort_keys=True))
PY
