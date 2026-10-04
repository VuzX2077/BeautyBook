# BBook UGC moderation rollout

Implementation spans BeautyBook backend, bbeauty-app and bbook-admin. Web admin queue: /moderation. Mobile admin also has a moderation screen. Reports are distinct from financial booking complaints.

## Local verification before release
1. Use two normal test accounts and an active normal Admin in a local/staging environment. Do not reuse production reviewer scenarios for this test.
2. Report a public portfolio, comment and review; report an incoming private message. Check required reason, optional description, retry after network failure and duplicate submission.
3. In bbook-admin open the Content reports menu, filter Pending, paginate, inspect the exact reported content, enter a decision note and confirm Reviewed, Dismissed or Removed. User reports cannot remove an account. Private image preview is limited to the reported message and an authorized Admin.
4. Verify removed portfolio/comment/review/message disappears or is redacted on subsequent reads, including cached feed refresh, reply previews and private image access. Unrelated content stays visible.
5. Block the other user. Both directions must reject new chat, reactions, comments, reviews and follows; test unblock from settings. Existing chat history, bookings and financial obligations remain. Previously committed notifications are not recalled.
6. Verify unauthenticated/non-admin callers cannot use admin APIs; outsiders cannot report private messages. Demo reports cannot enter the normal admin queue or cross the demo/normal domain.
7. Verify account deletion still succeeds for an eligible normal account and retains financial/deletion safeguards. Moderation text is scrubbed by the deletion flow; removal decisions remain to prevent content reappearing.

## Release requirements
New additive migration: 20261004130144_AddUgcModeration (ContentReports, UserBlocks, owner-write triggers). It has NOT been applied to production by this task. Updated mobile/admin clients require the matching backend and schema. No provisioning rerun is needed. Preserve verifier-only local changes separately from runtime deployment.

Do not roll back to an application version that ignores active blocks/removals without an explicit compatibility review. Do not automatically run migration Down or clean production data.

Before changing Play Console declarations, test an actual Android candidate and the admin workflow end to end. Review the built app's SDK/data flows separately: these features alone do not establish encryption, diagnostics collection, location sharing or all Data Safety answers. Confirm the support mailbox is staffed and publish matching community/privacy rules. The bundled policy was updated locally; any public website policy needs its own reviewed publication.

Production actions in this task: NONE. No commit, push, deployment, simulation/config change, provisioning or production DB/Storage operation.

## Final local verification
Backend build passed. Full backend suite: 471 passed, 0 failed, 0 skipped (458 baseline + 13 moderation cases). Includes migrated PostgreSQL guard coverage and existing account-deletion/private-media regressions. Web admin: typecheck, lint, production build passed; 16 tests passed. Mobile typecheck passed; full Jest suite: 203 passed, 0 failed, 0 skipped across 36 suites (201 existing + 2 report-form tests). All database tests used the dedicated local test cluster, which was stopped after the suite.
