# Convex pentagonal tilings — representative fixture inventory

**Branch scope:** add representative, periodic, monohedral tilings for the fifteen
convex pentagon families as ordinary catalog data. The generic periodic topology,
metric validator, map renderer and traversal engine remain authoritative.

**Current state:** all fifteen classified convex pentagon tiling families
have one static periodic representative and are registered in the general
catalog. Automated domain and PostgreSQL HTTP acceptance tests cover each.
The branch must pass its complete CI and independent live browser review
before it is considered ready for merge.

## Mathematical source

Casey Mann and Jennifer McLoud-Mann, *Puzzling Pentagons—How Many Ways Can We Cover
a Flat Surface?* (2023), Figure 2:
https://doi.org/10.3389/frym.2023.953114

The following conditions are a **reference checklist**, not sufficient input for
a tiling realization. Lettering is as in that figure. Each source family can include
many pentagon shapes, and families can overlap; Types 14 and 15 are rigid up to
similarity. See https://mathworld.wolfram.com/PentagonTiling.html.

| Type | Source's defining conditions | Verified motif |
| --- | --- | --- |
| 1 | D + E = 180° | Implemented: two congruent half-hexagon pentagons, two cells per primitive motif |
| 2 | C + E = 180°; a = d | Implemented: distinct equilateral Cairo realization, eight congruent pentagons per motif |
| 3 | A = C = D = 120°; a = b; d = c + e | Implemented: 3 congruent pentagons per hexagon; six atomic interfaces and five geometric sides |
| 4 | A = C = 90°; a = b; c = d | Implemented: standard Cairo tiling; eight congruent pentagons per square translation motif |
| 5 | A = 60°, C = 120° (source discrepancy below); a = b, c = d | Implemented: six congruent pentagons per motif |
| 6 | C + E = 180°; A = 2C; a = b = e; c = d | Implemented: Kershner six-family construction: four congruent pentagons, five atomic edges per tile |
| 7 | 2B + C = 360°; 2D + A = 360°; a = b = c = d | Implemented: Kershner construction: eight congruent pentagons, five atomic edges per tile |
| 8 | 2A + B = 360°, 2D + C = 360°; a = b = c = d | Implemented: eight congruent pentagons, four equal sides |
| 9 | 2E + B = 360°; 2D + C = 360°; a = b = c = d | Implemented: Rice construction: eight congruent pentagons, five atomic edges per tile |
| 10 | E = 90°, A + D = 180°, 2B − D = 180°, 2C + D = 360°; a = e = b + d | Implemented: six congruent pentagons, two with seven atomic interfaces |
| 11 | A = 90°, C + E = 180°, 2B + C = 360°; d = e = 2a + c | Implemented: eight congruent pentagons, four with seven atomic interfaces |
| 12 | A = 90°; C + E = 180°; 2B + C = 360°; 2a = c + e = d | Implemented: Rice construction: eight congruent pentagons, each having six atomic interfaces on five sides |
| 13 | A = C = 90°; 2B = 2E = 360° − D; c = d; 2c = e | Implemented: Rice construction: eight congruent pentagons, four with six atomic interfaces |
| 14 | D = 90°; 2E + A = 360°; A + C = 180°; b = c = 2a = 2d | Implemented: Stein fixed-shape construction: six congruent pentagons, exact algebraic reconstruction of rounded source coordinates |
| 15 | A = 60°; B = 135°; C = 105°; D = 90°; E = 150°; a = 2b = 2d = 2e | Implemented: 12 congruent pentagons per period, four with six atomic interfaces |

## Implemented Type 1 example

The first example bisects each regular hexagon along the segment joining
the midpoints of opposite edges, creating two congruent convex pentagons.
Their interior angles, in cyclic order, are 90°, 120°, 120°, 120°, 90°;
the two adjacent right angles satisfy the Type 1 condition. A rotation
through 180° maps one pentagon onto the other. The primitive translation
motif contains two pentagons and uses the regular hexagonal lattice.

The fixture is defined by `VerifiedPentagonType1()` in
`CrawlTilingNames.cs`, with both an explicit topology witness and a metric
realization. The existing validator proves periodic incidence, reciprocal
edges, nonoverlap and area coverage. The domain suite checks congruence
and generic cell traversal geometry. HTTP integration tests cover catalog
delivery, finite-bounds world creation, expedition startup and reload.

The construction is independent of the particular pentagonal gallery
illustrations; the Type 1 classification is supported by the cited general
family definition.

## Implemented Type 2 equilateral Cairo example

A second, distinctly realized Cairo pattern uses the same verified
eight-cell periodic topology but a different metric. Put the bar offset at
`(sqrt(7) - 1) / 6` of a square unit. Each pentagon then has four long
edges of length `sqrt(1/4 + (1/2 - offset)^2)` and a fifth edge of
length `2 * offset`; these are equal at precisely this value. The pentagon
has two nonadjacent right angles. Under the Type 2 family relabeling
`C + E = 180°`, and `a = d` holds because all sides are equal.

This special metric lies in the **intersection of Types 2 and 4**.
The catalog labels it Type 2 while retaining the non-equilateral Cairo
metric as the separate Type 4 example. These are distinct previews and
world geometries, even though they share one canonical D-symbol.

The presentation vocabulary compares scale-invariant geometric shapes
when multiple catalog entries share a D-symbol. This is a general
ambiguity-resolution improvement, not a new topology mechanism.

## Implemented Cairo Type 4 example

The eight-pentagon Cairo motif has a square translation basis and uses
alternating horizontal and vertical short segments centered within a square
grid. Its pentagons have two nonadjacent right angles and three 120° angles.
The four longer sides are equal; the remaining shorter side has length
(1 - 1/\sqrt{3}) relative to a square unit. It is cataloged as Type 4.
An earlier assumption that this exact metric also satisfies Type 2 was not
established from the defining length conditions; the separate Type 2
representative below establishes those conditions explicitly.
The implementation uses no Cairo-specific runtime.

Reference: https://doi.org/10.1038/s41598-025-17010-4

## Implemented Type 3 hexagonal subdivision

A regular hexagon is divided into three congruent convex pentagons by
joining the hexagon center to the midpoints of alternating boundary edges.
In one pentagon, three angles are 120° and two are 90°; the two inward
radial sides are equal and the full outer hexagon side is the sum of its
two half-side neighbors. This is an explicit Type 3 construction.

The shared edges cross the periodic hexagon boundary: a five-sided pentagon
has **six atomic edge interfaces** because one geometric side contains a
T-junction midpoint. No special movement mode is needed. The Type 3 fixture
tests both generic edge subdivision and geometry-aware naming.

Reference: https://commons.wikimedia.org/wiki/File:Pent-Hex-Type3-3.svg

## Implemented Type 15 fixed-shape pentagon

The MathWorks [Type 15 construction](https://blogs.mathworks.com/graphics/2015/08/19/type-15-convex-pentagon/)
provides exact coordinates for the fixed convex pentagon, reflections,
rotations, twelve pentagons in a repeating region, and two independent
translation vectors. The motif was independently reconstructed from the
published transformations.

The resulting boundary graph contains twelve pentagons and 64 atomic edge
interfaces. Four tiles contain a collinear T-junction split and therefore
six atomic interfaces, while all twelve have five geometric sides.
The primitive translation basis was reoriented to positive determinant,
and individual motif tiles were moved by whole lattice periods to remain
within the existing bounded metric verifier. These transformations do not
change the tiling.

Independent domain tests verify the source's 60°, 90°, 105°, 135° and
150° angles; the edge-length ratios; positive, nonoverlapping geometry;
the primitive translation cover; and reciprocal interfaces.
The existing HTTP acceptance test also exercises world creation,
finite bounds, expedition startup and persisted world reload.

No Type 15 logic was added to the general spatial engine.

## Formula-derived Types 5, 8, 10 and 11

Maxie D. Schmidt's [WXML Tilings Programming HOWTO](https://github.com/maxieds/WXMLTilingsHOWTO/blob/master/Pentagons.py)
provides polygon-coordinate formulas and two independent translation vectors
for numbered pentagon tilings, based on Ed Pegg Jr's Wolfram Demonstrations.
We evaluated those formulas with the
[published example parameters](https://github.com/maxieds/WXMLTilingsHOWTO/blob/master/tiling_pc_plots.py),
oriented the polygons counterclockwise, normalized them by whole lattice
translations, and independently derived periodic adjacency from segment matches.

| Type | Parameters (radians) | Motif cells | Atomic boundary counts |
| --- | --- | ---: | --- |
| 5 | AA = 2.356, b = 0.5 | 6 | all 5 |
| 8 | AA = π/2 | 8 | all 5 |
| 10 | AA = 1.4 | 6 | four 5, two 7 |
| 11 | AA = 2.113 | 8 | four 5, four 7 |

All examples use the existing domain validator to establish reciprocal edge
geometry, positive area, nonoverlapping interiors and a primitive Z² cover.
Independent tests check convexity, congruence, five geometric sides,
and the defining angle and side relationships.

**Type 5 source discrepancy:** The Frontiers for Young Minds
[Figure 2](https://doi.org/10.3389/frym.2023.953114) states
`C = 2A = 90°`, but Ed Pegg Jr's
[classification](https://mathpuzzle.com/tilepent.html), the
[Fano lecture](https://www.fano.co.uk/talks/Pentagonal.pdf), and
Sugimoto and Ogawa's
[Forma paper](https://forma.katachi-jp.com/pdf/2403/24030093.pdf)
use the angles 60° and 120° with equal neighboring side pairs.
The implemented Type 5 realizes the latter conditions. We preserve
this discrepancy rather than treating the two formulas as equivalent.

## Remaining six source-derived representatives

The [Apache-2.0-licensed OpenSCAD implementation](https://github.com/pinkfish/openscad_boardgame_toolkit/blob/master/pentagon_tilings.scad)
provides polygon coordinates and two translation vectors for the remaining
Type 6, 7, 9, 12, 13, and 14 constructions, based on Ed Pegg Jr's
Wolfram Demonstrations.

| Type | Source parameters | Motif cells | Atomic boundaries per cell |
| --- | --- | ---: | --- |
| 6 | AA = 120° | 4 | all 5 |
| 7 | AA = 150° | 8 | all 5 |
| 9 | b = 1.573 | 8 | all 5 |
| 12 | AA = 125° | 8 | all 6 |
| 13 | AA = 135° | 8 | four 5, four 6 |
| 14 | Fixed Stein pentagon | 6 | two 5, two 6, two 7 |

The source's trigonometric expressions were evaluated with angles in
degrees, and resulting polygon coordinates were oriented and normalized
to the fundamental lattice. We independently derived the atomic
boundary adjacencies, including T-junction subdivisions, then obtained
canonical translation D-symbols and primitive Z² lattice witnesses.
All six use the unchanged generic periodic topology and metric
contracts.

### Type 14 numeric precision correction

The published OpenSCAD Type 14 example truncates some vertex
coordinates and periods to approximately five or six significant digits,
which produces area and edge-closure errors. We reconstructed the
exact algebraic quantities using the pentagon's known side lengths.
With

`a = (sqrt(57) - 3) / 8`,
`k = sqrt(1 - a²)`, and
`h = sqrt(4 - (3a - 1)²)`,

the approximate source values have exact equivalents:

| Rounded value | Exact expression |
| ---: | --- |
| 0.822525 | k |
| 1.87118 | h |
| 1.64505 | 2k |
| 2.6937 | h + k |
| 3.51623 | h + 2k |
| 4.33875 | h + 3k |
| 2.46757 | 3k |
| 0.935588 | h / 2 |
| 2.80676 | 3h / 2 |
| 5.27434 | 3h / 2 + 3k |

The corrected six-cell geometry closes exactly at the double-precision
tolerance used by the existing validator. Four cells have subdivided
straight boundaries. The Stein prototile has two equal short edges,
two edges twice their length, one distinct side, and a right angle.
No new runtime handling is necessary.

## Requirements for admitting a representative

1. Record a source for the construction and the verified single pentagonal
   prototile. Independently calculate its side lengths and interior angles.
2. Supply the finite periodic topology witness, reciprocal interfaces and
   translation vectors in existing Delaney–Dress and periodic metric contracts.
   Count geometric pentagon sides separately from subdivided atomic interfaces.
3. Prove every motif polygon is congruent to the prototile, allowing reflections,
   translations and rotations. Prove no gaps or overlapping interiors in the
   primitive periodic cover using existing validators.
4. Confirm the source's family conditions on the proposed representative.
   Record any overlaps with other families rather than claiming exclusivity.
5. Ensure the selected catalog entry recreates its specific validated geometry:
   a D-symbol alone does not select a particular metric realization.
6. Test current preview, world creation and restart, boundary and vertex contact,
   traversal, finite bounds and ruleset compatibility. Add the entry to the
   category catalog only after those checks pass.

Keep each example as data and generic tests. A failure does not justify a
pentagon-specific execution path. A proposed change to the general engine
requires a reproducible failing fixture and a broader capability rationale.
