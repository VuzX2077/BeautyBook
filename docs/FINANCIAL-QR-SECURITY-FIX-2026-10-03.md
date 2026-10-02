# Financial QR — implementation and human review report (03/10/2026)

## 1. Root cause
Original MoMo input used `SupabaseImageStorage.UploadOwnedPublicImageAsync`, whose configured public bucket defaults to `images`. `BankAccounts.QrCodeUrl`, `Payouts.QrCodeUrlSnapshot`, `Refunds.DestinationQrCodeUrl` persisted/exposed financial image URLs. BANK also generated external `img.vietqr.io` URLs containing account identifiers. Decoder already accepts memory bytes; storing BANK input was unnecessary. Admin still needs a transfer QR, so removing every QR without replacement was not acceptable.

## 2–3. Git and preserved work

| Repository | Current branch | HEAD (unchanged) |
|---|---|---|
| BeautyBook backend | feature/fixdelete | 9288af5efe56140f5f8c838c4c1ffb212962c5ac |
| bbeauty-app | feature/payouts | 72d459a4acd7d8319dfc175d1a7d201b00908d1a |
| bbook-web | feature/policypage | b42d32bcf63580ee29a4720e67a24eb5382e7a81 |

Read status/diff/stat/branch/HEAD before MoMo work. Existing BANK changes retained. Before-MoMo tracked patches saved under D:/EXE/financial-qr-before-momo-{backend,app,web}.patch. Original FINAL-IMPLEMENTATION-POLICY-AUDIT report preserved. No reset, checkout overwrite, merge, commit, push or deployment. Changes remain reviewable in their existing workspaces. Proposed separate commit subjects: `fix(security): separate private MoMo media and on-demand BANK payout QR`; `fix(app): support private MoMo QR and authorized payout preview`; `docs(privacy): distinguish temporary BANK input from private MoMo retention`.

## 4–5. BANK/MOMO architecture

| Flow | New behavior |
|---|---|
| BANK QR input | `/api/Upload/bank-qr`: validate actual JPEG/PNG/WebP, max multipart 5 MB, max 24 MP, normalize/decode in memory, structured response, no Storage/object URL/raw payload. User edits fields, OTP-bound explicit save. |
| BANK manual input | Existing manual flow retained; deprecated client URLs ignored. |
| BANK Admin output | Local NAPAS account-transfer TLV + CRC16/ZXing PNG from immutable payout BIN/account, positive integer VND amount, `BB` + compact payout-ID reference. No network image provider, no Storage write. |
| MoMo MUA input | Temporary decode + new MUA-only private upload. Original QR pixels normalized as JPEG without EXIF, not a newly invented QR. Stable GUID in DB; owner preview fetched through authenticated backend, component state holds data URI. |
| MoMo manual input | Allowed without image. Existing phone/account/name validation and OTP; Admin manual fallback. |
| MoMo Admin output | Reads payout's private GUID snapshot, verifies owner=MuaId and context=BankAccountId. Original stored QR, never generated from phone/name. Amount/reference displayed separately; original can contain another amount. |
| Customer refund form | BANK/MoMo decode-only convenience and manual destination entry retained. Customer cannot persist/access MUA private financial media. Refund Admin uses manual destination fields; no public QR fallback. |
| PayOS booking/wallet QR | Unrelated provider payment payload, unchanged. |

Parser recognition does NOT certify a MoMo recipient or all official MoMo formats. If no supported phone/name is available, user enters fields; Admin must verify receiver and amount in the real payment app. No guessed name or invented MoMo specification. Synthetic tests are not proof of real-money transfer compatibility.

## 6. Private financial storage
Reuse private transport/image validation + VerificationMedia durable upload manifest, but separate `IFinancialStorage`, purpose `financial-momo-receive-qr`, configurable separate private bucket. Object key `financial/{owner:N}/{media:N}.jpg`. Upload manifest/checksum/location binding written before provider upload. ReadyAt only after upload; service confirms private bytes/checksum before returning GUID. Failure leaves a recorded deletion candidate, not an untracked object.

`SUPABASE_FINANCIAL_BUCKET` or `Supabase:FinancialBucket` is required for financial operations. Missing config, bucket=public bucket, bucket=identity bucket, invalid provider config or `public=true` fails closed; no public fallback. Manual BANK/MoMo work is not disabled by the missing financial bucket. Every financial download/delete rechecks bucket privacy on the server. No permanent/signed Storage URL returned, no service-role key in app.

## 7. Access matrix

| Actor | MoMo upload | Owner preview | Payout QR |
|---|---|---|---|
| Anonymous | 401 | 401 | 401 |
| Customer | 403 | 403 | 403 |
| Other MUA | Only own upload | 404 for foreign ID | 403 |
| Owner MUA | Own upload | Own ready/current private QR | 403 |
| Admin | 403 | 403 (no generic financial browser) | Authorized Admin payout endpoint only |

Upload owner comes from JWT, not a client owner parameter. Payout endpoint accepts payout ID, not arbitrary bucket/key/media ID. Missing payout 404; missing/invalid/terminal QR safe 409/manual fallback. Responses `no-store`.

## 8–9. Admin and MUA UX
MUA can choose BANK/MoMo; BANK image only prefills, remains editable and explicit OTP save. MoMo upload + authorized preview + optional remove/manual fallback. Private upload failure preserves entered fields and old reference. Changing private GUID is a sensitive bank change, resets approval/default as existing workflow, GUID included in OTP fingerprint. Current bank owner lock serializes replacement. Old QR remains referenced by already-created payout; payout A cannot obtain B's image/amount. Admin always sees bank/phone/account, holder, amount, payout ID. Scanning never transfers money or marks Paid automatically. Final payout statuses do not expose a transfer QR.

## 10. Account deletion and replacement cleanup
Phase 3 captures every owned VerificationMedia row, including financial purpose, before personal/reference cleanup. Bank row removed; owned payout/refund financial GUID snapshots cleared with other personal data. Correct financial provider/location/path used. Private financial deletion rechecks all three GUID reference writers, bucket private invariant; DELETE followed by authenticated missing-object confirmation before StorageDeletedAt and request Completed. Provider errors or ignored DELETE remain RetryPending; invalid/unbound ownership or unresolved legacy remains NeedsReview. Existing token revocation, settlement checks, public-media manifest and legacy safeguards retained.

Replacement sets old ledger DeletedAt in the bank update transaction; existing payout/refund snapshot references keep the old QR until no longer referenced/account deletion. Background worker only considers durable financial manifest candidates (failed/replaced, or unattached older than 24 hours); no legacy public scan/bulk delete. Settling incomplete uploads get at least 10 minutes. It preserves checkpoint rows, verifies recorded location/checksum, confirms absence, retries failures, and is restart-idempotent. Unattached 24-hour threshold is operational code, NOT a guaranteed deletion SLA.

Exclusive global advisory lock `724266524669002` protects cleanup; additive DB triggers on BankAccounts/Payouts/Refunds acquire shared transaction lock and validate new GUID ownership/context/purpose/readiness. Writer at reference-check→DELETE is rejected; deleted GUID cannot be attached afterwards. Existing identity cleanup excludes financial purpose. Concurrent replacements and wrong-owner/OTP-swapped GUID tested.

## 11. Legacy production strategy — NOT EXECUTED
Legacy URL columns preserved; DTOs do not serve them. Hiding URL is NOT migration and does NOT remove existing public exposure. Read-only inventory `docs/financial-qr-legacy-inventory.sql` prepared, not run in production.

1. Identify configured project/bucket/object, owner, all bank/payout/refund references; verify exact OwnedPublicMedia ownership manifest. Unknown/shared/lost ownership => unresolved/NeedsReview, never infer ownership from URL alone.
2. Separate BANK/external VietQR from MoMo original QR. No deletion of external provider images or entire public bucket; intentional public avatar/portfolio/service/review unchanged.
3. For proven MoMo: fetch through controlled provider access, validate image/QR and checksum, copy into financial private bucket and durable ledger with correct owner/context. Verify private flag/location/bytes/ownership before any DB switch.
4. Under reviewed locks update stable bank GUID and relevant payout/refund snapshots to the correct private receiver. Keep old URL/manifest evidence until successful migration checkpoint.
5. Verify deployed application uses private QR. Independently authorized cleanup rechecks privacy/checksum/all references and ownership under writer lock, deletes only proven public financial object, confirms missing and records checkpoint. Do not invoke identity migration for this financial purpose.
6. Only then retire legacy references/evidence as approved. Manual inventory/backup/CDN review needed. No production migration/cleanup utility is automatically run by this patch; support tooling for this separate operation must be reviewed first.

## 12. Additive schema migration
`20261002182347_AddPrivateFinancialQrReferences`: nullable UUID `BankAccounts.FinancialQrMediaId`, `Payouts.FinancialQrMediaIdSnapshot`, `Refunds.DestinationFinancialQrMediaId`, indexes, FK Restrict to VerificationMedia, three financial writer triggers/function. Existing URL/data columns retained. No DROP/RENAME/data copy/delete in Up. Down throws and requires reviewed roll-forward, no destructive automatic rollback. Model snapshot synchronized.

Startup production remains `ApplyMigrations=true`; migration failure retries then throws before workers/HTTP. Never run new backend against old schema. Rollout order: bucket config → reviewed backend/migration deployment → app → matching web policy → disposable validation → separately approved legacy process. This is a future operator sequence, not actions performed here.

## 13–14. Owner setup after review
Create private `financial-private` (or another separate configured name), allow `image/jpeg`, max 5 MB. No public access or anon/authenticated Storage policies for this backend-mediated flow. Set `SUPABASE_FINANCIAL_BUCKET=financial-private` in backend environment; retain existing backend Supabase URL/service role, verification bucket and public media config. Production `ApplyMigrations=true`. Do not put privileged credentials in Expo config/app. This task made none of these production changes.

## 15. Files
Backend: Upload/Payout/AdminBank controllers; BankAccount DTO/model/service/eligibility; Payout/Refund models/services; context + migration/snapshot; SupabaseVerificationStorage reused constructor; VerificationMediaService/Maintenance; AccountDeletionData/Storage; Program; new FinancialMediaController/Service/Storage/WriteGuard/Cleanup and PayoutQrGenerator. Tests: FinancialMomo/FinancialStorage/FinancialQrSecurity/PayoutQr, existing bank/refund/deletion/HTTP fixtures plus historical guard fixture. Existing migration files untouched.

App: MUA account form/list, Customer refund form, Admin bank/refund/payout screens, supabase helper, financialMediaService, adminPayoutService, ApiBankAccountRepository, bank types, financial/form/Admin QR tests and bundled policy. No extra QR dependency.

Web: legal/content.json, legal/privacy.txt, privacy.html, privacy/index.html, delete-account.html, delete-account/index.html. Existing website style and clean routes preserved; generated build under dist. Original terms prefix in app retained.

## 16. API contracts
- POST `/api/Upload/bank-qr`: structured fields only, no URL/raw payload.
- POST `/api/financial-media/momo-qr`: MUA multipart, returns financialQrMediaId/method/optional accountNumber/accountName, never Storage URL.
- GET `/api/financial-media/{id}/preview`: owner MUA only, `{imageDataUrl: JPEG inline}`.
- Bank add/update/request-OTP draft: optional `financialQrMediaId` for MoMo; `qrCodeUrl` deprecated/ignored.
- GET `/api/admin/payouts/{id}/transfer-qr`: Admin only, `{payoutId,amount,imageDataUrl,kind,containsPayoutAmount}`. BANK_GENERATED/PNG/true; MOMO_ORIGINAL/JPEG/false (means BBook did not embed this payout amount, NOT proof original QR has no amount).
- Bank DTO returns owned GUID, never original URL. Payout/refund DTO original QR URLs redacted; legacy DB reference evidence retained.

Compatibility:

| App/backend | Result |
|---|---|
| Old/old | Original public MoMo exposure remains. |
| Old/new | Manual fields continue; deprecated URL ignored. Old scan/save expects URL and may fail; old Admin QR display unavailable, manual info retained. Old update without GUID can switch to manual MoMo. |
| New/old | UNSAFE combination: old decode endpoint can already persist original MoMo publicly, new private endpoint missing. Backend must roll out first. |
| New/new + configured private bucket | Intended BANK/MoMo flows, tests pass locally; real provider/scan still requires validation. |

## 17–22. Verification, exact counts and Git stat
Final test results/stat are appended after final runs. Backend tests use isolated local PostgreSQL test databases; hosted production workers are never contacted. App full suite uses `--runInBand --testTimeout=15000`; this runner timeout avoids prior existing 5-second UI test flake under concurrent database/build load, not a change to production behavior. Expo export is Android JS/assets validation, NOT an APK/AAB. No real user identity/account/QR used by new MoMo tests.

## 23. Policy / Data Safety
Policy now separates temporary BANK input from private persisted MoMo receive QR. Owner and contextual Admin access, original-QR amount caveat, manual fallback, confirmed async deletion/retry/NeedsReview described. No encryption, instant deletion, never-stored-anywhere, provider zero-retention or invented 30-day guarantee. Existing retention uncertainty preserved. Website/app text synchronized and Android export policy asset checked.

Data Safety preparation must include actual collected financial information and MoMo uploaded images; BANK QR input temporary decode must be assessed separately from persisted bank data. Cloud processors/payment providers and existing Data Safety answers still need operator review against actual production SDK/config. No Console submission or declaration changes made; do not infer all photos are public or all QR images are ephemeral.

## 24. Production validation still required
Private bucket/provider policies/config, deployed commit/migration/guard coverage, real MoMo formats and real bank-app scanning, actual disposable account deletion/file absence, missing bucket/provider failures, legacy public object inventory/CDN/backup behavior, published clean policy URLs and Play Data Safety are not production-verified. Tests prove local contract/authorization/ownership/locking, not provider certification or automatic Google approval.

Public media endpoint recognition rejects supported decoded financial QR under every intentional public purpose; it is defense in depth, not a universal classifier for concealed/blurred/unsupported financial content in arbitrary public photographs.

## Final security search classification
| Source occurrence group | Classification | Evidence |
|---|---|---|
| UploadBankQr, new financial upload/owner preview, financial purpose/storage/guard | SAFE | Decode endpoint has no Storage call; financial upload calls only private adapter; image response contains no provider URL. |
| BANK generator and Admin transfer-qr | SAFE | Server Admin role + per-payout snapshots; inline output/no-store; wrong owner/context denied. |
| QR URL fields, old migrations, compatibility type declarations | LEGACY | Existing DB values preserved as deletion/migration evidence, not served by new bank/payout/refund UI. |
| Ownership/deletion URL parsing and inventory SQL | LEGACY/SAFE | Not a new upload/serve path; manifest + reference checks remain. |
| Original MoMo public upload and external img.vietqr.io generation | MUST FIX → REMOVED FROM NEW FLOWS | No current backend/app runtime generator reference; new MoMo file persists only via IFinancialStorage. |
| SupabaseImageStorage UploadOwnedPublicImageAsync and /object/public/ | UNRELATED intentional public media | Existing avatar/portfolio/service/review; supported decoded financial codes rejected at public entry point. |
| PayOS BookingPayments/WalletTopUps QrCode | UNRELATED | Provider payment payload, not recipient QR uploaded into Storage. |
| Tests, docs and historical audit | UNRELATED/historical | May deliberately contain old URL pattern/synthetic fixtures. |

Search evidence saved locally: D:/EXE/financial-momo-security-backend-search.txt and D:/EXE/financial-momo-security-app-search.txt. Files contain source matches, not production database inventory. Remaining compatibility DTO fields are not rendered as image URLs in new financial screens.

## Final verified results

| Check | Result | Evidence |
|---|---|---|
| Full backend, isolated local PostgreSQL enabled | **218/218 passed, 0 failed, 0 skipped** | D:/EXE/financial-momo-backend-verified.log; final run 5.7027 minutes, exit 0 |
| Full app suite | **144/144 passed; 29/29 suites** | D:/EXE/financial-momo-app-final.log; --runInBand --testTimeout=15000 |
| TypeScript | PASS | D:/EXE/financial-momo-typescript-final.log; no diagnostics |
| Android Expo export | PASS, current policy asset matches source | D:/EXE/financial-momo-android-build.log; D:/EXE/bbook-financial-momo-export; JS/assets, not APK/AAB |
| Web build / generated clean routes | PASS | D:/EXE/financial-momo-web-build.log; privacy and deletion HTML match clean-route HTML |
| Policy app ↔ website ↔ Android export | PASS byte/content equality | Bundled app policy ends with legal/privacy.txt, exported .txt equals complete app asset |
| EF model versus last migration | PASS, no pending changes | D:/EXE/financial-momo-model-check.log |
| git diff --check | PASS in all three repositories | Exit 0; ordinary LF/CRLF notices only, no whitespace errors |

An earlier full backend run was 217/218 because a historical-schema fixture lacked new columns; fixed fixture reinstalls exact old account-deletion guard on current additive schema and applies exact reviewed guard migration SQL. It still verifies old 23514 failure and upgraded live-MUA payout without changing any deployed old migration. Final full run passes all 218. Earlier new-test errors (tracked entity assertion, DI constructor ambiguity, JSON decimal assertion) corrected and covered by final full run. Backend clean build has three existing warnings (unused ServiceController ex, two MuaService nullable warnings); final --no-build test run reports no new warnings.

Tracked Git stat (new untracked security files are additional):
- Backend: 24 files changed, 252 insertions(+), 82 deletions(-).
- App: 11 files changed, 86 insertions(+), 43 deletions(-).
- Web: 6 files changed, 52 insertions(+), 32 deletions(-).

Full exact per-file `git diff --stat`, branches/HEAD and untracked inventory: `docs/financial-qr-git-diff-stat.txt`. Untracked migration/designer/new service/test files MUST be included in human review; normal `git diff --stat` does not show them. No new commits created; proposed commit subjects above. Original untracked FINAL audit is historical preserved work, not a newly modified implementation file.

## Conclusions (local implementation versus production)
BANK QR SECURITY: FIXED
MOMO QR SECURITY: FIXED
ADMIN PAYOUT QR: PASS
ACCOUNT DELETION INTEGRATION: PASS
PUBLIC FINANCIAL MEDIA P0: NOT RESOLVED
PRODUCTION VALIDATION: REQUIRED
READY FOR HUMAN REVIEW: YES

FIXED/PASS refer to the tested new local implementation. P0 is NOT RESOLVED globally because previously public production financial objects have NOT been inventoried/migrated/deleted and real production configuration has not been validated. The new paths prevent public financial storage/serving; this is not a claim that legacy files disappeared. Human review readiness is not permission to merge/deploy or a guarantee of Google Play approval.
