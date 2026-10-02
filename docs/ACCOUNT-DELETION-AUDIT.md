# BBook account deletion audit — 2026-10-02

Audit before implementation. Source: ApplicationDbContext, all Models, UserService,
UploadController/SupabaseImageStorage, VerificationMediaService/Maintenance,
ChatHub/ChatService, JWT OnTokenValidated and app deletion screens.

Database location: tables below in PostgreSQL schema public. Storage location:
configured Supabase verification-private bucket and public bucket (default images).
No production data/configuration was inspected or changed during this work.

| Data / tables | Action | Reason / limit |
| --- | --- | --- |
| Users | ANONYMIZE, disable credentials | Retain FK tombstone, not an identifiable login; never physically delete financial FK root blindly |
| MakeupArtistProfiles | ANONYMIZE all personal/identity/review fields, suspend | FK root for booking/services; identity documents are not retained for financial history |
| VerificationMedia | DELETE owned private objects, invalidate references first | Includes attached/unattached/failed uploads; preserve deletion metadata for retries and legacy tombstones |
| Public avatar/portfolio/service/review/QR objects | DELETE only server-proven owned and unreferenced objects | Existing uploader has random paths and no ownership ledger; old URLs need review, never trust client-selected bucket/object |
| DevicePushTokens, user's AppNotifications, EmailOtps | DELETE | Authentication/push data no longer needed; OTP indexed by old email, remove before changing email |
| Other recipients' linked AppNotifications | ANONYMIZE previews/title/JSON/error, skip pending | Platform-created copies of deleted sender/booking identity; keep minimal structural IDs |
| MuaFollows, MUAStyles, MuaOperatingAreas, MuaWorkingSchedules, MuaTimeOffs | DELETE | Remove preferences, public visibility and private availability/reasons |
| PortfolioLikes, PortfolioSaves, MessageReactions | DELETE user's rows; also likes/saves on owner's portfolios | Personal engagement no longer needed |
| Portfolios, Services | ANONYMIZE, hide/unlist | Preserve Services FK used by BookingServices; remove titles/descriptions/images/tags |
| PortfolioComments | ANONYMIZE user's content and comments on hidden portfolio | Parent/comment FK safety; no need to retain free text |
| Messages | ANONYMIZE sender content/image; delete owned attachments | Keep room/reply IDs for counterpart's conversation. Counterpart's authored content belongs to that party |
| ChatRooms | RETAIN structural IDs | Counterpart history, not a claim of irreversible anonymization |
| Reviews, ProductReviews | ANONYMIZE authored text/media/replies | Retain rating and IDs initially for service history; no claim of full anonymization |
| Bookings | RETAIN IDs, service/time/status/amount; clear customer address/coordinates/notes/reasons | Preserve financial relationships, minimize personal text after obligations resolved |
| BookingServices | ANONYMIZE service-name snapshot; RETAIN IDs/numbers | Booking FK and amount/duration snapshots, no unnecessary personal free text |
| BankAccounts | DELETE, unlink nullable Payout/Refund FK first | No need to keep beneficiary account profiles after settlement |
| BookingPayments, WalletTopUps | RETAIN numeric/status/provider reference; clear checkout/QR/raw webhook | Raw JSON may contain personal financial data |
| Refunds, Payouts | RETAIN amount/status/provider identifiers; redact beneficiary/account/QR/free text | Retain audit trail without unnecessary account numbers/names |
| Wallets, WalletTransactions, MuaReceivables, PayoutItems | RETAIN numeric/status/FKs; redact free text | Must not destroy settled ledger or alter balances; deletion blocked on unsettled balance |
| BookingComplaints, ComplaintMessages | Block while open; ANONYMIZE resolved authored text/media and customer complaint description | Preserve outcome/refund IDs and counterpart's evidence until their own applicable deletion; no new complaint feature |
| NotificationCampaigns | Global/admin data outside Customer/MUA deletion | Admin self deletion must not accidentally erase campaigns |
| MakeupStyles, Products | RETAIN | Global catalog, not user-owned data |
| PrivateMediaJobs | RETAIN minimal operator audit | Existing administrative command log, no media URL or financial payload |
| New account-deletion request / object manifest | RETAIN minimal state until completion/review | Durable retry, no false complete status; object keys must be server-generated/proven |
| Provider logs/backups/email/PayOS data | NOT proven deleted by application code | Requires provider/operational process; no fabricated 72h/30d or legal retention period |

Existing deletion checks are outside a transaction; simultaneous booking/payment/media
writes can bypass them. Reuse global database advisory-lock invariant, install database
guards for account-related writers, check obligations under lock, preserve manifests
before clearing references, and process storage only after the DB transaction commits.
Uploads must share that lock across provider request so deletion cannot race a late upload.

Existing JWT validation rejects inactive/deleted users on new HTTP requests. Existing
SignalR connections currently keep groups: require disconnect on deletion and reject
future joins/typing for deleted users, including a connect/delete race.

Retention of numeric relationship IDs is pseudonymization, NOT irreversible anonymity.
Business/legal retention duration, support-email handling and provider backup retention
are not established by code. Publishing a policy does not solve these operational gaps.

No production inspection, migrations or deletion were performed for this audit.

## Proposed minimum retention (proposal, not a published SLA)

| Location / data | Proposed deletion time / criterion | Implementation required |
| --- | --- | --- |
| PostgreSQL profile/contact/bank/OTP/preferences | In accepted deletion transaction, after obligations are settled | Transaction and guards; no fixed waiting period |
| Supabase private/public server-owned files | Queue immediately after accepted deletion; retry provider errors with durable checkpoints | Acknowledged deletion must be followed by an authenticated missing-object check; no “completed” before confirmation |
| Legacy files without owner proof / shared files | Review immediately; finish after ownership/shared references resolved | Do not silently delete another account's object; support tracks unresolved manifest |
| PostgreSQL minimal settled ledger | Proposed review at 180 days after settlement; erase only after confirming no applicable accounting/legal/dispute obligation | NOT approved/legal determination; do not install a blind ledger purge or publish 180 days as current behavior |
| Operational application logs | Proposed 30-day rolling retention, secrets/PII omitted | Hosting/log-provider configuration must be checked before publication |
| Deletion checkpoints / URL tombstones | Keep while needed to prevent deleted URLs being attached again and retry failures; review after deletion workflow is stable | Structural GUID/key records, not identity images or email; no fabricated legal exception |
| Backups | Proposed maximum 30-day rotation and deletion replay after restoration | Verify provider plan, backup configuration and restoration procedure; not enforced by app code |
| Support emails | Proposed erase 30 days after request completion, retain minimal completion receipt | Owner-operated inbox process, not a backend guarantee |
| Payment provider records | Ask provider for deletion/minimization where supported | BBook cannot promise deletion of independently retained provider records |

Owner confirmed on 2026-10-02: bbooksupport@gmail.com is official and personally
monitored; retain only necessary settled transaction IDs/amounts/status/timestamps,
remove unnecessary personal text/bank/media; no established ledger/log/backup period.
The periods above remain proposals. This implementation does not invent a legal
retention obligation or run any timed financial purge.

## Implemented local Phase 3

- AccountDeletionService checks obligations under the global PostgreSQL media lock,
  persists Blocked requests, captures unresolved legacy references before clearing fields,
  minimizes DB data and disables credentials.
- AccountDeletionRequests persists PendingStorage / RetryPending / NeedsReview /
  Completed. VerificationMedia.StorageDeletedAt and OwnedPublicMedia checkpoint each
  confirmed storage deletion. DELETE success alone is insufficient; missing-object
  verification is required. Recent uploads with an unknown response defer confirmation.
- Users.MediaOwnershipTracked defaults false for existing rows, true for new registrations.
  Old/deleted accounts retain NeedsReview for uploads whose original references were lost.
  Random old public paths cannot prove owner; no false automatic completion.
- Legacy checksum/shared-reference checks, private bucket verification and database
  writer guards protect deletions. Coverage is checked before deletion. New public
  uploads have server-generated owner-scoped paths and a durable ledger.
- HTTP JWT validation rejects deleted users. Connected chat is aborted; active status
  is checked for joins/typing and periodically across live instances.
- Admin-only support request list/verified-owner action; no new dashboard or arbitrary
  bucket/key deletion. Legacy review is not automatically dismissed.
- Unused direct Supabase client upload helper removed. Owner must verify production
  anon/authenticated storage INSERT/UPDATE/DELETE policies are disabled before release.
- Minimal monetary/FK records and counterpart-authored content remain. This is not
  irreversible anonymity. No timed financial purge or new legal retention rule.

Migration: 20261002114018_AddAccountDeletionStorageLifecycle. Up adds two tables,
four columns and guards/indexes; no deletion/rename of existing records. Production
requires ApplyMigrations=true; migration failure must prevent backend startup.

Private storage uploads bind a non-secret hash of origin/bucket. Config changes fail
closed instead of interpreting a 404 from the wrong location as successful deletion.
Legacy unbound metadata needs a checksum-verified object before binding.

Legacy source location is only verified by new migration runs that record its origin/
bucket binding. Historical unbound sources require review; private copies may still be
deleted independently after current location/ownership/checksum proof.

Platform notification snapshots (names/chat previews/JSON/error text) related to the
deleted account are scrubbed even for another recipient; pending copies are skipped.
Already delivered device/provider copies are not remotely erased by this code.
