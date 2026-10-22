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

## Player visibility and static player exports

Map disclosure is a separate, versioned setting on the overworld. The owner can
explicitly release each location or spatial feature (point, line, or region),
and separately release its label. Terrain coloring and grid visibility are
independent settings. Source images are excluded unless their role is
`Player` **and** the owner explicitly releases them. All release lists and
terrain visibility default to empty/off.

The owner-only `player-map-preview` endpoint constructs a server-filtered
map contract. Unselected objects and their IDs, geometry, names, categories,
source-image metadata, and environment facts are removed before response
serialization. Geometry released without its label receives neutral
`Unlabeled` name and category values to avoid transmitting unreleased labels
or revealing category text. Only explicitly released cell terrain tags are
projected when the owner enables terrain. Non-terrain facts and private notes
or provenance are always excluded.

Player PNG export uses this filtered contract in an isolated offscreen
renderer, never the full live GM canvas. Player source images remain
owner-authenticated during rendering; the PNG is what the owner may then
distribute offline. The preview endpoint does **not** grant player accounts
access to the original world, map data, or source asset URLs.

**Selected raster images may have secret details permanently baked into their
pixels.** The owner must review the resulting PNG before sharing it. A source
map's `Player` role and explicit release are not proof that the image content
is harmless. Full-map and viewport export both support a filtered player copy;
unbounded worlds are limited to viewport export.

## Live expedition player display

The GM's spatial expedition workbench includes **Open player map**, which
opens a second, owner-authenticated, read-only browser window at
`/expeditions/{expeditionId}/player-map`. It refreshes every five seconds
while visible, and can be panned or zoomed without changing expedition state.
The GM continues managing watches, travel, and discoveries in the original
window. The player display itself contains no return link to GM controls.

The owner-only `GET /api/expeditions/{id}/player-map` endpoint composes the
player view on the server from the saved expedition's `PlayerKnowledgeState`
and `MapPresentationPolicy`, plus explicit static map disclosures:

- Discovered feature and location IDs become visible, including their names.
  Unreleased, undiscovered objects are omitted, and explicitly displayed
  objects without released labels have their names and categories redacted.
- Automatic hidden-until-known terrain reveals tags only for known,
  tiling-qualified cells. Always-visible terrain reveals all terrain tags.
  DM-controlled/manual terrain remains hidden without an explicit GM
  terrain-release override.
- Grid outlines are governed by both the expedition presentation policy
  and the explicit map grid setting.
- Source images require the `Player` role **and** explicit GM release.
  No GM source images, non-terrain facts, private notes, or runtime history
  enter the player-map response.
- The party marker is omitted until its location is known under the selected
  presentation mode.

This feature **does not introduce independent cross-account player access**.
The live display is launched by the authenticated world owner for second-screen
presentation. It remains protected by the established owner authorization;
there is no anonymous share URL or player token to expose GM maps. Granting
remote players access under their own logins requires a separate ownership
and sharing design, not a client-side visibility toggle.

The existing exploration/discovery mechanics are unchanged. This route reads
their persisted outcomes and does not mutate knowledge or rules.

Appearance and disclosure are optional fields in the version 3 PostgreSQL
world snapshot. Existing snapshots deserialize without those fields, using
empty appearance overrides and default-private disclosure. Updated snapshots
save both fields and restore them on restart. The SQL schema and map assets
remain unchanged; no destructive migration or database reset is required.
HTTP integration tests explicitly cover persistence across a new host instance.

## Regression coverage

- Browser tests cover palette defaults and overrides, the shared encoder,
  finite geometry requests, feature names, and client-side validation.
- Domain tests cover normalization, rejecting invalid colors, and JSON round trip.
- HTTP integration tests cover hex, square, and mixed tilings; pagination,
  complete exported geometry, optimistic concurrency, owner isolation, and
  rejection of unbounded or oversized full-map requests.
