# Phase 16 — independent-review remediation handback

**State: implementation changes submitted for independent re-review;
release remains NO-GO.** Do not merge or deploy without authorization.
The production v2 workflow, database schema (version 9) and existing
tester records remain untouched. The independent review was performed
against Hex Crawl `78749ccac40df7ad08b141abe809a06759ad5bc0`
and Surveyor `b93c738a8c0c40a192dcff1c19a8e4df5f63ffe6`.

## Findings and remediation

| Review ID | Changed implementation | Regression evidence / caveat |
| --- | --- | --- |
| F1 — triangle/mixed rejected | Hex `ObservedRasterMotifGeometryValidator.cs` now evaluates bounded **registered contour deviation** separately from global rigid-drift; parser requires the exact proof fields | Four-way real HTTP matrix includes triangle, mixed-1907, rotated/scaled mixed-2911; source image, world and stored expedition unchanged |
| F2 — incomplete confident proof | Surveyor `experimental-observer.ts` returns inconclusive when metric registration or independent unchanged-source projection fails; Hex `SurveyorPeriodicMotifInvestigationClient.cs` retains and validates complete metrics | Held-out tests assert every positive has registered metric and supported projection; isolated topology-only candidates no longer masquerade as qualified identities |
| F3 — forged scale | Hex parser checks source width/height against analysis width/height and scale even without full-resolution flag | Forged analysis scale with doubled translation vectors must be rejected |
| F4 — tiny valid square | Hex `PeriodicMetricWitnessValidator.cs` uses per-segment length-dimensional intersection predicates, no unit floor; adjusted area tolerance | Harmonic square metamorphic scales include 2e-6 and rotations, and other mathematical suites remain green |
| F5 — config-dependent rollback | Both deploy workflows use `.github/scripts/phase16-deployment-config.py` to snapshot exact running env/network/mount/restart state and fail closed if proposed config differs; deploy and restore use the frozen environment | Isolated real-Docker rollback matrix restores image and volume with host configuration intentionally still invalid; secrets never in CI logs/artifacts |
| F6 — preprocessing/queue bounds | Surveyor experimental v3 has its own 1-worker, zero-queue lane, a single admitted request prior to buffering, and an end-to-end deadline across upload, Sharp preparation and worker call | Tests assert concurrent v3 admission rejects with 503, existing separate v2 lane returns 200, and stalled experimental worker expires with 504 |

**Limits:** The v3 result is experimental and non-authoritative; these controls
do not prove broader natural image support or a calibrated identity
confidence. Image metadata decoding remains bounded by Sharp/input caps;
the abort signal is checked before/after metadata, propagated to the
decode pipeline and worker, and can terminate a stalled upload. Some
host-level errors (forced runner loss or cancellation, invalid actual
volume permissions) are not reproducible by the CI shell-step rehearsal.

## Scope disposition requiring reviewer/owner attention

PR #66 says **generalize the existing detector rather than replace it**.
The actual v3 path directly reuses v2 Sobel `buildEdgeField`, but
hex-specific 3-line Hough/autocorrelation, candidate pitch/phase fitting are
**not directly called**. The generalized displacement-vote basis/period
fitter and distant-region gates are new. This is a *limited deviation from
literal numerical-kernel reuse*, not a replacement of the deployed v2 path.
See Surveyor `docs/phase-16-detection-research.md` for a stage-by-stage
explanation and evidence. Ask the reviewer whether this satisfies the
intent; do not silently claim use of Hough kernels.

## CI and operational gates

The latest four-way compatibility matrix executes disposable HTTP services
and preserves PostgreSQL plus map-asset volumes for an actual old-client
reopen; the independent-review reproduction has been expanded beyond a
square to triangle, clean mixed and rotated mixed image evidence. The
deployment recovery rehearsal uses disposable services and retained
volumes; it does **not** validate the deployment host.

**Before authorization:** independently inspect actual revised branch
heads, rerun application tests, validate mixed v2/v3 HTTP saturation and
disconnect behavior, review runtime-snapshot security/cleanup and
failure modes, and obtain read-only actual tester data plus production
running-image/backups evidence. After *separately authorized deployment*,
repeat authenticated existing-record smokes. Automatic main deploy after
merge makes merge authorization a deployment-risk decision; do not merge
either repo on a CI-green conclusion alone.

## Second independent review — R1 and R2 remediation

The October 10 second independent review examined Hex
`d1a3c1a185283b87c9b6dc2359ac0386645b999b` and Surveyor
`503724c6812785f55a20587d5a4992bb46bbce5a`.
It independently confirmed that F1–F4 and F6 were resolved, but
identified two remaining **release blockers** in the deployment safeguards:

| Finding | Defect demonstrated by independent reviewer | Current feature-branch fix |
| --- | --- | --- |
| R1 — partial runtime comparison and candidate-dependent rollback | Mount access, published ports, and explicit command/entrypoint were not checked; failed recovery still consumed candidate `docker-compose.yml` | Strict bounded Compose allowlist, safe default handling, live-image command/entrypoint comparison, RW named-volume and no-host-port checks; after successful preflight copy the verified Compose definition privately and restore with the **retained** file while hostile checkout remains unchanged |
| R2 — cancellation cleanup deletes recovery snapshot | `always()` + `not failure` treated cancelled/skipped outcomes as safe | Explicit cleanup only for successful verify, successful verified rollback, or both deploy/verify skipped; named outcome semantics checked against actual workflow expressions with cancellation matrix |

The recovery design **intentionally fails closed** on new resource,
security, process and port configuration. This Phase 16 safety measure is
not intended to perform arbitrary authorized infrastructure changes. Any
necessary runtime config change requires an independent operator-approved
deployment procedure with its own recovery plan. The frozen environment
and Compose files can contain credentials; mode-0700/0600 restricted local
snapshot files must not be uploaded, logged or added to artifacts. Unknown
or cancelled outcomes retain the files for operator inspection.

The expanded [both-service real-Docker rehearsal](https://github.com/LegendMaster03/dorks-and-dice-hex-crawl/actions/runs/38024042740)
proves restoration with a hostile Compose **port binding, command or
read-only volume still present**; it also tests known-good environment
retention with a corrupted host env file, missing prior image, mismatched
revision, and persisted-volume contents. The exact cleanup and restore
predicates are tested for cancelled/unknown states by
`.github/scripts/phase16-deploy-outcome-contract.py`.

**Not established:** actual running production container versions,
credential and filesystem recoverability, actual runner cancellation
execution, force-lost host or storage, and signed-in existing tester
record survival after an authorized deployment. Existing production
systems and tester data were not modified. Merge/deploy remain NO-GO
until explicit authorization and the release runbook's live-operational
gates are met.

The second reviewer found the geometry detector changes technically
valid but identified a documented bounded deviation from literal
hex Hough/autocorrelation numerical-kernel reuse. The original Sobel
kernel is directly reused; generalized displacement-vote fitting
replaces the hex-specific pitch/phase fitter. Owner/reviewer scope
disposition remains explicit rather than implied by green CI.
