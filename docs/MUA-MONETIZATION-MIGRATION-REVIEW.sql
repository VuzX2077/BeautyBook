START TRANSACTION;


DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20261027085058_MuaMonetizationFoundation') THEN
    CREATE TABLE "MuaMonetizationAccounts" (
        "MuaId" uuid NOT NULL,
        "Cohort" character varying(30) NOT NULL,
        "PolicyVersion" character varying(40) NOT NULL,
        "EnrolledAt" timestamp with time zone NOT NULL,
        "ActivationPurchaseReference" character varying(160),
        "ActivatedAt" timestamp with time zone,
        "ActivationRevokedAt" timestamp with time zone,
        "FirstPublishedAt" timestamp with time zone,
        CONSTRAINT "PK_MuaMonetizationAccounts" PRIMARY KEY ("MuaId"),
        CONSTRAINT "CK_MuaMonetizationAccounts_Cohort" CHECK ("Cohort" IN ('LegacyWaiver','RolloutWaiver','PaidActivation')),
        CONSTRAINT "CK_MuaMonetizationAccounts_PaidActivation" CHECK (("ActivatedAt" IS NULL AND "ActivationPurchaseReference" IS NULL) OR ("Cohort" = 'PaidActivation' AND "ActivatedAt" IS NOT NULL AND length(trim("ActivationPurchaseReference")) > 0 AND "ActivationPurchaseReference" IS NOT NULL)),
        CONSTRAINT "CK_MuaMonetizationAccounts_Revocation" CHECK ("ActivationRevokedAt" IS NULL OR ("ActivatedAt" IS NOT NULL AND "ActivationRevokedAt" >= "ActivatedAt")),
        CONSTRAINT "FK_MuaMonetizationAccounts_MakeupArtistProfiles_MuaId" FOREIGN KEY ("MuaId") REFERENCES "MakeupArtistProfiles" ("MUAId") ON DELETE RESTRICT
    );
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20261027085058_MuaMonetizationFoundation') THEN
    CREATE TABLE "MuaVisibilityGrants" (
        "Id" uuid NOT NULL,
        "MuaId" uuid NOT NULL,
        "Kind" character varying(30) NOT NULL,
        "SourceReference" character varying(160) NOT NULL,
        "StartsAt" timestamp with time zone NOT NULL,
        "EndsAt" timestamp with time zone NOT NULL,
        "RevokedAt" timestamp with time zone,
        "RevocationReason" character varying(500),
        "CreatedAt" timestamp with time zone NOT NULL,
        CONSTRAINT "PK_MuaVisibilityGrants" PRIMARY KEY ("Id"),
        CONSTRAINT "CK_MuaVisibilityGrants_Interval" CHECK ("EndsAt" > "StartsAt"),
        CONSTRAINT "CK_MuaVisibilityGrants_Kind" CHECK ("Kind" IN ('FirstActivationGift','Paid','Compensation')),
        CONSTRAINT "CK_MuaVisibilityGrants_Revocation" CHECK (("RevokedAt" IS NULL AND "RevocationReason" IS NULL) OR ("RevokedAt" IS NOT NULL AND "RevocationReason" IS NOT NULL AND length(trim("RevocationReason")) > 0)),
        CONSTRAINT "CK_MuaVisibilityGrants_Source" CHECK (length(trim("SourceReference")) > 0),
        CONSTRAINT "FK_MuaVisibilityGrants_MuaMonetizationAccounts_MuaId" FOREIGN KEY ("MuaId") REFERENCES "MuaMonetizationAccounts" ("MuaId") ON DELETE RESTRICT
    );
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20261027085058_MuaMonetizationFoundation') THEN
    CREATE UNIQUE INDEX "IX_MuaMonetizationAccounts_ActivationPurchaseReference" ON "MuaMonetizationAccounts" ("ActivationPurchaseReference");
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20261027085058_MuaMonetizationFoundation') THEN
    CREATE INDEX "IX_MuaMonetizationAccounts_Cohort_EnrolledAt" ON "MuaMonetizationAccounts" ("Cohort", "EnrolledAt");
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20261027085058_MuaMonetizationFoundation') THEN
    CREATE INDEX "IX_MuaVisibilityGrants_MuaId_EndsAt" ON "MuaVisibilityGrants" ("MuaId", "EndsAt");
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20261027085058_MuaMonetizationFoundation') THEN
    CREATE UNIQUE INDEX "IX_MuaVisibilityGrants_SourceReference" ON "MuaVisibilityGrants" ("SourceReference");
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20261027085058_MuaMonetizationFoundation') THEN
    CREATE UNIQUE INDEX "UX_MuaVisibilityGrants_FirstGift" ON "MuaVisibilityGrants" ("MuaId") WHERE "Kind" = 'FirstActivationGift';
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20261027085058_MuaMonetizationFoundation') THEN
    CREATE TRIGGER account_deletion_write_guard BEFORE INSERT OR UPDATE ON "MuaMonetizationAccounts"
        FOR EACH ROW EXECUTE FUNCTION public.account_deletion_write_guard();
    CREATE TRIGGER private_media_write_guard BEFORE INSERT OR UPDATE ON "MuaMonetizationAccounts"
        FOR EACH ROW EXECUTE FUNCTION public.private_media_write_guard();
    CREATE TRIGGER account_deletion_write_guard BEFORE INSERT OR UPDATE ON "MuaVisibilityGrants"
        FOR EACH ROW EXECUTE FUNCTION public.account_deletion_write_guard();
    CREATE TRIGGER private_media_write_guard BEFORE INSERT OR UPDATE ON "MuaVisibilityGrants"
        FOR EACH ROW EXECUTE FUNCTION public.private_media_write_guard();

    CREATE FUNCTION public.mua_monetization_history_guard() RETURNS trigger LANGUAGE plpgsql AS $guard$
    BEGIN
        IF TG_TABLE_NAME = 'MuaMonetizationAccounts' THEN
            IF NEW."MuaId" IS DISTINCT FROM OLD."MuaId"
                OR NEW."Cohort" IS DISTINCT FROM OLD."Cohort"
                OR NEW."PolicyVersion" IS DISTINCT FROM OLD."PolicyVersion"
                OR NEW."EnrolledAt" IS DISTINCT FROM OLD."EnrolledAt"
                OR (OLD."FirstPublishedAt" IS NOT NULL AND NEW."FirstPublishedAt" IS DISTINCT FROM OLD."FirstPublishedAt") THEN
                RAISE EXCEPTION 'Monetization enrollment history cannot be rewritten' USING ERRCODE='23514';
            END IF;
        ELSE
            IF NEW."Id" IS DISTINCT FROM OLD."Id"
                OR NEW."MuaId" IS DISTINCT FROM OLD."MuaId"
                OR NEW."Kind" IS DISTINCT FROM OLD."Kind"
                OR NEW."SourceReference" IS DISTINCT FROM OLD."SourceReference"
                OR NEW."StartsAt" IS DISTINCT FROM OLD."StartsAt"
                OR NEW."EndsAt" IS DISTINCT FROM OLD."EndsAt"
                OR NEW."CreatedAt" IS DISTINCT FROM OLD."CreatedAt"
                OR (OLD."RevokedAt" IS NOT NULL AND NEW."RevokedAt" IS DISTINCT FROM OLD."RevokedAt") THEN
                RAISE EXCEPTION 'Visibility history cannot be rewritten; use a compensation grant' USING ERRCODE='23514';
            END IF;
        END IF;
        RETURN NEW;
    END $guard$;
    CREATE TRIGGER mua_monetization_history_guard BEFORE UPDATE ON "MuaMonetizationAccounts"
        FOR EACH ROW EXECUTE FUNCTION public.mua_monetization_history_guard();
    CREATE TRIGGER mua_monetization_history_guard BEFORE UPDATE ON "MuaVisibilityGrants"
        FOR EACH ROW EXECUTE FUNCTION public.mua_monetization_history_guard();
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20261027085058_MuaMonetizationFoundation') THEN
    INSERT INTO "__EFMigrationsHistory" ("MigrationId", "ProductVersion")
    VALUES ('20261027085058_MuaMonetizationFoundation', '8.0.0');
    END IF;
END $EF$;
COMMIT;
