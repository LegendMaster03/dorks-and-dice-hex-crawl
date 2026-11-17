# PR #102 — Localhost browser review: correction follow-up

**Basis:** `pr-102-localhost-browser-acceptance-20261116.md` (WorkChat review of the locally running feature build at commit `62f05012e14f9c6f62c77cace0ffbd98c922539e`).

The reviewer created, rendered, reloaded, and exercised a reciprocal route in all 15 pentagonal families. The review found three browser issues, classified **Fail** until corrected and retested. The report did **not** establish that the major Rhombille viewport failure was introduced by PR #102.

| Finding | Correction on feature branch | Automated coverage | Browser retest |
| --- | --- | --- | --- |
| UI-01 — previews obscure full polygons | Center on the exemplar polygon and constrain scale to leave space around its full bounding box, independently of short atomic subdivisions | `tiling-preview.test.mjs`: short-segment, full-tile framing and existing culling tests | Pending: Types 1, 10, 11 and other variants |
| UI-02 — ruleset chooser closes and drops focus | Update same-D-symbol preview without rerendering. For topology changes, keep chooser expansion state and restore focus to the selected control following recomposition | Existing category-choice DOM tests plus the frontend build; specific authoring browser behavior remains to verify | Pending: same-D-symbol and changed-topology ruleset flows |
| UI-03 — default Rhombille viewport returns bounded enumeration HTTP 400 | When and **only when** the API returns the known 2,500-candidate overflow, request a closer zoom while preserving center. Never retry unrelated API errors, exceed maximum zoom, or change server enumeration budgets | `map-editor-runtime.test.mjs`: recovery, bounded zoom, unrelated-error rejection | Pending: default wide Rhombille viewport, reset, reload, dense map zoom-out |

These are general UI and viewport fixes, not tiling-specific runtime branches. The authoritative topology, metric, cell enumeration cap, movement, and Surveyor implementation are unchanged.

## Acceptance checks still required

1. Rebuild the feature branch locally and verify the **exact new HEAD**; the original graphical browser run exercised an older commit and can not certify these corrections.
2. Recheck all three findings with the WorkChat procedures and capture screenshots / active-element or network evidence.
3. Confirm that other world-grid presets, including square and hexagonal choices, remain usable at default and reset zoom.
4. Confirm that the preview zoom correction still shows neighboring cells, not only a clipped or isolated polygon.
5. Verify ruleset selection when the two choices share a D-symbol (Types 2 and 4), and when switching to a genuinely different topology. Preserve keyboard focus and category expansion in both cases.
6. For UI-03, verify no repeated HTTP 400 loop and that unrelated network/authorization errors are still surfaced as failures.
7. Keep PR #102 in draft until browser acceptance is rerun. Automated tests are necessary but do not substitute for that review.

**No authorization to merge or deploy follows from this report.**
