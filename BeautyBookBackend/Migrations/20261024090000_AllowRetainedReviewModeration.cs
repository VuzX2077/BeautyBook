using BeautyBookBackend.Data;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

namespace BeautyBookBackend.Migrations;

[DbContext(typeof(ApplicationDbContext))]
[Migration("20261024090000_AllowRetainedReviewModeration")]
public sealed class AllowRetainedReviewModeration : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder) => Replace(migrationBuilder, allowReviewReports: true);
    protected override void Down(MigrationBuilder migrationBuilder) => Replace(migrationBuilder, allowReviewReports: false);

    private static void Replace(MigrationBuilder migrationBuilder, bool allowReviewReports)
    {
        var exception = allowReviewReports ? """
            AND NOT (
              TG_TABLE_NAME='ContentReports' AND data->>'TargetType'='Review'
              AND EXISTS (SELECT 1 FROM "Reviews" r
                WHERE r."ReviewId"=(data->>'TargetId')::uuid AND r."CustomerId"=(data->>'TargetOwnerId')::uuid)
              AND NOT EXISTS (SELECT 1 FROM "Users" u WHERE u."UserId"=ANY(ids) AND u."DeletedAt" IS NOT NULL
                AND (u."UserId"<>(data->>'TargetOwnerId')::uuid OR u."UserId"=(data->>'ReporterId')::uuid OR u."UserId"=(data->>'ReviewedBy')::uuid))
            )
            """ : "";
        migrationBuilder.Sql($$"""
            CREATE OR REPLACE FUNCTION public.ugc_owner_write_guard() RETURNS trigger LANGUAGE plpgsql AS $guard$
            DECLARE data jsonb := to_jsonb(NEW); ids uuid[]; id uuid;
                    deleting text := current_setting('bbook.deletion_owner', true);
            BEGIN
              IF NOT pg_try_advisory_xact_lock_shared(724266524669002) THEN
                RAISE EXCEPTION 'Account/media operation active; retry later' USING ERRCODE='55000';
              END IF;
              IF TG_TABLE_NAME='UserBlocks' THEN
                ids := ARRAY[(data->>'BlockerId')::uuid,(data->>'BlockedId')::uuid];
                IF ids[1]=ids[2] THEN RAISE EXCEPTION 'Cannot block self' USING ERRCODE='23514'; END IF;
              ELSE
                ids := ARRAY[(data->>'ReporterId')::uuid,(data->>'TargetOwnerId')::uuid,(data->>'ReviewedBy')::uuid];
              END IF;
              FOR id IN SELECT DISTINCT value FROM unnest(ids) AS u(value) WHERE value IS NOT NULL ORDER BY value LOOP
                PERFORM 1 FROM "Users" WHERE "UserId"=id FOR SHARE;
              END LOOP;
              IF (deleting IS NULL OR deleting='') AND EXISTS(SELECT 1 FROM "Users" u WHERE u."UserId"=ANY(ids) AND u."DeletedAt" IS NOT NULL)
                {{exception}} THEN
                RAISE EXCEPTION 'Deleted account cannot receive new UGC data' USING ERRCODE='23514';
              END IF;
              RETURN NEW;
            END $guard$;
            """);
    }
}
