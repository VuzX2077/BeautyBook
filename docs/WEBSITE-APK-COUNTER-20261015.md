# APK clicks by day, with fast admin updates

This release uses an internal first-party count for the APK KPI and daily trend, rather than waiting for GA4 daily processing. GA4 attribution and other metrics remain independent; historical GA4 counts are never added to internal counts.

## Deployment order (operator performed)

1. Backend: deploy the reviewed code and additive migration `20261015000000_AddWebsiteApkCounters`. Set `WebsiteMetrics__Enabled=true` on the backend. Default write origins are exactly `https://bbookmakeup.com` and `https://www.bbookmakeup.com`. For local testing only, explicitly configure `WebsiteMetrics__AllowedOrigins__0=http://localhost:5173`. Keep the bounded session pool fix when releasing these changes; startup must show the reviewed pool maximum.
2. Admin: deploy the daily counter UI and proxy. Existing `BACKEND_API_URL` and Admin session are used; no new secrets. Admin-only GET `/api/analytics/apk-daily?start=YYYY-MM-DD&end=YYYY-MM-DD` proxies to authenticated backend `/api/WebsiteMetrics/apk-daily`. Server cache is 10 seconds. The page refreshes this source every 45 seconds while visible, without repeatedly loading GA4 reports.
3. Website: set public `APK_COUNTER_API_URL=https://beautybook-13zj.onrender.com/api/WebsiteMetrics/apk-click`, keep the existing GA Measurement ID/consent UI, rebuild and deploy. The generated config is public: do not put passwords, JWTs or API keys there. Ensure `/apk-counter.js` returns 200. Both the Node server and dist build include it.

Until the backend is enabled, Admin reports the source as unavailable; it does not substitute a fake zero or a GA4 number. Until the first accepted event, trackingStartedAt/total/daily are unknown. The first successfully recorded click starts coverage. Dates before that stay null (—); the first day is partial. This feature does not reconstruct earlier clicks, including clicks between backend deployment and activation of website tracking.

## Delivery and privacy

Only trusted, noncancelled clicks on enabled APK links, with existing Analytics consent granted, trigger capture. Consent is rechecked on each retry. GA4 events remain unchanged. Payload contains only a random event UUID. No account ID, client IP, browser fingerprint, full URL or referrer is stored in counter tables. Event rows store UUID, server receipt time and server-derived Vietnam day (UTC+7). Daily counts are receipt-day counts, not a client-supplied timestamp.

Unique event ID and INSERT ON CONFLICT make retries idempotent even if the first response is lost. There are at most three browser attempts, with the same ID, keepalive, bounded timeout and no persistent client queue. Failure never blocks the download. Offline browsing, closing a browser before delivery, consent refusal and client blockers can prevent recording. Updating within roughly 45–60 seconds assumes backend availability; Render cold starts/outages can add delay or lose best-effort telemetry.

The anonymous write API checks Origin, rejects empty IDs and oversized bodies, and has a fixed maximum of 300 requests/minute/process. Origin is not proof of a human: this is usage telemetry, not a fraud-resistant billing counter. No counters are inferred from APK GET/HEAD/range downloads, avoiding browser range/resume double counting. Event IDs are retained for deduplication; monitor table size, request limits and database connections as usage grows. No new background worker, financial transaction change, auto cleanup or production data change is included.

## Acceptance

- Deploy backend → admin → website. Check schema applied, pool budget, Origin and endpoint URLs.
- Approve Analytics on the website, click APK once and confirm it still downloads normally. Admin should show a new internal count for the Vietnam date after the next refresh. Repeat a genuine click and expect another event. Refreshing a page without clicking does not increment this counter.
- Browser retry with the same ID must not increment again. Denied consent must send no internal event. Wrong origin must return 403; unauthenticated read must return 401; non-admin read must return 403.
- Verify earlier dates show —, not fabricated historical zeros. Restart/redeploy backend and confirm stored counts persist.
- Roll back the application without dropping the counter tables to preserve data. Migration Down drops the two operational counter tables and therefore their data; do not run Down on production as an automatic rollback step.

Local tests use isolated PostgreSQL only. Production connection settings, schema, cross-origin connectivity and real click acceptance remain operator checks after deployment.

## Local results

- Final backend acceptance: 47 passed, 0 failed, 0 skipped, including new counter, pool budget, matching, bank/style and financial regressions. PostgreSQL localhost:55544 only. The regression run caught missing writer guards on the two new tables; both guards were added to the new migration and coverage assertions now pass, preserving account deletion and private-media checks without changing their logic.
- Admin: 82 tests passed; TypeScript/lint and production build passed. Website: 11 tests passed, syntax checks and HTTP smoke (homepage + counter script) passed. No APK binary changed or rebuilt. Full website release packaging was not rerun; the build copy list includes the new script and the APK/link were left unchanged.
- Backend evidence: `D:/EXE/artifacts/website-counter-20261015/website-counter-acceptance.trx`, `D:/EXE/artifacts/website-counter-acceptance.log`.
- Admin evidence: `D:/EXE/artifacts/website-counter-admin-tests-final.log`, `D:/EXE/artifacts/website-counter-admin-build.log`. Website evidence: `D:/EXE/artifacts/website-counter-web-tests-final.log`, `website-counter-smoke.log`.
- No production database access, environment changes, deploy, commit or push performed in this implementation. Final acceptance is not the earlier failing trial; earlier logs are retained for audit.
