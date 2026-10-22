# Map editor: appearance, labels, and PNG export

This feature is presentation-only. It does not alter semantic terrain annotations,
world geometry, movement rules, discoverability, or other game mechanics.

## Editor

- A selected cell's terrain is still stored as an ordinary typed `terrain` fact
  under a qualified, server-authoritative cell identity.
- The Map appearance section configures separate per-category colors for terrain
  fills, feature geometry (lines, regions, and points), and text labels.
  Defaults are applied only by the client renderer. Color overrides belong to
  the overworld aggregate and use its existing owner-scoped, versioned write path.
- If a cell has conflicting terrain tags, the renderer does not pick an
  arbitrary color. The underlying facts remain intact.
- Named feature labels use existing semantic `Name` fields; no parallel names
  or geometry are stored. Location labels take priority in placement, followed
  by points, lines, and regions. Collisions may hide a lower-priority label at
  the current scale without hiding the feature.
- Client-side checks require a point position, two line vertices, or three
  region vertices enclosing non-zero area before saving. Selecting an existing
  feature preserves its geometry when editing only the name or category.

## Image export

The export form supports:

- **Visible area:** captured from the current map viewport after querying
  authoritative visible cell geometry, rather than trusting an in-flight UI fetch.
- **Full bounded map:** independent offscreen render of a finite map's complete
  authoritative polygon geometry, using a version-pinned paginated API. Available
  maximum image dimensions are 1600, 2400, and 3200 pixels; aspect ratio is
  preserved.
- **Background:** light (default), dark, or transparent.
- **Full-map overlays:** include/exclude tile grid and text labels.

The full-map endpoint returns at most 500 cells per request and refuses worlds
larger than 20,000 finite cells. Worlds without finite bounds can export their
visible viewport but not an invented full world extent.

Export waits for visible source-image layers to load. Full-map export requires
their saved affine registrations (or that the layer be hidden), because a
provisional viewport-centered placement is not a reliable global position.
Export does not silently omit missing images or incomplete cell batches.

The renderer uses the same topology and world-space geometry as the live map;
no rule-system-specific hex/square drawing engine is introduced.

## Security and next phase

**PNG exports from the world editor are GM exports.** They may show hidden
locations or unpublished spatial features. Do not share them with players
when secrets are present.

Player-safe projection is a separate authorization feature, not just a canvas
visibility toggle. A future implementation must filter the owner-scoped
world/knowledge payload and image assets appropriately before offering a
player-export mode. This branch does not claim player-safe exports.

The appearance model is an additive optional world JSON field; the existing
PostgreSQL schema and map assets are unchanged. No destructive migration is
required.

## Regression coverage

- Browser tests cover palette defaults and overrides, the shared encoder,
  finite geometry requests, feature names, and client-side validation.
- Domain tests cover normalization, rejecting invalid colors, and JSON round trip.
- HTTP integration tests cover hex, square, and mixed tilings; pagination,
  complete exported geometry, optimistic concurrency, owner isolation, and
  rejection of unbounded or oversized full-map requests.
