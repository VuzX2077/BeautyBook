using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace BeautyBookBackend.Migrations
{
    /// <inheritdoc />
    public partial class AddPrivateFinancialQrReferences : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<Guid>(
                name: "DestinationFinancialQrMediaId",
                table: "Refunds",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "FinancialQrMediaIdSnapshot",
                table: "Payouts",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "FinancialQrMediaId",
                table: "BankAccounts",
                type: "uuid",
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_Refunds_DestinationFinancialQrMediaId",
                table: "Refunds",
                column: "DestinationFinancialQrMediaId");

            migrationBuilder.CreateIndex(
                name: "IX_Payouts_FinancialQrMediaIdSnapshot",
                table: "Payouts",
                column: "FinancialQrMediaIdSnapshot");

            migrationBuilder.CreateIndex(
                name: "IX_BankAccounts_FinancialQrMediaId",
                table: "BankAccounts",
                column: "FinancialQrMediaId");

            migrationBuilder.AddForeignKey(
                name: "FK_BankAccounts_VerificationMedia_FinancialQrMediaId",
                table: "BankAccounts",
                column: "FinancialQrMediaId",
                principalTable: "VerificationMedia",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_Payouts_VerificationMedia_FinancialQrMediaIdSnapshot",
                table: "Payouts",
                column: "FinancialQrMediaIdSnapshot",
                principalTable: "VerificationMedia",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_Refunds_VerificationMedia_DestinationFinancialQrMediaId",
                table: "Refunds",
                column: "DestinationFinancialQrMediaId",
                principalTable: "VerificationMedia",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);
            migrationBuilder.Sql("""

CREATE FUNCTION public.financial_media_write_guard() RETURNS trigger LANGUAGE plpgsql AS $guard$
DECLARE media_id uuid; old_id uuid; owner_id uuid; bank_id uuid; data jsonb := to_jsonb(NEW); prior jsonb := '{}'::jsonb;
BEGIN
 IF TG_OP='UPDATE' THEN prior:=to_jsonb(OLD); END IF;
 IF TG_TABLE_NAME='BankAccounts' THEN
  media_id:=(data->>'FinancialQrMediaId')::uuid; old_id:=(prior->>'FinancialQrMediaId')::uuid;
  owner_id:=(data->>'UserId')::uuid; bank_id:=(data->>'Id')::uuid;
 ELSIF TG_TABLE_NAME='Payouts' THEN
  media_id:=(data->>'FinancialQrMediaIdSnapshot')::uuid; old_id:=(prior->>'FinancialQrMediaIdSnapshot')::uuid;
  owner_id:=(data->>'MuaId')::uuid; bank_id:=(data->>'BankAccountId')::uuid;
 ELSE
  media_id:=(data->>'DestinationFinancialQrMediaId')::uuid; old_id:=(prior->>'DestinationFinancialQrMediaId')::uuid;
  SELECT b."CustomerId" INTO owner_id FROM "Bookings" b WHERE b."BookingId"=(data->>'BookingId')::uuid;
  bank_id:=(data->>'DestinationBankAccountId')::uuid;
 END IF;
 IF media_id IS NULL THEN RETURN NEW; END IF;
 IF NOT pg_try_advisory_xact_lock_shared(724266524669002) THEN
  RAISE EXCEPTION 'Financial cleanup active; retry later' USING ERRCODE='55000';
 END IF;
 IF media_id IS NOT DISTINCT FROM old_id THEN
  IF TG_TABLE_NAME='BankAccounts' AND (data->>'UserId' IS DISTINCT FROM prior->>'UserId' OR data->>'AccountNumber' IS DISTINCT FROM prior->>'AccountNumber' OR data->>'Method' IS DISTINCT FROM prior->>'Method') OR
     TG_TABLE_NAME='Refunds' AND (data->>'BookingId' IS DISTINCT FROM prior->>'BookingId' OR data->>'DestinationBankAccountId' IS DISTINCT FROM prior->>'DestinationBankAccountId' OR data->>'DestinationAccountNumber' IS DISTINCT FROM prior->>'DestinationAccountNumber') OR
     TG_TABLE_NAME='Payouts' AND (data->>'MuaId' IS DISTINCT FROM prior->>'MuaId' OR data->>'BankAccountId' IS DISTINCT FROM prior->>'BankAccountId' OR data->>'AccountNumberSnapshot' IS DISTINCT FROM prior->>'AccountNumberSnapshot' OR data->>'BankCodeSnapshot' IS DISTINCT FROM prior->>'BankCodeSnapshot') THEN
   RAISE EXCEPTION 'Financial receiver snapshot immutable; replace QR' USING ERRCODE='23514';
  END IF;
  RETURN NEW;
 END IF;
 IF NOT EXISTS(SELECT 1 FROM "VerificationMedia" m JOIN "Users" u ON u."UserId"=m."OwnerId"
  WHERE m."Id"=media_id AND m."OwnerId"=owner_id AND m."Purpose"='financial-momo-receive-qr'
   AND m."ContextId"=bank_id AND m."ReadyAt" IS NOT NULL AND m."DeletedAt" IS NULL AND m."StorageDeletedAt" IS NULL
   AND u."IsActive" AND u."DeletedAt" IS NULL AND u."Role"=2) THEN
  RAISE EXCEPTION 'Financial QR ownership/reference invalid' USING ERRCODE='23514';
 END IF;
 IF TG_TABLE_NAME='BankAccounts' AND data->>'Method'<>'MOMO' OR TG_TABLE_NAME='Payouts' AND data->>'BankCodeSnapshot'<>'MOMO' THEN
  RAISE EXCEPTION 'MoMo QR cannot attach to BANK' USING ERRCODE='23514';
 END IF;
 RETURN NEW;
END $guard$;
CREATE TRIGGER financial_media_write_guard BEFORE INSERT OR UPDATE ON "BankAccounts" FOR EACH ROW EXECUTE FUNCTION public.financial_media_write_guard();
CREATE TRIGGER financial_media_write_guard BEFORE INSERT OR UPDATE ON "Payouts" FOR EACH ROW EXECUTE FUNCTION public.financial_media_write_guard();
CREATE TRIGGER financial_media_write_guard BEFORE INSERT OR UPDATE ON "Refunds" FOR EACH ROW EXECUTE FUNCTION public.financial_media_write_guard();

""");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder) => throw new NotSupportedException("Financial media migration requires a reviewed roll-forward; automatic destructive rollback is disabled.");
    }
}
