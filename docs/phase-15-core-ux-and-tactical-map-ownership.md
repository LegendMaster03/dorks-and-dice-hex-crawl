# Phase 15 core UX and tactical-map ownership

## Status

Phase 15 preserves the campaign-owned `CampaignProcedure` and the Phase 0–14 authority boundaries while making the resulting product easier to operate at the table. It also closes the tactical/battle-map ownership question that earlier roadmap text deliberately deferred until Phase 15.

The decision in this document supersedes earlier statements that battle-map ownership is "deferred to Phase 15", "intentionally undecided", or otherwise unresolved pending Phase 15 evaluation.

## Product decision

Hex Crawl does **not** become the tactical battle-map authority.

The product keeps two map scopes that may coexist without sharing authority:

- **Hex Crawl owns exploration and expedition mapping**: the overworld, mathematical exploration grid, authored locations and features, expedition position, environment context, discoveries/player knowledge, and encounter context.
- **Tactical scene/battle maps remain external provider-owned linked resources**. Hex Crawl may retain provider-neutral linked-scene references, but it does not own tactical map editing or tactical map state.
- **Block Initiative remains authoritative for tactical combat** after the encounter handoff.
- **The server-authoritative encounter handoff is the integration seam**. It carries the expedition context needed by combat, including linked-scene references, without moving combat or tactical-map authority into Hex Crawl.
- **Surveyor remains computation-only shared infrastructure**. Reusing image/grid processing for Hex Crawl, a future Battle Map tool, or another Dorks & Dice tool does not transfer domain ownership to Surveyor.

A future dedicated Battle Map tool can own tactical map authoring, storage, and rendering while Hex Crawl continues to store only provider-neutral links and encounter context. Such a tool does not require a second tactical-map model inside Hex Crawl.

## Rationale

The exploration map and a tactical battle map solve different product problems.

Hex Crawl needs a durable continuous-world and expedition surface: where the party is, what locations and features exist, what has been discovered, what the environment is, and which encounter context applies. Tactical combat instead needs encounter-scale positioning and combat interaction. Combining both authorities in Hex Crawl would couple expedition persistence to combat presentation and would create competing ownership with Block Initiative or a future map provider.

Phase 14 already established the appropriate boundary through the v2 encounter handoff and provider-neutral linked scenes. Phase 15 therefore retains that boundary instead of adding tactical-map persistence or a tactical editor to Hex Crawl.

The Phase 13 Surveyor extraction reinforces this separation. Shared image-processing capability is reusable infrastructure, not evidence that one consuming product should own every map type.

## Procedure authoring UX

Phase 15 exposes three editing depths over one authoritative `CampaignProcedure`:

1. **Compact** presents ordinary tabletop concepts and procedure areas without requiring mechanic IDs or dependency vocabulary.
2. **Advanced** exposes exact generic mechanics, parameters, input/output contracts, dependencies, alternatives, and execution support.
3. **JSON** exposes the canonical `CampaignProcedure` representation for expert editing.

These are presentation/editing modes, not separate procedure models. Presets remain creation-time recipes only. Saving a procedure still creates an ordinary campaign-owned revision.

The JSON path is server-authoritative:

- JSON is parsed by the server using the same enum/string conventions used by persistence;
- the reconstructed `CampaignProcedure` must pass domain validation;
- procedure identity can not be changed while creating a revision;
- optimistic concurrency rejects stale revisions;
- semantically unchanged canonical JSON is rejected as a no-op rather than creating revision noise;
- origin preset metadata remains separate provenance and is preserved across revisions without becoming runtime authority.

## Expedition workspace UX

Phase 15 adds a procedure-aware operating surface above the existing authoritative controllers rather than replacing them.

The workspace emphasizes:

- the **current action** derived from authoritative expedition pause/runtime state and journey state;
- compact current-state summaries for time, travel/navigation/encounter state when spatial behavior applies, and procedure-specific state when it does not;
- contextual access to party, current map/hex context, history, resources/effects, and journeys;
- existing encounter handoff behavior when combat interrupts expedition execution;
- existing map keyboard/pointer interaction and world truth rather than a second map runtime.

The existing mutation paths remain authoritative. The Phase 15 workspace does not reconstruct travel rules, journey transitions, resource mutations, environment precedence, or encounter handoff state in presentation code.

Application-owned DOM remains driven by explicit state transitions. Phase 15 does not introduce `MutationObserver` synchronization.

## Nonspatial and no-interval procedures

Phase 15 preserves the no-fabrication guarantees established by the generic procedure architecture.

A nonspatial procedure with a real focused interval may expose watch/time bookkeeping. A journey-oriented procedure without a repeating interval remains journey-first: the workspace directs the DM to the active journey process and does not invent a map, travel watch, or interval merely to fit the spatial UI.

This is especially important for the One Ring proof graph and other structural or activity/journey-driven procedures.

## Persistence and migration impact

Phase 15 introduces no tactical-map persistence into Hex Crawl.

- Existing provider-neutral linked-scene references remain the durable tactical-map seam.
- `CampaignProcedure` remains the only authoritative persisted procedure representation.
- Canonical JSON editing creates normal campaign procedure revisions; it does not create a parallel JSON authority.
- Expedition/runtime, environment, effects, resources/survival, journey, party, knowledge, and map/world state remain in their existing aggregate boundaries.
- No database schema change is required solely by the tactical-map ownership decision or the Phase 15 presentation restructuring.

Because internal human testing has begun, later breaking persistence changes should continue to evaluate straightforward migrations before resets, without preserving obsolete parallel representations.

## Acceptance expectations

Phase 15 validation must continue to cover:

- Compact, Advanced, and JSON authoring over the same `CampaignProcedure` authority;
- canonical JSON parsing, domain validation, immutable revision history, no-op rejection, identity protection, and stale-write conflicts;
- procedure-origin independence from runtime behavior;
- spatial expedition current-action behavior without bypassing existing watch/navigation/encounter controllers;
- nonspatial/no-interval journey behavior without fabricated map or interval state;
- resources/effects and journey context remaining reachable from the primary workspace;
- the Phase 14 server-authoritative encounter handoff and Block Initiative combat ownership;
- keyboard/focus behavior for focused workspace surfaces;
- PostgreSQL and mapless restart persistence;
- the existing prohibition on application-owned `MutationObserver` synchronization.

## Authority summary

```text
Hex Crawl
  owns overworld/exploration map + expedition state + encounter context
        |
        | server-authoritative encounter handoff
        v
Block Initiative
  owns tactical combat
        |
        +-- may use linked tactical/scene map provider

Tactical map provider / future Battle Map tool
  owns tactical map state and authoring

Surveyor
  owns shared image/grid computation only
```

This separation is the Phase 15 product decision unless a later explicit architecture change revisits it.