# Raster hex-grid alignment acceptance

This document records the review baseline for `feature/raster-grid-alignment`. It is deliberately separate from deployment acceptance: these measurements were taken from the supplied fixture rasters with an equivalent bounded-resolution detector pipeline, while final browser verification must still be performed against the deployed product before a tester is considered unblocked.

## Scope

Hex Crawl treats three questions independently:

1. **Raster lattice geometry** — orientation, rotation, center-to-center pixel spacing, and phase come from the raster image itself.
2. **Physical scale** — miles/kilometers per lattice step are changed only when a separate trustworthy source supplies that information. A raster may therefore have a high-confidence geometric fit with no detected physical scale.
3. **Structured-source to raster relationship** — Wonderdraft project coordinates are mapped to the raster only when the dimensions establish an exact or acceptably proportional export relationship. Grid fitting does not hide a project/raster mismatch.

The detector follows a global line-family approach rather than local corner matching. It uses image gradients, three approximately 60-degree-separated line families, Hough-style projection profiles, periodic carrier-line fitting, harmonic rejection, phase fitting, spatial coverage, competing-fit separation, and distant-region residual checks. This is consistent with the global-grid reasoning in Hansard et al., *Automatic detection of calibration grids in time-of-flight images* (Computer Vision and Image Understanding 121, 2014), adapted from two checkerboard line families to the three line families of a hexagonal lattice.

## Supplied raster fixtures

| Fixture | Raster | Result | Orientation | Rotation | Detected center spacing | Confidence | Distant residual | Spatial support |
| --- | --- | --- | --- | ---: | ---: | ---: | ---: | ---: |
| Humblewood | `Humblewood_Hex.png`, 2048×1536 | detected | FlatTop | 0.00° | 79.949 px | 98.19% | 0.506 px | 19 / 27 distant region/family checks |
| Magnostephis | `Magnostephis.png`, 1403×1026 | detected | PointyTop | 0.00° | 42.995 px | 96.14% | 2.063 px | 27 / 27 distant region/family checks |
| Bellowing Wilds | `FG_TheBellowingWilds_GMGridMap.png`, 2048×1325 | detected | FlatTop | 0.00° | 132.694 px | 97.65% | 2.023 px | 27 / 27 distant region/family checks |

The measurements above are in original raster pixels after scaling results back from the bounded analysis image. They are not claims about deployed browser rendering yet.

### Humblewood source cross-check

The matching Wonderdraft project and raster are both 2048×1536, so project-to-raster scale is 1.0. The known native project metadata reports a grid size of 80 project pixels. The raster detector returns 79.949 px center spacing, a difference of approximately 0.064%. This is strong fixture-specific agreement, but the importer does not use `grid.size` as a replacement for raster detection.

The scale bar is a separate source of information: 10 miles per segment × 3 segments over 220 project pixels gives 30 / 220 = 0.136363636 miles per project pixel. Applying that independently to the detected 79.949 px lattice gives approximately 10.902 miles per detected hex-center step. An exact 80 px step would imply 10.909 miles. The product presents a physical-scale change separately from lattice confidence and requires explicit confirmation when it materially changes the world's configured distance per hex.

The retained Wonderdraft source population is 2,616 records: 86 labels, 2,510 symbols, 19 paths, and 1 territory. The branch includes an application/persistence regression that imports 2,616 unique records, repeats the same-project import without replacing an existing registration, then performs explicit grid repair and reloads the world. The test requires the source-record count and unique keys, retained source archive, and provenance to survive repair.

### Magnostephis project/raster relationship

The known project canvas is 2107×1536 while the supplied raster is 1403×1026. These dimensions do not establish a sufficiently uniform proportional export. Hex Crawl therefore classifies the structured project/raster relationship as unresolved rather than multiplying project X and Y coordinates by independent scale factors.

That decision is independent of raster grid detection: the baked grid can still be detected geometrically at approximately 42.995 px center spacing, but the importer must not claim that Wonderdraft source records have been registered to that raster until a trustworthy project-to-raster transform is established.

### Bellowing Wilds

This fixture demonstrates the generic path. No Wonderdraft metadata is required. The raster provides a high-confidence flat-top lattice at approximately 132.694 px center spacing. Without a separate trustworthy physical-scale source, Hex Crawl preserves the world's configured physical neighbor distance and uses the raster only to align grid geometry.

## Required product behavior

- `Image contains a baked-in hex grid` enables the normal automatic workflow but does not make a fit trustworthy by itself.
- Detection is raster-driven and source-format agnostic.
- The preview renders the proposed raster registration and proposed Hex Crawl grid together before persistence.
- Low-confidence or gridless results do not enable automatic Apply.
- Gridless maps retain manual registration and do not receive an invented canonical grid.
- A trustworthy physical scale can modify `NeighborCenterDistance`, but that change is shown separately and requires explicit confirmation when materially different from the saved value.
- Locations, features, and unrelated source maps retain their world coordinates. When grid geometry changes, the preview explicitly warns about that impact.
- Applying repair updates the selected raster registration and world grid atomically under the overworld version check.
- Existing grid identity is preserved.
- Existing expeditions continue to protect grid geometry from mutation; repair fails without a partial raster update.
- Same-project Wonderdraft re-import is a content refresh, not an alignment repair. It preserves the saved registration and must not duplicate source records.
- A baked-grid Wonderdraft import on an unplaced raster opens the generic detector preview automatically. Wonderdraft metadata remains a constraint/cross-check and optional physical-scale source, not the lattice detector.

## Post-deployment browser acceptance

Use disposable/recreated test worlds only. Do not modify a human tester's active saved world for acceptance.

### 1. Humblewood canonical alignment

1. Create a disposable overworld with no expedition.
2. Upload `Humblewood_Hex.png` and mark it as containing a baked hex grid.
3. Import the matching `Humblewood_Expanded_v0.3.wonderdraft_map` against that raster.
4. Confirm that the automatic grid-alignment preview opens without requiring a separate Detect click.
5. Confirm the preview reports a flat-top lattice near 80 px center spacing and high confidence.
6. Confirm the Wonderdraft grid-size cross-check agrees with the raster fit rather than replacing it.
7. Confirm the physical scale is presented separately from lattice confidence. If it changes the current world's distance per hex, reject the confirmation once and verify that nothing is persisted; then repeat and explicitly confirm it.
8. Inspect the center, all four map corners, and several intermediate distant regions. The mathematical grid lines/centers must remain phase-aligned with the baked raster grid rather than merely appearing close near the center.
9. Inspect at multiple browser zoom levels. Zoom must change only display scale, not raster/grid registration.
10. Apply the preview and reload the page. Alignment must persist.
11. Open retained Wonderdraft source review. Confirm exactly 2,616 records remain available: 86 labels, 2,510 symbols, 19 paths, and 1 territory.
12. Re-import the identical project. Confirm there is still one raster, 2,616 retained source records, no duplicated promoted/authored objects, and the saved grid repair is preserved rather than silently recomputed as part of re-import.

### 2. Explicit repair of a bad saved registration

1. In a disposable world, create or reproduce a visibly bad affine registration for the Humblewood raster.
2. Use **Repair grid alignment** rather than deleting/re-uploading the raster.
3. Confirm the preview removes inappropriate shear/nonuniform scale and aligns the mathematical lattice to the baked grid.
4. Confirm retained source records and archive remain present before Apply.
5. Apply, reload, and reconfirm the raster asset, imported record population, provenance, and archive remain intact.
6. Confirm unrelated locations, features, and other placed reference maps have not silently changed world coordinates.

### 3. Magnostephis mismatch

1. Upload `Magnostephis.png` as a baked-grid raster.
2. Run generic grid detection and confirm a pointy-top lattice near 43 px center spacing is previewed without requiring Wonderdraft.
3. Attempt to attach the known 2107×1536 Wonderdraft project to the 1403×1026 raster.
4. Confirm Hex Crawl reports the project/raster relationship as unresolved and does not silently create an independent X/Y stretch.
5. Confirm the generic raster lattice preview remains usable independently of that structured-source mismatch.

### 4. Bellowing Wilds generic raster

1. Upload `FG_TheBellowingWilds_GMGridMap.png` as a baked-grid raster with no Wonderdraft project.
2. Confirm a flat-top lattice near 132–133 px center spacing is detected and previewed.
3. Confirm the world physical distance per hex is unchanged because there is no separate scale source.
4. Apply and reload; inspect center and corners for phase stability.

### 5. Gridless rejection

1. Upload a raster without a repeated hex lattice and leave it marked gridless.
2. Explicitly run Detect if desired.
3. Confirm Hex Crawl reports no reliable grid or an inconclusive fit and does not enable automatic Apply.
4. Confirm Advanced registration remains available.

### 6. Expedition safety

1. Create an expedition in a disposable world after a raster is present.
2. Attempt a repair that would change grid geometry.
3. Confirm the operation is rejected and neither the grid nor selected raster registration is partially changed.

## Acceptance boundary

A green branch validation plus the fixture measurements above establishes implementation readiness for browser acceptance. It does **not** establish that the affected human tester is unblocked. That conclusion requires the deployed build to pass the Humblewood center/corner/zoom/reload checks above against the actual baked raster grid.
