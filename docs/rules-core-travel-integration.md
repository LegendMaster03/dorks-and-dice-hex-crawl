# Rules Core travel and environment integration

Hex Crawl consumes structured travel and environment mechanics from Rules Core while retaining ownership of expedition procedure and state.

## Ownership boundary

Rules Core owns source-backed mechanic definitions, provenance, required inputs, units, factor semantics, source-specific formulas, effective global/campaign rules, conflicts, and adjudication state.

Hex Crawl owns expedition state, routes, watches, time progression, current environment, map state, party travel state, navigation state, random direction, encounter cadence/outcomes, DM choices, generated-roll audit events, and application of resolved mechanics to the active expedition.

Character Sheet remains authoritative for character state. Block Initiative remains authoritative for combat state.

The dependency therefore remains:

`Hex Crawl state/context -> explicit mechanic inputs -> Rules Core resolution -> typed result -> Hex Crawl procedure/state`

Rules Core does not advance watches, set lost state, choose random directions, or apply encounter state.

## Transport and scope

Hex Crawl uses the same Tool Host delegation pattern as other Dorks & Dice consumers. The browser calls Hex Crawl; the Hex Crawl server delegates to Rules Core through the Site using the short-lived delegation capability issued during Tool Host authentication. Hex Crawl does not create a second unauthenticated Rules Core channel.

An expedition has an optional `CampaignId` rules scope:

- `CampaignId != null`: use campaign-effective Rules Core endpoints.
- `CampaignId == null`: use global effective Rules Core endpoints.

Campaign scope is explicit. Hex Crawl does not infer an expedition campaign from the authenticated user's campaign memberships. Assigning a campaign is validated against the authenticated Tool Host campaign context.

The optional campaign ID is stored inside the existing expedition `context_json` snapshot. Existing snapshots that do not contain the field deserialize as `null`, so no database schema migration is required and existing expeditions continue to use global rules until explicitly associated with a campaign.

## Current automatic-helper integration

The existing procedure-resolution helper remains the integration point for mechanics that already map cleanly to Hex Crawl state.

### Travel distance

The helper can consume:

- `travel.overland.walk-distance`
- `travel.overland.hustle-distance`
- `travel.overland.terrain-distance-factor`

Walking and hustle resolution require an explicit base speed. Hex Crawl requests an hourly source-backed quantity and applies it to the current watch segment duration. Returned units are retained through the Rules Core contract and converted only through Hex Crawl's existing explicit distance-unit conversion model.

Terrain and route are optional and must be supplied together. Hex Crawl applies the terrain factor only when the effective Rules Core definition explicitly reports `distance-multiplier` semantics. A movement-cost factor is not reinterpreted as expedition distance.

### Navigation

The helper can consume:

- `travel.navigation.avoid-getting-lost`

The UI supplies explicit applicable risk factors. Rules Core resolves the source-backed DC and returns cadence/competency metadata. Hex Crawl continues to own whether a navigation check is due and what failure does to expedition navigation state.

### DM override precedence

Existing DM-authored values remain valid.

For the automatic helper:

1. An explicit expected distance wins over source-backed walk/hustle/terrain calculation.
2. An explicit navigation DC wins over source-backed navigation-risk resolution.
3. Source-backed mechanics are consulted only when the corresponding explicit value is absent.

This preserves existing campaign/world configuration and party movement references instead of silently replacing them with Rules Core results.

The workbench shows source-backed choices in the existing automatic-resolution context. It does not expose a separate Rules Core configuration page. Conflicted, adjudication-required, or unavailable mechanics are disabled or reported as unresolved; explicit DM inputs remain available.

## Conflict and failure behavior

Hex Crawl does not select a source or edition when Rules Core reports a conflict.

The integration distinguishes:

- resolved;
- input required;
- not applicable;
- requires adjudication;
- conflicted;
- service/transport failure.

`requires-adjudication` and `conflicted` return a DM-facing error that directs the DM to adjudicate the effective rule or use an explicit override. Missing inputs and not-applicable results are also surfaced rather than silently falling back to another definition.

A Rules Core transport failure does not invalidate explicit DM-authored values. If the current helper request actually needs Rules Core, the operation fails visibly with service-unavailable behavior instead of fabricating a result.

## AutomaticRoll integrity

Rules Core resolution does not weaken the existing server-verifiable AutomaticRoll design.

Source-backed inputs are resolved server-side before the procedure helper rolls. The server constructs provenance containing the mechanic key, effective scope, source identifiers when available, resolved quantity/check/factor metadata, and factor semantics where relevant. That provenance becomes part of the generated helper result that is immediately persisted and audited.

The client does not supply trusted Rules Core resolution metadata. Existing generated-resolution IDs, version checks, supersession, audit history, exact value verification, and consumption checks remain authoritative when an `AutomaticRoll` result is applied to an expedition.

## Mechanic keys exposed but not automatically applied

The generic expedition-scoped catalog/resolution endpoints can expose the full Rules Core travel/environment contract. The following mechanics are intentionally not wired into automatic expedition mutation yet because Hex Crawl does not currently have enough explicit state to apply them safely:

- `travel.overland.standard-travel-duration`: the existing Travel -> Watch -> Navigation -> Encounter procedure is not redesigned around a source-specific daily duration.
- `travel.overland.forced-march-check`: Rules Core can resolve the DC, but Hex Crawl does not yet model the generalized failed-check damage/fatigue consequence pipeline required to apply the result correctly.
- `travel.overland.mount-vehicle-distance`: mount/vehicle mode, rider/load, crew/passenger, and related travel context are not yet represented coherently enough for automatic use.
- `travel.environment.hampered-movement`: source definitions use materially different semantics. Automatic application waits for explicit environment applicability/context rather than normalizing them.
- `travel.environment.difficult-terrain-movement-cost`: this is a movement-space cost rule and is not treated as an expedition-distance multiplier.
- `travel.environment.high-altitude-travel-time-cost`: automatic use waits for explicit elevation plus subject/applicability state; acclimation/native/exemption state is not guessed.
- `travel.water.downstream-current-speed-bonus` and `travel.water.guided-downstream-float-duration`: automatic use waits for an explicit water-travel procedure/context including vehicle, guidance, and downstream state.
- `travel.navigation.recognize-lost` and `travel.navigation.set-new-course`: Rules Core can resolve their DCs from `random-travel-hours`, but Hex Crawl does not currently persist a dedicated random-travel-hours accumulator. The generic resolver remains available for explicit use; the expedition engine does not fabricate that input.

These are integration boundaries, not substitute Hex Crawl rules.

## Later cross-cutting requirements

This integration reinforces two Rules Core capabilities that should be handled centrally rather than as Hex Crawl one-offs.

### Character capability / mechanic modifier composition

Future travel resolution needs a general way to compose character-dependent effects such as proficiency/tool effects, skill ranks, class features, species/features, encumbrance, movement exceptions, and other capability modifiers.

Hex Crawl should consume the composed result; it should not learn individual character feature formulas.

### Generalized effect / consequence composition

Forced march and environmental mechanics require a general effect pipeline capable of representing damage, fatigue/exhaustion, saves, conditions, exposure, environment consequences, and mount endurance consequences.

Hex Crawl can decide when a consequence is due in expedition procedure, but the rule definition and cross-character effect composition belong outside the Hex-specific travel engine.

## Journey Challenge boundary

4e-style Journey Challenges / Complex Hazards remain a possible layer on top of normal expedition procedure. They do not replace:

`Travel -> Watch -> Navigation -> Encounter`

This integration does not introduce skill-challenge state or redesign the core watch procedure.

## Compatibility

The integration is designed to preserve existing expeditions, world data, Wonderdraft imports, party movement references, navigation state, map handling, encounter handoff behavior, audit history, and server-verifiable AutomaticRoll behavior. No publisher prose is copied into Hex Crawl; Rules Core remains the source-normalization and adjudication boundary.
