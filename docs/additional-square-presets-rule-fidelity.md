# Additional square-grid presets: rules evidence and execution scope

These are **catalog recipes over shared generic geometry and procedures**, not separate game-specific engines. The existing Fallout: Wasteland Wanderer (5×5) and Wasteland Warfare 2e (9×9) recipes, finite-map import behavior, and database schema are unchanged.

## Implemented catalog additions

| Preset | Geometry / movement | Time interval | Exploration flow | Bounded? |
| --- | --- | --- | --- | --- |
| Mutant: Year Zero | Square; edges and vertices (diagonals) | Four-hour baseline | Explicit first-entry `explore`, repeat-entry `cross`; GM resolves steps | No |
| The Walking Dead Universe RPG | Square; edges only (no diagonals) | One day on foot | Enter sector, optional Scout, resolve sector | No |
| Fallout (1997) | Square; calibrated continuous travel | One-hour accounting interval | Travel, encounter decision, map reveal | No |
| Fallout 2 (1998) | Square; calibrated continuous travel | One-hour accounting interval | Travel, encounter decision, map reveal | No |

Unbounded means there is **no arbitrary maximum map size**; the author can opt into finite bounds using the existing generic map editor. A continuous-distance session still requires world-distance calibration.

## Rules checks

- **Mutant: Year Zero**: diagonal sector travel is permitted. Four hours is typical to explore a new sector; once explored, it typically takes one hour to traverse again. Darkness doubles exploration time, vehicles shorten it, and Stalker outcomes and terrain can further adjust time. Scouting, Rot, supplies, ruins, encounters, and artifacts require the published rules or GM adjudication. Source: [Core rulebook Zone chapter overview](https://www.yumpu.com/en/document/view/62743784/mutant-year-zero-core-rules/124) and [starter booklet](https://churapereviews.com/wp-content/uploads/2024/04/MYZ_starter_booklet-1.pdf).
- **The Walking Dead Universe RPG**: each square sector represents 10×10 km. A party walking on foot covers one sector per day; diagonal crossings are prohibited. A PC entering a new sector may Scout for hazards and obstructions. Vehicle movement changes the rate. Source: [Core rules, travel excerpt](https://anyflip.com/buima/pxww/basic/51-100).
- **Fallout (1997)**: original world-map sectors are squares and travel is continuous. Listed nominal crossing times are 6 hours through city, 10 hours through desert/coast, and 20 hours through mountains. These values are documented but not configured as automated movement rates.
- **Fallout 2 (1998)**: nominal crossings are 25 hours through desert/city/coast and 32.5 hours through mountains. The Highwayman, random encounter checks, Outdoorsman avoidance, and world-map reveal are distinct mechanics. Sources: [World-map reference](https://fallout.fandom.com/wiki/World_map) and [worldmap.txt format](https://falloutmods.fandom.com/wiki/Worldmap.txt_File_Format).

## Important automation limits

- The generic fixed watch length **does not change by explored/unexplored state**, terrain, nighttime, vehicles, or Stalker results. MYZ's four-hour baseline must not be mistaken for a fixed time cost of all crossings. The user must adjudicate time adjustments until a generic conditional-time mechanic is added.
- The Walking Dead Scout stage identifies a manual optional decision; it does not automatically roll Scout. The one-day watch represents the **on-foot** crossing only, not vehicle speeds.
- The classic Fallout crossing rates are **documented references**, not live movement-speed arithmetic. Neither a `movement.budget` nor a dependent `movement.terrain` module is selected, since a terrain policy without its required budget producer is invalid. World-distance calibration and GM movement inputs remain required. Original-game sector travel durations differ between Fallout and Fallout 2, so they use distinct recipes.
- Exploring a sector in a procedure and revealing it to players are separate. DM notes and official copyrighted random tables are never exposed or fabricated automatically. No official tables are bundled.
- These new presets use existing generic movement/contact, flow, ledger, authoring and serializer mechanisms. There is no migration or database reset.

## Regression criteria

1. All catalog entries materialize, validate and survive JSON serialization while retaining square geometry, expected adjacency and stable identities.
2. MYZ permits vertex-only neighbors while Walking Dead denies them; this follows the already-tested generic `CellAdjacencyMode` execution path.
3. Both Fallout classic presets require calibrated continuous-distance travel; their distinct nominal terrain crossing times remain documented and manually adjudicated until a valid generic movement budget and terrain policy are configured.
4. Existing 5×5 and 9×9 Fallout preset bounds, imports, and exploration flows remain unchanged. Existing hex presets remain hex-only.
5. Verify start, travel, restart, and authoring behavior in CI or live browser before merging; successful catalog-level tests alone do not establish full published-rules fidelity.
