# Phase 3 local handoff — no deployment performed

1. Review ACCOUNT-DELETION-AUDIT.md. Proposed retention periods are not current policy.
2. Owner checks Supabase: verification-private stays private; anon/authenticated cannot
   INSERT/UPDATE/DELETE storage directly. Keep public image GET URLs functional.
3. Review/commit backend, app and website as desired. This session did not merge, push
   or deploy. Existing website index/build/serve edits were preserved.
4. Owner deployment must retain ApplyMigrations=true. Additive migration:
   20261002114018_AddAccountDeletionStorageLifecycle. A migration failure stops startup.
   Do not invoke a production private-media CLI/cleanup merely to apply this migration.
5. After authorized deployment, test only disposable accounts: deletion accepted,
   old JWT rejected, bank/profile data cleared, owned storage files missing, state Completed.
6. Resolve NeedsReview securely: inspect persisted UnresolvedReferences in the database,
   establish ownership/source evidence, check active references, confirm actual storage
   deletion and document scope. Never bulk-delete arbitrary uploads from a client URL or
   clear a marker without proof. Lost old references require inventory investigation.
7. Build the updated app and publish matching pages after backend validation. Verify
   privacy.html and delete-account.html return HTTPS 200 without login before Play Console.

## Support (works without Render Shell)

- Official inbox: bbooksupport@gmail.com. Owner verifies registered email/account identity
  appropriately; do not request passwords, OTP or CCCD by email.
- Identify correct UserId in existing admin user list. A reused email can represent a
  new registration; do not delete that account instead of the old one.
- In-app receipt displays the account reference code. User should save/include it for
  progress questions; original email is removed from the account during deletion.
- Admin JWT GET /api/admin/account-deletions returns state/counts, no media URLs.
- POST /api/admin/account-deletions/{userId} with
  {"confirmOwnerRequestVerified":true} is an explicit destructive support action, only
  after verified owner request. Same obligations/guards as DELETE /api/User/me.
- Blocked: settle obligations and confirm deletion again. Worker does not automatically
  delete live blocked accounts when a balance later clears.
- PendingStorage/RetryPending: durable retry; Render Free sleeping may delay work.
- NeedsReview: known owned files processed, remaining legacy/shared scope requires review.
  Inspect references securely via database administration; no automatic final success.
- Completed: DB cleanup and known owned storage objects confirmed missing. Does not certify
  backups, CDN caches, inbox, provider records or recipients' copies.
- Owner communicates progress, erased/retained data and reasons by email. No email was sent
  by this session; no automated email-delivery feature was added.

## Compatibility and retention

Old app/new backend: DELETE remains 2xx (202) and logout works, but new app wording is
needed before release. Public uploads still require purpose; purpose-less identity/private
uploads are not restored. New app/old backend must not ship with the new policy because
old backend does not provide the described storage deletion. Use new app/new backend.

Proposals: review minimal settled ledger at 180 days; logs/backups 30-day rotation;
support emails erase 30 days after completion. Require owner/legal/provider verification
and enforcement before publication. No such SLA, purge or provider deletion was applied.

Concurrency note: storage deletion holds the existing global media advisory lock.
Other writes can be rejected temporarily and require retry while storage requests run;
provider timeouts/large account histories affect duration. Validate this on disposable
accounts after owner-authorized deployment, without claiming a deletion SLA.
