-- Explicit pre-paid-rollout operation, NOT executed by application startup/migrations.
-- Review/snapshot MUA IDs first. Run one batch at a time until INSERT reports zero rows.
-- Run only before paid enrollment opens; preserve the output batch IDs for the rollout audit.
BEGIN;
SET LOCAL lock_timeout = '3s';
SET LOCAL statement_timeout = '15s';
WITH batch AS (
    SELECT p."MUAId"
    FROM "MakeupArtistProfiles" p
    JOIN "Users" u ON u."UserId" = p."MUAId"
    WHERE u."DeletedAt" IS NULL AND NOT u."IsDemoAccount"
      AND NOT EXISTS (SELECT 1 FROM "MuaMonetizationAccounts" a WHERE a."MuaId" = p."MUAId")
    ORDER BY p."MUAId"
    LIMIT 1000
    FOR SHARE OF p, u SKIP LOCKED
)
INSERT INTO "MuaMonetizationAccounts" ("MuaId", "Cohort", "PolicyVersion", "EnrolledAt")
SELECT "MUAId", 'LegacyWaiver', 'mua-v1', transaction_timestamp() FROM batch
ON CONFLICT ("MuaId") DO NOTHING
RETURNING "MuaId", "Cohort", "PolicyVersion", "EnrolledAt";
COMMIT;
