# BBook Advisor HTTPS — source preparation, 13/10/2026

No deploy, Render env change, Git commit/push/merge, DB migration, APK/AAB build or billing change was performed. Current Gemini project is unpaid; real facial-summary sharing stays OFF. Source is prepared for a reviewed data-processing arrangement, not evidence that Google has approved this use.

## Recommendation and alternatives

Use **an isolated Advisor staging web service first**, sharing the existing contract/provider/controller source through a .NET project reference. It has no DbContext registration, migration startup, background workers, upload endpoint or public static file route. This avoids deploying the candidate's legacy backend startup. Candidate HEAD is 22 commits behind the locally known origin/main; this is not a fresh remote fetch or proof of Render's deployed revision. Original dirty backend remains untouched.

| Topic | A: existing Render backend | B: isolated Advisor staging, implemented |
|---|---|---|
| Cost | No extra service if current instance capacity suffices; Gemini usage still separate | One extra web service; Free possible for small tests, shared workspace limits and cold starts |
| Complexity | Lowest operational complexity; same JWT middleware and user-active check | One Dockerfile/service/HTTPS URL; BBookIdentity auth delegates to existing backend |
| Authentication | Existing JWT signature/issuer/audience/expiry + active-account DB check | Forwards existing Bearer to HTTPS `GET /api/User/profile`; requires verified GUID and isActive=true; no JWT signing secret copied |
| JWT compatibility | Same app login token | Same token, validated by authoritative BBook endpoint; live deployed compatibility still needs acceptance |
| Rate limiting | Existing policy; can share API resources | Same Advisor policy, per verified account; identity verification adds a main-backend read per request |
| Production risk | Deploy executes existing main startup, migrations and unrelated workers; candidate needs rebase/review | No main startup/DB/migration; small additional authentication read load on main backend |
| Auto-deploy | Owner must inspect linked branch/Auto-Deploy before pushing | Owner uses candidate branch and Auto-Deploy Off; main need not be merged |
| Rollback | Disable cloud/env gate and roll back code; DB migrations are not rolled back by code rollback | Disable cloud flag or roll back service; no database state to reverse |

Render Free sleeps after 15 minutes idle, can take about one minute to wake, and has **750 shared free hours/workspace/month**. Two free services may consume the same allowance. It is unsuitable for promising consistently short latency. App preflight times out and keeps local advice; manual retry after wake can work. No compute plan has been purchased. Check account-specific limits and current [pricing](https://render.com/pricing); [Free limits](https://render.com/docs/free). Auto-deploy behavior is documented by [Render](https://render.com/docs/deploys). Existing instance plan/branch/settings were not inspected through an authenticated dashboard.

For a later production MVP, A can save the extra service once the backend is reconciled with current main, unrelated migrations/startup are reviewed and the feature is accepted. It uses the same controller/provider, not a second AI implementation. Do not set ASPNETCORE_ENVIRONMENT=Development on Render.

## Service implementation and security

`BeautyBookAdvisor.Api` references backend assembly but never executes its Program. `AdvisorApiHost` registers only `MakeupAdvisorController` plus `/health`. No diagnostics, booking, login or upload controllers are mapped. Default appsettings contains only safe OFF values. Static MSBuild publish-file inspection confirms legacy backend appsettings are excluded; Docker build/deploy remains owner work.

HTTPS is required before auth/body handling. Render terminates TLS; forwarded scheme is trusted only when both `AdvisorHost:TrustRenderProxy=true` and platform-provided `RENDER=true`. Generic hosts do not trust a caller's forwarded scheme. Kestrel request limit is 12 KiB; strict summary v3 validation, one active generation/account, duplicate UUID TTL5min. Fixed-window **12 HTTP requests/minute/account**, shared by capabilities + recommend (normally 2 requests/scan), no queue; per-process protection, intended for one MVP instance.

Identity verification uses a dedicated no-redirect HTTPS client, 3-second deadline and 64 KiB response cap. It discards profile fields except authenticated userId/isActive, forwards no facial data and stores no token/profile. Invalid/expired/deleted/inactive account, upstream auth error, malformed or oversized profile fail closed. A failure appears as Advisor unavailable without logging the user out of the rest of BBook. There is no new age/region database model; reviewed audience/region eligibility remains a release requirement. Identity API path/JSON contract must be validated with a real tester login after owner deploy. Authentication reads occur before the Advisor's authenticated limiter; monitor read load for the small tester pool.

Provider request contains summary + trusted measurement metadata, never local recommendations, raw selfie/base64/point arrays/JWT/userId. Model gemini-3.5-flash-lite. JSON output exactly 3 unique supported styles, 8 fields, 4-5 regional makeupFocus strings. One attempt, default 18-second Gemini deadline, 3840 output tokens, 32 KiB response cap. 429/503/timeout/invalid schema or prohibited assertions → 503 LOCAL_FALLBACK; client shows on-device advice. HTTP/provider/auth loggers are disabled in isolated host, no geometry/body persistence. Main backend does not add request-body logging for Advisor.

## Gemini approval gate

Current [Gemini terms](https://ai.google.dev/gemini-api/terms) prohibit personal/sensitive/confidential submissions to Unpaid Services and allow review/product improvement. Removing a selfie does not establish face geometry as anonymous. Consent does not override provider terms. **Inference:** current unpaid project is not a reviewed basis for real face-summary sharing; cloud OFF. Paid-data terms, eligible region/audience (including Google's age restrictions), privacy notice and consent must be reviewed before owner enables real data. Nothing automatically activates billing or certifies compliance.

All of these are required: Enabled, CloudEnabled, RealUserDataApproved, DataProcessingApproval=approved, ProviderDataPolicy=paid_terms, ModelAvailabilityVerified, configured model/key, and online environment approval outside Development. The paid_terms value describes an independently reviewed applicable processing basis, not a billing API check; do not set it merely to bypass the gate on the current unpaid project. Synthetic diagnostics remains independent and local-only. Legacy `-EnableRealData`/`-EnableGemini` switches cannot automatically approve the current unpaid setup.

## Render settings for owner deployment

Create a **separate Docker web service**, candidate/feature-summary-advisor branch, repository root as Docker context, Dockerfile path `Dockerfile.advisor`, health path `/health`, Auto-Deploy **Off**, no pre-deploy migration command and no DB variables. Leave original service configuration unchanged. Owner decides plan/region and Git push; no main merge is required for staging.

| Variable | Safe initial value / owner action |
|---|---|
| ASPNETCORE_ENVIRONMENT | Staging |
| AdvisorHost__IdentityApiUrl | https://beautybook-13zj.onrender.com/api |
| AdvisorHost__TrustRenderProxy | true on Render managed ingress only |
| FeatureAdvisor__Enabled | false |
| FeatureAdvisor__CloudEnabled | false |
| FeatureAdvisor__OnlineEnvironmentApproved | false |
| FeatureAdvisor__RealUserDataApproved | false |
| FeatureAdvisor__DataProcessingApproval | pending |
| FeatureAdvisor__ProviderDataPolicy | unpaid |
| FeatureAdvisor__ModelAvailabilityVerified | false until checked for the configured Render project/key |
| FeatureAdvisor__Model | gemini-3.5-flash-lite |
| FeatureAdvisor__ApiKey | Owner enters key in secret field; absent initially is safe |
| FeatureAdvisor__TimeoutSeconds | 18 |
| FeatureAdvisor__RequireHttps | true |
| FeatureAdvisor__SyntheticDiagnosticsEnabled | false; forcibly off outside Development |
| PORT / RENDER | Provided by Render; do not invent values |

No Jwt__Key/Issuer/Audience, local nonce, connection string or selfie storage is required for option B. Existing BBook API verifies JWT. Never put Google key in Expo public env, APK/AAB or Git. Do not redeploy diagnostics as the online service.

Owner sequence: review/stage source → commit/push approved candidate files after inspecting Auto-Deploy → create service with safe OFF env → manual deploy → inspect HTTPS health and no-token 401 → verify logged-in capabilities shows false/schema3 → complete provider/data/audience review → explicitly enable reviewed server flags → set frontend HTTPS URL to the new service `/api`, cloud=1 and approval=approved in the chosen EAS profile → build manually → consent/runtime acceptance → closed testing. URL shown in current profile is the existing production API placeholder and cloud stays off; it does not claim online Advisor is already deployed there.

For rollback, owner turns cloud/Enabled OFF first (capabilities false → local fallback), then uses Render rollback/manual pinned commit with Auto-Deploy Off. Free service supports only limited recent rollback history; see Render's current docs. Reconcile candidate with current main separately before option A; no old image-upload/daily-quota migrations should be copied into this candidate.

## Source tests

Advisor .NET tests: **56 PASS**. Full backend: **487 PASS, 99 SKIP, 0 FAIL**; skipped tests need DB fixtures and were not run against production. HTTPS, forwarded proxy trust, valid/invalid/inactive identity, missing JWT/local nonce rejection, rate limiting, v3 request/output, approved mock-provider pipeline, pending/unpaid gates and fallback are covered. Gemini synthetic diagnostic with updated 4-region schema: OK, selectedModelAvailable=true, syntheticOnly=true, validRecommendations=3. No real-token online deployment or real selfie was tested.
