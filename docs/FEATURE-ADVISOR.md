> Current HTTPS release source/gates: [ONLINE-ADVISOR.md](ONLINE-ADVISOR.md). Historical local instructions below are superseded where they enable current unpaid real-user sharing; cloud remains OFF until independent provider/data review.

# BBook Feature Advisor — summary v3 / local MVP

Current flow, all changed files and device instructions: `D:\EXE\bbeauty-app-mlkit-advisor\docs\advisor\LOCAL-E2E.md`. This supersedes schema v2. Production/default flags remain OFF. No database/migration/deploy is required for the separate local host.

`POST /api/MakeupAdvisor/recommend`, application/json, <=12 KiB. Local host authentication is an ephemeral `X-Advisor-Local` nonce. The existing MVC candidate controller still requires JWT, but Program.cs forces Advisor options OFF outside Development. The app uses only the separate loopback host; it does not forward the account JWT.

Synthetic documentation example:

```json
{"requestId":"00000000-0000-4000-8000-000000000001","consent":true,"summary":{"schemaVersion":3,"analysisMethod":"face_mesh","quality":{"frontal":true,"singleFace":true,"expressionLimited":false},"detection":{"faceCount":1,"boundingBox":{"x":0.2,"y":0.2,"width":0.5,"height":0.6},"pose":{"yaw":0,"pitch":0,"roll":0}},"features":{"outlineAspect":1.25,"eyeSpacing":0.32},"featureSources":{"outlineAspect":"face_mesh","eyeSpacing":"face_mesh"},"missing":[],"limitations":["Số đo tương đối 2D của ảnh, chưa hiệu chuẩn; không phải phân loại dáng mặt."]}}
```

Face count must be 1; box is normalized to image dimensions, positive, in [0,1]; finite yaw/pitch <=15 degrees and roll <=10. Required numeric/quality fields cannot silently default to zero/false. <=27 finite measured features with one conservative source per key, positive ratios and valid ranges, <=34 unique missing names, whitelisted limitations. Older versions, unknown nested fields, image/base64/URI/raw points/mesh Z/identity/arbitrary prompt strings are rejected. Smiling-limited input must omit expression-sensitive mouth ratios. Quality gates do not establish lighting/occlusion detection accuracy.

Backend attaches trusted metadata (formula, point group, normalization, range, quality gate, possible makeup use) to each actual feature. Input contains no local recommendation. `outlineAspect` is roll-aligned outline height/width; `eyeSpacing` eye centroid distance/outline width; `mouthWidth` lip span/outline width. Unavailable ratios remain absent; no subjective face-shape descriptor is invented.

Response is exactly `{recommendations:[...]}` with **3 distinct** supported styles (korean, portrait, event, bridal). Each row has exactly:

| Field | Type / limit |
|---|---|
| styleKey | supported enum |
| title | string <=100 |
| shortDescription, whyItFits | strings <=500 each |
| makeupFocus | 1-5 strings <=500 each |
| colorSuggestions | 1-5 strings <=80 each |
| occasionFit | 1-4 strings <=100 each |
| serviceQueryKeywords | 1-5 strings <=60 each |

No blank/control/fenced text, unknown fields or text outside JSON. Prompt forbids medical/sensitive inference, definitive face shape, undertone, confidence, invented MUA/services/prices/availability and numerical measurement leakage in UI. Common prohibited claims fail validation; semantic usefulness still requires human review. Colors and occasions are suggestions to explore, not attributes inferred from a face. JSON generation uses responseJsonSchema with minItems=maxItems=3.

Capabilities require Enabled + RealUserDataApproved + ModelAvailabilityVerified + valid model/key. Local executable also requires model `gemini-3.5-flash-lite`, binds 127.0.0.1:5088 and maps exactly two app endpoints in --serve-advisor mode. Development only, no CORS and no production backend startup. No-store, no request/response logging or persistent geometry. Twelve calls/minute, one active generation, duplicate UUID TTL5min; metadata-only admission state. Upstream one attempt, 18-second default deadline, 3840 output tokens, 32 KiB output cap. Failures return 503 LOCAL_FALLBACK and frontend shows three local ideas plus retry.

`--verify` and `--serve` remain fixed synthetic diagnostics. New `--serve-advisor` accepts validated caller summary, separate from the fixed synthetic endpoint. Do not deploy the diagnostic executable or set its flags on Render. See GEMINI-SETUP.md for local commands and free-tier data risks. No EF schema/migration or billing change.
