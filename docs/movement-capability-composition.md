# Movement capability composition

Phase 8 is the generic movement-capability composition layer for Hex Crawl. It derives current effective movement from the exact pinned campaign procedure plus expedition-owned or externally resolved capability inputs. It does not add a second movement runtime, a named-system dispatcher, or a provider dependency to the deterministic domain runtime.

## Authority

Movement composition has one policy authority:

`StoredExpedition.CampaignProcedure`

`MovementCompositionPolicyResolver` reads the materialized `movement.budget` and `movement.terrain` modules from that exact snapshot. It does not consult origin preset metadata, current preset recipes, or the current global mechanic catalog to replace persisted behavior.

Unsupported handler/version pairs or future semantic values remain preserved and unsupported. The composer reports unresolved/unsupported state instead of silently substituting current defaults.

## Contributors

`MovementCapabilityContributor` is the generic current-state/consequence model. It can represent:

- participant movement capability;
- mounts and vehicles;
- load/encumbrance consequences;
- travel mode or pace consequences;
- resolved terrain/route consequences;
- resolved environment consequences;
- resolved persistent-effect consequences;
- an explicit DM final override.

A contributor carries a stable ID, generic key, kind, operation, scope, numeric or symbolic value, unit/per-unit metadata, optional physical `DistanceUnit`, optional participant/movement-unit references, optional rider/passenger replacement assignments, provenance, and note.

Hex Crawl does not copy Character Sheet inventory, stat blocks, or provider DTOs into this model. `ExternalCharacterId` remains an optional lookup key on the party member. Manual contributor entry and `PartyMovementReference` remain valid without Character Sheet or Rules Core.

## Composition order

Current composition is deterministic:

1. choose the applicable participant/mount/vehicle or explicit party base capability;
2. apply load/encumbrance consequences;
3. apply the selected generic travel mode/pace consequence;
4. apply pinned terrain semantics and resolved terrain/route consequences;
5. apply resolved environment consequences;
6. apply resolved persistent-effect consequences;
7. apply an explicit DM override last.

`CountsTowardPartyMovement` controls whether a participant can limit the party. An assigned mount/vehicle replaces the walking movement unit of its listed riders/passengers so the same participant is not double-counted. An unassigned conveyance does not limit the party.

Compatible physical values are converted only when explicit `DistanceUnit` conversion metadata is available. Incompatible custom units remain unresolved.

## PartyMovementReference

`PartyMovementReference` remains supported and is not treated as obsolete compatibility state.

Its role is explicit in each composition result:

- `AuthoritativeBase` when it is the expedition's explicit movement authority and no stronger composition policy/capability is available;
- `Fallback` when automatic composition is incomplete but the stored reference can keep the expedition operable;
- `InformationalOnly` when typed composition already resolves movement;
- `None` when it is absent or not applicable.

The reference is never silently added to an already-composed movement value.

## Terrain and non-distance semantics

Phase 8 does not flatten all movement budgets into miles per hour.

Numeric `multiplier` and `distance-per-hour-multiplier` terrain relationships can alter a compatible numeric base. Other models retain their own semantics:

- `activity-cost`;
- `movement-points-per-distance`;
- `hexes-per-quarter-day`;
- `terrain-difficulty`;
- `maximum-pace`.

The D&D 2024 proof value `arctic=fast-if-appropriately-equipped` remains a symbolic conditional maximum-pace rule. The composer reports that adjudication/input is required rather than treating Arctic terrain as unconditional fast pace.

Pathfinder Hexploration activity budgets, Forbidden Lands quarter-day activity budgets, AD&D-style movement points, and The One Ring journey progress likewise remain non-distance when their stored contracts say they are non-distance. The One Ring proof does not fabricate a repeating watch or physical distance.

## Optional providers

Optional providers supply missing resolved capability inputs; they do not own movement composition.

For provider-backed physical travel, `ProcedureResolutionProviderEnricher` follows this order:

1. explicit resolved expected distance, when supplied;
2. locally resolved/manual movement composition;
3. optional provider resolution for a missing capability;
4. explicit `PartyMovementReference` fallback when provider resolution is unavailable and the reference is usable.

A provider physical rate is converted into a typed resolved movement contributor and sent through `MovementCapabilityComposer`. A provider terrain factor is accepted only when its factor semantic is `distance-multiplier` and the pinned procedure terrain model is compatible with numeric multiplication.

Provider availability and unresolved states remain distinct: unavailable, unsupported, input-required, not-applicable, requires-adjudication/conflict, failed, and resolved. Provider identity/source attribution is retained as provenance. The runtime assembly has no provider dependency.

## Watch-distance suggestion

A composition result may include `SuggestedExpectedDistance` when its effective quantity is physical and the exact stored interval semantics make a deterministic suggestion possible.

- Per-hour capability is scaled by the pinned interval for a new watch.
- A resumed/partial watch uses active-watch remaining time.
- A per-watch reference is used directly for a new/full watch.
- Non-distance budgets do not produce a physical suggestion.
- An unresolved/unsupported composition does not invent a value.

The client consumes this server-derived suggestion. Browser code may perform presentational physical-unit conversion to the session display unit, but it does not recompute party limiting, interval duration, terrain semantics, provider precedence, or fallback rules.

Manual expected-distance entry remains available when no suggestion can be produced.

## Persistence and history

Only stable expedition-owned movement state is persisted with `CrawlPartySheet`: explicit/manual contributors and `PartyMovementReference`. Provider caches or provider-native records are not persisted as a second authority.

Party mutation remains optimistic-concurrency protected. PostgreSQL/restart behavior preserves movement contributors and references. Generated or consumed procedure-resolution records retain their resolved movement values and provenance even when later party edits change current movement state; normal aggregate-version rules still prevent stale automatic results from being applied as if they were current.

## UI projection

`MovementCapabilityCompositionContract` is part of expedition detail. The UI presents server-derived composition using ordinary DM-facing fields rather than raw JSON, including:

- effective movement and composition status;
- limiting participant/unit;
- contributor breakdown;
- reference/fallback role;
- provenance;
- missing inputs and adjudication/unsupported diagnostics;
- suggested physical watch distance when available.

## Deferred boundaries

Phase 8 deliberately does not implement:

- Phase 9 generalized environment context/evaluation;
- Phase 10 generalized effect/consequence lifecycle;
- Phase 11 resource, survival, exposure, or forced-travel execution;
- Phase 12 multi-stage journey execution.

Environment and persistent-effect contributors in Phase 8 therefore represent already-resolved movement consequences only. Their upstream lifecycle remains owned by later phases.
