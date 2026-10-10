# Phase 16 — live-data acceptance and non-destructive release runbook

**Status: release NO-GO until the existing-record checks have actual signed-in evidence and release authorization is confirmed.**
This is an operational checklist, not permission to merge, deploy, migrate, reset,
create test data in production, or roll back production. Only the real owner or
an authorized operator should perform the signed-in checks; do not put session
cookies, service tokens, user data, or map binaries in public CI logs.

## 1. Evidence already confirmed without accessing tester records

- [Isolated old/new compatibility and rollback run 38015306142](https://github.com/LegendMaster03/dorks-and-dice-hex-crawl/actions/runs/38015306142)
  passed all four container combinations, with positive v2 detection and read-only
  v3 capability behavior. It used disposable PostgreSQL and image assets.
- The most recent successful **main-branch production deployment workflows**
  before this release attempt were:
  - Surveyor [run 37877969080](https://github.com/LegendMaster03/dorks-and-dice-surveyor/actions/runs/37877969080),
    building `e9ad1cc9f04ce375fa1baf241894357abc116e09`; deployment and
    post-deploy readiness verification passed October 8, 2026 (US/Eastern).
  - Hex Crawl [run 37889199540](https://github.com/LegendMaster03/dorks-and-dice-hex-crawl/actions/runs/37889199540),
    building `afb4760f4ea30366f3516e307e6335a1b6edd12c`; deployment and
    post-deploy readiness verification passed October 9, 2026 (US/Eastern).
- These exact baseline revisions are the old images pinned in the isolated
  compatibility matrix. Successful Actions deployment history is not proof
  of the exact container IDs **currently** running on the host.
- All feature work remains on `feature/tile-crawl-phase-16`; the production
  image safeguards below will only take effect after an authorized merge.

## 2. Pre-deployment live acceptance — signed-in, existing records only

Capture the following evidence using the *existing* authenticated Tool Host UI
or its already-authenticated, documented GET endpoints, before any release:

1. Confirm access through the normal signed-in Site -> Hex Crawl Tool flow;
   do not enable standalone identity or work around the Tool Host.
2. Choose an existing tester world and note its stable ID, version, source-map
   count and identifiers. Reload the page and verify those values did not change.
   Do not create a new world, rename a record, or save.
3. Open a previously uploaded source map. Verify that its image is present,
   matches its saved metadata (dimensions/role/format), and remains aligned.
   Do not re-upload, register, realign, convert, or delete it.
4. Open an existing procedure configuration and confirm its content survives
   a reload. Do not open an editor that autosaves or submit a change.
5. Open an existing expedition, confirm its ID, watch/progress, route and
   procedure reference, then reload and compare. Do not advance, undo,
   reset, or submit any action.
6. If expressly approved for a non-mutating analyzer request, invoke the
   existing v2 `grid-analysis` operation on that same authorized map once.
   It must return a typed v2 result or an explicit bounded failure, and the
   world version, source-map alignment and expedition must remain unchanged.
   **Do not use the v3 candidate as authoritative map geometry.**
7. Record pass/fail and check time for each record, with private identifiers
   in the operator's private release notes. Repeat after any authorized
   deployment; verify the same IDs, counts, versions and binary assets.
   Avoid placing user-identifying values in this repository.

If no signed-in browser/session access is available, this gate is **NOT TESTED**,
not passed. Public homepage availability and CI green status do not substitute
for it.

## 3. Confirm actual running images — read only

On the deployment host, an authorized operator may run the following commands
without stopping or modifying a container:

```bash
docker inspect --type container --format '{{.Name}} running={{.State.Running}} image={{.Image}}' dorks-and-dice-surveyor
docker inspect --type container --format '{{.Name}} running={{.State.Running}} image={{.Image}}' dorks-and-dice-hex-crawl
docker image inspect --format '{{.Id}} labels={{json .Config.Labels}}' "$(docker inspect --format '{{.Image}}' dorks-and-dice-surveyor)"
docker image inspect --format '{{.Id}} labels={{json .Config.Labels}}' "$(docker inspect --format '{{.Image}}' dorks-and-dice-hex-crawl)"
```

The Phase 16 deploy workflows label newly built images with
`org.opencontainers.image.revision` and verify the expected commit on
the running container. Legacy images may lack that label. If missing,
retain the actual image ID and compare it with trusted deployment/build
logs; do not infer identity from a mutable `:latest` tag.

## 4. Authorized release order and preservation constraints

Perform only after the explicit release decision and after the signed-in
baseline has been recorded. Back up existing PostgreSQL and image volumes
using the established platform backup process before deployment; **never**
drop, truncate, reset, re-seed, or recreate production data.

1. Upgrade **Surveyor first** while deployed Hex Crawl is still old. The
   isolated old-client/new-provider pairing passed, and v2 remains its
   existing supported request path.
2. Confirm Surveyor liveness/readiness, authenticated v2 behavior through
   Hex Crawl, and signed-in existing-world/map/expedition reloads.
3. Upgrade **Hex Crawl second**. The matrix passed the new/new pairing and
   the isolated rollback to the old client with the same retained data.
4. Confirm new client/UI entry and the exact same existing-record state
   without producing or saving a generalized candidate. Phase 16 v3 is
   opt-in, experimental and explicitly non-authoritative.
5. If any invariant fails, stop rollout. Do not compensate by resetting
   the database, removing named volumes, changing user ownership, or
   translating candidate D-symbols into map geometry.

No Phase 16 database migration or source-map binary transformation is
required.

## 5. Image retention, failed-deploy rollback and operator limits

The feature-branch deployment workflows now capture the **currently running**
production container image **before** overwriting `:latest`, tag it
`dorks-and-dice-{service}:pre-deploy`, and build the new image under
both `:latest` and its immutable full Git commit SHA. Production deploys are
serialized rather than cancelled mid-recreation. For these established
services, **failure to capture the currently running image aborts the deployment
before any build or image-tag mutation**. The missing-service state requires
an operator to investigate and authorize a specific recovery procedure; it is
not interpreted as a routine first deployment.

If deploy or post-deploy verification fails, the workflows attempt to
recreate the previous image using the same Compose project, environment,
external network and persistent volumes; they then check readiness and
the restored container's exact image ID. The GitHub run remains failed even
when restore succeeds. A failed rollback requires immediate human intervention
and must not be described as safe recovery. The [isolated deployment rehearsal](https://github.com/LegendMaster03/dorks-and-dice-hex-crawl/actions/runs/38016975981)
executed the workflow's actual capture, deploy, verify and restore shell blocks
against disposable Compose containers for both services. Healthy rollout,
failed-readiness restoration, wrong-revision restoration, an actual Compose
startup failure after the old container was removed, missing-prior-image
handling and retained-volume file checks passed. Each service passed all five
rehearsal cases. These automatic branches
have **not** been exercised against production.

For a **manual** rollback, an operator must first establish that
`:pre-deploy` points to the correct previously healthy image and that
the current database/schema remains backward-compatible. The normal
workflow uses only stable named volumes; never remove those volumes.
Do not run an operator rollback without explicit authorization.

The `:pre-deploy` tag contains only the immediately prior running image,
not historical backups. Preserve external database/asset backups separately,
and verify the deployed Git revision and the post-rollback existing-record
checks before declaring recovery.

## 6. Sign-off

Do not mark this phase production-ready without a recorded signed-in
existing-world, source-map, procedure and expedition reload; the running-image
identity check; observed post-deployment v2 behavior; and an operator's
review of database, volume and image rollback readiness. Green synthetic
detection and four-way isolated CI are already established but are not
a substitute for these production-only checks.
