# BBook Play Review — Phase 4A

## A. Architecture implemented

Standalone developer console, không web host/HTTP maintenance endpoint/hosted worker. Bốn commands validate, provision, refresh-scenarios, rotate-credential. Existing Phase 3 simulator không redesign. Tool dependencies chỉ DB, time/calculators, shared password hasher, image normalization và public/private sample storage adapters. Không register PayOS/Brevo/Expo hoặc production workers.

## B. Files changed

| Existing file | Change |
|---|---|
| [AuthService.cs](D:/EXE/BeautyBook/BeautyBookBackend/Services/AuthService.cs) | Persisted demo marker blocks forgot OTP/reset/change; delegates unchanged algorithm to hasher |
| [AccountDeletionService.cs](D:/EXE/BeautyBook/BeautyBookBackend/Services/AccountDeletionService.cs) | Protected result before request/scrub |
| [MuaEligibilityService.cs](D:/EXE/BeautyBook/BeautyBookBackend/Services/MuaEligibilityService.cs) | Exclude demo before application pagination |
| [BankAccountService.cs](D:/EXE/BeautyBook/BeautyBookBackend/Services/BankAccountService.cs) | Read-only known-code/BIN mapping helper; no behavior change to bank operations |
| [BeautyBook.sln](D:/EXE/BeautyBook/BeautyBook.sln) | Add console project, retain existing platforms |
| [Tests project](D:/EXE/BeautyBook/BeautyBookBackend.Tests/BeautyBookBackend.Tests.csproj) | Reference tool for isolated tests |

## C. New files

- [PasswordHasher.cs](D:/EXE/BeautyBook/BeautyBookBackend/Services/PasswordHasher.cs)
- [Console project](D:/EXE/BeautyBook/BeautyBook.PlayReviewProvisioning/BeautyBook.PlayReviewProvisioning.csproj)
- [Program.cs](D:/EXE/BeautyBook/BeautyBook.PlayReviewProvisioning/Program.cs)
- [ProvisioningSettings.cs](D:/EXE/BeautyBook/BeautyBook.PlayReviewProvisioning/ProvisioningSettings.cs)
- [ReviewProvisioner.cs](D:/EXE/BeautyBook/BeautyBook.PlayReviewProvisioning/ReviewProvisioner.cs)
- [ReviewScenarioSeeder.cs](D:/EXE/BeautyBook/BeautyBook.PlayReviewProvisioning/ReviewScenarioSeeder.cs)
- [SampleMediaProvisioner.cs](D:/EXE/BeautyBook/BeautyBook.PlayReviewProvisioning/SampleMediaProvisioner.cs)
- [Operator README](D:/EXE/BeautyBook/BeautyBook.PlayReviewProvisioning/README.md)
- [Provisioning tests](D:/EXE/BeautyBook/BeautyBookBackend.Tests/PlayReviewProvisioningTests.cs)
- [Account protection tests](D:/EXE/BeautyBook/BeautyBookBackend.Tests/PlayReviewAccountProtectionTests.cs)
- This report.

Không có asset binaries/config credentials/manifest trong repository.

## D. CLI commands

Exact syntax từ repository root:

```powershell
dotnet run --no-build --project BeautyBook.PlayReviewProvisioning -- validate --config <local-json> --expect-env <environment> --expect-host <host> --expect-db <database>
dotnet run --no-build --project BeautyBook.PlayReviewProvisioning -- provision --config <local-json> --expect-env <environment> --expect-host <host> --expect-db <database> --assets <local-directory>
dotnet run --no-build --project BeautyBook.PlayReviewProvisioning -- provision --config <local-json> --expect-env <environment> --expect-host <host> --expect-db <database> --assets <local-directory> --apply --maintenance-ack STOPPED_AND_DRAINED
dotnet run --no-build --project BeautyBook.PlayReviewProvisioning -- refresh-scenarios --config <local-json> --expect-env <environment> --expect-host <host> --expect-db <database> --assets <local-directory> --apply --maintenance-ack STOPPED_AND_DRAINED
dotnet run --no-build --project BeautyBook.PlayReviewProvisioning -- rotate-credential --config <local-json> --expect-env <environment> --expect-host <host> --expect-db <database> --apply --maintenance-ack STOPPED_AND_DRAINED
```

Without apply, mutation commands are dry runs. Validate checks a complete existing graph with read-only storage downloads; empty DB validation fails, while initial provision dry run checks collisions/assets and reports intended creation. Exit 0 success, 2 failure. Password never an argument. No manifest is emitted. Details in README.

## E. Safety gates

Explicit expected environment/host/database, nonempty distinct configured IDs, valid emails, known bank mapping, operating catalog pair, local approved sample assets. Apply also requires maintenance acknowledgement. Ack is an operator attestation, **not an automatic session detector**; operator must stop/drain traffic and background writers. Switch false alone is not sufficient.

PostgreSQL-only writes; pair-specific session advisory lock across all stages; controlled busy for concurrent tool. User locks inside transactions. Existing normal accounts are never adopted, even empty ones. Existing demo graph is audited using DB relationships and deterministic IDs, not a manifest. Unknown owner-linked activity conservatively aborts; tool never deletes unrelated activity to make refresh succeed.

## F. Review Account graph

User active/not deleted, IsDemoAccount=true, Role=MUA, normalized review email, PBKDF2 password hash, professional name/avatar. Draft MUA profile with city/catalog operating area, public sample meeting point, bio/specialization; one active service 500000 VND/60 minutes; one visible portfolio/three images; seven 08:00–20:00 schedules. One Pending sample bank and three private sample identity references. No fake reviews/rating. TotalBookings derives from completed demo history.

## G. Counterpart graph

One User, active/not deleted, IsDemoAccount=true, Role=MUA, internal unique email, PasswordHash=null. Same minimal Draft MUA/service/portfolio/schedule/location capability. No bank/identity provisioned for counterpart, no JWT, no fake IDs/Admin.

## H. Seven booking scenarios

One UTC T per seed run, normalized to PostgreSQL microsecond precision. Business calendar from configured BookingTimeService. All bookings IsDemo, exact pair, immutable financial snapshots and service snapshots. Paid simulated payment per scenario, negative deterministic unique order code; URL/QR/provider IDs null.

| Scenario | Approximate appointment | State / timestamps | Financial relations |
|---|---|---|---|
| customer-confirm | T−1 day | WaitingCustomer; Confirmed/Started; recent WaitingCustomerAt=T−10m; deadline +24h | Paid, DepositHeld |
| customer-refund | T+7 days | CancelledAt=T−1h; prior Approved/Confirmed, >30m after confirmation | Refunded payment + Completed full refund, policy-calculated |
| mua-accept | T+30 days | PendingConfirmation; no confirmation/start timestamps | Paid, DepositHeld |
| mua-reject | T+31 days | PendingConfirmation, separate slot | Paid, DepositHeld |
| mua-finish | Previous valid day slot | InProgress; coherent Created/Paid/Confirmed/Started | Paid, DepositHeld |
| mua-earnings | T−2 days | Completed, historical Waiting/Completed | Released; Available receivable |
| mua-payout-history | T−4 days | Completed; PaidOut after completion | Released; PaidOut receivable + active item + Simulated/Paid payout |

Dates/slots move only for new generations to avoid any existing same-MUA appointments. No PendingPayment seed; interactive customer checkout creates it. Source calculator: total 500000, deposit 150000, fee 40000, MUA deposit earnings 110000, remaining 350000. No arbitrary duplicated amounts. Cancelled sample replays BookingRefundPolicyService and validates metadata/refund reconciliation. Completed ledger inserted directly and validated, never by impersonating counterpart.

## I. Refresh behavior

Provision creates initial generation once and validates consumed states without resetting them. Refresh appends only consumed/stale interactive scenarios; old booking/payment/refund/payout states and IDs remain. Highest generation is selected by parsed key, not CreatedAt. New appointments use free schedule slots; no old appointments move. Completed refund/payout history is preserved without redundant generations. No automatic reset/pruning.

Incoming refresh if state consumed or appointment <now+7d. Customer confirmation refresh if consumed/deadline elapsed. InProgress refresh if consumed/old. Earnings refresh if no longer Available. Relative dates are not an evergreen guarantee; operator reviews freshness before each review round during maintenance.

## J. Password strategy

Secure interactive input, no redirected stdin, 12–128 characters, no arguments/config/log/manifest/report password. Initial creation only; successful rerun doesn't rotate. Explicit rotation writes only review hash. Shared hasher preserves 210000-iteration PBKDF2/SHA256 and existing legacy verification/login behavior. Persisted IsDemoAccount blocks forgot OTP issue and reset before consume, plus password change; independent of simulation switch. Generic forgot response retained. VerifyPassword itself remains unchanged.

## K. Delete protection

Both demo accounts receive PLAY_REVIEW_ACCOUNT_PROTECTED before deletion request/scrub. Existing controller maps controlled non-deleted result; normal deletion retained. No globally removed deletion UI.

## L. MUA application filter

Base application query excludes User.IsDemoAccount before pagination. Demo profiles remain Draft; submit/review direct-ID guards retained. No Admin simulator.

## M. Sample bank

Owned by configured reviewer; known code/BIN validated against shared source mapping. BANK, BBOOKREVIEWONLY, holder BBOOK APP REVIEW, PENDING_ADMIN, active, not default. Activated/reviewed/QR/financial-media fields null. Existing production bank eligibility remains false; only Phase 3 sample-bank capability permits simulated payout. Runtime sample bank ID is explicit and must match tool config.

## N. Sample identity/storage

Operator-provided local assets, never arbitrary URL downloads or committed binaries. File/purpose/type/decode/size/dimension/checksum validation; existing normalization removes EXIF/GPS. Front/back get large SAMPLE / NOT A REAL ID / FOR APP REVIEW watermark. Operator must attest no real document/person information; image validation cannot semantically prove this.

Private VerificationMedia rows tracked before upload, Ready only after success/existing-byte checksum recovery, Attached only on committed profile reference. Owner/purpose/hash/location validated; no legacy/public identity links. Existing demo upload API guard unchanged. Upload/attach failures leave tracked cleanup-safe resources and don't create fake Ready/Attached. Foundation can remain if later stages fail; simulation must stay disabled until complete validation. Public avatars/portfolio use existing owner-tracked uploader, not identity bucket.

DB and storage cannot be one atomic transaction. Foundation, individual media attach and all new scenario/ledger writes use their own transactions; failed scenario validation rolls back new financial graph. Private bucket must already exist/private; tool does not create buckets.

## O. DB migration

**NONE.** No schema/model-snapshot changes in Phase 4A. No seed-tracking/generation table or columns. The tool does not run Migrate; tests use existing migrations only on disposable DBs.

## P. Automated tests

Targeted Phase 4A trên bản build cuối: **18 passed, 0 failed, 0 skipped**, 2.4893 phút. Build thành công. [Targeted TRX](D:/EXE/BeautyBook/BeautyBookBackend.Tests/TestResults/phase4a-targeted.trx).

Coverage includes dry-run/target/IDs/maintenance zero writes, actual console invocation, real account/email collision, normal/mixed history, deterministic rerun/refresh, preserved consumed states, concurrent CLI lock, media upload/attach rollback and retry, secret redaction, asset validation, bank capabilities, Draft identity, application filtering, demo password/delete and normal password/delete behavior, exact financial shape/timestamps and actual participant transitions. Existing transition reads WaitingCustomerAt/deadline with two UtcNow calls: validator allows a small elapsed interval after the 24-hour target rather than incorrectly demanding tick equality. Optional payout BankBinSnapshot accepts null as produced by existing Phase 3. No simulator behavior changed.

## Q. PostgreSQL tests

Real disposable cluster, own directory D:/EXE/playreview-phase4a-pgdata, localhost:55439; isolated migrated databases. Separate contexts for concurrent commands. SQLite only used for sequential password/hasher tests, never concurrency claims. No production DB operations. Sau full suite pass, cluster riêng đã dừng và xóa; không tác động PostgreSQL instance khác.

## R. Provider proof

PayOS/Brevo/Expo counters asserted zero during provisioning/storage tests and actual seeded participant actions. Real Supabase adapters use counting in-memory HTTP handler; allowed requests are bucket/private/public storage only. Actual worker tests additionally assert no PayOS/refund provider or Expo delivery. Forgot-password test verifies zero OTP issue for demo; full existing Brevo tests validate sender boundary. No real provider called, no production bucket uploaded.

## S. Normal regression

**330 passed, 0 failed, 0 skipped** trên đúng bản build cuối; 312 existing cases + 18 Phase 4A cases. TRX: 330 executed, error/timeout/aborted/notExecuted đều 0. Vòng cuối kết thúc 2026-10-04 03:51:24 +07:00, tổng 23.6894 phút.

[Full TRX](D:/EXE/BeautyBook/BeautyBookBackend.Tests/TestResults/phase4a-full.trx) · [Full log](D:/EXE/BeautyBook/BeautyBookBackend.Tests/TestResults/phase4a-full.txt).

Existing 312 baseline/Phase 1–3 tests retained. Runtime simulator/business guards were not changed. Normal password hashing/verification and deletion exercised alongside demo protection. Known-code helper is read-only. No frontend changed.

## T. Git diff

Phase 4A modifies six existing files and creates ten source/test/readme files plus this report. Working-tree cumulative diff also includes prior uncommitted Phase 1–3; these were preserved. Cumulative tracked diff: 52 files, 625 insertions, 190 deletions, excluding untracked source/reports. Git diff --check pass. No commit/push/merge/deploy.

## U. Exact remaining Phase 4B work

- Consume login/profile demo marker and support Customer/MUA mode entry for Draft review profile.
- Counterpart entry from DemoCounterpartMuaId.
- Simulated checkout/success UI without PayOS browser/QR.
- Backend-authorized counterpart buttons from availableDemoActions.
- Seeded incoming/status/completion/history presentation.
- Earnings/withdraw UI using CanRequestSimulatedPayout and permitted sample bank, retaining false production eligibility.
- Sample bank/identity owner reads and disabled mutation explanations.
- Review-account labels and no-real-money explanation.
- Password/forgot/delete protected UI handling.
- Reviewer smoke-test and short English App Access instructions aligned with final UX.

No Phase 4B implementation performed.

## V. Exact production activation steps — description only

1. Review code/tests/tool, prepare approved non-real operator assets outside Git and secret injection.
2. Choose fresh two user IDs and sample bank ID; audit existing email collision. Do not convert an existing real account.
3. Keep SimulationEnabled=false; deploy only through a separately authorized deployment process.
4. Stop/drain application traffic and background writers; verify explicit expected environment/host/database and schema compatibility.
5. Run provision dry run; inspect target/plan; run apply with STOPPED_AND_DRAINED attestation and hidden credential input.
6. Run validate; ensure two Draft accounts, seven scenarios, private sample media, Pending sample bank and no production obligations.
7. Configure the already-selected PlayReview ReviewUserId/CounterpartUserId/SampleBankAccountId. Enable simulation only after Phase 4B is reviewed and compatible.
8. Resume traffic; smoke-test credential, both modes, simulated checkout/refund/payout and sample reads. Enter credential and short English guidance into Play Console without logging it.
9. Before later review rounds, enforce maintenance again, validate/refresh/validate, retain history; rotate credential only explicitly and update Play Console accordingly.

**These steps were not executed.** Stop after implementation → disposable validation/tests → full regression → report. No production provisioning/upload/environment change/activation/deploy or Phase 4B.
