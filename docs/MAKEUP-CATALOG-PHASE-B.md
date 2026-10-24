# Phase B — Backend Makeup Catalog Core

## Scope and safety

Backend only. No mobile/admin UI, portfolio tagging/link tables, safe merge, Advisor mapping table, scoring, ML Kit or Gemini changes. The existing MakeupStyles/MUAStyles/ServiceStyles identities and UnifiedStyle transaction/locking/retry/idempotency remain in use.

New schema: columns on MakeupStyles (Key, Source, CreatedByUserId, ReviewStatus, SortOrder, UpdatedAt, ArchivedAt, Version, immutable SystemDefinition), plus MakeupCatalogTerms, MakeupStyleTermLinks and MakeupCatalogChanges. Legacy Key remains nullable until reviewed import; stable StyleId is always available. Historical Source=LegacyUnknown is not guessed. Custom final-form saves generate stable style-* keys, remain active immediately with Pending review (Admin creation is Approved).

Migration: `20261018003245_MakeupCatalogCore`. Its Up is expansion only, preserves existing IDs/links and does not seed data. Account deletion/private-media barriers cover all new tables. PostgreSQL triggers and DbContext checks protect assigned keys and original baselines. UpdatedAt for legacy records initializes at migration time, not an invented historical edit time. Down removes the expansion and is destructive after adoption; do not use against live data.

Reviewed all repository migration files/snapshot before scaffolding, including the pre-existing untracked `20261015000000_AddWebsiteApkCounters`. That migration is operational SQL outside the EF entity snapshot and was not changed. Live pending migration history has NOT been queried. Production Program.cs auto-applies pending migrations on startup: do not deploy this branch or run database update against production/closed-test without separate approval of the entire pending list.

## Public API

`GET /api/makeup-catalog` returns:

- `version`: content SHA256, also quoted in ETag; matching If-None-Match returns304.
- `styles`: all active, non-archived styles, not restricted by artistCount. Fields styleId/key/name/description/sortOrder/isActive/metadata.
- `terms`: active items, including groups. Fields id/kind/key/label/description/groupId/broaderTermId/sortOrder/isActive/archivedAt/version. Group-inactive children are excluded from selection.
- Cache-Control public,max-age=60; no creator IDs, review log or engine rules. Catalog metadata is NOT a claim that Advisor supports every new key. Clients must keep the existing engine capability whitelist.

Kinds: occasion, intensity, color-tone, effect, occasion-group, effect-group. GroupId is typed by validation; broader hierarchy is only for occasion, with cycle checks. Intensity keys are fixed to light/medium/bold. No new intensity level can be created. Keys are lowercase ASCII slug identifiers, max100; labels NFKC/whitespace normalized, max100, unique case-insensitively within Kind. Description max2000 (style description remains255).

## Admin API contracts

All these controllers have backend Admin role authorization; mutation services also validate the live actor and demo-account policy. No DELETE/hard-delete or merge endpoint.

Existing routes remain compatible:

- GET `/api/admin/makeup-styles?page=1&pageSize=10&search=&status=all&source=MUA&reviewStatus=Pending`
- GET `/api/admin/makeup-styles/{id}`
- POST `/api/admin/makeup-styles`
- PUT `/api/admin/makeup-styles/{id}`
- PATCH `/api/admin/makeup-styles/{id}/status`

Style writes: required name, optional description/key/sortOrder/metadataTermIds/expectedVersion. Key is accepted on creation for ordinary styles, but the20 canonical keys are reserved for reviewed import. Update key must match existing identity and cannot assign/change a legacy key. Omitted sort/metadata preserves them; metadataTermIds=[] clears editable metadata. Max100 distinct term IDs, no group IDs, at most one intensity. Adding inactive/archived metadata or metadata under an inactive group is rejected; historical inactive links may be retained/removed. Baseline fields cannot be edited through this DTO.

Style read adds key/sortOrder/source/reviewStatus/archivedAt/version/metadata to the original fields. Legacy clients can ignore additions. New clients should always send expectedVersion from the read: stale edits return409. For compatibility, existing callers may omit it; shared catalog locking still serializes writes. Production writes must continue through these services, not raw SQL.

Additional style operations:

- GET `/api/admin/makeup-styles/{id}/usage`
- POST `/api/admin/makeup-styles/{id}/archive` with `{expectedVersion,reason}`; keeps references, disables selection.
- POST `/api/admin/makeup-styles/{id}/review` with `{status:"Approved"|"Rejected",expectedVersion,reason}`. Only Source=MUA, non-archived styles; required reason max500. Reject disables selection but preserves links. Approving does not silently reactivate a rejected style; Admin may then explicitly activate it. Archived styles cannot reactivate through status.

Term operations:

- GET `/api/admin/makeup-catalog/terms?kind=&search=&status=all&groupId=&page=1&pageSize=20`
- GET `/api/admin/makeup-catalog/terms/{id}`
- POST `/api/admin/makeup-catalog/terms` with `{kind,key,label,description,groupId,broaderTermId,sortOrder}`
- PUT `/api/admin/makeup-catalog/terms/{id}` with label/description/groupId/broaderTermId/sortOrder/expectedVersion. Optional key/kind, if supplied, must equal original. Put is a full editable replacement: null parent IDs explicitly clear the relationship where allowed.
- PATCH `/api/admin/makeup-catalog/terms/{id}/status` with `{isActive,expectedVersion,reason}`
- POST `/api/admin/makeup-catalog/terms/{id}/archive` with `{expectedVersion,reason}`
- GET `/api/admin/makeup-catalog/terms/{id}/usage`

Style list filters: status all/active/inactive/archived; source System/Admin/MUA/LegacyUnknown; review Pending/Approved/Rejected/NotApplicable. Term Kind/group/status filters use the same pagination envelope `{items,total,page,pageSize}`, pageSize<=100.

Usage is distinct persisted references: muaCount/serviceCount/styleMetadataCount/childTermCount. Term MUA/service counts are INDIRECT via metadata-linked styles (groups include children), not direct profile tag selections. PortfolioCount=null and portfolioUsage=unavailable-until-structured-tagging: do not display a false zero or infer portfolio ownership from legacy text. Usage includes historical/inactive references for safety.

Errors use existing StyleWriteException Code/Message filter: CATALOG_KEY_INVALID/RESERVED/IMMUTABLE, DUPLICATE, VERSION_CONFLICT, ITEM_INACTIVE, GROUP_INVALID/REQUIRED, CYCLE, ARCHIVED, REVIEW_INVALID. Unique/concurrency races become409 CATALOG_WRITE_CONFLICT. Existing STYLE_* codes and legacy request semantics remain unchanged.

For the next bbook-admin phase: extend existing styles page and explicit proxy allowlist for these paths/methods; reuse existing session/same-origin protection. Never add arbitrary admin proxy access, DELETE or a mutable baseline/rules editor. Six UI tabs can consume one term service by Kind. Group records must never be selectable as style metadata.

## Import source and reviewed manifest

Embedded original input: `BeautyBookBackend/Data/MakeupCatalog/catalog-v1.3.json`, copied without changing bytes from the supplied download. Original SHA256:
`8F45E1B7DAA4E6F7BA097BDFCE536D7CAC56F1409E88D24669B7603DA85C710F`.

Independent baseline: `Data/MakeupCatalog/baseline-v1.2.json`, copied read-only from the audited mobile v1.2 source. Its20 objects match v1.3 exactly. Neither mobile runtime file was modified.

Importer validates schema1.3/vi-VN/type,20 baseline objects, exact reviewed taxonomy counts/key sets and initial artifact content. Admin edits occur via APIs, not by modifying the import file. The manifest SHA hashes the actual UTF8 source text supplied to the importer. Labels/source changes require a new reviewed import version, not silently overwriting the original baseline.

Manifest shape:

```json
{
  "sourceSha256": "SHA256_OF_THE_REVIEWED_SOURCE",
  "styles": [
    {"key":"soft-korean","existingStyleId":123,"expectedName":"EXACT_DB_NAME","expectedVersion":1,"createMissing":false}
  ]
}
```

This example is NOT an operational manifest: it deliberately omits the other19 keys and has a placeholder ID. A valid manifest contains exactly20 unique canonical keys. Each must explicitly map a real existing ID with expected name/version, or explicitly authorize createMissing=true with no existing ID. No approximate/name-based inference. Existing name collisions stop createMissing and require review. One ID cannot represent two canonical keys.

No production/closed-test reviewed ID manifest was supplied, so import against actual data is intentionally not performed. Synthetic createMissing manifests belong only to disposable test fixtures and must not be reused against real data.

Operator CLI (not startup, no HTTP import endpoint):

```powershell
# Set this variable yourself to an explicitly disposable LOCAL beautybook_test_* DB.
# Never load it from production appsettings or put credentials in command arguments.
$env:BBOOK_CATALOG_TEST_CONNECTION = '<LOCAL_TEST_CONNECTION>'
dotnet run --project BeautyBook.StyleMaintenance --no-build -- `
  --catalog-import --connection-env BBOOK_CATALOG_TEST_CONNECTION `
  --confirm-database beautybook_test_REVIEWED_NAME `
  --source BeautyBookBackend/Data/MakeupCatalog/catalog-v1.3.json `
  --manifest '<REVIEWED_MANIFEST_PATH>'
```

Without --apply it is a dry-run. It prints inserted (PLANNED), unchanged, conflicts, source hash, dryRun and null batchId. Missing/mismatched manifest returns conflicts and exit2, with no writes. --apply always performs a dry-run first, stops on any conflict, then revalidates inside a new transaction with the shared catalog lock. The service independently rejects any non-loopback/non-confirmed/non-beautybook_test_* PostgreSQL target. Only in-memory SQLite tests are additionally allowed. This is NOT a production import command.

Apply never overwrites an existing canonical baseline or later Admin label/status/metadata edits. Existing reviewed IDs keep original labels/status/source/provenance and links; only the previously null canonical key/baseline are assigned. Newly created canonical records have Source=System and receive baseline occasion/intensity metadata. No automatic color/effect mapping from descriptive strings; no portfolio claims or engine change. Successful apply records per-row snapshots/hash/batch in the journal. Second identical apply is a no-op. Any exception rolls back rows, links and journal together; reported conflicts do not apply partial plans.

Rollback: prefer rolling back API consumers and retaining additive schema. Use import batch before/after/version records for a reviewed compensating operation. Do not delete any newly referenced item; archive it. A generic destructive rollback or production importer is intentionally not exposed. Back up and rehearse on disposable PostgreSQL before any future rollout.

## Verification and remaining release gate

Actual build/test results and the exact changed-file list are recorded after final verification in this document's completion section. The previous testhost timeout is reproducible inside the sandbox and disappears when running the same SQLite tests outside it (14 existing Admin tests passed on the initial diagnostic run). PostgreSQL tests use a newly initialized instance bound to127.0.0.1:55483 under D:\EXE\artifacts; the existing harness creates/drops only beautybook_test_* databases there. No real database credentials/configuration are used.

Before any deployment: review all pending migrations including unrelated pre-existing changes, obtain a real reviewed canonical-ID manifest, rehearse on a restored isolated test copy, review retained legacy names/source classifications, then separately approve a production rollout. This Phase B does not authorize deployment or production migration/import.

## Files changed for Phase B

Existing files extended:

- BeautyBook.StyleMaintenance/Program.cs
- BeautyBookBackend/BeautyBookBackend.csproj
- BeautyBookBackend/Controllers/AdminMakeupStylesController.cs
- BeautyBookBackend/DTOs/AdminMakeupStyleDtos.cs
- BeautyBookBackend/Data/ApplicationDbContext.cs
- BeautyBookBackend/Migrations/ApplicationDbContextModelSnapshot.cs
- BeautyBookBackend/Models/MakeupStyle.cs
- BeautyBookBackend/Program.cs (only two catalog DI registrations; preserve pre-existing unrelated edits)
- BeautyBookBackend/Services/AdminMakeupStyleService.cs
- BeautyBookBackend/Services/StyleWriteExceptionFilter.cs (catalog error handling is scoped to catalog entities/actions)
- BeautyBookBackend/Services/UnifiedStyleService.cs (provenance/key/review/audit on successful custom creation)
- BeautyBookBackend.Tests/UnifiedStyleTests.cs (use a real Admin fixture only in the administrative-rename test; retain onboarding's single-user fixture)

New files:

- BeautyBookBackend/Controllers/MakeupCatalogController.cs
- BeautyBookBackend/Controllers/AdminMakeupCatalogController.cs
- BeautyBookBackend/DTOs/MakeupCatalogDtos.cs
- BeautyBookBackend/Data/MakeupCatalogModel.cs
- BeautyBookBackend/Data/MakeupCatalog/baseline-v1.2.json
- BeautyBookBackend/Data/MakeupCatalog/catalog-v1.3.json
- BeautyBookBackend/Models/MakeupCatalog.cs
- BeautyBookBackend/Services/MakeupCatalogService.cs
- BeautyBookBackend/Services/MakeupCatalogImportService.cs
- BeautyBookBackend/Migrations/20261018003245_MakeupCatalogCore.cs
- BeautyBookBackend/Migrations/20261018003245_MakeupCatalogCore.Designer.cs
- BeautyBookBackend.Tests/MakeupCatalogCoreTests.cs
- docs/MAKEUP-CATALOG-PHASE-B.md

Pre-existing DatabasePoolPolicy/WebsiteCounter/website analytics files and migration are outside this implementation and were not authored or removed by Phase B. No frontend repository was edited in this phase.

## Completion results — 18/10/2026

- Backend/test-project build: SUCCESS,0 errors. Final incremental build0 warnings; earlier full compilation reported3 pre-existing warnings in ServiceController/MuaService (no new catalog compiler warnings).
- StyleMaintenance CLI build: SUCCESS,0 errors. NU1900 warning: network could not reach NuGet vulnerability feed; no package was added and no production connection was used.
- Final regression: **51 passed,0 failed,0 skipped**, including all selected UnifiedStyleTests, AdminMakeupStyleTests, PostgreSqlMakeupStyleTests, AdvisorStyleMatchingTests and8 new MakeupCatalogCoreTests. SQLite and real PostgreSQL HTTP/migration/concurrency cases executed. The Admin fixture correction is isolated to its rename test; onboarding's original single-user assumptions remain intact.
- EF model check: `No changes have been made to the model since the last migration.`
- Migration generated and applied successfully only on the newly created localhost disposable databases; existing legacy service/profile relations and six seeded legacy styles were retained. No live migration history was queried.
- Import tests: missing manifest blocks with no writes; approved synthetic disposable fixtures dry-run115 planned inserts (20 styles +95 taxonomy/groups), then apply, repeat no-op, preserve later Admin edits and reviewed legacy IDs/references, reject changed baseline and roll back injected failures. Concurrent PostgreSQL imports yield one insert run and one unchanged run.
- Actual CLI dry-run without a real reviewed manifest: **exit2,1 conflict,0 inserts,batchId=null**. The dedicated CLI test DB remained6 legacy styles/0 terms/0 catalog journal rows. This is a successful safety block, NOT a completed real-data import.
- Final test log: `D:\EXE\artifacts\catalog-phaseB-tests-ready.log`; TRX: `D:\EXE\artifacts\catalog-phaseB-tests\catalog-phaseB-ready.trx`.
- Review SQL: `D:\EXE\artifacts\catalog-phaseB-migration.sql`; dry-run report: `D:\EXE\artifacts\catalog-phaseB-dryrun-missing-manifest.json`.
- Testhost diagnosis: loopback process communication was restricted inside the sandbox (also psql returned socket permission denied); running local tests outside the sandbox resolved it. Build and test are run sequentially on Windows to avoid DLL copy locks.
- Still NOT performed: mapping/import for real data (no reviewed canonical key→StyleId manifest supplied), production/closed-test DB changes, frontend changes, portfolio tagging, safe merge, Advisor mapping/scoring changes, APK/AAB, commit/push/merge or deploy.

The local PostgreSQL test instance is stopped at handoff; its files/logs remain under D:\EXE\artifacts for inspection. No test service is intentionally left running. Before any deployment, the rollout and complete pending-migration set still require separate approval, especially because production startup auto-applies migrations.
