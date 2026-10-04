# Environment context and evaluation

Phase 9 introduced a generic environment context between authoritative world/session state and the Phase 8 movement-composition boundary. It gives Hex Crawl one ruleset-neutral way to represent terrain, route, weather, physical conditions, hazards, and campaign-defined environmental facts without making named systems runtime authorities.

## Authority and ownership

Environment state has two authoritative owners.

### Static world truth

`OverworldDefinition.EnvironmentAnnotations` owns static facts attached to the continuous world. An annotation has an explicit scope:

- `World` — applies throughout the overworld;
- `Hex` — applies at one axial hex coordinate;
- `SpatialFeature` — applies where the current hex intersects the referenced point/line/region feature.

Feature category is not environment truth by itself. A road-shaped or forest-category feature affects environment only when an explicit environment annotation says so. This prevents presentation/import categories from silently becoming mechanics.

### Expedition-owned current state

`StoredExpedition.Environment` owns session-local environment state:

- `CurrentFacts` — transient/current conditions such as changing weather or a temporary hazard;
- `Overrides` — explicit DM decisions that supersede lower-authority facts for the same dimension.

This state is deliberately outside `ExpeditionState`. `ExpeditionState` remains deterministic traversal/runtime state. It does not acquire an overworld repository, Rules Core provider, or environment lookup dependency.

Abstract-hex and non-spatial sessions may use current facts and overrides without an overworld.

## Fact model

`EnvironmentFact` is ruleset-neutral. Every fact has:

- a stable fact ID;
- an open-string `Dimension`;
- either a tag value or a numeric `EnvironmentMeasurement` with explicit unit;
- optional human-readable provenance;
- optional note.

Common dimensions are exposed as constants for interoperability, not as a closed enum:

- `terrain`
- `route`
- `weather`
- `visibility`
- `elevation`
- `depth`
- `water`
- `current`
- `temperature`
- `hazard`
- `regional-effect`

Campaigns and providers may introduce additional dimensions without changing the domain model.

A tag fact and a measurement fact can not be mixed inside one `EnvironmentFact`. Measurements must be finite and always carry an explicit unit. The environment layer preserves a declared measurement; it does not infer unit conversions or mechanical meaning from the dimension name.

## Effective-context resolution

`EnvironmentContextResolver` computes `EffectiveEnvironmentContext` in the application layer.

For a world-bound session it validates that the matching overworld was supplied, then gathers:

1. world-scope annotations;
2. annotations for the expedition's current hex;
3. annotations on spatial features intersecting that current hex;
4. expedition `CurrentFacts`;
5. expedition `Overrides`.

For abstract/non-spatial sessions only expedition-owned facts participate.

The resolver groups facts by exact trimmed dimension and applies these precedence levels:

| Precedence | Source |
| ---: | --- |
| 0 | world, current hex, intersecting spatial feature |
| 1 | expedition current/transient facts |
| 2 | DM overrides |

Only facts at the highest present precedence for a dimension are mechanically effective. Lower-precedence candidates remain in the context with `Effective = false` so provenance and overridden world truth remain inspectable.

Source ordering inside one precedence level is deterministic (`World`, `Hex`, `SpatialFeature`, `ExpeditionCurrent`, `DmOverride`) and fact IDs provide stable final ordering. Ordering is for stable output only; it is not a hidden tie-breaker that selects one conflicting scalar.

Moving an expedition changes which hex/feature annotations are gathered. Static facts are never copied into `environment_json`, so current world truth is recomputed from the new location.

## Compatible values and conflicts

Multiple equally authoritative tag facts for one dimension may coexist. This supports cases such as a hex being both `hills` and `forest` without forcing the environment layer to invent a combination rule.

The resolver reports an explicit `EnvironmentConflict` when equally authoritative facts can not safely coexist as raw truth:

- the same dimension mixes tag and measurement value kinds; or
- scalar measurements at the same authority disagree in value or unit.

Conflicts produce `EnvironmentContextStatus.RequiresAdjudication`. The candidates and conflict detail remain available to the caller; no first/worst/last rule is invented.

A context with facts and no conflicts is `Resolved`. A context with no applicable facts is `Unavailable`.

## Pinned-procedure interpretation

Environment truth and mechanical interpretation are separate concerns.

`EnvironmentProcedureEvaluator` consumes:

- the stored expedition;
- its resolved effective environment context;
- the movement policy projected from that expedition's exact pinned `CampaignProcedure`.

It does not inspect preset identity. It does not replace the pinned procedure with a current catalog recipe.

The evaluator currently maps generic `terrain`, `route`, and `weather` facts into a `MovementCompositionInput` for Phase 8.

### Terrain

If the pinned terrain policy recognizes exactly one applicable terrain, that terrain key can flow into movement composition.

If multiple understood terrains map to the same pinned mechanical value, a deterministic equivalent key may be selected and a diagnostic records why that is safe.

Adjudication is required when:

- the effective terrain dimension itself is conflicted;
- multiple understood terrain values map to materially different pinned values;
- multiple terrain values exist and only a subset is understood by the pinned procedure;
- an unsupported terrain policy would otherwise require choosing among multiple values.

An unknown terrain value remains valid environment truth. If the pinned procedure has no interpretation for it, the evaluator reports it in `UnsupportedSemantics` rather than deleting the fact or inventing behavior.

### Route

Route facts are preserved even when the pinned route model is `none`. If the pinned model consumes a route and there is exactly one effective route value, that key may flow into movement composition. Multiple current route values require adjudication unless the procedure defines a safe combination rule.

### Weather

Weather facts are visible and retain provenance. A weather model of `none` has no movement effect. When a pinned weather model names behavior but Hex Crawl does not have a safe local generic formula, Phase 9 emits a symbolic environment limit and requires adjudication/provider resolution. It does not invent a weather formula.

This conservative rule also applies to conditional equipment semantics and other missing capability data: environment truth does not prove that a character has equipment or capability merely because a rules text mentions it.

## Movement boundary

Phase 9 does not own a second movement engine.

The normal path is:

`EnvironmentContextResolver.Resolve`

`-> EnvironmentProcedureEvaluator.Evaluate`

`-> MovementCompositionInput`

`-> MovementCapabilityComposer.Compose`

The same path is used for the environment workbench and ordinary expedition movement projection. A D&D 3.5-style `difficult` terrain fact, for example, becomes the pinned terrain input and the existing Phase 8 composer applies the stored multiplier to the existing party capability.

Environment source descriptions and the pinned terrain/mechanical adjustment are retained in composition provenance so users can explain both where a condition came from and why it changed movement.

Non-distance movement contracts remain non-distance. Pathfinder Hexploration activities, Forbidden Lands quarter-day activity budgets, The One Ring journey progress, symbolic maximum pace, movement points, and other stored semantics are not flattened to miles per hour by the environment layer.

## Optional Rules Core/provider integration

Rules Core is optional enrichment, not environment authority.

`ITravelEnvironmentProvider` exposes typed catalog and resolution contracts with distinct provider/evaluation states. Provider integration preserves:

- mechanic key;
- provider identity;
- campaign/global scope metadata;
- declared units and per-unit time base;
- factor semantic;
- missing input keys;
- source attribution;
- resolved, input-required, not-applicable, adjudication, unsupported, unavailable, and failed states.

Hex Crawl converts accepted provider output into explicit typed movement contributors before composition. It refuses unsafe reinterpretation. Examples include:

- a provider walking/hustling quantity must have the expected hourly time base before Hex Crawl treats it as an hourly distance rate;
- a provider terrain factor is accepted only when its declared semantic is `distance-multiplier` and the pinned terrain policy can safely compose numeric multiplication;
- provider terrain resolution requires both terrain and route or neither, rather than sending a partially specified pair;
- provider failure/unavailability does not mutate authoritative environment state.

Provider responses and provider-native DTOs are not persisted into the expedition as a second source of truth.

## Persistence

Phase 9 introduced explicit environment persistence in schema version 5. The current pre-release schema is version 8 and preserves the same authority boundary:

- static annotations are serialized inside `overworlds.world_json` as part of `OverworldDefinition`;
- `expeditions.environment_json` stores `CurrentFacts` and `Overrides`.

Effective context, conflicts, pinned-procedure evaluation, provider output, and composed movement are derived and recomputed. Phase 12 journey events may retain effective environment facts as historical event/resolution snapshots, but those snapshots do not become current environment authority. Restart tests exercise both world-bound and mapless persistence paths.

Because the project remains pre-release, prior development schema versions are rejected with a reset/reinitialize instruction rather than supported through compatibility fallback columns or dual models.

## Concurrency and mutation ownership

Environment mutations follow existing aggregate optimistic concurrency:

- world/hex/feature annotation authoring uses the overworld aggregate version;
- expedition current facts and DM overrides use the expedition aggregate version.

A stale write returns a conflict rather than overwriting newer state. There is no independent environment aggregate with a competing version counter.

## HTTP and UI boundary

The `environment-context` Web module depends on both `worlds` and `expeditions` because it exposes both static world-authoring and expedition-runtime surfaces.

HTTP contracts expose typed environment data rather than raw persistence JSON. The expedition environment workbench exposes:

- current facts and DM overrides;
- effective facts and whether each candidate is effective;
- source/provenance information;
- conflicts and adjudication state;
- pinned-procedure terrain/route/weather interpretation;
- resulting Phase 8 movement composition.

The TypeScript client edits environment inputs and renders server-derived results. It does not duplicate precedence, conflict detection, pinned-procedure interpretation, or party movement composition in browser code.

Mapless sessions retain environment editing/evaluation without requiring an overworld.

## Manual and cross-phase boundaries

The environment model still does not fabricate semantics that the pinned procedure/provider does not define. The following remain manual, symbolic, provider-resolved, or adjudicated when no safe current contract exists:

- weather formulas not explicitly executable in the pinned generic contract;
- equipment/capability checks not actually supplied by party state or a provider;
- incompatible competing terrain/route facts with no combination rule;
- campaign-defined hazards whose effect formula is not encoded;
- encounter consequences beyond the existing handoff/runtime boundary.

Phase 10 owns generalized consequence/effect lifecycle. Phase 11 owns resource, survival, exposure, camping/foraging, and forced-travel execution. Phase 12 owns multi-stage journey/process and journey-event execution. Those phases consume environment truth without changing Phase 9 ownership.

## Core invariants

Phase 9 must continue to satisfy these invariants:

- named systems are removable creation-time presets;
- `CampaignProcedure` is the mechanical authority for interpretation;
- static environment truth stays with the world;
- transient state and DM overrides stay with the expedition aggregate;
- deterministic runtime does not query the world or optional providers;
- precedence and conflicts are explicit and deterministic;
- unknown facts are preserved rather than discarded;
- no unsupported mechanical formula is invented;
- provider outputs are typed inputs, not authorities;
- `MovementCapabilityComposer` remains the sole final movement composer;
- derived effective/provider/composition results are not persisted as competing truth.
