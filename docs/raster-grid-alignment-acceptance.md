# Map-image hex-grid alignment acceptance

This document records the review baseline for automatic hex-grid alignment. The fixture measurements remain useful detector parity references, while final browser verification must still be performed against the deployed product before a tester is considered unblocked.

## Scope

Hex Crawl treats three questions independently:

1. **Image lattice geometry** — orientation, rotation, center-to-center pixel spacing, and phase come from the map image itself through Surveyor.
2. **Physical scale** — miles/kilometers per lattice step change only when a separate trustworthy source supplies usable scale information. A map can therefore have a trustworthy geometric fit with no detected physical scale.
3. **Structured-source to image relationship** — Wonderdraft project coordinates are mapped to the image only when the dimensions establish an exact or acceptably proportional export relationship. Grid fitting does not hide a project/image mismatch.

The detector follows a global line-family approach rather than local corner matching. Surveyor owns that generic image-processing implementation. Hex Crawl owns interpretation, trust policy, alignment proposal, world mutation, and persistence.

## Supplied image fixtures

| Fixture | Image | Result | Orientation | Rotation | Detected center spacing | Confidence | Distant residual | Spatial support |
| --- | --- | --- | --- | ---: | ---: | ---: | ---: | ---: |
| Humblewood | `Humblewood_Hex.png`, 2048×1536 | detected | FlatTop | 0.00° | 79.949 px | 98.19% | 0.506 px | 19 / 27 distant region/family checks |
| Magnostephis | `Magnostephis.png`, 1403×1026 | detected | PointyTop | 0.00° | 42.995 px | 96.14% | 2.063 px | 27 / 27 distant region/family checks |
| Bellowing Wilds | `FG_TheBellowingWilds_GMGridMap.png`, 2048×1325 | detected | FlatTop | 0.00° | 132.694 px | 97.65% | 2.023 px | 27 / 27 distant region/family checks |

The measurements above are in original source-image pixels after scaling results back from the bounded analysis image. They are parity references, not a substitute for deployed browser verification.

### Humblewood source cross-check

The matching Wonderdraft project and image are both 2048×1536, so project-to-image scale is 1.0. The known native project metadata reports a grid size of 80 project pixels. The image detector returns 79.949 px center spacing, a difference of approximately 0.064%. This is strong fixture-specific agreement, but `grid.size` remains an independent cross-check rather than a replacement for Surveyor detection.

The scale bar is a separate source of information: 10 miles per segment × 3 segments over 220 project pixels gives 30 / 220 = 0.136363636 miles per project pixel. Applying that independently to the detected 79.949 px lattice gives approximately 10.902 miles per detected hex-center step. An exact 80 px step would imply 10.909 miles.

The retained Wonderdraft source population is 2,616 records: 86 labels, 2,510 symbols, 19 paths, and 1 territory. Grid alignment must preserve retained source content, archive, provenance, and authored world records.

### Magnostephis project/image relationship

The known project canvas is 2107×1536 while the supplied image is 1403×1026. Those dimensions do not establish a sufficiently uniform proportional export. Hex Crawl therefore classifies the structured project/image relationship as unresolved rather than inventing independent X/Y scale factors.

That decision is independent of visible-grid detection: the printed grid can still be detected geometrically, but retained Wonderdraft source coordinates must not be claimed as aligned until a trustworthy project-to-image transform exists.

### Bellowing Wilds

This fixture demonstrates the generic path. No Wonderdraft metadata is required. The image provides a high-confidence flat-top lattice at approximately 132.694 px center spacing. Without a separate trustworthy physical-scale source, Hex Crawl preserves the world's configured physical neighbor distance and uses the image only to align grid geometry.

## Required product behavior

- Upload success is visually obvious: an unaligned map image is rendered immediately using temporary centered placement rather than being omitted from the canvas.
- Temporary centered placement is presentation-only and is never persisted as authoritative alignment.
- **Detect / repair hex grid** is one user action. The initiating button becomes an analyzing/applying progress indicator and there is no separate Preview, Apply, or Confirm button sequence.
- The Surveyor `grid-analysis` request remains non-mutating. Hex Crawl builds a transient proposal and persists it through the separate `grid-alignment` mutation only when the result passes Hex Crawl's automatic-apply trust policy.
- Automatic persistence requires a source-resolution-verified canonical fit. Inconclusive, gridless, downscaled-only, or excessive-residual results remain non-persistent.
- A failed or insufficiently trustworthy automatic result leaves the map image visible and directs the user to **Manual placement** rather than exposing detector mathematics.
- A trustworthy independent Wonderdraft physical scale may supplement the proposal. Failure to read optional Wonderdraft scale context must not invalidate an otherwise trustworthy image-grid fit; the existing physical distance is preserved when no usable scale is available.
- Applying alignment updates the selected map image alignment and world grid atomically under the overworld version check.
- Existing grid identity is preserved.
- Existing expeditions continue to protect grid geometry from mutation; repair fails without a partial map-image update.
- Same-project Wonderdraft re-import is a content refresh, not an alignment repair. It preserves saved alignment and must not duplicate retained source records.
- Manual placement remains available for gridless images, differently cropped versions, and separate regional maps.
- Normal map-management UI uses plain language such as **map image**, **map set**, **map view**, and **Manual placement**. Technical source-map/raster terminology remains internal or behind technical disclosure.

## Map sets and map views

A **map set** is a collection of alternate versions of the same underlying map/geographic extent. Typical versions include GM/player images, copies with or without a printed grid, and auxiliary numbered/keyed references used during preparation.

- The upload workflow can add one or several map images to a set in one operation.
- Existing sets are selected from a list rather than requiring exact retyping.
- GM and Player versions can participate in the temporary coordinated **Map view** selector.
- Shared/neutral and auxiliary/reference-only images retain independent visibility while a coordinated GM/Player view is active.
- Returning to Manual visibility restores the GM/Player visibility choices that existed before entering a coordinated view.
- Whole source images are not the final layer model. They remain independent evidence for future image differencing that may derive a common base and true composable difference layers.
- A neighboring region or otherwise different geographic extent belongs in a separate map set and is manually placed into the shared overworld for now.
- Automatic neighboring-map alignment through landmarks, roads, or other visual features is future Surveyor work and is not implied by the current UI.

## Post-deployment browser acceptance

Use disposable/recreated test worlds only. Do not modify a human tester's active saved world for acceptance.

### 1. Upload visibility and navigation

1. Create a disposable overworld with no expedition.
2. Upload a PNG/JPEG/WebP map image without aligning it.
3. Confirm the image is immediately visible with temporary centered placement.
4. Confirm normal left-drag pans the map and wheel scrolling zooms it.
5. Reload before saving alignment and confirm the temporary placement was not persisted as authoritative alignment.

### 2. Humblewood canonical one-click alignment

1. Upload `Humblewood_Hex.png` and mark it as including a visible hex grid.
2. Click **Detect grid** / **Detect / repair hex grid** once.
3. Confirm the initiating button shows analysis progress and then applying progress without moving the user to another panel.
4. Confirm no separate Apply or confirmation dialog is required for a canonical result.
5. Reload the page and confirm alignment persists.
6. Inspect the center, all four map corners, and several intermediate distant regions. The mathematical grid must remain phase-aligned with the printed image grid rather than merely appearing close near the center.
7. Inspect at multiple viewport zoom levels. Zoom must change display scale only, not saved image/grid alignment.
8. If retained Wonderdraft source is imported, confirm exactly 2,616 records remain available: 86 labels, 2,510 symbols, 19 paths, and 1 territory.
9. Re-import the identical project and confirm retained records are not duplicated and the saved grid alignment is preserved.

### 3. Rejection of an untrusted automatic fit

1. Use an image/result that is gridless, inconclusive, downscaled-only, or otherwise fails the canonical source-fit policy.
2. Run **Detect / repair hex grid**.
3. Confirm Hex Crawl does not persist the proposed world grid or map alignment.
4. Confirm the map image remains visible.
5. Confirm the error explains that automatic alignment could not be trusted and offers **Manual placement** without requiring the user to interpret confidence/residual mathematics.

### 4. Explicit repair of a bad saved alignment

1. In a disposable world, create or reproduce a visibly bad affine placement for a grid-bearing reference map.
2. Use **Repair grid alignment** rather than deleting/re-uploading the image.
3. Confirm one click analyzes and applies a canonical repair.
4. Reload and reconfirm the map image asset, retained imported records, provenance, archive, and unrelated authored world records remain intact.

### 5. Magnostephis mismatch

1. Upload `Magnostephis.png` as a grid-bearing reference image.
2. Run generic grid detection and confirm a canonical pointy-top result near 43 px center spacing can persist without requiring Wonderdraft.
3. Attempt to attach the known 2107×1536 Wonderdraft project to the 1403×1026 image.
4. Confirm Hex Crawl reports the project/image relationship as unresolved and does not silently create an independent X/Y stretch.
5. Confirm image-grid alignment remains logically independent from that structured-source mismatch.

### 6. Bellowing Wilds generic image

1. Upload `FG_TheBellowingWilds_GMGridMap.png` with no Wonderdraft project.
2. Run one-click grid alignment.
3. Confirm a canonical flat-top fit near 132–133 px center spacing persists.
4. Confirm the world physical distance per hex is unchanged because there is no separate scale source.
5. Reload and inspect center and corners for phase stability.

### 7. Map-set coordinated views

1. Create one map set with GM/grid, GM/no-grid, Player/grid, Player/no-grid versions when available, plus an auxiliary numbered/keyed reference.
2. Switch Map view among the four GM/Player combinations.
3. Confirm every map set switches to the requested matching version where present.
4. Confirm a missing version is reported rather than replaced with an unsafe audience fallback.
5. Toggle the auxiliary reference while a coordinated view is active. Confirm the auxiliary visibility changes without exiting the coordinated GM/Player view.
6. Return to Manual visibility and confirm the prior manual GM/Player visibility state is restored.

### 8. Separate regional maps and Manual placement

1. Add a neighboring region as a different map set.
2. Confirm it appears immediately even before alignment.
3. Use **Manual placement** with three shared landmarks when trustworthy common points are available.
4. Confirm saved placement persists after reload.
5. Do not expect automatic landmark/road matching; that capability is explicitly deferred.

### 9. Expedition safety

1. Create an expedition in a disposable world after a reference map is present.
2. Attempt a repair that would change grid geometry.
3. Confirm the operation is rejected and neither the grid nor selected map-image alignment is partially changed.

## Acceptance boundary

A green branch validation plus the fixture measurements above establishes implementation readiness for browser acceptance. It does **not** establish that the affected human tester is unblocked. That conclusion requires the deployed build to pass the upload-visibility, one-click alignment, map-set/view, pan/zoom, reload, and distant-region alignment checks above.

## Phase 13 shared-processing ownership

Hex-lattice detector correctness and encoded PNG/JPEG/WebP preprocessing are owned by Dorks & Dice Surveyor. Hex Crawl retains alignment proposal, automatic-apply trust policy, physical-scale interpretation, Wonderdraft integration, map rendering, optimistic concurrency, and persistence. Surveyor observations never directly mutate Hex Crawl world state.
