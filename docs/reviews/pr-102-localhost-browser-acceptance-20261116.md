# PR #102 localhost browser acceptance review

**Recommendation: Incomplete.** Required browser acceptance could not be performed because the graphical browser and application command runtime are in separate environments with no verified localhost connection. This is an environment blocker, not a demonstrated application defect. Keep the PR in draft pending a complete browser review.

## Implementation and environment

- Review date: November 16, 2026 (America/New_York).
- Repository: `LegendMaster03/dorks-and-dice-hex-crawl`.
- Branch: `feature/hierarchical-tiling-catalog-pentagons`.
- Source commit: `62f05012e14f9c6f62c77cace0ffbd98c922539e`.
- Initial working tree: clean. No existing checkout or uncommitted user work was overwritten.
- PR metadata independently confirmed: open, draft, unmerged, and matching the supplied head.
- Server endpoint reported by Kestrel: `http://0.0.0.0:38315`; attempted browser URL: `http://127.0.0.1:38315`.
- Command runtime: Debian 13, .NET SDK 10.0.404 installed locally, Node/npm available.
- Command-runtime Chromium reports version 156.0.8078.79. The graphical browser is a separate Chromium instance; its exact version was not verified.
- Database: disposable PostgreSQL 17.11, local TCP only, database `hex_crawl_review`, temporary cluster `/tmp/hex-review-pg`, port 42405. This differs from CI's PostgreSQL 18 image.
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

## Environment blocker ENV-01

**Classification:** environment blocker; prevents required acceptance.

Reproduction and evidence:

1. Build the exact feature commit and start the isolated database and application as above.
2. Open the actual graphical Chromium window and navigate to `http://127.0.0.1:38315`.
3. Browser visibly displays: “This site can’t be reached,” “127.0.0.1 refused to connect,” and `ERR_CONNECTION_REFUSED`.
4. A direct supporting request from a separate command invocation also receives connection refused, while the original application session reports a running listener.
5. Launch the desktop terminal and attempt `bash /tmp/hex-start-review.sh`, a startup script created in the command workspace.
6. Desktop terminal reports `No such file or directory`. Its host prompt differs from the command environment, corroborating that these are separate environments rather than a shared local filesystem and loopback.

Expected: the graphical browser reaches the feature-branch application and permits end-to-end interaction.
Actual: no application document loads. No application console or frontend network evidence can therefore be collected.

A browser reload after a verified connection was not possible. No application world was created. No screenshot artifact was saved; the error page and terminal error were directly observed in graphical screenshots during the review.

**Recommended environment correction:** run the checkout, .NET application, disposable PostgreSQL, and graphical browser in one shared environment, or provide a supported private forwarding route from the browser environment to this application. Verify readiness and current assets through that route before restarting acceptance. Do not substitute the public deployment.

## Fifteen-family acceptance matrix

**Blocked** means not executed in the application browser. It does not mean the family failed.

| Family | Preview | World creation | Adjacency / movement | Persistence |
|---|---|---|---|---|
| Type 1 | Blocked | Blocked | Blocked | Blocked |
| Type 2 | Blocked | Blocked | Blocked | Blocked |
| Type 3 | Blocked | Blocked | Blocked | Blocked |
| Type 4 | Blocked | Blocked | Blocked | Blocked |
| Type 5 | Blocked | Blocked | Blocked | Blocked |
| Type 6 | Blocked | Blocked | Blocked | Blocked |
| Type 7 | Blocked | Blocked | Blocked | Blocked |
| Type 8 | Blocked | Blocked | Blocked | Blocked |
| Type 9 | Blocked | Blocked | Blocked | Blocked |
| Type 10 | Blocked | Blocked | Blocked | Blocked |
| Type 11 | Blocked | Blocked | Blocked | Blocked |
| Type 12 | Blocked | Blocked | Blocked | Blocked |
| Type 13 | Blocked | Blocked | Blocked | Blocked |
| Type 14 | Blocked | Blocked | Blocked | Blocked |
| Type 15 | Blocked | Blocked | Blocked | Blocked |

## Remaining browser checks

| Required check | Result |
|---|---|
| Feature assets and catalog API verified in loaded application | Blocked |
| World creation and initial reload | Blocked |
| Nested categories and numerical family ordering | Blocked |
| Existing visibility rules and lazy previews | Blocked |
| Selection state, focus preservation, and keyboard navigation | Blocked |
| Narrow viewport, clipping, and overflow | Blocked |
| Type 2 versus Type 4 independent selection and distinct geometry | Blocked |
| Type 2 versus Type 4 save/reload and ruleset ambiguity | Blocked |
| Finite bounds, edge/corner exits, and reciprocal movement | Blocked |
| Hexagonal regression | Blocked |
| Square regression | Blocked |
| Fallout 5×5 regression | Blocked |
| Fallout 9×9 regression | Blocked |
| Triangular and mixed-polygon regressions | Blocked |
| Preview culling, Type 15, zoom, pan, and interaction delays | Blocked |
| Application console errors and failed API requests | Unavailable: application never loaded |

## Limited implementation inspection

Inspected the branch diff summary and the category component, category/identity model, world picker changes, and catalog endpoint changes as supporting diagnostics. This is not a completed independent implementation review.

The inspected code uses preset identity to distinguish catalog choices, passes explicit topology and metric realization during world creation, avoids claiming a unique ruleset choice for ambiguous D-symbols, lazily populates categories, and updates pressed state without rebuilding the world picker. These are source observations only; their browser behavior and persistence remain unverified.

The change summary shows catalog fixtures and shared presentation changes. No need for a pentagon-specific runtime was established. No application defect was confirmed within this limited review, which must not be read as evidence that none exist.

## Disposition

**Incomplete.** Acceptance has not passed. Required next work is the full graphical matrix and regressions after resolving ENV-01.

Only this report is to be committed to the existing feature branch. No gameplay fixes, engine changes, merge, deployment, main-branch modification, or production-data changes are part of this review.
