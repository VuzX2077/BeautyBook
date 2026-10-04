# Final private Storage verification

This dedicated executable checks the approved reviewer manifest only. It is not
the earlier one-object missing-object diagnostic and cannot provision or repair.

## Configuration and command

Use the existing Storage environment configuration; never pass credentials as
arguments. The tool reads `BBOOK_Supabase__Url`, `BBOOK_Supabase__ServiceRoleKey`,
`BBOOK_Supabase__StorageBucket`, and `BBOOK_Supabase__VerificationBucket`, falling
back respectively to `SUPABASE_URL`, `SUPABASE_SERVICE_ROLE_KEY`,
`SUPABASE_STORAGE_BUCKET`, and `SUPABASE_VERIFICATION_BUCKET`.
It does not load appsettings, DB configuration, an application host or services.

Published working-tree artifact based on commit f1e70dd (not a new commit):

```powershell
dotnet "D:\EXE\BeautyBook\artifacts\storage-verification\f1e70dd-tooling\BeautyBook.StorageVerification.dll"
$LASTEXITCODE
```

This command performs production GETs when configured for production. It has not
been executed against production during preparation. Do not use provisioning,
resume Render, or enable simulation as part of this check.

## Request policy

At most four requests, in fixed order:

1. GET `/storage/v1/bucket/verification-private`; require 200 and `public=false`.
2. GET `/storage/v1/object/authenticated/verification-private/verification/13ff36b7f79d4a0a8c12d7c8fe37ec25/1f2704faa6c5961590112899a6bdc5a0.jpg`.
3. GET the same prefix with `41d38076a8dff76fb141b9cc51e7ae8e.jpg`.
4. GET the same prefix with `d6b7c8eb04977faef2db54fa58cd1746.jpg`.

All objects require 200 and exact manifest size/SHA256. The manifest is fixed in
source and cannot be overridden by CLI arguments or environment variables.
The real SupabaseVerificationStorage generates the requests and uses the existing
auth helper: sb_secret apikey only; legacy JWT apikey plus Bearer.

The tool guard rejects other methods, request bodies, paths, origins, queries,
fragments, changed response endpoints and a fifth request. Transport disables
redirects, cookies and proxies, with no retry policy. Total cancellation deadline
is 30 seconds. Bucket/error bodies are limited to 4096 bytes; each object to its
expected size. An extra byte may be read to detect overflow when length is unknown.

Downloaded bytes stay in memory. Output contains fixed object keys, status,
byte length, SHA256 and result. Provider error codes are strictly allowlisted;
raw provider bodies, headers, exception details, credentials and image content
are never output. No signed URLs or downloaded image files are created.

## Result

PASS requires three `object result: MATCH` lines, final
`result: PASS_ALL_3_OBJECTS`, and exit code 0. Every other outcome is a failure
(exit code 2). Missing objects, including HTTP 404 or 400 not_found, fail; no
upload or repair follows. Stop on failure and review only the safe output.
Verification PASS alone does not authorize runtime activation.

## Local validation

StorageVerifierTests use synthetic bytes, synthetic credentials and fake HTTP
handlers. No live Storage calls are needed. The internal fixture seam is not
accessible through the executable's arguments or configuration. The full backend
suite requires an isolated local PostgreSQL test instance.
