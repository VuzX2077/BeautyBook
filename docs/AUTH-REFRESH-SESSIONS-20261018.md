# Auth sessions — 2026-10-18

## Behavior
- Existing clients keep the configured JWT duration (repo default: 24 hours). Native mobile login opts in with useRefreshSession=true; opt-in login and refresh issue 15-minute access tokens and a random 256-bit refresh credential with a fixed 30-day family expiry. Web does not opt in.
- POST /api/Auth/refresh accepts refreshToken, atomically consumes it, returns a new access/refresh pair and preserves the original family expiry. Replay/concurrent reuse revokes the family. A lost refresh response can require signing in again; the client does not blindly replay refresh mutations.
- POST /api/Auth/logout revokes the refresh family. Ordinary access tokens remain valid until their expiry; inactive/deleted accounts are rejected by existing bearer validation.
- Refresh rejects expired sessions, inactive/deleted users and changed password hashes. Each successful refresh reloads current role/profile capability. Only SHA-256 refresh hashes and password-context hashes are persisted, never raw refresh credentials.
- Expired rows for the account are removed when it next opts into a new login. Revoked-but-unexpired rows are retained to detect replay. Auth responses use no-store.

## Database and rollout
Additive migration: 20261018093324_RefreshSessions. One table, hash uniqueness, family/user/expiry indexes, cascade FK to Users, account-deletion/private-media writer triggers. Account deletion removes refresh credentials. Existing historical migrations were not edited. Migration was applied only to disposable local PostgreSQL during tests. Snapshot has no pending model changes.

Do not treat an APK build as backend rollout. This backend applies ALL pending migrations at production startup; the earlier R1 mapping/migration/backup gates remain applicable. Review the complete target pending chain and backup/restore before separately authorized deployment. Do not bypass disposable import guards. No production database was accessed for this work.

Deploy the compatible backend with the reviewed migration before testing long-lived mobile sessions. Old backend versions ignore the opt-in property and retain legacy login behavior; old installed apps continue to receive legacy-duration JWTs from the new backend. A later migration Down destroys refresh sessions and requires reauthentication for those devices; prefer an application rollback that leaves the additive table.

## Tests
8 PostgreSQL tests cover rotation/hash-only storage, family replay, legacy compatibility, logout, malformed requests, password change, disabled/deleted users, expiry concurrent reuse, account-deletion cleanup and rejection of new session writes for deleted owners. Final results and device acceptance are recorded in the mobile stability report.
