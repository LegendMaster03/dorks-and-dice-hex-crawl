# Hex Crawl design references

## Purpose

Hex Crawl automates established tabletop wilderness procedures rather than inventing a replacement exploration game. No single D&D edition is authoritative for the product. The implementation should preserve useful concepts across editions, keep edition-specific mechanics in Rules Core where appropriate, and use source-defined values instead of hard-coded campaign assumptions.

The current reference hierarchy is:

1. **B/X + AD&D** — core wilderness exploration procedure, time/movement cadence, DM-facing bookkeeping, and information-dense running-sheet conventions.
2. **3e + 3.5e (3.Xe)** — detailed movement, terrain, environment, skills, modifiers, and simulation-oriented mechanics.
3. **5e + 5.5e (5.Xe)** — modern terminology and character-facing travel responsibilities, pace consequences, and terrain concepts.
4. **The Alexandrian** — procedural organization, watch-based usability, running-sheet layout, and practical synthesis where official rules leave gaps.
5. **4e** — complex journey resolution, structured hazards, consequential failure, progressive expedition state, and encounter handoff.

This order is not a quality ranking. It describes what each source family primarily informs.

## Reviewed mechanics currently executable

The materialized `alexandrian-advanced` preset is the current concrete source-backed executable baseline. Its reviewed mechanics remain regression-tested:

- a watch is 4 hours;
- travel uses continuous distance with separately resolved expected and actual distance;
- the optional actual-distance helper rolls `2d6+3` and applies each roll point as 10% of expected distance;
- encounter cadence is once per watch;
- the automatic encounter helper uses `1d8`, with 1 as the wandering-encounter result and 8 as the keyed-location result;
- encounter timing divides the watch into eight equal slots;
- navigation uses a `1d20` helper while DC, situational modifier, and failure veer remain explicit inputs;
- persistent veer, deliberate double-back, intra-hex progress, and direction-change costs remain active;
- near/far progress and turn costs are stored as scale-independent factors rather than hard-coded miles.

Additional source mechanics such as creature-speed movement tables, pace-specific consequences, terrain/weather modifiers, foraging actions, mounts/vehicles, and encounter-table content are not implicitly invented by Hex Crawl. They remain resolved inputs or later generic/provider work until implemented.

## Core-procedure rule

The existing deterministic crawl loop remains:

`Travel -> Watch -> Navigation -> Encounter`

4e-style skill challenges do not replace this loop.

The deterministic runtime remains responsible for ordinary executable watch advancement, navigation/lost state, spatial progress, encounter cadence, and pauses. More complicated journey-scale problems should be an optional layer that can span ordinary watches.

## 4e-derived future layer: Journey Challenge / Complex Hazard

A future generic multi-stage expedition process should represent obstacles such as difficult crossings, severe weather, haunted regions, collapsing routes, or other problems requiring several checks/stages.

It should be able to:

- span multiple watches or travel stages;
- track progress toward a goal;
- reference skills/competencies/tools through stable external identifiers rather than hard-coded names;
- accept multiple valid approaches;
- record successes, failures, complications, and consequences;
- allow failure to alter circumstances rather than merely stop progress;
- retain generated/manual/external/DM-override provenance.

Likely consequences include resource loss, delay, exposure, altered route, worsened encounter position, surprise state, reinforcements, or encounter-composition changes.

### Ownership boundary

A Journey Challenge is persistent expedition state adjacent to the ordinary crawl runtime. It is not a replacement procedure representation, not a special `EncounterOutcomeKind`, and not prose hidden only in an event message.

The expected ownership split is:

- **Rules Core** owns canonical skill/competency/tool/condition definitions and edition-specific mechanics when used.
- **Hex Crawl** owns active journey-process state, its progress, when stages occur, and expedition consequences.
- **Block Initiative** owns tactical combat after handoff.
- **Character Sheet / other tools** may supply capabilities but do not own expedition state.

## Travel capability

Travel capability should ultimately compose actual participants, creatures, mounts, vehicles, carried constraints, terrain, and procedure/source data.

Current and future code should avoid hard-coded assumptions about miles versus kilometers, fixed physical hex scale, terrain rates, or campaign-specific movement values.

The existing party movement reference remains a valid DM-owned authoritative reference and fallback. Later automation may derive or propose values without changing stored distance semantics.

## Marching order and travel roles

Hex Crawl already persists flexible marching order and watch state. This is the correct base for later typed travel roles.

Future roles may include front, middle, rear, scout, navigator, mapper, forager, or source-defined equivalents without introducing tactical-grid ownership.

Current active-watch activity keys remain intentionally open. Typed participant activity execution belongs to a later phase.

## Progressive expedition conditions

Environmental hazards may need state that improves, remains stable, or worsens across checks. Examples include exposure, dehydration, altitude effects, disease, or supernatural corruption.

Canonical definitions and edition-specific mechanics can come from Rules Core when available. Hex Crawl owns expedition timing and the structured consequences applied to expedition state.

Do not overload `RuntimePauseReason` for persistent effects. Pause reasons explain why a current transition can not continue; effects describe ongoing state.

## Encounter handoff

Travel and hazard outcomes should influence later encounters through structured context rather than prose-only notes.

Potential handoff fields include surprise, advantageous/disadvantaged circumstances, delayed arrival, reinforcements, altered composition, depleted resources, route/location changes, source process identifiers, and provenance.

Hex Crawl should not acquire tactical initiative, combat-round, or tactical-grid ownership.

## Present architecture assessment

The current architecture already supports this direction:

- `CampaignProcedure` is the single materialized procedure representation;
- `CrawlRuntimeEngine` is deterministic and accepts resolved inputs;
- `StoredExpedition` is an aggregate envelope with adjacent party and generated-resolution state;
- runtime events are retained as structured JSON records;
- independent provenance distinguishes procedure defaults, automatic rolls, manual rolls, external systems, and DM overrides;
- party marching/watch state does not assume fixed tactical dimensions;
- distances carry explicit units;
- semantic world categories do not automatically imply procedure mechanics;
- focused assistants and the full workbench share the same persisted session.

Areas that require later design rather than current Phase 3 execution work are:

1. persistent challenge/hazard state;
2. typed Rules Core mechanic references;
3. generalized structured consequences;
4. richer encounter handoff context;
5. progressive effects;
6. derived movement integration.

None of these require changing the current deterministic loop in Phase 3.

## Preset/procedure separation

Published, community, and otherwise named procedures are creation-time preset metadata. The preset catalog owns name/key/revision and a generic recipe. Applying a preset materializes a campaign-owned `CampaignProcedure`.

The persisted expedition stores that snapshot independently from optional `ProcedureOriginMetadata`. Origin metadata is informational and removable. A missing, renamed, or revised catalog preset does not reconstruct or reinterpret an existing expedition.

This is the architecture used by current Phase 3 proof presets and later Procedure Composer work.
