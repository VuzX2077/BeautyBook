# Supabase session connection budget

Incident: Render logs report EMAXCONNSESSION, pool_size 15, on Supabase shared session pool port 5432. A live deploy does not establish database readiness. This is not a 15-user login limit. The logs establish connection exhaustion, not a connection leak or the identity of all consumers.

## Change

Runtime connection string is bounded after DB_* overrides. Supabase shared session pool defaults to maximum 5 per process, minimum 0, idle lifetime 30 seconds, pruning interval 10 seconds. A smaller connection-string maximum is retained. Other hosts/ports remain unchanged unless Database:MaxPoolSize is explicitly configured. Credentials, SSL, session port, command timeouts, transactions and business workers are preserved. No automatic mutation retries are added.

Database__MaxPoolSize is an optional deployment override (1–1000), not a recommendation to increase. Effective maximum is min(connection-string maximum, configured budget). Pooling=false or Multiplexing=true is rejected when applying a bounded policy; unpooled connections cannot enforce this budget and session locks need stable sessions. Startup logs only effective nonsecret pool values.

The current EF Core provider uses NpgsqlConnection for UseNpgsql(string); the AI quota repository receives that same bounded string. Do not create independent data sources or differently configured strings without accounting for an additional pool.

## Deploy and acceptance

1. Keep Supabase shared session host and port 5432. Do not switch to transaction mode 6543: migration/media maintenance use session advisory locks.
2. Review every Render service/instance, external tool and local backend using this database user/pool. With session capacity 15, two overlapping processes capped at 5 leave at most 5 slots for other consumers. More processes require a smaller per-process budget or increased reviewed capacity. This patch cannot reclaim connections owned by the old binary or other clients.
3. Deploy the fix through the normal reviewed PR. Confirm startup shows max=5, min=0, idleSeconds=30 (or reviewed smaller maximum). Wait for the old instance to stop; do not repeatedly redeploy. Stop unneeded external clients. Do not terminate arbitrary production sessions.
4. Check EMAXCONNSESSION no longer repeats; test concurrent login, profile/tags/service updates, search and booking. Verify payment expiry/payout reconciliation succeed on following worker cycles. A Live badge alone is insufficient.
5. Inspect database connection metrics and latency during representative load and deploy overlap before release. Local PostgreSQL tests verify client pool behavior; they do not reproduce Supavisor or prove production capacity. If other clients already consume the remaining slots, investigate their budgets before increasing this one.

No migration, API contract or mobile rebuild is required. Rollback loses the automatic budget unless it is separately set in the previous binary's connection string. Free-host cold starts, external provider availability and device acceptance remain separate release checks.

References: https://www.npgsql.org/doc/connection-string-parameters ; https://supabase.com/docs/guides/database/connecting-to-postgres/pooling-and-limits

## Local verification, 2026-10-15

- Full backend: 697 passed, 0 failed, 0 skipped; 20m32s. Includes PostgreSQL integration on isolated localhost:55544 with per-test databases.
- Focused policy tests: 14 passed, including 30 concurrent requests queueing/reusing at most 5 physical PostgreSQL connections and advisory lock release.
- Build: 0 errors, 3 preexisting warnings. git diff --check passes. No migrations or financial business logic modified.
- Evidence: `D:/EXE/artifacts/pool-fix-20261015/full/backend-pool-final.trx`, `full.log`, `pool-policy.trx`, `build.log`.
- No production Supabase access, Render settings change, deploy, commit/push or mobile rebuild performed for this fix. Production Supavisor capacity and representative release load still require post-deploy acceptance.
