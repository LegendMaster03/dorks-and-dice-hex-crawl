# Pentagonal tiling catalog — independent acceptance review

**Scope:** [draft PR #102](https://github.com/LegendMaster03/dorks-and-dice-hex-crawl/pull/102)
on `feature/hierarchical-tiling-catalog-pentagons`.

## Current disposition

- **Implemented:** all 15 convex pentagonal families, ordinary periodic
  motif fixtures, nested category navigation, reuse of the existing preview,
  world creation through existing explicit topology + metric contracts.
- **Independent mathematical check:** added
  `PentagonalFamilyConditionsTests.cs`. This checks the complete published
  angle and side-length equations of each family for **every motif polygon**,
  not just a named prototile. It explores cyclic relabelings, both orientations
  and the source conventions associating side labels with a vertex.
- **General spatial engine changes:** none. Surveyor and movement code are
  unchanged. Existing periodic edge subdivisions handle T-junctions.
- **Automated gates:** run the standard full GitHub Actions workflow against
  the final feature branch head. An earlier full run on the 15-family branch
  passed at commit `024b63be788bed68a33fdea32e952fadaab306b5`.
- **Not yet accepted:** interactive deployed browser review. The feature
  branch must be available in a running application before this is possible.
  Do not confuse DOM unit tests, API tests, or inspection of main with
  acceptance of the feature branch in a real browser.

## Independent mathematical sources

The 15-family list and defining equations are given by Mann and
McLoud-Mann, *Puzzling Pentagons—How Many Ways Can We Cover a Flat Surface?*
(2023), Figure 2: https://doi.org/10.3389/frym.2023.953114.

See also Mann, McLoud-Mann and Von Derau,
*Convex pentagons that admit i-block transitive tilings* (2018),
https://faculty.washington.edu/cemann/pentagon.pdf and
Sugimoto and Ogawa, *Systematic Study of Convex Pentagonal Tilings*
(2005), https://forma.katachi-jp.com/pdf/2403/24030093.pdf.

**Type 5 source discrepancy:** the 2023 figure transcribes
`C = 2A = 90°`, while the Sugimoto–Ogawa classification gives
`A = 120°, C = 60°` with the same corresponding side equalities.
The Type 5 fixture satisfies the latter. Do not silently change the
implemented geometry to the 2023 figure without reconciling sources.

## Browser acceptance matrix (pending)

Use the **deployed feature-branch commit**, not main. Preserve screenshots,
console errors, failed network requests and the created world IDs.

1. In world creation, expand **Pentagons → Convex**. Confirm Types 1–15
   appear in numeric order. Expand/collapse other polygon categories.
   Existing square, hexagon, triangle and mixed-polygon options must remain.
2. For all fifteen, verify the existing preview is nonblank, recognizable
   as a pentagonal tessellation, not clipped incorrectly, and responsive.
   Type 3 and Types 10–15 include collinear atomic subdivisions and should
   still display five geometric sides per cell.
3. Select each family and create an unbounded world. Confirm the world
   editor renders the selected exact motif. Select and inspect several
   neighboring cells, including across a repeating-unit boundary.
4. Repeat with finite map bounds. Confirm invalid exits are omitted at
   world borders but interior movement and adjacency remain available.
5. Reload the world editor; verify that the selected geometry is retained.
   Confirm the world does not fall back to a harmonic solution sharing the
   same D-symbol.
6. Specifically compare **Type 2 vs Type 4**: the same topology is
   allowed, but both previews and resulting world geometry must be
   distinct and persist distinctly after reload.
7. Exercise the ruleset authoring selector. Rulesets persist **topology**
   rather than an example's metric realization; selecting two examples
   with the same D-symbol is a preview choice, not a saved geometry
   selection. Confirm the UI communicates this limitation.
8. Test keyboard navigation, focus behavior and phone-width layouts
   with categories expanded, including preservation of the selected
   item and no severe UI overflow or rendering stall.
9. Revisit existing hexagonal and square presets, including bounded
   Fallout 5×5 and 9×9 world workflows. Confirm no regressions.

## Merge gate

Do not merge or deploy without the user's explicit approval. All
automated suites must pass on the final branch head. Resolve any
browser defect with a general fix or document the evidence-backed
limitation, not a polygon-family-specific runtime path.
