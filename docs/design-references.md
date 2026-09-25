# Hex Crawl design references

## Purpose

Hex Crawl automates established tabletop wilderness procedures rather than inventing a replacement exploration game. No single D&D edition is authoritative for the product. The implementation should preserve useful concepts across editions, keep edition-specific mechanics in Rules Core where appropriate, and use source-defined values instead of hard-coded campaign assumptions.

The current reference hierarchy is:

1. **B/X + AD&D** — core wilderness exploration procedure, time/movement cadence, DM-facing bookkeeping, and information-dense running-sheet conventions.
2. **3e + 3.5e (3.Xe)** — detailed movement, terrain, environment, skills, modifiers, and simulation-oriented mechanics.
3. **5e + 5.5e (5.Xe)** — modern terminology and compatibility, character-facing travel responsibilities, pace consequences, and terrain-profile concepts.
4. **The Alexandrian** — procedural organization, watch-based usability, running-sheet layout, and practical synthesis where official rules leave gaps.
5. **4e** — complex journey resolution, structured hazards, consequential failure, progressive expedition state, and encounter handoff.

This order is not a quality ranking. It describes what each source family is primarily being used to inform.

## Core-procedure rule

The existing Hex Crawl procedure remains:

`Travel -> Watch -> Navigation -> Encounter`

4e-style skill challenges do **not** replace this loop.

The deterministic crawl runtime remains responsible for ordinary watch advancement, navigation/lost state, spatial progress, encounter cadence, and pauses. More complicated journey-scale problems should be modeled as an optional layer that can span ordinary watches without becoming the watch engine itself.

## 4e-derived future layer: Journey Challenge / Complex Hazard

The highest-priority 4e-derived future capability is a persistent Journey Challenge / Complex Hazard model for expedition-scale obstacles such as:

- mountain crossings;
- flooded regions;
- sandstorms;
- haunted forests;
- collapsing underground routes;
- severe weather;
- other obstacles that should take several checks, watches, or travel stages to resolve.

A Journey Challenge should be capable of:

- spanning multiple watches or travel stages;
- tracking progress toward a goal;
- defining applicable skills, competencies, tools, or other mechanics through Rules Core references instead of hard-coded skill names;
- accepting multiple valid approaches;
- recording successes, failures, complications, and consequences;
- allowing failure to alter circumstances rather than merely preventing progress;
- producing delayed or downstream effects;
- retaining generated/manual/external/DM-override provenance through the existing audit/history model.

Likely consequences include resource loss, delay, exposure, altered route, worsened encounter position, surprise state, reinforcements, encounter-composition changes, or other later-state effects.

### Ownership boundary

A Journey Challenge should be persistent **expedition state adjacent to the ordinary crawl runtime**, not a replacement `CrawlProcedureProfile`, not a special `EncounterOutcomeKind`, and not prose hidden in a runtime-event message.

The expected ownership split is:

- **Rules Core** owns canonical skill/competency/tool/condition definitions and edition-specific resolution mechanics.
- **Hex Crawl** owns which journey challenge is active, its progress/state, when checks or stages occur in expedition time, and the expedition consequences produced by resolved stages.
- **Block Initiative** owns combat initiative and tactical encounter execution after a handoff.
- **Character Sheet / other tools** may supply character capabilities, but they do not own expedition state.

A future challenge definition should therefore refer to external mechanics through stable identifiers/keys plus provenance, not embed a closed list of D&D skills into Hex Crawl.

## Travel capability

Travel capability should ultimately be derivable from actual party members, creatures, mounts, vehicles, carried constraints, terrain, and procedure/source data.

Current and future code should continue to avoid hard-coded assumptions about:

- miles versus kilometers;
- segment counts or segment distance;
- terrain rates;
- fixed party movement values;
- campaign-specific values such as Humblewood map scale.

The existing party movement reference remains useful as a DM-facing authoritative reference and override. Later automation may derive or propose those values from Rules Core/Character data without changing the meaning of the stored distance units.

## Marching order and travel roles

Hex Crawl already persists a flexible marching order and watch list. This is the correct base for later travel-role context.

Future role state may include concepts such as front, middle, rear, scout, navigator, mapper, forager, or source-defined equivalents. These should not introduce tactical-grid requirements into Hex Crawl.

The current active-watch `Activities` collection remains intentionally open text/keys. If Rules Core integration needs machine-readable character-to-duty assignments, add a typed travel-duty model rather than converting those strings into a fixed Hex Crawl skill enum.

## Progressive expedition conditions

Environmental hazards may need state that improves, remains stable, or worsens across multiple checks. Examples include:

- exposure;
- dehydration;
- altitude effects;
- disease;
- supernatural corruption.

Canonical condition definitions and edition-specific mechanics belong in Rules Core when available. Hex Crawl should own the expedition timeline and the events that apply, advance, reduce, or clear those effects.

Do not force progressive hazards into `RuntimePauseReason`. Pause reasons answer "why can the current deterministic transition not continue?" They are not a general status/effect model.

## Encounter handoff

Travel and hazard outcomes should be able to influence a later encounter through structured context rather than prose-only notes.

Potential handoff fields include:

- surprise / loss of surprise;
- advantageous or disadvantaged starting circumstances;
- delayed arrival;
- reinforcements;
- altered encounter composition;
- depleted resources;
- route or location changes;
- source challenge/hazard identifier;
- provenance for the effect.

This context should remain system-neutral enough for Hex Crawl to persist and Block Initiative to consume. Hex Crawl should not acquire tactical initiative, tactical-grid, or combat-round ownership.

## Present architecture assessment

The current implementation does not require a core-procedure redesign for these future features.

Existing choices that already support the direction:

- `CrawlRuntimeEngine` is deterministic and accepts resolved inputs rather than owning Rules Core.
- `StoredExpedition` is an aggregate envelope with adjacent party and generated-resolution state, so another persisted expedition-owned capability can be added without making it spatial engine state.
- runtime events are persisted as complete JSON records in `expedition_events`, allowing event contracts to grow without replacing the event table;
- independent resolution provenance already distinguishes procedure defaults, automatic rolls, manual rolls, external systems, and DM overrides;
- party marching order and watch rotation are persisted and do not assume fixed formation dimensions;
- distances carry explicit units;
- semantic world categories do not automatically imply movement multipliers;
- focused assistants and the full workbench share the same persisted session rather than owning competing state.

Areas that will require deliberate future design, but not current refactoring:

1. **Persistent challenge/hazard state.** There is no generic long-running expedition complication aggregate today. Add a dedicated typed model rather than bloating `ActiveWatchState`.
2. **Rules Core mechanic references.** Current travel activities are strings. Journey Challenges will need stable, typed mechanic references for skills/competencies/tools and possibly source/version identity.
3. **Structured consequences.** `CrawlRuntimeEvent` currently has generic event metadata plus message/subject/distance fields. Rich cross-tool consequences should receive typed payload/context rather than being encoded only in `Message`.
4. **Encounter handoff contract.** `ResolvedEncounter` currently describes encounter kind, timing, keyed location, note, and provenance. A separate structured handoff/context contract will be needed before Hex Crawl can communicate starting circumstances to Block Initiative.
5. **Progressive conditions/effects.** Current navigation and pause state are purpose-specific. A future expedition-effect model should be introduced instead of repurposing lost state or pause reasons.
6. **Derived movement integration.** Party movement is currently stored as an optional reference. Deriving it from creatures, mounts, vehicles, and characters requires Rules Core/Character integration but does not require replacing the distance model.

None of these gaps require changing the current Travel -> Watch -> Navigation -> Encounter loop in the present development pass.

## Current scope

For the current running-sheet/backend pass:

- finish and validate existing mechanics and UI;
- preserve modular source ownership;
- keep movement values and units data-driven;
- keep Rules Core integration external and identifier-based;
- keep marching order/watch data flexible;
- retain structured provenance and audit history;
- do not implement Journey Challenges, progressive hazards, or Block Initiative handoff yet.

Journey Challenge / Complex Hazard support is the highest-priority future 4e-derived feature. The remaining 4e concepts are design guidance or backlog items until they naturally intersect scheduled work.
