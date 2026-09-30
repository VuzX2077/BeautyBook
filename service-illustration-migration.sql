START TRANSACTION;

ALTER TABLE "Services" ADD "ImageUrls" text[] NOT NULL DEFAULT ('{}'::text[]);

UPDATE "Services" SET "ImageUrls" = ARRAY["ImageUrl"] WHERE "ImageUrl" IS NOT NULL AND "ImageUrl" <> '';

INSERT INTO "__EFMigrationsHistory" ("MigrationId", "ProductVersion")
VALUES ('20260930184659_AddServiceIllustrationImages', '8.0.0');

COMMIT;

