# PR #102 localhost browser acceptance review

**Recommendation: Incomplete.** The retry resolved localhost access and completed a partial graphical catalog review. Authentication prevents world creation, movement, and persistence testing. A minor preview-framing defect is recorded below. Keep the PR in draft pending complete acceptance.

## Implementation and environment

- Review date: November 16, 2026 (America/New_York).
- Repository: `LegendMaster03/dorks-and-dice-hex-crawl`.
- Branch: `feature/hierarchical-tiling-catalog-pentagons`.
- Source commit: `62f05012e14f9c6f62c77cace0ffbd98c922539e`.
- Initial working tree: clean. No existing checkout or uncommitted user work was overwritten.
- PR metadata independently confirmed: open, draft, unmerged, and matching the supplied head.
- Initial endpoint: `http://0.0.0.0:38315` (unreachable from the browser). Retry endpoint: **`http://127.0.0.1:5099/worlds`**, reached in graphical Chromium.
- Command runtime: Debian 13, .NET SDK 10.0.404 installed locally, Node/npm available.
- Command-runtime Chromium reports version 156.0.8078.79. The graphical browser is a separate Chromium instance; its exact version was not verified.
- Database: disposable PostgreSQL 17.11, local TCP only, database `hex_crawl_review`. Retry uses the browser machine's `/tmp/hex-desktop-pg` cluster on port 55433. This differs from CI's PostgreSQL 18 image.
- No production database, shared deployment, or persistent user data was used.

## Setup and supporting verification

Read `README.md`, `docker-compose.yml`, `src/HexCrawl.Web/HexCrawl.Web.csproj`, `.github/workflows/validate.yml`, and startup/authentication code.

The environment initially lacked .NET, PostgreSQL, and Docker. Installed .NET from Microsoft's installer and extracted PostgreSQL packages from the configured Debian package source into temporary directories. No application source changes were made.

Commands used for dependency/build verification:

```bash
# In src/HexCrawl.Web/Client:
npm install --no-audit --no-fund
npm run build

# At the repository root:
/tmp/hex-dotnet/dotnet restore src/HexCrawl.Web/HexCrawl.Web.csproj \
  --disable-parallel -m:1 -nr:false -p:BuildInParallel=false
/tmp/hex-dotnet/dotnet build src/HexCrawl.Web/HexCrawl.Web.csproj \
  --no-restore -p:BuildClient=false -m:1 -nr:false \
  -p:BuildInParallel=false -p:UseSharedCompilation=false
```

Results:

- Frontend: **343 tests passed, zero failed**, TypeScript checking passed, Vite build succeeded.
- Backend: build succeeded with **zero warnings and zero errors**.
- Full .NET test suite and PostgreSQL integration suite: **not run**.
- Initial default MSBuild invocations failed with zero reported compiler errors; serial restore/build succeeded. This was not treated as an application defect.
- PostgreSQL's initial Unix socket creation was denied by the command environment. Disabling Unix socket creation and using TCP allowed database startup.
- Launched database and application within the same command session because separate command sessions did not share a reachable loopback listener.

Actual application startup configuration:

```bash
cd src/HexCrawl.Web
ConnectionStrings__HexCrawl='Host=127.0.0.1;Port=42405;Database=hex_crawl_review;Username=agent' \
ASPNETCORE_URLS='http://0.0.0.0:38315' \
ASPNETCORE_ENVIRONMENT=Development \
/tmp/hex-dotnet/dotnet bin/Debug/net10.0/HexCrawl.Web.dll
```

The disposable PostgreSQL cluster used local trust authentication and no external listener. No standalone application identity or authentication bypass was enabled.

Observed application output:

```text
Now listening on: http://0.0.0.0:38315
Application started. Press Ctrl+C to shut down.
Hosting environment: Development
Content root path: /workspace/scratch/ce13fe7aeef6/hex-crawl/src/HexCrawl.Web
```

These logs establish server startup, not graphical access, API correctness, or acceptance.

## Retry: ENV-01 resolved

The earlier conclusion about filesystem separation was too broad. The browser machine shares `/workspace` with the command workspace, while its `/tmp` and loopback are separate.

Copied the downloaded .NET and PostgreSQL runtimes into `/workspace/scratch/ce13fe7aeef6/local-review-runtime/`, then launched the existing compiled application and a fresh disposable database through the graphical desktop terminal:

```bash
bash /workspace/scratch/ce13fe7aeef6/local-review-runtime/start.sh
```

The script starts PostgreSQL on 127.0.0.1:55433, creates `hex_crawl_review`, and runs the feature-build DLL from the original checkout with:

```text
ConnectionStrings__HexCrawl=Host=127.0.0.1;Port=55433;Database=hex_crawl_review;Username=agent
ASPNETCORE_URLS=http://127.0.0.1:5099
ASPNETCORE_ENVIRONMENT=Development
```

The application loaded in actual graphical Chromium at `http://127.0.0.1:5099` and its Worlds / maps view loaded the new catalog. Source checkout remains at `62f05012e14f9c6f62c77cace0ffbd98c922539e`; the prior report commit does not change application code. No public deployment or mock catalog was used.

## Current environment blockers

### ENV-02: authenticated world operations unavailable

The browser shows: “Your Dorks & Dice session is no longer available. Refresh or sign in again, then retry.”

The desktop application log shows HTTP 401 for `GET /api/overworlds` and `GET /api/procedures`. The catalog remains accessible, so preview/category interactions could be reviewed. No world was created and no protected data was accessed.

The isolated standalone application has no configured Site host or interactive sign-in screen. The repository documents a standalone development identity, but it was not enabled because the acceptance assignment requests interactive sign-in rather than bypassing authentication. Completing protected tests requires an authenticated local environment or separate authorization to use that documented identity in this disposable review.

This is a setup blocker, not a demonstrated authentication defect.

### ENV-03: browser DevTools blocked

Attempted Ctrl+Shift+J. Chromium displays “DevTools not allowed on this page” and “Your organization has blocked the use of DevTools on this page.” Dismissed the dialog without bypassing the restriction. Console inspection remains unavailable. HTTP status evidence above comes from the actual application log.

## Browser finding UI-01: previews do not frame recognizable tiles

- **Severity:** minor.
- **Component:** shared tiling preview; clearest examples Types 1, 10, and 11.
- **Steps:** open `/worlds`, expand Change tiling → Pentagons → Convex, inspect those thumbnails at 100% zoom. Also observed at 75% zoom.
- **Expected:** previews show recognizable pentagonal tiles and enough surrounding geometry to compare the patterns.
- **Actual:** Type 1 shows a cropped diagonal fragment. Types 10 and 11 show predominantly horizontal/vertical edge fragments, without a complete recognizable pentagon. The canvases are populated, but the framing does not satisfy the requested recognizable-pentagon preview check.
- **Evidence:** directly observed graphical screenshots at 100% zoom in the 1050-pixel-wide browser window; no screenshot file was saved.
- **Reload reproducibility:** not tested.
- **Confirmed source observation:** `tiling-preview.ts` sets scale using the shortest polygon segment, `Math.min(22 / side, 220)`, then clips to a 74×52 canvas.
- **Likely cause (hypothesis):** a short side or atomic boundary subdivision can drive magnification high enough to crop the actual tile shape. This does not establish invalid world geometry or a defect in the off-screen culling logic.
- **Recommendation:** adjust shared preview framing to fit at least one complete tile and useful adjacent context, while respecting the actual geometry. No family-specific renderer is needed.

## Fifteen-family acceptance matrix

**Visible** means its populated thumbnail was individually observed in the actual catalog; full geometry/alignment acceptance remains unverified. **Partial / UI-01** records the preview-framing issue. **Blocked** means the authenticated test was not performed.

| Family | Preview | World creation | Adjacency / movement | Persistence |
|---|---|---|---|---|
| Type 1 | Partial / UI-01 | Blocked: auth | Blocked: auth | Blocked: auth |
| Type 2 | Visible; full acceptance pending | Blocked: auth | Blocked: auth | Blocked: auth |
| Type 3 | Visible; full acceptance pending | Blocked: auth | Blocked: auth | Blocked: auth |
| Type 4 | Visible; full acceptance pending | Blocked: auth | Blocked: auth | Blocked: auth |
| Type 5 | Visible; full acceptance pending | Blocked: auth | Blocked: auth | Blocked: auth |
| Type 6 | Visible; full acceptance pending | Blocked: auth | Blocked: auth | Blocked: auth |
| Type 7 | Visible; full acceptance pending | Blocked: auth | Blocked: auth | Blocked: auth |
| Type 8 | Visible; full acceptance pending | Blocked: auth | Blocked: auth | Blocked: auth |
| Type 9 | Visible; full acceptance pending | Blocked: auth | Blocked: auth | Blocked: auth |
| Type 10 | Partial / UI-01 | Blocked: auth | Blocked: auth | Blocked: auth |
| Type 11 | Partial / UI-01 | Blocked: auth | Blocked: auth | Blocked: auth |
| Type 12 | Visible; full acceptance pending | Blocked: auth | Blocked: auth | Blocked: auth |
| Type 13 | Visible; full acceptance pending | Blocked: auth | Blocked: auth | Blocked: auth |
| Type 14 | Visible; full acceptance pending | Blocked: auth | Blocked: auth | Blocked: auth |
| Type 15 | Visible; full acceptance pending | Blocked: auth | Blocked: auth | Blocked: auth |

## Browser interactions and remaining checks

| Check | Result |
|---|---|
| Feature catalog in loaded application | New categories and all 15 entries visible |
| Nested categories and numerical ordering | Observed Pentagons → Convex, Types 1–15 in order |
| Category expansion and selection | Expanded Hexagons and Pentagons/Convex; selecting choices retained the open tree |
| Type 2 versus Type 4 | Clicked Type 2, then Tab twice and Enter selected Type 4; distinct previews and labels; prior choice lost its pressed styling |
| Keyboard focus | Visible focus remained on Type 4 after Enter; limited to this sequence |
| Type 15 | Selected Type 15; selected summary label and preview updated |
| Initial hexagonal choice | Default hexagonal summary and preview rendered |
| Other categories | Quadrilaterals, Squares, and Triangles visible; individual entries not reviewed |
| Lazy rendering / screen-reader labels | Not established by graphical inspection |
| Responsive layout | Observed at 1050-pixel window width and 100%/75% zoom; narrow viewport not tested |
| Ruleset authoring | Not tested under authenticated identity |
| Type 2/4 world geometry and reload | Blocked: authentication |
| All family worlds, adjacency, movement, persistence | Blocked: authentication |
| Finite bounds and all existing-grid runtime regressions | Blocked: authentication |
| World pan, zoom, reload performance | Blocked: no test world |
| Browser console | Blocked by DevTools policy |
| Performance | No measured timing claim; expanded catalog remained interactive |

## Limited implementation inspection

Inspected the branch diff summary and the category component, category/identity model, world picker changes, catalog endpoint changes, and shared preview scale/clipping implementation as supporting diagnostics. This is not a completed independent implementation review.

The inspected code uses preset identity to distinguish catalog choices, passes explicit topology and metric realization during world creation, avoids claiming a unique ruleset choice for ambiguous D-symbols, lazily populates categories, and updates pressed state without rebuilding the world picker. These are source observations only; their browser behavior and persistence remain unverified.

The change summary shows catalog fixtures and shared presentation changes. No need for a pentagon-specific runtime was established. UI-01 is a preview usability finding; no world geometry or movement defect has been established.

## Disposition

**Incomplete.** Localhost access is resolved. Acceptance has not passed. Required next work is the authenticated world matrix, ruleset checks, and regressions after resolving ENV-02. ENV-03 also limits console evidence. UI-01 should be reviewed through the shared preview renderer.

Only this report is to be committed to the existing feature branch. No gameplay fixes, engine changes, merge, deployment, main-branch modification, or production-data changes are part of this review.
