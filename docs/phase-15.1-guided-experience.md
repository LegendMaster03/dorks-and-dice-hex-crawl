# Phase 15.1 Guided Hex Crawl experience

## Purpose

Phase 15.1 adds the beginner-facing Guided layer over the Phase 15 Compact interaction architecture.

The target user understands tabletop RPGs or D&D but does not already understand hexcrawling or Hex Crawl's workflow. Guided must help that user answer four questions without learning implementation vocabulary first:

1. What is this concept or setting?
2. Why does it matter at the table?
3. What is happening now?
4. What should I do next?

Guided is presentation only. It does not introduce a second procedure model, duplicate runtime state, infer rules the authoritative procedure does not provide, or bypass existing application/runtime mutations.

## Initial human-testing evidence

Early internal testing produced a direct example of the problem:

- an authenticated tester did not know what **Hex center distance** meant until it was explained as the game-world distance from the center of one hex to the center of an adjacent hex;
- the tester explicitly requested tooltips/explanations for numeric settings;
- the tester observed that much of the tool assumes prior knowledge of hex travel and suggested a short basics tutorial plus a more advanced follow-up.

This is treated as representative product evidence, not as a request to hardcode one tester's wording or to make video content a runtime dependency.

## Guided interaction model

Guided enriches existing Compact surfaces through reusable affordances:

- point-of-use field explanations for domain-specific terminology and numeric values;
- beginner callouts that explain the shortest successful path through a workflow;
- **Why?** disclosures that explain the purpose of a rule or next action;
- examples when a value is difficult to interpret without one;
- consequence/next-action coaching derived from the same authoritative state already used by Compact;
- an in-product basics area that remains useful even when no external tutorial video exists.

Guidance is default-on for a user who has not chosen otherwise and can be hidden without changing campaign, procedure, map, or runtime state.

## Numeric-setting rule

A numeric field that requires domain knowledge should explain, at minimum:

- what the number measures;
- which unit or reference frame it uses;
- whether it is an ordinary gameplay value or an advanced/internal alignment value;
- what a normal example looks like when an example is safe and non-authoritative.

For example:

> Hex center distance is the game-world distance from the center of one hex to the center of an adjacent hex. If adjacent hexes represent 6 miles, enter 6 and choose Miles.

Internal geometry values such as origin, rotation, or world-coordinate hex radius must be clearly distinguished from physical travel distance.

## Tutorial/content boundary

A short screen-recorded basics tutorial and separate advanced tutorial may be useful supplemental material, but the application must remain understandable without a video. The in-product Guided layer is the authoritative onboarding surface; external video can demonstrate it later.

The initial in-product basics content covers procedures/presets, spatial versus non-spatial contexts, hex center distance, travel periods/watches, intended course versus resolved travel, navigation, pace/travel mode, q/r axial coordinates, and when advanced setup is actually needed.

## Runtime coaching

The existing **Next action** surface remains primary. Guided adds a **Why is this next?** explanation based on the same action kind already derived from authoritative expedition, journey, survival, and pause state. Pending effect and resource consequences also expose **Why is this pending?** explanations derived from their authoritative pending reason and required action. Guidance explains authoritative state; it does not calculate replacement outcomes in the browser.

## Host/authentication boundary

A tester who opened a restricted deep link before signing in received a generic page-not-found experience. Hex Crawl does not own Site authentication or tester-role authorization, so that behavior is not corrected by inventing tool-local authentication. Host integration should prefer an explicit sign-in/access-required state for restricted tool deep links when the Site supports it.

## Implemented Guided slices

The current Phase 15.1 branch includes:

- default-on, dismissible beginner help backed by local presentation preference only;
- a home-page basics primer covering procedure/context selection and core travel terminology;
- point-of-use help for world/grid settings, including hex center distance and internal geometry distinctions;
- Guided Compact procedure explanations and per-rule **Why?** disclosures;
- **Why is this next?** coaching on the authoritative expedition Next action;
- **Why is this pending?** coaching for pending effect and resource consequences without inventing automatic outcomes;
- beginner-oriented preset summaries derived from active generic procedure behavior and automation level rather than preset identity or mere module presence;
- curated domain explanations for known Compact numeric procedure settings, while unmodeled fields continue to use their pinned schema descriptions;
- rendered-review assertions for Guided home, preset, Compact, world/grid, and runtime surfaces.

The Site authentication/access-denied behavior remains a host-level follow-up rather than a Hex Crawl runtime responsibility.

## Acceptance direction

Phase 15.1 is successful when a tester with general tabletop RPG knowledge can begin from the Hex Crawl home page, choose a reasonable starting procedure/context, understand unfamiliar required settings, and follow current-action coaching through representative spatial and nonspatial play without needing Discord explanations from the developer.

## Phase 15.1.1 — guided workflow and progressive disclosure

Phase 15.1.1 refines the Phase 15.1 interaction model. Earlier references to a large, default-on home primer or all-at-once preset advice describe the previous UI; the current presentation prioritizes a concrete task and makes supplemental explanations optional.

- **Home/start:** Guided setup presents map/travel setup, exploration ruleset, and expedition details as three reversible steps over the same existing form and start API. Hiding beginner help shows all steps in a direct form; re-enabling guidance restores the current step without discarding input. Selecting a step never creates an expedition or a campaign ruleset.
- **Map scale:** A grouped distance/value and unit replace the ambiguous `Hex center distance` label in home, world setup/editor, and focused utility setup. Center-to-center distance is still explained at the field; internal grid radius, origin, and rotation remain separate, advanced values.
- **Preset selection:** The first card shows name, short experience description, four aligned facts derived from configured module behavior, and an up-front manual/non-executable caution when applicable. `View details` retains full rule coverage, automation, table burden, and source/provenance; `Use this preset` is the explicit selection action. The rendering uses active modules, not preset-name switches. Catalog descriptions are written for tabletop decisions rather than implementation history.
- **Active play:** The current authoritative Next action remains the only progression entry. A concise guided action guide identifies the current input, what resolving it can change, and the latest recorded event when available. Pause, journey, survival, navigation, and movement-composition state supply the action; guidance never resolves a roll or moves the party.
- **Direct/advanced access:** Guided help is presentation-only. Custom Compact, Advanced, and JSON modes, focused utilities, campaign ruleset revisions, DM authority, and manual resolution stay available.

The Site-owned restricted deep-link sign-in/access-denied experience remains a separate host-level dependency. Phase 15.1.1 does not introduce Hex Crawl authentication or grant access to testers.

See `docs/phase-15.1.1-coverage.md` for the surface audit and validation record. Passing automated checks alone does not establish that new human testers complete onboarding successfully.
