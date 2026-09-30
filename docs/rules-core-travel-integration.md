# Optional travel/environment providers and Rules Core adapter

Hex Crawl can consume source-backed travel and environment mechanics without making any external rules service part of its procedure identity or deterministic runtime.

The governing boundary is:

> Hex Crawl owns the capability contract. Rules Core is one adapter that can satisfy it.

## Ownership boundary

Hex Crawl owns expedition procedure, campaign-owned `CampaignProcedure` snapshots, expedition state, routes, watches, time progression, map state, navigation state, encounter cadence/outcomes, DM choices, generated-resolution audit records, and application of resolved values to the active expedition.

External providers may supply capability results that Hex Crawl explicitly requests. They do not own procedure execution and do not mutate Hex Crawl runtime state.

Rules Core remains authoritative for the source-backed mechanic definitions, provenance, required inputs, units, factor semantics, source-specific formulas, effective global/campaign rules, conflicts, and adjudication state that its adapter exposes.

Character Sheet remains authoritative for character state. Block Initiative remains authoritative for combat state.

The runtime boundary is therefore:

```text
external provider
    ↓
resolved capability value
    ↓
Hex Crawl application command/input
    ↓
generic deterministic runtime
```

The runtime never performs provider or HTTP calls.

## Hex Crawl provider contract

Travel/environment integration is expressed through the provider-neutral `ITravelEnvironmentProvider` capability interface in the application layer.

The interface exposes:

- provider identity and display metadata;
- availability through catalog results;
- capability/mechanic discovery;
- typed resolution requests;
- resolved quantity, factor, or check values;
- missing input keys;
- explicit unresolved states;
- source attribution/provenance.

`TravelEnvironmentMechanicKeys` remain in the provider-neutral application contract because keys such as `travel.overland.walk-distance` and `travel.navigation.avoid-getting-lost` are semantic capabilities that Hex Crawl requests, not Rules Core transport routes.

The provider contract contains no Tool Host path, Rules Core endpoint, authentication, or HTTP details.

## Provider selection and multiple-provider readiness

`TravelEnvironmentProviderRegistry` accepts capability providers without branching on vendor identity.

Selection is deterministic:

- an explicitly requested provider key selects that provider when registered;
- a single registered provider is selected automatically;
- when multiple providers exist, exactly one may be marked as the default;
- otherwise selection remains unresolved rather than inventing precedence.

Rules Core is currently the only production travel/environment provider. No fake production provider exists merely to demonstrate extensibility.

A future provider can implement the same capability without requiring application services or procedure/runtime code to understand Rules Core.

## Rules Core adapter

`RulesCoreTravelEnvironmentProvider` is the concrete Rules Core adapter in the web/provider boundary.

Only that adapter knows:

- the `rules-core` Tool Host target slug;
- Rules Core travel/environment HTTP routes;
- Tool Host delegation capability handling;
- Rules Core transport failures and timeouts;
- Rules Core JSON response shapes;
- Rules Core provider identity and source attribution.

The adapter translates Rules Core responses into the provider-neutral application model before returning them to application services.

Provider-specific transport failures are not exposed as Rules Core-specific application exceptions.

## Availability and resolution states

Provider absence is normal feature state, not application failure detection by exception.

Catalog availability distinguishes:

- `available`;
- `unavailable` for missing configuration/delegation;
- `failed` for transport or unreadable provider responses.

Capability resolution distinguishes:

- `resolved`;
- `input-required`;
- `not-applicable`;
- `requires-adjudication`;
- `unsupported`;
- `unavailable`;
- `failed`.

A missing capability is therefore distinct from a missing provider, and both are distinct from transport failure.

When provider-backed enrichment is required but unresolved, the application raises a provider-neutral unresolved condition with the mechanic key, status, provider metadata when available, and missing input keys. The DM can then enter an explicit value, adjudicate the external rule, retry, or use another provider when one exists.

Hex Crawl never fabricates a replacement rule.

## Startup and standalone behavior

PostgreSQL remains a required startup dependency.

Rules Core and Tool Host provider configuration do not.

Hex Crawl can start and native procedures can execute when:

- `ToolHost:BaseUrl` is absent;
- no delegation capability was issued for Rules Core;
- Rules Core is unreachable or times out;
- the requested capability is unsupported;
- the provider requires more input;
- the provider returns not-applicable or adjudication-required state.

In those cases, provider availability is represented honestly and native/manual behavior remains available where the procedure permits it.

## Campaign and global scope

An expedition retains an optional `CampaignId` provider query context:

- `CampaignId != null`: the Rules Core adapter uses campaign-effective endpoints;
- `CampaignId == null`: the adapter uses global effective endpoints.

Campaign selection is not provider selection. The campaign ID is context supplied to the chosen provider.

Assigning a campaign remains validated against the authenticated Tool Host campaign context. This phase does not change Site campaign ownership or identity architecture.

## Procedure-resolution enrichment

`ProcedureResolutionProviderEnricher` supplements only missing external values before deterministic procedure resolution.

The existing precedence is preserved:

1. An explicit expected distance supplied by the DM bypasses provider distance resolution.
2. An explicit navigation DC bypasses provider navigation resolution.
3. Provider enrichment is attempted only when the corresponding value is absent and the helper request includes provider-backed inputs.
4. A provider result is an input to the procedure helper; it does not overwrite materialized/native procedure behavior.

This preserves the Phase 3 `OptionalProvider` dependency-source model without widening every procedure input to accept a provider.

## Strict semantic interpretation

Provider-neutral integration does not relax semantic validation.

For travel distance:

- walking/hustling must return a quantity;
- supported distance units are converted only through Hex Crawl's explicit unit model;
- unsupported units are rejected rather than guessed;
- the quantity must actually be hourly when the helper expects an hourly rate.

For terrain adjustment:

- terrain and route must be supplied together;
- the provider capability must be resolvable;
- conflict/adjudication remains unresolved;
- the factor definition must use `distance-multiplier` semantics;
- negative factors are rejected.

For navigation:

- the provider must return a check/DC result;
- missing input, not-applicable, unsupported, failed, and adjudication states remain distinct.

Provider abstraction is not permission to reinterpret incompatible output.

## Provenance

Provider-generated values retain provider identity and source attribution.

Generic application structures do not hardcode `Rules Core` as a semantic rule. When the Rules Core adapter actually supplied a value, user-facing audit text may correctly contain:

```text
Provider: Rules Core
Source: ...
```

Provider identity is therefore provenance and capability-source information, not procedure identity.

Generated `AutomaticRoll` values remain server-generated, persisted, version-checked, superseded/consumed through the existing generated-resolution flow, and auditable.

The client does not supply trusted provider-resolution metadata.

## Client behavior

The workbench presents travel/environment resolution as an external provider capability rather than as a Rules Core feature.

The UI may display the currently available provider by name, for example `Available provider: Rules Core`, because provider attribution is useful. Controls and workflow terminology remain provider-neutral.

No generic provider-management/settings UI is introduced in this phase.

## Mechanic keys exposed but not automatically applied

The generic expedition-scoped catalog/resolution endpoints can expose capabilities that Hex Crawl does not yet apply automatically.

The following remain intentionally outside automatic expedition mutation until later phases provide sufficient state/effect infrastructure:

- `travel.overland.standard-travel-duration`;
- `travel.overland.forced-march-check`;
- `travel.overland.mount-vehicle-distance`;
- `travel.environment.hampered-movement`;
- `travel.environment.difficult-terrain-movement-cost`;
- `travel.environment.high-altitude-travel-time-cost`;
- `travel.water.downstream-current-speed-bonus`;
- `travel.water.guided-downstream-float-duration`;
- `travel.navigation.recognize-lost`;
- `travel.navigation.set-new-course`.

These are capability boundaries, not substitute Hex Crawl rules. Phase 6 does not implement participant activities, movement composition, generalized environment context, or effect/consequence execution.

## Generated procedure documentation

Phase 5 procedure references remain completely provider-independent.

Reference generation continues to derive from the exact immutable `CampaignProcedure` snapshot, including structural/unsupported mechanics, and does not fetch provider data.

Provider integration supplies optional application/runtime inputs; it does not alter the procedure contract being documented.

## Compatibility and preserved behavior

This phase preserves:

- campaign-owned `CampaignProcedure` authority;
- removable preset independence;
- generic handler/version runtime dispatch;
- immutable procedure revisions and pinned expedition snapshots;
- `OptionalProvider` dependency-source semantics;
- existing map/Wonderdraft behavior;
- encounter handoff behavior;
- server-verifiable `AutomaticRoll` behavior;
- PostgreSQL as the sole structured persistence backend.

The retired `IRulesCoreTravelGateway`, `ProcedureResolutionRulesCoreAdapter`, `RulesCoreTravelGatewayException`, and Rules Core-named client helper are not retained as compatibility paths because Hex Crawl is pre-release.
