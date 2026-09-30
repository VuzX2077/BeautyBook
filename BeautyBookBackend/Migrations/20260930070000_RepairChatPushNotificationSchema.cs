using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace BeautyBookBackend.Migrations;

public partial class RepairChatPushNotificationSchema : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        // The preceding AddChatPushNotifications migration recorded the model
        // change but contained no DDL. Repair databases that already applied it,
        // while tolerating a database where the column was added manually.
        migrationBuilder.Sql("""
            ALTER TABLE "AppNotifications"
                ADD COLUMN IF NOT EXISTS "MessageId" uuid NULL;

            CREATE UNIQUE INDEX IF NOT EXISTS "IX_AppNotifications_MessageId"
                ON "AppNotifications" ("MessageId")
                WHERE "MessageId" IS NOT NULL;

            DO $$
            BEGIN
                IF NOT EXISTS (
                    SELECT 1 FROM pg_constraint
                    WHERE conname = 'FK_AppNotifications_Messages_MessageId'
                      AND conrelid = '"AppNotifications"'::regclass
                ) THEN
                    ALTER TABLE "AppNotifications"
                        ADD CONSTRAINT "FK_AppNotifications_Messages_MessageId"
                        FOREIGN KEY ("MessageId") REFERENCES "Messages" ("MessageId")
                        ON DELETE CASCADE;
                END IF;
            END $$;
            """);
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropForeignKey(
            name: "FK_AppNotifications_Messages_MessageId", table: "AppNotifications");
        migrationBuilder.DropIndex(
            name: "IX_AppNotifications_MessageId", table: "AppNotifications");
        migrationBuilder.DropColumn(name: "MessageId", table: "AppNotifications");
    }
}
