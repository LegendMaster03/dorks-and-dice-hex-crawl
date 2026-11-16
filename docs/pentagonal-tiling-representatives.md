# Convex pentagonal tilings — representative fixture inventory

**Branch scope:** add representative, periodic, monohedral tilings for the fifteen
convex pentagon families as ordinary catalog data. The generic periodic topology,
metric validator, map renderer and traversal engine remain authoritative.

**Current state:** the hierarchical catalog and Types 1, 3, and 4 have concrete
periodic representative definitions and automated tests. Types 2, 5–15 remain
pending. Do not manufacture preview geometry from an illustration or add a
name-only selector entry.

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
| 2 | C + E = 180°; a = d | Pending |
| 3 | A = C = D = 120°; a = b; d = c + e | Implemented: 3 congruent pentagons per hexagon; six atomic interfaces and five geometric sides |
| 4 | A = C = 90°; a = b; c = d | Implemented: standard Cairo tiling; eight congruent pentagons per square translation motif |
| 5 | C = 2A = 90°; a = b; c = d | Pending |
| 6 | C + E = 180°; A = 2C; a = b = e; c = d | Pending |
| 7 | 2B + C = 360°; 2D + A = 360°; a = b = c = d | Pending |
| 8 | 2A + B = 360°; 2D + C = 360°; a = b = c = d | Pending |
| 9 | 2E + B = 360°; 2D + C = 360°; a = b = c = d | Pending |
| 10 | E = 90°; A + D = 180°; 2B − D = 180°; 2C + D = 360°; a = e = b + d | Pending |
| 11 | A = 90°; C + E = 180°; 2B + C = 360°; d = e = 2a + c | Pending |
| 12 | A = 90°; C + E = 180°; 2B + C = 360°; 2a = c + e = d | Pending |
| 13 | A = C = 90°; 2B = 2E = 360° − D; c = d; 2c = e | Pending |
| 14 | D = 90°; 2E + A = 360°; A + C = 180°; b = c = 2a = 2d | Pending |
| 15 | A = 60°; B = 135°; C = 105°; D = 90°; E = 150°; a = 2b = 2d = 2e | Pending |

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

## Implemented Cairo Type 4 example

The eight-pentagon Cairo motif has a square translation basis and uses
alternating horizontal and vertical short segments centered within a square
grid. Its pentagons have two nonadjacent right angles and three 120° angles.
The four longer sides are equal; the remaining shorter side has length
(1 - 1/\sqrt{3}) relative to a square unit. This specific symmetric Cairo
shape also satisfies the Type 2 family conditions. It is **cataloged as Type 4
only**, so a distinct Type 2 construction can be supplied rather than showing
two identical selections. The implementation uses no Cairo-specific runtime.

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
