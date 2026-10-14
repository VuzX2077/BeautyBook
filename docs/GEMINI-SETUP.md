> Current HTTPS release source/gates: [ONLINE-ADVISOR.md](ONLINE-ADVISOR.md). Historical local instructions below are superseded where they enable current unpaid real-user sharing; cloud remains OFF until independent provider/data review.

# Gemini — local Feature Advisor, 2026-10-13

Owner confirmed the configured FeatureAdvisor key and gemini-3.5-flash-lite access. The updated contract was independently verified with fixed synthetic generation: OK, selectedModelAvailable=true, validRecommendations=3. One additional synthetic HTTP smoke through the local app route returned OK/schemaVersion 3/3 recommendations. No selfie was sent, billing changed, Render key set or production flow opened.

## Existing key

Use only `FeatureAdvisor:ApiKey` (environment `FeatureAdvisor__ApiKey`) in the backend. The separate Development executable loads the existing User Secrets ID `61bf10f0-9f77-448d-a773-73c0454459cd` and environment configuration. Do not list/probe other secrets or paste keys into chat/Expo/source. The existing Configure-FeatureAdvisorKey.ps1 is available for owner-managed rotation; no need to rerun it for the currently valid key. User Secrets is outside Git but not encrypted: [Microsoft documentation](https://learn.microsoft.com/en-us/aspnet/core/security/app-secrets?view=aspnetcore-8.0).

## Local commands

```powershell
Set-Location D:\EXE\BeautyBook-feature-advisor
dotnet run --project .\BeautyBookAdvisor.Diagnostics -- --verify
& .\docs\Start-LocalAdvisor.ps1 -EnableRealData
```

The start script sets only process env and restores it after stopping: Enabled/RealUserDataApproved/ModelAvailabilityVerified=true, Model=gemini-3.5-flash-lite. Host runs Development at 127.0.0.1:5088, reads the backend key through normal configuration, never runs the production Program/DbContext/migrations. It prints a random 48-character local nonce, not a Google key. Ctrl+C stops it. Without -EnableRealData the capability stays false.

In the frontend worktree, connect a spare Android device with the Advisor debug dev client, then:

```powershell
Set-Location D:\EXE\bbeauty-app-mlkit-advisor
$advisorNonce=Read-Host 'Nonce from local host'
& .\scripts\Start-LocalAdvisor.ps1 -Nonce $advisorNonce -EnableGemini
```

This enables only the internal advisor-development profile and loopback transport, configures adb reverse and starts Metro. Each tester still opts in separately; default is on-device only. Original Explore/auth/booking API stays unchanged. No Google key/account JWT is in the Advisor request. URL/nonce are stripped from ordinary builds. Program forces the production Advisor provider OFF regardless of inherited local flags.

Complete device/debug APK guide, synthetic smoke command and changed-file inventory: `D:\EXE\bbeauty-app-mlkit-advisor\docs\advisor\LOCAL-E2E.md`. Old release preview APK contains v2 and cannot hot-reload the new flow.

## Diagnostics and fallback

`--verify` lists accessible models and performs one fixed synthetic GenerateContent request. It prints sanitized model/access/count/status only, not key, prompt, measurements or response prose. It does not establish billing, project-specific RPM/TPM/RPD or automatically persist approval flags. HTTP 429/503/timeouts/invalid output lead to one-attempt local fallback; no retries or billing upgrade.

`--serve` still maps only `/diagnostics/recommend`, with X-Advisor-Diagnostic random nonce, empty body, fixed synthetic fixture and one/minute. `--serve-advisor` instead maps `/api/MakeupAdvisor/capabilities` and `/api/MakeupAdvisor/recommend`, with X-Advisor-Local nonce and validated real-summary contract. Neither mode is deployed or registered in production. No daily-usage DB or migration is added.

## Owner-requested free tier and remaining data risk

The owner explicitly requested free tier and real summary input; the local scripts make that opt-in path available without enabling billing. This is an owner enablement decision, not evidence of provider approval. [Gemini Unpaid Services terms](https://ai.google.dev/gemini-api/terms) permit product improvement/human review and instruct against submitting personal/sensitive/confidential information. Detailed derived face geometry is not established as anonymous merely by removing the photo/identity, and consent does not override those terms. UI discloses free-tier use of data and permits on-device-only advice. No assertion that this real-user use is compliant with Google's terms is made; resolve this before external tester distribution. No paid account, financial commitment or quota increase was created.

The app sends only actual SDK-derived summary; no raw selfie, base64, coordinates, mesh Z, identity or arbitrary prompt. Recommendations forbid health/gender/ethnicity/undertone/definitive shape inference and are reference makeup ideas. Numerical validity and stable JSON do not prove makeup expertise. Human review and device acceptance remain necessary.

## Production

Do not change Render or deploy this candidate. Safe example flags remain false/key empty/model empty. Existing production Program has migration startup; do not run it against production DB to test this feature. Current local host bypasses that startup. Native APK checkpoint still has unresolved 16 KiB prebuilt RELRO findings; no new APK/device proof was produced in this continuation.
