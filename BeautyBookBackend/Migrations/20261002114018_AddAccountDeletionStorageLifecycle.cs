using System;
using System.Collections.Generic;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace BeautyBookBackend.Migrations
{
    /// <inheritdoc />
    public partial class AddAccountDeletionStorageLifecycle : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<bool>(name: "MediaOwnershipTracked", table: "Users", type: "boolean", nullable: false, defaultValue: false);
            migrationBuilder.AddColumn<string>(name: "StorageLocationId", table: "VerificationMedia", type: "character varying(64)", maxLength: 64, nullable: true);
            migrationBuilder.AddColumn<bool>(name: "LegacyLocationVerified", table: "VerificationMedia", type: "boolean", nullable: false, defaultValue: false);
            migrationBuilder.AddColumn<DateTime>(
                name: "StorageDeletedAt",
                table: "VerificationMedia",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "AccountDeletionRequests",
                columns: table => new
                {
                    UserId = table.Column<Guid>(type: "uuid", nullable: false),
                    RequestedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    DatabaseCompletedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    StorageCompletedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    Status = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: false),
                    Attempts = table.Column<int>(type: "integer", nullable: false),
                    NextAttemptAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    ErrorCode = table.Column<string>(type: "character varying(80)", maxLength: 80, nullable: true),
                    UnresolvedReferences = table.Column<List<string>>(type: "text[]", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_AccountDeletionRequests", x => x.UserId);
                    table.ForeignKey(
                        name: "FK_AccountDeletionRequests_Users_UserId",
                        column: x => x.UserId,
                        principalTable: "Users",
                        principalColumn: "UserId",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "OwnedPublicMedia",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    OwnerId = table.Column<Guid>(type: "uuid", nullable: false),
                    ObjectKey = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: false),
                    Url = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    ReadyAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    DeletedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    StorageDeletedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_OwnedPublicMedia", x => x.Id);
                    table.ForeignKey(
                        name: "FK_OwnedPublicMedia_Users_OwnerId",
                        column: x => x.OwnerId,
                        principalTable: "Users",
                        principalColumn: "UserId",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_OwnedPublicMedia_ObjectKey",
                table: "OwnedPublicMedia",
                column: "ObjectKey",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_OwnedPublicMedia_OwnerId",
                table: "OwnedPublicMedia",
                column: "OwnerId");
            migrationBuilder.Sql("""
            CREATE FUNCTION public.account_deletion_write_guard() RETURNS trigger LANGUAGE plpgsql AS $guard$
            DECLARE data jsonb := to_jsonb(NEW); previous jsonb := '{}'::jsonb;
                    field record; ids uuid[] := ARRAY[]::uuid[]; v text; tombstone record;
                    deleting text := current_setting('bbook.deletion_owner', true);
            BEGIN
              IF NOT pg_try_advisory_xact_lock_shared(724266524669002) THEN
                RAISE EXCEPTION 'Account/media operation active; retry later' USING ERRCODE='55000';
              END IF;
              IF TG_OP='UPDATE' THEN previous := to_jsonb(OLD); END IF;
              IF TG_TABLE_NAME='Users' AND TG_OP='UPDATE' AND previous->>'DeletedAt' IS NOT NULL AND
                 (data->>'DeletedAt' IS NULL OR data->>'IsActive'='true' OR
                  data->>'Email' IS DISTINCT FROM previous->>'Email' OR data->>'FullName' IS DISTINCT FROM previous->>'FullName' OR
                  data->>'AvatarUrl' IS NOT NULL OR data->>'PhoneNumber' IS NOT NULL) THEN
                RAISE EXCEPTION 'Deleted account cannot be restored' USING ERRCODE='23514';
              END IF;
              FOR field IN SELECT key,value FROM jsonb_each_text(data) LOOP
                IF field.key IN ('UserId','OwnerId','CustomerId','MUAId','MuaId','SenderId','AuthorId','RequestedBy','CreatedByAdminId','LastHandledBy','ReviewedBy','ReviewedByAdminId','CancelledBy','DecidedBy') AND field.value IS NOT NULL THEN
                  ids := array_append(ids,field.value::uuid);
                END IF;
              END LOOP;
              IF data->>'BookingId' IS NOT NULL THEN
                ids := ids || COALESCE((SELECT ARRAY[b."CustomerId",b."MUAId"] FROM "Bookings" b WHERE b."BookingId"=(data->>'BookingId')::uuid),ARRAY[]::uuid[]);
              END IF;
              IF data->>'ChatRoomId' IS NOT NULL THEN
                ids := ids || COALESCE((SELECT ARRAY[r."CustomerId",r."MUAId"] FROM "ChatRooms" r WHERE r."ChatRoomId"=(data->>'ChatRoomId')::uuid),ARRAY[]::uuid[]);
              END IF;
              IF data->>'WalletId' IS NOT NULL THEN
                ids := ids || COALESCE((SELECT ARRAY[w."UserId"] FROM "Wallets" w WHERE w."WalletId"=(data->>'WalletId')::uuid),ARRAY[]::uuid[]);
              END IF;
              IF data->>'MessageId' IS NOT NULL THEN
                ids := ids || COALESCE((SELECT ARRAY[m."SenderId",r."CustomerId",r."MUAId"] FROM "Messages" m JOIN "ChatRooms" r ON r."ChatRoomId"=m."ChatRoomId" WHERE m."MessageId"=(data->>'MessageId')::uuid),ARRAY[]::uuid[]);
              END IF;
              IF data->>'PortfolioId' IS NOT NULL THEN
                ids := ids || COALESCE((SELECT ARRAY[p."MUAId"] FROM "Portfolios" p WHERE p."PortfolioId"=(data->>'PortfolioId')::uuid),ARRAY[]::uuid[]);
              END IF;
              IF data->>'ServiceId' IS NOT NULL THEN
                ids := ids || COALESCE((SELECT ARRAY[s."MUAId"] FROM "Services" s WHERE s."ServiceId"=(data->>'ServiceId')::uuid),ARRAY[]::uuid[]);
              END IF;
              IF data->>'ComplaintId' IS NOT NULL THEN
                ids := ids || COALESCE((SELECT ARRAY[b."CustomerId",b."MUAId"] FROM "BookingComplaints" c JOIN "Bookings" b ON b."BookingId"=c."BookingId" WHERE c."Id"=(data->>'ComplaintId')::uuid),ARRAY[]::uuid[]);
              END IF;
              IF (deleting IS NULL OR deleting='') AND EXISTS(SELECT 1 FROM "Users" u WHERE u."UserId"=ANY(ids) AND u."DeletedAt" IS NOT NULL) THEN
                RAISE EXCEPTION 'Deleted account cannot receive new data' USING ERRCODE='23514';
              END IF;
              FOR field IN SELECT key,value FROM jsonb_each(data) LOOP
                IF field.value IS DISTINCT FROM previous->field.key AND field.value <> 'null'::jsonb THEN
                  FOR tombstone IN SELECT "ObjectKey" AS key FROM "OwnedPublicMedia" WHERE "DeletedAt" IS NOT NULL
                    UNION ALL SELECT 'media:' || "Id"::text FROM "VerificationMedia" WHERE "DeletedAt" IS NOT NULL LOOP
                    IF strpos(lower(field.value::text),lower(tombstone.key))>0 OR
                       strpos(lower(field.value::text),lower(public.private_media_uri_path(tombstone.key)))>0 OR
                       strpos(lower(field.value::text),lower(replace(public.private_media_uri_path(tombstone.key),'/','%2F')))>0 THEN
                      RAISE EXCEPTION 'Deleted media cannot be attached' USING ERRCODE='23514';
                    END IF;
                  END LOOP;
                END IF;
              END LOOP;
              RETURN NEW;
            END $guard$;
            DO $install$ DECLARE t record; BEGIN
              FOR t IN SELECT table_name FROM information_schema.tables WHERE table_schema='public' AND table_type='BASE TABLE'
                AND table_name NOT IN ('VerificationMedia','OwnedPublicMedia','AccountDeletionRequests','PrivateMediaJobs','__EFMigrationsHistory') LOOP
                EXECUTE format('CREATE TRIGGER account_deletion_write_guard BEFORE INSERT OR UPDATE ON public.%I FOR EACH ROW EXECUTE FUNCTION public.account_deletion_write_guard()',t.table_name);
              END LOOP;
            END $install$;
            CREATE TRIGGER private_media_write_guard BEFORE INSERT OR UPDATE ON "OwnedPublicMedia" FOR EACH ROW EXECUTE FUNCTION public.private_media_write_guard();
            CREATE TRIGGER private_media_write_guard BEFORE INSERT OR UPDATE ON "AccountDeletionRequests" FOR EACH ROW EXECUTE FUNCTION public.private_media_write_guard();
            """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(name: "MediaOwnershipTracked", table: "Users");
            migrationBuilder.DropColumn(name: "StorageLocationId", table: "VerificationMedia");
            migrationBuilder.DropColumn(name: "LegacyLocationVerified", table: "VerificationMedia");
            migrationBuilder.Sql("""
            DO $remove$ DECLARE t record; BEGIN
              FOR t IN SELECT r.relname FROM pg_trigger g JOIN pg_class r ON r.oid=g.tgrelid JOIN pg_namespace n ON n.oid=r.relnamespace
                WHERE n.nspname='public' AND g.tgname='account_deletion_write_guard' LOOP
                EXECUTE format('DROP TRIGGER account_deletion_write_guard ON public.%I',t.relname);
              END LOOP;
            END $remove$;
            DROP FUNCTION public.account_deletion_write_guard();
            """);
            migrationBuilder.DropTable(
                name: "AccountDeletionRequests");

            migrationBuilder.DropTable(
                name: "OwnedPublicMedia");

            migrationBuilder.DropColumn(
                name: "StorageDeletedAt",
                table: "VerificationMedia");
        }
    }
}
