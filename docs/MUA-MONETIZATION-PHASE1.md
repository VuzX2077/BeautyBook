# MUA monetization — Phase 1: additive storage and shadow evaluation

## Scope and production boundary

This change starts the monetization rollout with a read-only backend foundation. It does not charge, activate a paid cohort, publish a profile, grant a gift, rank sponsored results, or change booking/withdrawal rights. `MuaMonetization:Enabled` defaults to false; this flag enables only shadow reads. There is intentionally no switch enabling checkout/enforcement in Phase 1.

The current workspace backend starts from `9847562` on `feature/shareprofile`. The mobile workspace starts from `168265b` on `feature/shareprofile`; its pre-existing deleted image is not part of this change. These are local baselines, not proof of the binary or commit currently in production.

The local Android Gradle file reports versionCode `1`, versionName `1.0.0`, and package `com.bbook.app`. Actual production versionCode/commit still requires verification against the Play release record. No native/mobile dependency was changed.

## Implemented

- Separate account activation/cohort records and immutable-source visibility intervals, with uniqueness for purchase references and the first gift (including a revoked gift).
- Read-only `GET /api/mua/me/monetization` authenticated from the JWT user, with no route/query user override. Deleted/inactive/demo/non-MUA accounts return 404; malformed identity returns 401. Response is not cacheable and does not include provider references.
- Optional `monetization` projection in eligibility. When disabled, the field is omitted and no new table is queried. Existing review checklist, completion, status transitions, booking and withdrawal calculations are unchanged.
- UTC interval calculations use an inclusive start and exclusive end. A purchase made before expiry is scheduled after existing time, for exactly 30 days. Revoked intervals are excluded. The function calculates only; it never writes a grant.
- Existing accounts without a record are treated as waived in shadow mode. A missing record is not evidence of unpaid activation. Phase 2 must persist enrollment atomically with profile creation before enforcing a new paid cohort.
- The migration creates two new tables/indexes/checks and attaches the existing account-deletion/private-media writer guards. It does not update profiles, booking tables, balances or seed payment data.

## Local verification

Use a writable `DOTNET_CLI_HOME`, SDK 8, `MSBuildEnableWorkloadResolver=false` and serial build on this Windows host. No dependency or SDK change is required in the repository. Test PostgreSQL uses a dedicated localhost-only cluster and disposable databases with synthetic data; never populate `BEAUTYBOOK_TEST_POSTGRES` with a production server.

Representative commands:

```powershell
dotnet build BeautyBookBackend.Tests/BeautyBookBackend.Tests.csproj --no-restore -m:1 -nr:false -p:UseSharedCompilation=false
dotnet test BeautyBookBackend.Tests/BeautyBookBackend.Tests.csproj --no-restore --no-build --filter 'FullyQualifiedName~MuaMonetization'
```

The PostgreSQL tests exercise upgrading a schema containing a listed legacy MUA, unchanged legacy publication, guard coverage, migration rollback in a disposable DB, concurrent first-gift uniqueness, and rejecting writes for deleted accounts. SQLite sequential tests cover boundaries, stacking, refund exclusion, authentication/ownership, no writes on reads, disabled compatibility and database constraints. SQLite is not evidence of PostgreSQL lock behavior.

Verified on 27/10/2026:

- Backend/test projects build successfully with no new packages. The environment cannot fetch NuGet vulnerability metadata (`NU1900`); this does not represent a completed dependency-security scan.
- 155 regression cases passed, zero failed/skipped, covering the monetization foundation plus MUA onboarding, Play-review safety, sharing, account deletion, booking confirmation, deposit confirmation, customer refunds and immediate payouts. Result: `BeautyBookBackend.Tests/TestResults/monetization-phase1-regression.trx`.
- The final focused run passed all 22 monetization cases, zero failed/skipped, including repeat execution of the delivered SQL migration and waiver-backfill scripts, immutable history, JWT ownership and shadow eligibility parity. Result: `BeautyBookBackend.Tests/TestResults/monetization-phase1-verified.trx`. These runs overlap; their counts must not be added as unique cases.
- EF reports no pending model changes. The generated idempotent SQL review artifact is `docs/MUA-MONETIZATION-MIGRATION-REVIEW.sql`.
- Default-off/omitted-JSON behavior and enabled shadow evaluation are covered. A paid/unpaid shadow record cannot change profile completion, publication, booking or withdrawal rights in this release.
- No push, deployment, production migration, production backfill or real payment was performed.

## Deployment order

1. Verify actual backend commit, Play versionCode and current schema against the approved release baseline. Review the migration SQL. The existing production host automatically applies migrations at startup; pushing/merging into an auto-deployed branch is therefore a deployment action.
2. Rehearse migration and restore on a disposable/staging DB. Configure no monetization environment keys for production; the feature remains disabled.
3. Deploy the additive backend only after review. The schema must exist before enabling shadow reads. Disabling the feature skips new-table reads, but the new schema must remain present for future writes.
4. Optionally enable `MuaMonetization__Enabled=true` in staging for shadow comparison. It still returns all checkout/enforcement fields as false.
5. Snapshot existing MUA IDs before paid enrollment opens. The separate preview/backfill scripts can record waivers in bounded batches before Phase 2; do not infer grandfathering from user registration date or client app version. Backfill never updates an existing account record.

## Rollback

Disable `MuaMonetization__Enabled` and roll back application code if necessary. Keep the additive tables. Do not run the migration Down operation in production after real data exists. The Down path is tested only to verify migration reversibility on disposable data. There are no payment workers or real transactions in this release to disable.

## Phase 2 prerequisites and next work

- Verify the release binary/native Expo/RN baseline and choose a compatible native IAP SDK without upgrading unrelated dependencies.
- Configure Google Play product IDs, localized prices, service-account permission, authenticated RTDN and license testing. No service-account private key belongs in source/mobile code.
- Implement durable purchase verification, order idempotency, inbox/outbox, backend acknowledge/consume, restore, reconciliation and voided/refund handling.
- Persist cohort enrollment and separate review readiness from paid publication. Cover all public projections and new-booking entry points before adding any enforcement switch. Existing bookings, payouts and legacy clients remain compatible.
- Atomically grant activation, first publication and the one-time 30-day gift after verified payment; compensate instead of charging again after a timeout.
- Add mobile checkout/status/restore, admin support/audit, sponsored delivery with organic fallback, social fulfillment and measurable reporting.
- Internal-track payment tests and a small supported pilot precede any production fee enforcement.

Phase 1 is not a production-ready payment feature. No money should be collected from it.
