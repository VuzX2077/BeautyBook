START TRANSACTION;


DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20261028071420_MuaCommerceOfflineFoundation') THEN
    CREATE TABLE "MuaCommerceOrders" (
        "Id" uuid NOT NULL,
        "MuaId" uuid NOT NULL,
        "ProductId" character varying(80) NOT NULL,
        "AmountVnd" integer NOT NULL,
        "Currency" character varying(3) NOT NULL,
        "IdempotencyKey" character varying(80) NOT NULL,
        "TermsVersion" character varying(40) NOT NULL,
        "Origin" character varying(40) NOT NULL,
        "State" character varying(40) NOT NULL,
        "CreatedAt" timestamp with time zone NOT NULL,
        "ExpiresAt" timestamp with time zone NOT NULL,
        "VerifiedPaidAt" timestamp with time zone,
        CONSTRAINT "PK_MuaCommerceOrders" PRIMARY KEY ("Id"),
        CONSTRAINT "AK_MuaCommerceOrders_Id_MuaId" UNIQUE ("Id", "MuaId"),
        CONSTRAINT "CK_MuaCommerceOrders_Catalog" CHECK (("ProductId" = 'bbook_mua_activation_v1' AND "AmountVnd" = 100000) OR ("ProductId" = 'bbook_visibility_30d_v1' AND "AmountVnd" = 75000)),
        CONSTRAINT "CK_MuaCommerceOrders_Currency" CHECK ("Currency" = 'VND'),
        CONSTRAINT "CK_MuaCommerceOrders_Expiry" CHECK ("ExpiresAt" > "CreatedAt"),
        CONSTRAINT "CK_MuaCommerceOrders_Identity" CHECK (length(trim("IdempotencyKey")) > 0 AND length(trim("TermsVersion")) > 0),
        CONSTRAINT "CK_MuaCommerceOrders_Origin" CHECK ("Origin" = 'IndependentWebsite'),
        CONSTRAINT "CK_MuaCommerceOrders_State" CHECK ("State" IN ('PaymentPending','PaidAwaitingFulfillment','ManualReview','Fulfilled','Cancelled','Refunded')),
        CONSTRAINT "FK_MuaCommerceOrders_MakeupArtistProfiles_MuaId" FOREIGN KEY ("MuaId") REFERENCES "MakeupArtistProfiles" ("MUAId") ON DELETE RESTRICT
    );
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20261028071420_MuaCommerceOfflineFoundation') THEN
    CREATE TABLE "MuaCommerceAttempts" (
        "Id" uuid NOT NULL,
        "MuaId" uuid NOT NULL,
        "OrderId" uuid NOT NULL,
        "OrderCode" bigint NOT NULL,
        "Channel" character varying(80) NOT NULL,
        "PaymentLinkId" character varying(100),
        "CreatedAt" timestamp with time zone NOT NULL,
        CONSTRAINT "PK_MuaCommerceAttempts" PRIMARY KEY ("Id"),
        CONSTRAINT "CK_MuaCommerceAttempts_Reference" CHECK ("OrderCode" > 0 AND length(trim("Channel")) > 0 AND ("PaymentLinkId" IS NULL OR length(trim("PaymentLinkId")) > 0)),
        CONSTRAINT "FK_MuaCommerceAttempts_MuaCommerceOrders_OrderId_MuaId" FOREIGN KEY ("OrderId", "MuaId") REFERENCES "MuaCommerceOrders" ("Id", "MuaId") ON DELETE RESTRICT
    );
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20261028071420_MuaCommerceOfflineFoundation') THEN
    CREATE TABLE "MuaCommerceAudit" (
        "Id" uuid NOT NULL,
        "MuaId" uuid NOT NULL,
        "OrderId" uuid NOT NULL,
        "Action" character varying(50) NOT NULL,
        "EvidenceHash" character varying(64) NOT NULL,
        "CreatedAt" timestamp with time zone NOT NULL,
        CONSTRAINT "PK_MuaCommerceAudit" PRIMARY KEY ("Id"),
        CONSTRAINT "FK_MuaCommerceAudit_MuaCommerceOrders_OrderId_MuaId" FOREIGN KEY ("OrderId", "MuaId") REFERENCES "MuaCommerceOrders" ("Id", "MuaId") ON DELETE RESTRICT
    );
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20261028071420_MuaCommerceOfflineFoundation') THEN
    CREATE TABLE "MuaCommerceInbox" (
        "Id" uuid NOT NULL,
        "MuaId" uuid NOT NULL,
        "OrderId" uuid NOT NULL,
        "EventKey" character varying(64) NOT NULL,
        "EvidenceHash" character varying(64) NOT NULL,
        "Source" character varying(30) NOT NULL,
        "Outcome" character varying(40) NOT NULL,
        "ReceivedAt" timestamp with time zone NOT NULL,
        CONSTRAINT "PK_MuaCommerceInbox" PRIMARY KEY ("Id"),
        CONSTRAINT "FK_MuaCommerceInbox_MuaCommerceOrders_OrderId_MuaId" FOREIGN KEY ("OrderId", "MuaId") REFERENCES "MuaCommerceOrders" ("Id", "MuaId") ON DELETE RESTRICT
    );
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20261028071420_MuaCommerceOfflineFoundation') THEN
    CREATE TABLE "MuaCommerceOutbox" (
        "Id" uuid NOT NULL,
        "MuaId" uuid NOT NULL,
        "OrderId" uuid NOT NULL,
        "Kind" character varying(50) NOT NULL,
        "CreatedAt" timestamp with time zone NOT NULL,
        "AvailableAt" timestamp with time zone NOT NULL,
        "CompletedAt" timestamp with time zone,
        "Attempts" integer NOT NULL,
        "LastErrorCode" character varying(80),
        CONSTRAINT "PK_MuaCommerceOutbox" PRIMARY KEY ("Id"),
        CONSTRAINT "CK_MuaCommerceOutbox_Attempts" CHECK ("Attempts" >= 0),
        CONSTRAINT "FK_MuaCommerceOutbox_MuaCommerceOrders_OrderId_MuaId" FOREIGN KEY ("OrderId", "MuaId") REFERENCES "MuaCommerceOrders" ("Id", "MuaId") ON DELETE RESTRICT
    );
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20261028071420_MuaCommerceOfflineFoundation') THEN
    CREATE UNIQUE INDEX "IX_MuaCommerceAttempts_Channel_PaymentLinkId" ON "MuaCommerceAttempts" ("Channel", "PaymentLinkId");
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20261028071420_MuaCommerceOfflineFoundation') THEN
    CREATE UNIQUE INDEX "IX_MuaCommerceAttempts_OrderCode" ON "MuaCommerceAttempts" ("OrderCode");
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20261028071420_MuaCommerceOfflineFoundation') THEN
    CREATE UNIQUE INDEX "IX_MuaCommerceAttempts_OrderId" ON "MuaCommerceAttempts" ("OrderId");
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20261028071420_MuaCommerceOfflineFoundation') THEN
    CREATE INDEX "IX_MuaCommerceAttempts_OrderId_MuaId" ON "MuaCommerceAttempts" ("OrderId", "MuaId");
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20261028071420_MuaCommerceOfflineFoundation') THEN
    CREATE INDEX "IX_MuaCommerceAudit_OrderId_CreatedAt" ON "MuaCommerceAudit" ("OrderId", "CreatedAt");
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20261028071420_MuaCommerceOfflineFoundation') THEN
    CREATE INDEX "IX_MuaCommerceAudit_OrderId_MuaId" ON "MuaCommerceAudit" ("OrderId", "MuaId");
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20261028071420_MuaCommerceOfflineFoundation') THEN
    CREATE UNIQUE INDEX "IX_MuaCommerceInbox_EventKey" ON "MuaCommerceInbox" ("EventKey");
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20261028071420_MuaCommerceOfflineFoundation') THEN
    CREATE INDEX "IX_MuaCommerceInbox_OrderId_MuaId" ON "MuaCommerceInbox" ("OrderId", "MuaId");
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20261028071420_MuaCommerceOfflineFoundation') THEN
    CREATE UNIQUE INDEX "IX_MuaCommerceOrders_MuaId_IdempotencyKey" ON "MuaCommerceOrders" ("MuaId", "IdempotencyKey");
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20261028071420_MuaCommerceOfflineFoundation') THEN
    CREATE INDEX "IX_MuaCommerceOrders_State_CreatedAt" ON "MuaCommerceOrders" ("State", "CreatedAt");
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20261028071420_MuaCommerceOfflineFoundation') THEN
    CREATE UNIQUE INDEX "UX_MuaCommerceOrders_Activation" ON "MuaCommerceOrders" ("MuaId") WHERE "ProductId" = 'bbook_mua_activation_v1' AND "State" NOT IN ('Cancelled','Refunded');
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20261028071420_MuaCommerceOfflineFoundation') THEN
    CREATE INDEX "IX_MuaCommerceOutbox_CompletedAt_AvailableAt" ON "MuaCommerceOutbox" ("CompletedAt", "AvailableAt");
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20261028071420_MuaCommerceOfflineFoundation') THEN
    CREATE UNIQUE INDEX "IX_MuaCommerceOutbox_OrderId_Kind" ON "MuaCommerceOutbox" ("OrderId", "Kind");
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20261028071420_MuaCommerceOfflineFoundation') THEN
    CREATE INDEX "IX_MuaCommerceOutbox_OrderId_MuaId" ON "MuaCommerceOutbox" ("OrderId", "MuaId");
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20261028071420_MuaCommerceOfflineFoundation') THEN
    CREATE TRIGGER account_deletion_write_guard BEFORE INSERT OR UPDATE ON "MuaCommerceOrders" FOR EACH ROW EXECUTE FUNCTION public.account_deletion_write_guard();
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20261028071420_MuaCommerceOfflineFoundation') THEN
    CREATE TRIGGER private_media_write_guard BEFORE INSERT OR UPDATE ON "MuaCommerceOrders" FOR EACH ROW EXECUTE FUNCTION public.private_media_write_guard();
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20261028071420_MuaCommerceOfflineFoundation') THEN
    CREATE TRIGGER account_deletion_write_guard BEFORE INSERT OR UPDATE ON "MuaCommerceAttempts" FOR EACH ROW EXECUTE FUNCTION public.account_deletion_write_guard();
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20261028071420_MuaCommerceOfflineFoundation') THEN
    CREATE TRIGGER private_media_write_guard BEFORE INSERT OR UPDATE ON "MuaCommerceAttempts" FOR EACH ROW EXECUTE FUNCTION public.private_media_write_guard();
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20261028071420_MuaCommerceOfflineFoundation') THEN
    CREATE TRIGGER account_deletion_write_guard BEFORE INSERT OR UPDATE ON "MuaCommerceInbox" FOR EACH ROW EXECUTE FUNCTION public.account_deletion_write_guard();
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20261028071420_MuaCommerceOfflineFoundation') THEN
    CREATE TRIGGER private_media_write_guard BEFORE INSERT OR UPDATE ON "MuaCommerceInbox" FOR EACH ROW EXECUTE FUNCTION public.private_media_write_guard();
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20261028071420_MuaCommerceOfflineFoundation') THEN
    CREATE TRIGGER account_deletion_write_guard BEFORE INSERT OR UPDATE ON "MuaCommerceOutbox" FOR EACH ROW EXECUTE FUNCTION public.account_deletion_write_guard();
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20261028071420_MuaCommerceOfflineFoundation') THEN
    CREATE TRIGGER private_media_write_guard BEFORE INSERT OR UPDATE ON "MuaCommerceOutbox" FOR EACH ROW EXECUTE FUNCTION public.private_media_write_guard();
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20261028071420_MuaCommerceOfflineFoundation') THEN
    CREATE TRIGGER account_deletion_write_guard BEFORE INSERT OR UPDATE ON "MuaCommerceAudit" FOR EACH ROW EXECUTE FUNCTION public.account_deletion_write_guard();
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20261028071420_MuaCommerceOfflineFoundation') THEN
    CREATE TRIGGER private_media_write_guard BEFORE INSERT OR UPDATE ON "MuaCommerceAudit" FOR EACH ROW EXECUTE FUNCTION public.private_media_write_guard();
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20261028071420_MuaCommerceOfflineFoundation') THEN
    CREATE FUNCTION public.mua_commerce_history_guard() RETURNS trigger LANGUAGE plpgsql AS $guard$
    BEGIN
        IF TG_TABLE_NAME IN ('MuaCommerceAudit','MuaCommerceInbox') THEN
            RAISE EXCEPTION 'Commerce audit and receipts are append-only' USING ERRCODE='23514';
        ELSIF TG_OP = 'DELETE' THEN
            RAISE EXCEPTION 'Commerce financial history cannot be deleted' USING ERRCODE='23514';
        ELSIF TG_TABLE_NAME = 'MuaCommerceOrders' THEN
            IF ROW(NEW."Id",NEW."MuaId",NEW."ProductId",NEW."AmountVnd",NEW."Currency",NEW."IdempotencyKey",NEW."TermsVersion",NEW."Origin",NEW."CreatedAt",NEW."ExpiresAt")
                IS DISTINCT FROM ROW(OLD."Id",OLD."MuaId",OLD."ProductId",OLD."AmountVnd",OLD."Currency",OLD."IdempotencyKey",OLD."TermsVersion",OLD."Origin",OLD."CreatedAt",OLD."ExpiresAt")
                OR (OLD."VerifiedPaidAt" IS NOT NULL AND NEW."VerifiedPaidAt" IS DISTINCT FROM OLD."VerifiedPaidAt") THEN
                RAISE EXCEPTION 'Commerce order source is immutable' USING ERRCODE='23514';
            END IF;
        ELSIF TG_TABLE_NAME = 'MuaCommerceAttempts' THEN
            IF ROW(NEW."Id",NEW."MuaId",NEW."OrderId",NEW."OrderCode",NEW."Channel",NEW."CreatedAt")
                IS DISTINCT FROM ROW(OLD."Id",OLD."MuaId",OLD."OrderId",OLD."OrderCode",OLD."Channel",OLD."CreatedAt")
                OR (OLD."PaymentLinkId" IS NOT NULL AND NEW."PaymentLinkId" IS DISTINCT FROM OLD."PaymentLinkId") THEN
                RAISE EXCEPTION 'Commerce provider reference is immutable' USING ERRCODE='23514';
            END IF;
        END IF;
        RETURN NEW;
    END $guard$;
    CREATE TRIGGER mua_commerce_history_guard BEFORE UPDATE OR DELETE ON "MuaCommerceOrders"
        FOR EACH ROW EXECUTE FUNCTION public.mua_commerce_history_guard();
    CREATE TRIGGER mua_commerce_history_guard BEFORE UPDATE OR DELETE ON "MuaCommerceAttempts"
        FOR EACH ROW EXECUTE FUNCTION public.mua_commerce_history_guard();
    CREATE TRIGGER mua_commerce_history_guard BEFORE UPDATE OR DELETE ON "MuaCommerceInbox"
        FOR EACH ROW EXECUTE FUNCTION public.mua_commerce_history_guard();
    CREATE TRIGGER mua_commerce_history_guard BEFORE UPDATE OR DELETE ON "MuaCommerceAudit"
        FOR EACH ROW EXECUTE FUNCTION public.mua_commerce_history_guard();
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20261028071420_MuaCommerceOfflineFoundation') THEN
    INSERT INTO "__EFMigrationsHistory" ("MigrationId", "ProductVersion")
    VALUES ('20261028071420_MuaCommerceOfflineFoundation', '8.0.0');
    END IF;
END $EF$;
COMMIT;
