START TRANSACTION;


DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20261005071302_AddWorkLocationAndBookingDestination') THEN
    ALTER TABLE "MakeupArtistProfiles" ADD "AllowCustomerVisit" boolean NOT NULL DEFAULT FALSE;
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20261005071302_AddWorkLocationAndBookingDestination') THEN
    ALTER TABLE "MakeupArtistProfiles" ADD "WorkLocationAddress" character varying(500);
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20261005071302_AddWorkLocationAndBookingDestination') THEN
    ALTER TABLE "MakeupArtistProfiles" ADD "WorkLocationName" character varying(100);
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20261005071302_AddWorkLocationAndBookingDestination') THEN
    ALTER TABLE "Bookings" ADD "ServiceLocationName" character varying(100);
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20261005071302_AddWorkLocationAndBookingDestination') THEN
    ALTER TABLE "Bookings" ADD "ServiceLocationType" character varying(30);
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20261005071302_AddWorkLocationAndBookingDestination') THEN
    INSERT INTO "__EFMigrationsHistory" ("MigrationId", "ProductVersion")
    VALUES ('20261005071302_AddWorkLocationAndBookingDestination', '8.0.0');
    END IF;
END $EF$;
COMMIT;

