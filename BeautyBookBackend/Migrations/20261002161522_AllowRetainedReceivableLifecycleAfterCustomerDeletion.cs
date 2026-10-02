using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace BeautyBookBackend.Migrations
{
    public partial class AllowRetainedReceivableLifecycleAfterCustomerDeletion : Migration
    {
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("""
            CREATE OR REPLACE FUNCTION public.account_deletion_write_guard() RETURNS trigger LANGUAGE plpgsql AS $guard$
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
              IF (deleting IS NULL OR deleting='') AND EXISTS(SELECT 1 FROM "Users" u WHERE u."UserId"=ANY(ids) AND u."DeletedAt" IS NOT NULL)
              AND NOT (
                TG_TABLE_SCHEMA='public' AND TG_TABLE_NAME='MuaReceivables' AND TG_OP='UPDATE'
                -- Existing ledger only: no owner/reference/amount/creation changes, no inserts.
                AND (data - ARRAY['Status','UpdatedAt','AvailableAt','FrozenAt','PaidOutAt','ReversedAt'])
                  = (previous - ARRAY['Status','UpdatedAt','AvailableAt','FrozenAt','PaidOutAt','ReversedAt'])
                AND previous->>'Status' IN ('0','1','2','3')
                AND (previous->>'Status' = data->>'Status' OR
                  (previous->>'Status',data->>'Status') IN
                    (('0','1'),('0','2'),('0','5'),('1','2'),('1','3'),('1','5'),
                     ('2','0'),('2','1'),('2','5'),('3','0'),('3','1'),('3','2'),('3','4')))
                AND EXISTS (
                  SELECT 1 FROM "Bookings" b JOIN "Users" mua ON mua."UserId"=b."MUAId"
                  JOIN "Users" customer ON customer."UserId"=b."CustomerId"
                  WHERE b."BookingId"=(data->>'BookingId')::uuid
                    AND b."MUAId"=(data->>'MuaId')::uuid AND b."Status" IN (2,10)
                    AND mua."DeletedAt" IS NULL AND mua."IsActive"=TRUE
                    AND customer."DeletedAt" IS NOT NULL
                    AND NOT EXISTS (SELECT 1 FROM "Users" other WHERE other."UserId"=ANY(ids)
                      AND other."DeletedAt" IS NOT NULL AND other."UserId"<>b."CustomerId")
                )
              ) THEN
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
            """);
        }
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("""
            CREATE OR REPLACE FUNCTION public.account_deletion_write_guard() RETURNS trigger LANGUAGE plpgsql AS $guard$
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
            """);
        }
    }
}
