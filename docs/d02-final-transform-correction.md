# D02: preserve the measured raster lattice in final image placement

## Reproduced causes

The exact browser-review hex PNG reproduces the difference between a good
measured period and a drifting final overlay. The original hex detector reports
approximately 0.51 source pixels of worst-region error. The generalized
observation also measures near-correct translation vectors. Tile Crawl then
solved the final affine transform from three noisy contour corners and merely
tolerated the resulting per-period discrepancy. Small corner errors therefore
accumulated across the raster. Keeping distant checks inside Surveyor did not
protect this later conversion.

The blank 1584×1584 square response reproduces a separate protocol failure:
Surveyor accepts up to 2048² analysis pixels, while Tile Crawl retained the old
1.5-million-pixel candidate guard. The generic browser error hid that mismatch.

## Correction

Three corners now select a discrete polygon/lattice correspondence only. The
final linear transform maps both measured source periods exactly onto their
verified primitive integer world-lattice shifts. A single translation is fitted
over all corresponding motif vertices. Existing polygon, lattice determinant,
and reciprocal-interface verification still validates that final transform.
Raw contour uncertainty is used only to select the correspondence; final
world-overlay disagreement is capped at three analysis pixels independently.
There is no per-cell correction, image crop, or preset-specific importer.

The client analysis-pixel limit now matches Surveyor's 2048² envelope. Its JSON
response byte limit remains unchanged; these are different physical limits.

## Companion Surveyor correction

The Surveyor branch with the same name addresses the exact supplied square
fixtures using the existing generic pipeline:

- Distribute negative-space samples over cell interiors instead of treating a
  center POI as evidence that the whole cell is filled. The 82% support threshold
  remains unchanged, and truly filled interiors remain a negative control.
- Refine periods from polygon boundary centers rather than artwork-biased
  flood-fill mass centroids.
- Preserve the established translation-proposal pass, then expand its bounded
  spatial search when no lattice is found. Coarse 280px periods are not excluded
  by a fixed 220px support ceiling. Opposite shifts' regional support accounts
  for their different visible overlaps. Proposal and hypothesis budgets remain
  bounded and independent of tiling names or expected row/column counts.
- Retain weaker measured Sobel normals for dense corroboration without changing
  the original hex detector's voting sample or fitting behavior. Withhold
  crop-frame mask components from fitting and gather more source samples along
  each observed edge instead of reducing the minimum evidence count.
- Distinguish an inset Sobel/dilation contour from the final mathematical line:
  the preflight contour ink neighborhood is four pixels, and a mask corner can
  have an eight-pixel diagonal contour uncertainty. The independent final
  registered-cover source projection retains its original three-pixel ink
  neighborhood, regional support thresholds, topology/holonomy checks, and
  full-image negative-space validation. These contour bounds are not the
  permitted error of the transform being saved.

This follows the independent full-grid validation principle in Hansard et al.,
*Automatic detection of calibration grids in time-of-flight images*, section
2.6, DOI 10.1016/j.cviu.2014.01.007. It does not apply checkerboard-specific
transition rules to other tilings or claim to reproduce that paper verbatim.

## Review evidence

The exact original populated/blank 5×5 and 9×9 PNGs plus the original regular
hex PNG are checked into Surveyor's acceptance corpus with SHA-256 hashes.
The detector receives only pixels. All five pass independently projected
source edges in nine image regions; sampled edge support is 100% in each.
Shifted and 3%-drifting covers are rejected. Corpus data does not establish
performance on arbitrary publisher scans or noisy photographs.

Tile Crawl tests parse captured wire responses and exercise the actual final
registration method. Every returned transform must preserve distant period
repetitions and match independent printed vertices in first/middle/last rows
and columns within two source pixels. Two frozen pre-fix responses preserve
the original hex drift and square transport regressions; those tests fail on
the original Tile Crawl implementation. Current responses cover all five PNGs.

These are local branch results. No deployment or post-fix Cloud Browser
acceptance is implied. Persistence and the complete travel/player workflow
still need the deployment acceptance run. PostgreSQL-backed solution tests
could not complete locally because no test PostgreSQL service was available.

Local validation: Surveyor typecheck/build and all 207 tests passed (test-file
concurrency limited to one in this memory-constrained review environment);
30 focused backend parser/registration tests passed; 205 domain tests passed;
the client production build and all 312 client tests passed. The attempted
full solution run had 117 application-test failures caused by missing local
PostgreSQL, so this is not a claim that the database-backed solution suite
passed. No database schema, persistence, or authentication code was changed.
