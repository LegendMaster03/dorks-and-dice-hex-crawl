# Hex Crawl — map editor tester-feedback acceptance checklist

This is the branch acceptance plan for PR #96. Test on the deployed **feature
branch**, not an earlier deployment of `main`. Use a test world that can be
discarded. Do not include undisclosed campaign details in evidence captures.

## 1. Geometry authoring and validation

1. Create a point, set a name and category, choose **Author geometry on map**,
   click its position, and save. Confirm a temporary point marker appears
   before saving and the saved point remains afterward.
2. Create a line. Verify **Save feature** is disabled without two vertices;
   author one, then two, and verify the dashed draft appears before saving.
   Save it; edit only its name and save again **without redrawing geometry**.
3. Create a region. Verify three vertices are required, the draft polygon is
   visible, and saving a nonzero-area polygon succeeds. Try three collinear
   vertices and verify a specific validation error instead of a generic
   `Bad request`.
4. Edit or delete features while other map features remain unchanged. Confirm
   selecting an existing line or polygon loads its original vertices.
5. Zoom and pan during authoring. Neither zooming out nor switching geometry
   types should produce a disappearing permanent map or accidental vertices.

## 2. Category colors and map readability

1. Author several terrain cells with tags such as `forest`, `water`,
   `urban`, and `rural`. Confirm the documented defaults color them
   automatically without modifying the actual terrain facts.
2. Set an alternate terrain category color in **Map appearance**. Verify all
   cells of that category update. Reload the page and check the preference
   persists. Reset the override and verify the default returns.
3. Set color overrides on a line/point/region feature category and on
   **Text labels** for a feature or location category. Verify independent
   colors and readable outlines at different zoom levels.
4. Confirm regional fills, line strokes, and point colors are separate from
   world mechanics, movement permissions, and selected-cell environment facts.

## 3. PNG export and offline inspection

1. Export the current visible area with **Light**, **Dark**, and **Transparent**
   backgrounds. Open each in an external image viewer (not only the app).
   Check that grid outlines and labels are readable against the chosen color.
2. While authoring unsaved geometry, export the visible area; the dashed draft,
   active cell selection, and temporary registration previews must not appear
   in the exported image.
3. Export a finite square map (e.g. a 5×5 and a 9×9) and a finite hex map as
   **Full bounded map**. Change the browser zoom and pan first; the output
   should still show the same complete geometry and preserve aspect ratio.
4. Choose 1600, 2400, and 3200 maximum image dimensions. Confirm the output
   sizes correspond to the selected limits without cropping.
5. Repeat a full-map export with **Draw tile grid** unchecked and **Draw labels**
   unchecked. Terrain fills must remain present with the grid hidden.
6. With an unbounded map, verify that full-map export is unavailable while
   visible-area export remains usable.
7. Include an aligned source image and confirm it appears at its saved
   registration. Try an unaligned image and verify that export explains why
   its position is not exportable rather than silently omitting it.
8. Open saved PNGs without an internet connection. No application or remote
   assets should be needed to view them.

## 4. Labels and collision behavior

1. Label a line as a mountain range and a point as a bandit group. Confirm
   both names appear when allowed at the current scale.
2. Label a concave region; its name should be inside the region where
   practicable rather than on an arbitrary exterior point.
3. Create several tightly spaced objects and zoom. Confirm names do not
   overlap each other excessively; lower-priority labels may be suppressed
   without hiding their underlying geometry.

## 5. GM/player release controls

1. Create two locations and several points, lines, and regions, including one
   hidden location. Before making release selections, export a **Players** PNG
   and confirm the geometry and names of those objects are absent.
2. Release only the geometry of one point/line/region. Confirm the player
   export shows that geometry without its unreleased name or category.
   Enable its label separately and confirm the intended name appears.
3. Repeat for a location, then hide its geometry. The label selection should
   be cleared automatically.
4. Enable terrain colors and disable grid strokes. Confirm the player PNG
   still shows allowed terrain fills. Disable terrain and verify these fills
   disappear.
5. Upload source maps assigned `GM`, `Player`, or `Neutral` roles. Only a
   `Player`-role map explicitly selected for release should appear in a player
   export. Note that this does **not** automatically remove secrets baked
   into that image.
6. Remove a released feature/location or change a released Player source image
   to a different role. Verify the release configuration remains consistent.
7. Use **Hide all player details**. Confirm the next player export has no
   released markers, labels, terrain facts, or source images.
8. Verify the original GM map is unchanged by creating a player PNG.
   Export a GM PNG and confirm it includes all authored content.
9. With two distinct users, confirm the other user can not fetch the owner's
   raw world, source-map assets, or owner-only player-preview API.

## Automated validation

GitHub Actions checks frontend tests, TypeScript, .NET builds, domain tests,
HTTP integration/PostgreSQL persistence, and deployment smoke/backup flows.
The automated tests cover geometry invariants and static security projection,
but are **not a substitute for manual browser and external-viewer checks**.
A final browser acceptance review is required before merging.

**Release condition:** Leave PR #96 in draft until owner review. Do not merge,
deploy to production, or grant public access solely because CI is green.
