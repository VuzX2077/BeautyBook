using BeautyBookBackend.Data;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

namespace BeautyBookBackend.Migrations;

[DbContext(typeof(ApplicationDbContext))]
[Migration("20261008090000_AddFeedbackAccountDeletionGuards")]
public sealed class AddFeedbackAccountDeletionGuards : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        // New tables must participate in the same lock and owner checks as account deletion.
        migrationBuilder.Sql("""
            CREATE FUNCTION public.feedback_owner_write_guard() RETURNS trigger LANGUAGE plpgsql AS $guard$
            DECLARE data jsonb := to_jsonb(NEW); ids uuid[]; id uuid;
                    deleting text := current_setting('bbook.deletion_owner', true);
            BEGIN
              IF NOT pg_try_advisory_xact_lock_shared(724266524669002) THEN
                RAISE EXCEPTION 'Account/media operation active; retry later' USING ERRCODE='55000';
              END IF;
              IF TG_TABLE_NAME='UserFeedbacks' THEN
                ids := ARRAY[(data->>'UserId')::uuid];
              ELSE
                ids := ARRAY[(data->>'AdminId')::uuid,
                  (SELECT "UserId" FROM "UserFeedbacks" WHERE "Id"=(data->>'FeedbackId')::uuid)];
              END IF;
              FOR id IN SELECT DISTINCT value FROM unnest(ids) AS u(value) WHERE value IS NOT NULL ORDER BY value LOOP
                PERFORM 1 FROM "Users" WHERE "UserId"=id FOR SHARE;
              END LOOP;
              IF (deleting IS NULL OR deleting='') AND EXISTS(
                SELECT 1 FROM "Users" u WHERE u."UserId"=ANY(ids) AND u."DeletedAt" IS NOT NULL
              ) THEN
                RAISE EXCEPTION 'Deleted account cannot receive new feedback data' USING ERRCODE='23514';
              END IF;
              RETURN NEW;
            END $guard$;
            CREATE TRIGGER account_deletion_write_guard BEFORE INSERT OR UPDATE ON "UserFeedbacks"
              FOR EACH ROW EXECUTE FUNCTION public.feedback_owner_write_guard();
            CREATE TRIGGER account_deletion_write_guard BEFORE INSERT OR UPDATE ON "FeedbackEvents"
              FOR EACH ROW EXECUTE FUNCTION public.feedback_owner_write_guard();
            """);
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.Sql("""
            DROP TRIGGER account_deletion_write_guard ON "FeedbackEvents";
            DROP TRIGGER account_deletion_write_guard ON "UserFeedbacks";
            DROP FUNCTION public.feedback_owner_write_guard();
            """);
    }
}
