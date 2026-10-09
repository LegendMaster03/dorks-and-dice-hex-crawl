# Survival, Resources, and Forced Travel

Phase 11 makes expedition survival state operational without making Hex Crawl authoritative for character statistics or inventory and without adding named-system runtime branches.

## Authority boundaries

The Phase 11 model deliberately keeps five kinds of state separate:

```text
EnvironmentFact
    environmental truth owned by Phase 9

ExpeditionResourceState
    expedition-owned resource truth owned by Phase 11

ExpeditionSurvivalState
    forced-travel, exposure-progress, and camp/rest-resolution state owned by Phase 11

ExpeditionConsequence
    structured occurrence and stable idempotency identity owned by Phase 10

ExpeditionEffectState
    persistent ongoing effect state owned by Phase 10

MovementCapabilityComposition
    derived movement result owned by Phase 8
```

Character Sheet remains authoritative for character inventory, hit points, Constitution/endurance statistics, skills, and other persistent character state. A Phase 11 operation may consume a typed resolved result from Character Sheet, a DM, or an optional provider, but does not copy those authorities into Hex Crawl.

Phase 12 journey/process state remains a separate expedition subsystem. Journey resolutions can produce Phase 10 consequences, but journey code does not become a second resource/survival/effect owner.

## Exact-pinned focused policies

Phase 11 resolves only the exact materialized `CampaignProcedure` stored on the expedition. Runtime behavior does not consult preset identity, procedure origin metadata, or current catalog defaults.

Focused resolvers cover:

- `survival.resources`;
- `exploration.foraging`;
- `survival.camping`;
- `time.forced-travel`;
- `survival.exposure` when a campaign explicitly materializes that generic module.

A focused policy can be `None`, `Supported`, or `Unsupported`. Focused support does not change `procedure.isExecutable` and does not require `GenericProcedureRuntime.Bind` to support the entire procedure.

Unknown mechanic versions and unknown execution handlers are unsupported rather than guessed.

## Expedition resources

`ExpeditionResourceState` stores expedition-owned resources by stable resource ID, open resource key, Phase 10 target identity, inventory model, and explicit value fields.

Supported inventory models are:

- **Counted**: finite non-negative quantity plus an explicit unit. Adjust, set, and depletion are deterministic. A shortage does not silently clamp to zero.
- **Abstract**: explicit campaign-defined symbolic state. Phase 11 does not invent ordering between symbolic values.
- **SupplyDie**: explicit die state such as `d8` represented by die sides. Phase 11 can apply a resolved transition such as `d8 -> d6`, but does not infer a usage-roll algorithm, downgrade threshold, or depletion rule from a named ruleset.
- **ExternalManual**: Hex Crawl records that the resource is externally owned but does not pretend to own its quantity or state.

Resource keys remain open strings. `food`, `water`, `fuel`, `mount-feed`, or any custom key does not imply its own consumption rate.

Participant, mount, and vehicle resource targets reuse the same target/contributor identities used by Phase 10 and Phase 8. Party edits are rejected if they would leave resource, survival, or pending consequence references dangling.

Manual DM corrections are recorded in Phase 11 resource audit history. Procedure results such as consumption and foraging use the Phase 10 consequence path instead.

## Resource consequences and idempotency

Phase 10 `ResourceChangeConsequenceComponent` is generalized to an explicit operation model:

```text
AdjustQuantity
SetQuantity
SetState
SetSupplyDie
Deplete
```

The component contains only fields appropriate to the selected operation.

Phase 10 still accepts and records the consequence first. Resource work that Phase 10 can not perform remains in `PendingExpeditionConsequence` with the same stable consequence ID.

The Phase 11 resource consumer then, under one expedition optimistic-concurrency save:

1. locates the Phase 10 applied/pending occurrence;
2. validates the complete resource mutation against authoritative resource state;
3. refuses missing, ambiguous, mismatched, external/manual, unsupported, or insufficient state without changing inventory;
4. applies all valid resource components;
5. records before/after Phase 11 resource audit data;
6. updates or clears the corresponding Phase 10 pending record.

A consumed consequence ID has no pending resource work on retry and therefore can not mutate inventory twice. Phase 11 does not submit the same occurrence back through `ExpeditionConsequenceEngine.Process` after it is already accepted.

Phase 12 reuses this same consequence identity boundary for journey/process and journey-event outputs, so retries can not create a second inventory mutation path.

## Resource consumption

The structural names in current proof procedures are not formulas. For example, `fixed-per-person`, `daily-supplies`, and `usage-roll` do not state exact quantities, rolls, or transitions.

Phase 11 therefore distinguishes consumption that is not due, consumption that is due with explicitly resolved changes, input required, shortage/adjudication, unsupported inventory behavior, and external/manual ownership.

When the exact pinned procedure does not contain a complete rate, the DM or optional provider supplies typed `ResourceChange` operations. Phase 11 does not reconstruct publisher-specific rates from preset identity or memory.

## Foraging

The exact pinned `exploration.foraging` policy exposes its resolution model, time cost/unit, movement tradeoff, and whether it is activity-backed.

Activity-backed foraging consumes existing typed `participant.activity-state` assignments. It does not reintroduce free-form activity names and does not translate activity capacity into miles or hours.

A resolved forage result supplies explicit resource gains. The path is:

```text
resolved forage result
    -> ExpeditionConsequence(ResourceChange)
    -> Phase 11 resource consumer
    -> ExpeditionResourceState
```

Phase 11 does not invent skill formulas, DCs, dice formulas, or yield tables.

## Forced travel

`ForcedTravelState` persists procedure-relevant usage since an explicit reset. It is not derived from expedition age or wall-clock time.

The exact pinned policy supplies the normal limit and limit unit. Supported open units include hours, intervals, watches, quarter-days, and other campaign units when the exact stored procedure establishes their relationship to authoritative travel progress.

Authoritative spatial and nonspatial travel mutations account successful elapsed travel once. Hours are directly measurable. Interval accounting uses the exact pinned `time.interval` duration. A non-time unit such as `quarter-days` is derived automatically only when the same pinned procedure explicitly establishes the relationship through its interval and movement-budget unit. Simple singular/plural spelling variants of the same open unit, such as `watch` and `watches`, are treated as the same declared unit; unrelated open units are never guessed. Otherwise the typed forced-travel operation requires a resolved amount.

The normal threshold is distinct from forced travel beyond the threshold. Continuing past the normal limit creates a stable pending check occurrence. Check-model strings such as `escalating-check`, `escalating-constitution-save`, or `endurance-check` do not encode formulas, abilities, DCs, or escalation values, so those results remain typed DM/provider inputs unless a future generic procedure explicitly adds the missing parameters.

A failed resolved check produces a stable Phase 10 consequence. Persistent fatigue/exhaustion changes go through Phase 10. Character-owned nonlethal damage or similar state remains an external-state consequence.

Forced-travel reset is explicit. Camping, an activity name, watch completion, or time passage does not silently reset the counter.

## Environment and survival exposure

Phase 9 remains the environment-truth authority. Temperature, elevation, weather, hazard, water, current, and other environment facts have no built-in fatigue or damage meaning.

Phase 11 adds the generic `survival.exposure` / `survival-exposure-policy` abstraction for campaign procedures that explicitly need it. The contract identifies relevant environment dimensions, evaluation model/cadence, target scope, and consequence model. It is intentionally small and behavior-oriented rather than a scripting language.

A resolved exposure operation may update `ExpeditionExposureProgress`, produce a structured Phase 10 consequence, or both. Exposure progress is neither an `EnvironmentFact` nor an `ExpeditionEffect`.

```text
EnvironmentFact
    -> exact exposure policy + resolved input
    -> exposure progress and/or ExpeditionConsequence
    -> Phase 10 persistent effect handling
```

A cold temperature, high elevation, weather tag, or hazard tag alone never creates fatigue, exhaustion, or damage.

## Camping and recovery

The exact `survival.camping` policy may be interval-backed or activity-backed. Activity-backed camping uses typed participant assignments and does not fabricate a travel interval.

`ExpeditionCampState` records the explicit camp resolution and, when supplied, explicit rest qualification. Establishing camp is not equivalent to safe rest. A `sleep` or `rest` activity key has no recovery semantics by itself.

If the resolved camp result contains an explicit rest trigger, Phase 11 can bridge that trigger into Phase 10 recovery. The caller still supplies any unresolved clear/reduction magnitude. Phase 10 remains the only authority that mutates persistent effect state.

## Providers and manual resolution

Providers are optional. A provider may resolve a check, resource amount, foraging yield, or exposure outcome, but returns typed resolved data to the application. Provider identity is provenance only.

Providers do not mutate resources, effects, or survival state directly and are not deterministic runtime dependencies. Every Phase 11 workflow remains usable through explicit DM resolution when no provider is configured.

## Persistence and concurrency

Phase 11 introduced authoritative `resources_json` and `survival_json` in schema version 7 beside existing `effects_json` and `environment_json`. The current pre-release schema is version 8, which retains those Phase 11 boundaries and adds Phase 12 `journey_state_json` as a separate authority.

All Phase 11 changes use the existing expedition version for optimistic concurrency. Resource mutation and the matching Phase 10 consequence-state update are saved as one expedition aggregate operation. Journey-generated Phase 10/11 mutations are likewise committed with journey state in the same expedition optimistic-concurrency save.

Before Phase 15 acceptance, older development databases may still be reset rather than supported by duplicate legacy representations. After Phase 15 acceptance opens internal human testing, straightforward architecture-preserving migrations are preferred where practical before breaking tester data, without retaining obsolete parallel state models.

## UI and API

The server exposes typed operations for resource administration, pending resource consequence consumption, resource consumption, foraging, forced-travel accounting/check/reset, exposure resolution, camp resolution, and explicit rest recovery.

Phase 15 keeps those typed operations authoritative while presenting their state through the unified expedition workspace. Resources/effects and forced-travel status are reachable from compact current-state summaries; the detailed **Survival and resources** panel remains the focused workspace for explicit resource administration, pending operations, resolved checks, environment context, foraging/exposure results, and camp/rest qualification.

Persistent effects remain Phase 10 state rather than a duplicate Phase 11 fatigue authority.

Journey-generated Phase 10 consequences continue through these same Phase 11 resource/survival boundaries rather than implementing duplicate resource logic in the browser or journey service.

## Cross-phase boundaries

Phase 12 owns multi-stage journey execution, journey-event opportunities, journey completion/failure/abandonment, and journey-generated consequence handoff. Phase 11 remains the resource/survival owner for any resulting resource or survival mutation.

Phase 14 projects encounter-relevant linked resource/effect context through the server-authoritative v2 handoff while Block Initiative remains authoritative for tactical combat. Phase 15 changes presentation around those boundaries but does not move or duplicate their authority.

Phase 15 established the internal-human-testing gate and was followed by Guided refinements. The separate Phase 15.5 stabilization proposal has been superseded by the Tile Crawl Phases 16–21 transition. A future Battle Map tool and its cross-tool ownership/integration remain outside that roadmap.
