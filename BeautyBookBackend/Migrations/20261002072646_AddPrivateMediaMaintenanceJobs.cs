using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace BeautyBookBackend.Migrations
{
    /// <inheritdoc />
    public partial class AddPrivateMediaMaintenanceJobs : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "PrivateMediaJobs",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    RequestedBy = table.Column<Guid>(type: "uuid", nullable: false),
                    Action = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: false),
                    Status = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: false),
                    IsActive = table.Column<bool>(type: "boolean", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    StartedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    CompletedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    Attempts = table.Column<int>(type: "integer", nullable: false),
                    Result = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_PrivateMediaJobs", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_PrivateMediaJobs_CreatedAt",
                table: "PrivateMediaJobs",
                column: "CreatedAt");

            migrationBuilder.CreateIndex(
                name: "IX_PrivateMediaJobs_IsActive",
                table: "PrivateMediaJobs",
                column: "IsActive",
                unique: true,
                filter: "\"IsActive\" = TRUE");
            migrationBuilder.Sql("""
        CREATE OR REPLACE FUNCTION public.private_media_uri_path(value text) RETURNS text
        LANGUAGE sql IMMUTABLE STRICT AS $encode$
          SELECT string_agg(CASE WHEN chr(get_byte(convert_to(value,'UTF8'),i)) ~ '^[A-Za-z0-9._~/-]$'
             THEN chr(get_byte(convert_to(value,'UTF8'),i))
             ELSE '%' || upper(lpad(to_hex(get_byte(convert_to(value,'UTF8'),i)),2,'0')) END, '' ORDER BY i)
          FROM generate_series(0,octet_length(convert_to(value,'UTF8'))-1) AS i;
        $encode$;
        CREATE OR REPLACE FUNCTION public.private_media_write_guard() RETURNS trigger
        LANGUAGE plpgsql AS $body$
        DECLARE new_data jsonb := to_jsonb(NEW); old_data jsonb := '{}'::jsonb;
                field record; legacy record;
        BEGIN
          -- Fail fast: avoid a row-lock/advisory-lock deadlock with maintenance.
          IF NOT pg_try_advisory_xact_lock_shared(724266524669002) THEN
            RAISE EXCEPTION 'Private media maintenance is active; retry later' USING ERRCODE='55000';
          END IF;
          IF TG_OP='UPDATE' THEN old_data := to_jsonb(OLD); END IF;
          FOR field IN SELECT key,value FROM jsonb_each(new_data) LOOP
            IF field.value IS DISTINCT FROM old_data->field.key AND field.value <> 'null'::jsonb THEN
              FOR legacy IN SELECT DISTINCT "LegacyObjectKey" AS key FROM public."VerificationMedia"
                            WHERE "LegacyObjectKey" IS NOT NULL LOOP
                -- Also catch percent-encoded source paths, matching cleanup's conservative scan.
                IF strpos(field.value::text, legacy.key)>0 OR
                   strpos(lower(field.value::text), lower(public.private_media_uri_path(legacy.key)))>0 OR
                   strpos(lower(field.value::text), lower(replace(public.private_media_uri_path(legacy.key),'/','%2F')))>0 THEN
                  RAISE EXCEPTION 'Legacy private media URL cannot be attached' USING ERRCODE='23514';
                END IF;
              END LOOP;
            END IF;
          END LOOP;
          RETURN NEW;
        END $body$;
        DO $install$
        DECLARE target record;
        BEGIN
          FOR target IN SELECT DISTINCT table_name FROM information_schema.columns
            WHERE table_schema='public' AND data_type IN ('text','character varying','json','jsonb','ARRAY')
              AND table_name NOT IN ('VerificationMedia','PrivateMediaJobs','__EFMigrationsHistory') LOOP
            EXECUTE format('CREATE TRIGGER private_media_write_guard BEFORE INSERT OR UPDATE ON public.%I FOR EACH ROW EXECUTE FUNCTION public.private_media_write_guard()', target.table_name);
          END LOOP;
        END $install$;
        """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("""
        DO $remove$ DECLARE target record; BEGIN
          FOR target IN SELECT c.relname FROM pg_trigger t JOIN pg_class c ON c.oid=t.tgrelid
            JOIN pg_namespace n ON n.oid=c.relnamespace WHERE n.nspname='public' AND t.tgname='private_media_write_guard' LOOP
            EXECUTE format('DROP TRIGGER private_media_write_guard ON public.%I', target.relname);
          END LOOP;
        END $remove$;
        DROP FUNCTION public.private_media_write_guard();
        DROP FUNCTION public.private_media_uri_path(text);
        """);
            migrationBuilder.DropTable(
                name: "PrivateMediaJobs");
        }
    }
}
