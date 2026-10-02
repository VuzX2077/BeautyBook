# Phase 1 private media — pre-deployment review

Review date: 2026-10-02. Scope: identity documents/certificates and chat attachments. No production request/job, push, main merge, or deployment was performed.

## Git scope

Backend worktree: `D:/EXE/BeautyBook-phase1`, branch `security/phase1-private-media`, based on refreshed `origin/main` at `c053aa8`.
App worktree: `D:/EXE/bbeauty-app-phase1`, same branch name in its separate repository, based on refreshed `origin/main` at `ebfd0e7`.

Fetch revealed complaint and admin dashboard had already been merged into remote main by PRs #44/#45 (backend), #27 (app). This security diff does not add their feature/accounting implementation or commits. Original worktrees, uncommitted changes and commits were preserved; local security stashes are retained as backups. Ancestry necessarily includes features already on main; this branch is not a revert of them.

No changes to BookingService, RefundService, PayoutService, MuaReceivableService, AdminDashboardController, BookingComplaint models/DTOs, or the existing complaint migration.

Original complaint-private dependencies were: upload endpoint, private access authorization querying ComplaintMessages, complaint service attachments, maintenance migration/GC references and corresponding tests. Those private complaint components were omitted. Since complaint is now on main, the only complaint security changes reject nonempty image arrays, suppress public image responses and remove image upload controls. Text/financial workflows stay as in main. Existing evidence remains stored for Phase 2; this patch neither migrates nor deletes it.

## Security findings resolved

- Maintenance controller has backend `[Authorize(Roles="Admin")]`. HTTP tests run the actual Program/JWT middleware with isolated localhost databases: anonymous 401, Customer/MUA 403 for GET and every action, Admin GET 200 and audit POST 202.
- Request only chooses a whitelisted action plus explicit confirmations. Storage bucket/project comes from server configuration, object keys from metadata. Arbitrary-delete action is rejected. Clients cannot supply delete targets.
- Audit performs no uploads, reference changes or storage deletes. Migration creates/rechecks private copies and never deletes legacy sources. Startup only applies schema and resumes previously authorized persisted jobs; it never creates migrate/cleanup jobs.
- Cleanup is a separate admin action, requires successful audit/migrate and explicit confirmation. Worker re-audits, checksum-checks copy/source and scans current references. No cleanup is auto-queued after migrate.
- Cleanup calls server-side bucket inspection before processing, and again immediately before each legacy DELETE. Public, missing, malformed or unverifiable bucket fails closed. Job error text and worker logs contain no provider body, URL, token or service secret.
- Database BEFORE INSERT/UPDATE triggers guard every existing public table with text/varchar/JSON/array columns scanned by cleanup (except internal metadata/jobs/history). Shared transaction advisory lock conflicts with maintenance's exclusive session lock, so concurrent writers fail before saving. Trigger rejects introducing tracked legacy object paths even after deletion. Legacy metadata is retained as tombstones by GC.
- Guard covers direct SQL, profile/avatar, portfolio, service, review, bank QR/snapshots, notifications and complaint writers without refactoring each service. Cleanup verifies trigger coverage and fails if a new textual table lacks the guard. New table migrations must install the same trigger.
- Tests cover write attempts between reference check and DELETE, already referenced sources, encoded paths in text/JSON/arrays, disabled coverage, crash after provider DELETE, restarted Running jobs, concurrent workers and cancellation/recovery. Unlock uses non-canceled cleanup tokens and connection close is in finally.

Normal inserts/updates briefly acquire the shared lock. During an explicitly requested maintenance action, guarded writes may fail temporarily and users must retry. The tombstone check intentionally errs toward preserving/rejecting an ambiguous legacy path. It never authorizes storage deletion from a client URL.

## Migrations and startup

Only these three new migrations apply:

1. `20261002072400_AddPrivateVerificationMedia` — new metadata table, indexes, restrictive owner FK.
2. `20261002072602_BindPrivateAttachmentsToContext` — nullable ContextId on that new table.
3. `20261002072646_AddPrivateMediaMaintenanceJobs` — new jobs table/indexes and database guard functions/triggers.

Up operations add schema and guards; no existing table/column/data is dropped or renamed. Generated models/snapshot use current main including its preexisting complaint schema. Do not use migration Down after media references exist. Keep tombstones until a separately designed retention/deletion process can preserve their invariant.

Render must use `ASPNETCORE_ENVIRONMENT=Production` (Docker default) and `ApplyMigrations=true`. Production explicitly refuses false. Startup retries migration failures five times, then throws before HTTP or hosted workers start. A local production-mode HTTP test deliberately conflicts with a pending migration and verifies startup throws. Existing successful schema migrations may remain applied after failure; backend still must not serve until all pending migrations succeed.

## Compatibility

| App/backend | Behavior |
|---|---|
| Old app / old backend | Existing public uploads remain the old behavior; no Phase 1 protection. |
| Old app / Phase 1 backend | Upload without purpose rejected before storage, including old public flows. Identity URL writes and public chat URL writes rejected. Text chat still works. Legacy private previews suppressed until migrated. |
| Phase 1 app / old backend | Public purpose uploads generally work; identity/private chat endpoints are absent, so these flows cannot complete. No fallback to public storage. Complaint images disabled in new app. |
| Phase 1 app / Phase 1 backend | Public avatar/portfolio/service/review uploads include purpose; identity and chat use private IDs, short signed previews with renewal and no disk cache. Complaint accepts text only until Phase 2. |

Safe purpose-less compatibility is impossible from the existing request: old public and private callers use the same endpoint/form/file naming. Filename/MIME/user role cannot prove purpose. Do not infer avatar by default or whitelist old versions to reopen private public uploads.

MUA identity eligibility immediately requires owned private references. Before migration, eligibility evaluation may change Listed to Draft and prevent new bookings. Schedule the cutover and communicate temporary unavailability. New backend does not silently treat legacy documents as verified private evidence. Existing bank QR behavior is unchanged and remains a follow-up, not covered by identity/chat completion.

## Operator steps (manual only)

1. Review both local branch diffs; prepare database and media backups in private storage with a retention/deletion date. Confirm Render auto-deploy behavior before your own push to main. Build/test the new app for coordinated rollout; old app uploads will break during cutover.
2. In Supabase confirm `verification-private` Public OFF, 10 MB, image/jpeg and no broad anon/authenticated policies on storage.objects. App must never receive service-role credentials.
3. On Render verify Production, `ApplyMigrations=true`, `SUPABASE_VERIFICATION_BUCKET=verification-private`, existing Supabase origin/service-role/legacy-bucket variables. If Supabase:VerificationBucket / Supabase__VerificationBucket is configured, it must agree. Do not change buckets/project between migrate and cleanup.
4. Merge/push only after these checks (human action). Main push can trigger Render deployment. Verify all three migrations and health; a migration failure must leave the new deployment failed. Never disable ApplyMigrations to bypass it.
5. Distribute/install the Phase 1 app. Log in as Admin → Bảo vệ ảnh riêng tư. Run audit, keep the screen active to poll Render Free, wait for Succeeded. Resolve unsupported/shared sources before continuing; do not remove document fields merely to make audit pass.
6. Confirm private backup and run migrate. Wait for Succeeded. Source public objects remain. Jobs use PostgreSQL checkpoints and resume after wake/restart; Failed jobs need operator review and an explicit retry.
7. Smoke-test with non-sensitive test images and owner/Admin/outsider: purpose/ownership/context, chat membership, public-upload rejection without purpose, private previews, link expiry/renewal, anonymous denial and public avatar/portfolio/service/review operation. Check existing MUA eligibility/listing recovers with valid private identity.
8. Only after private copies and references are verified, explicitly request cleanup-legacy and confirm deletion. If bucket check, checksum, reference or trigger coverage fails, stop and fix the cause. Never disable guards/Public OFF to force success.
9. Verify legacy URLs by anonymous access and provider/CDN cache expiry. Run audit again. Manually run cleanup-orphans as needed (>24h, unreferenced); no paid job/Shell required and no recurring production scheduler was created.

Do not rollback to an old backend/app expecting public document URLs after switching data to media IDs. Backups, legacy complaint evidence and bank QR require separate retention/security work. No claim of full Google Play compliance or production validation is made by these local tests.

## Verification record

Final full backend: 163/163 pass, 0 skipped, PostgreSQL localhost with isolated disposable test databases. Final app: 120/120 pass across 25 suites; TypeScript --noEmit pass. HTTP tests include authorization, production refusing ApplyMigrations=false, and production migration failure before HTTP startup. Final branch hashes are reported with the final review. `dotnet ef migrations has-pending-model-changes --no-build` reports no changes. Three existing compiler warnings (ServiceController unused exception; MuaService nullable accesses) are unchanged from main. App suite uses a 15s timeout after one existing overlay test timed out under concurrent local load; no product/test implementation was changed for that timeout.
