# Square-based Fallout preset fidelity and execution status

This document tracks **preset configuration**, not exceptions in shared runtime code. All executable behavior is selected through versioned generic procedure mechanics.

## Evidence verified from the publisher

- **Fallout: Wasteland Wanderer**: Modiphius describes a recurring **Travel → Encounter → Action → Journaling** round. Its solo tables use d20 rolls to generate travel, events, decisions, and the narrative. Source: [Modiphius announcement, February 9, 2026](https://modiphius.net/blogs/press-releases/solo-fallout-rpg-wasteland-wanderer-now-available-for-pre-order). The publicly accessible announcement does **not** establish the exact per-step travel duration, supply costs, complete random tables, or the chosen 5×5 extent.
- **Fallout: Wasteland Warfare 2e**: Modiphius describes a **9×9** square campaign grid with a starting settlement at its center. Regions contain multiple squares, each with a Location. **Explore** enters normally unexplored space, checks for region changes, generates a location, then generates an Encounter with Opposition. **Travel** normally crosses previously visited locations and involves the choice to seek an encounter or avoid notice, potentially requiring a risk check. **Rest** applies at suitable settlements and supports healing, trade, equipment improvements, and recruitment. Source: [Developer blog, April 16, 2026](https://modiphius.net/blogs/news/dev-blog-4-how-do-i-explore-the-wasteland).
- The second-edition campaign also allows custom campaign faction and opposition tables. Region generation produces rules and descriptive details. Source: [Developer blog, September 25, 2026](https://modiphius.net/en-us/blogs/news/dev-blog-15-how-does-the-campaign-work-in-fallout-wasteland-warfare).

## Current generic implementation

Both presets use the same periodic geometry, movement, optional bounds, pinned exploration-flow policy, persisted journey-process engine, and expedition-local exploration-cell ledger. The 5×5 and 9×9 bounds are preset data. No shared executable code selects behavior by game key or publisher.

- Start an exploration process, progress through its pinned stages after explicit resolution, and resume the same process after reload/restart.
- Record visits and manually adjudicated location names/GM notes for the current authoritative cell. The site ledger is expedition-local; it is not a parallel world model. A repeat operation ID is not applied twice.
- Derive eligible exploration actions from the pinned flow and the recorded visit/rest-site state.
- Explain mandatory versus discretionary/risk-dependent encounter requirements **without** fabricating a roll, encounter, opponent, or location table.
- Preserve player-facing map knowledge separately. DM-entered site details are **not** automatically revealed to players.
- Existing generic journey-stage resolutions support explicit consequences, including time, resources, and encounter circumstances, where the DM supplies a resolved result.

## Deliberately manual or not yet integrated

- Published random location/region/encounter/opposition tables and table-driven location generation are not bundled or reproduced. Named source entries and actual tables require authorized data or supported user-supplied content.
- Exploration-cell records are DM confirmations, **not** automatic records of every runtime cell entry. A recorded visit and map visibility are distinct.
- Encounter requirements are policy evaluations, **not** durable unresolved encounter occurrences or a tactical-combat handoff. Existing generic runtime encounters, journey events, and Block Initiative handoff must be reused for that subsequent integration.
- Rest-site eligibility is recorded manually; resting recovery, costs, and supply consumption are not inferred.
- The presets' four-hour interval is a **placeholder**, not a verified duration for either published game. No unverified recovery or resource quantities are executed.
- The Wasteland Wanderer 5×5 bounds and its first-visit discovery trigger are carried over from project input/configuration, not independently verified from the publicly accessible publisher summary.

A full published-rules equivalence claim requires checking the relevant rulebooks, including any licensed tables and exceptions, and integration tests that cover the exact procedure. Do not label current manual fallbacks as automatic published rules.
