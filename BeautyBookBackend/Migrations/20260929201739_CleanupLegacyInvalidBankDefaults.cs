using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace BeautyBookBackend.Migrations
{
    /// <inheritdoc />
    public partial class CleanupLegacyInvalidBankDefaults : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("""
                UPDATE "MuaBankAccounts"
                SET "IsDefault" = FALSE
                WHERE "IsDefault" = TRUE
                  AND ("IsActive" = FALSE
                       OR "VerificationStatus" <> 'APPROVED'
                       OR "ActivatedAt" IS NULL
                       OR "ActivatedAt" > CURRENT_TIMESTAMP);

                UPDATE "CustomerBankAccounts"
                SET "IsDefault" = FALSE
                WHERE "IsDefault" = TRUE
                  AND ("IsActive" = FALSE
                       OR "VerificationStatus" <> 'APPROVED'
                       OR "ActivatedAt" IS NULL
                       OR "ActivatedAt" > CURRENT_TIMESTAMP);
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {

        }
    }
}
