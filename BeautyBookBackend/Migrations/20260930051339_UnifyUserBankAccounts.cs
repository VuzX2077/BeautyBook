using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace BeautyBookBackend.Migrations;

/// <summary>
/// Moves both legacy bank-account tables into one user-owned table. All copy,
/// reconciliation and refund snapshot work happens before the old tables drop.
/// </summary>
public partial class UnifyUserBankAccounts : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropForeignKey(
            name: "FK_Refunds_CustomerBankAccounts_DestinationBankAccountId",
            table: "Refunds");

        migrationBuilder.AddColumn<string>(name: "DestinationBankCode", table: "Refunds",
            type: "character varying(20)", maxLength: 20, nullable: true);
        migrationBuilder.AddColumn<Guid>(name: "BankAccountId", table: "Payouts", type: "uuid", nullable: true);
        migrationBuilder.AddColumn<string>(name: "BankBinSnapshot", table: "Payouts", type: "text", nullable: true);

        migrationBuilder.CreateTable(
            name: "BankAccounts",
            columns: table => new
            {
                Id = table.Column<Guid>(type: "uuid", nullable: false),
                UserId = table.Column<Guid>(type: "uuid", nullable: false),
                BankCode = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                BankBin = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                BankName = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                AccountNumber = table.Column<string>(type: "character varying(30)", maxLength: 30, nullable: false),
                AccountHolderName = table.Column<string>(type: "character varying(150)", maxLength: 150, nullable: false),
                Method = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false, defaultValue: "BANK"),
                CanonicalBankKey = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: false),
                NormalizedAccountNumber = table.Column<string>(type: "character varying(30)", maxLength: 30, nullable: false),
                QrCodeUrl = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: true),
                VerificationStatus = table.Column<string>(type: "character varying(30)", maxLength: 30, nullable: false, defaultValue: "PENDING_ADMIN"),
                ActivatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                IsDefault = table.Column<bool>(type: "boolean", nullable: false),
                IsActive = table.Column<bool>(type: "boolean", nullable: false),
                ReviewedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                ReviewedBy = table.Column<Guid>(type: "uuid", nullable: true),
                CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                UpdatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_BankAccounts", x => x.Id);
                table.ForeignKey(name: "FK_BankAccounts_Users_UserId", column: x => x.UserId,
                    principalTable: "Users", principalColumn: "UserId", onDelete: ReferentialAction.Restrict);
            });

        migrationBuilder.Sql("""
            WITH bank_map(code, bin) AS (VALUES
              ('MB','970422'),('VCB','970436'),('TCB','970407'),('ACB','970416'),
              ('VPB','970432'),('BIDV','970418'),('ICB','970415'),('VBA','970405'),
              ('STB','970403'),('TPB','970423'),('VIB','970441'),('SHB','970443'),
              ('HDB','970437'),('OCB','970448'))
            INSERT INTO "BankAccounts"
              ("Id","UserId","BankCode","BankBin","BankName","AccountNumber","AccountHolderName","Method",
               "CanonicalBankKey","NormalizedAccountNumber","QrCodeUrl","VerificationStatus","ActivatedAt",
               "IsDefault","IsActive","ReviewedAt","ReviewedBy","CreatedAt","UpdatedAt")
            SELECT c."Id",c."CustomerId",
              CASE WHEN UPPER(BTRIM(c."Method"))='MOMO' THEN 'MOMO' ELSE COALESCE(bm.code,'UNKNOWN') END,
              CASE WHEN UPPER(BTRIM(c."Method"))='MOMO' THEN 'MOMO' ELSE UPPER(BTRIM(c."BankBin")) END,
              COALESCE(NULLIF(BTRIM(c."BankName"),''),'Chưa xác định'),
              REGEXP_REPLACE(UPPER(BTRIM(c."AccountNumber")),'[^A-Z0-9]','','g'),
              REGEXP_REPLACE(UPPER(BTRIM(c."AccountHolderName")),'\s+',' ','g'),
              CASE WHEN UPPER(BTRIM(c."Method"))='MOMO' THEN 'MOMO' ELSE 'BANK' END,
              CASE WHEN UPPER(BTRIM(c."Method"))='MOMO' THEN 'MOMO' ELSE 'BIN:'||UPPER(BTRIM(c."BankBin")) END,
              REGEXP_REPLACE(UPPER(BTRIM(c."AccountNumber")),'[^A-Z0-9]','','g'),c."QrCodeUrl",
              CASE WHEN UPPER(BTRIM(c."Method"))='MOMO' OR bm.code IS NOT NULL THEN
                CASE UPPER(BTRIM(c."VerificationStatus")) WHEN 'APPROVED' THEN 'APPROVED' WHEN 'REJECTED' THEN 'REJECTED' ELSE 'PENDING_ADMIN' END
                ELSE 'PENDING_ADMIN' END,
              CASE WHEN UPPER(BTRIM(c."Method"))='MOMO' OR bm.code IS NOT NULL THEN c."ActivatedAt" ELSE NULL END,
              CASE WHEN UPPER(BTRIM(c."Method"))='MOMO' OR bm.code IS NOT NULL THEN c."IsDefault" ELSE FALSE END,
              c."IsActive",
              CASE WHEN UPPER(BTRIM(c."Method"))='MOMO' OR bm.code IS NOT NULL THEN c."ReviewedAt" ELSE NULL END,
              CASE WHEN UPPER(BTRIM(c."Method"))='MOMO' OR bm.code IS NOT NULL THEN c."ReviewedBy" ELSE NULL END,
              c."CreatedAt",c."UpdatedAt"
            FROM "CustomerBankAccounts" c
            LEFT JOIN bank_map bm ON bm.bin=UPPER(BTRIM(c."BankBin"));
            """);

        // Merge identical accounts from both legacy sources. Any conflicting
        // review or receiving data is deliberately returned to admin review.
        migrationBuilder.Sql("""
            WITH bank_map(code, bin) AS (VALUES
              ('MB','970422'),('VCB','970436'),('TCB','970407'),('ACB','970416'),
              ('VPB','970432'),('BIDV','970418'),('ICB','970415'),('VBA','970405'),
              ('STB','970403'),('TPB','970423'),('VIB','970441'),('SHB','970443'),
              ('HDB','970437'),('OCB','970448'))
            UPDATE "BankAccounts" b
            SET "VerificationStatus"='PENDING_ADMIN',"ActivatedAt"=NULL,"IsDefault"=FALSE,
                "ReviewedAt"=NULL,"ReviewedBy"=NULL,"IsActive"=b."IsActive" OR m."IsActive",
                "UpdatedAt"=GREATEST(b."UpdatedAt",m."UpdatedAt")
            FROM "MuaBankAccounts" m LEFT JOIN bank_map bm ON bm.code=UPPER(BTRIM(m."BankCode"))
            WHERE b."UserId"=m."MuaId"
              AND b."NormalizedAccountNumber"=REGEXP_REPLACE(UPPER(BTRIM(m."AccountNumber")),'[^A-Z0-9]','','g')
              AND b."Method"=CASE WHEN UPPER(BTRIM(m."Method"))='MOMO' THEN 'MOMO' ELSE 'BANK' END
              AND b."CanonicalBankKey"=CASE WHEN UPPER(BTRIM(m."Method"))='MOMO' THEN 'MOMO'
                    WHEN bm.bin IS NOT NULL THEN 'BIN:'||bm.bin ELSE 'CODE:'||UPPER(BTRIM(m."BankCode")) END
              AND (b."VerificationStatus" IS DISTINCT FROM
                    CASE UPPER(BTRIM(m."VerificationStatus")) WHEN 'APPROVED' THEN 'APPROVED' WHEN 'REJECTED' THEN 'REJECTED' ELSE 'PENDING_ADMIN' END
                OR b."ActivatedAt" IS DISTINCT FROM m."ActivatedAt"
                OR b."IsActive" IS DISTINCT FROM m."IsActive"
                OR b."AccountHolderName" IS DISTINCT FROM REGEXP_REPLACE(UPPER(BTRIM(m."AccountHolderName")),'\s+',' ','g')
                OR b."QrCodeUrl" IS DISTINCT FROM m."QrCodeUrl");
            """);

        migrationBuilder.Sql("""
            WITH bank_map(code, bin) AS (VALUES
              ('MB','970422'),('VCB','970436'),('TCB','970407'),('ACB','970416'),
              ('VPB','970432'),('BIDV','970418'),('ICB','970415'),('VBA','970405'),
              ('STB','970403'),('TPB','970423'),('VIB','970441'),('SHB','970443'),
              ('HDB','970437'),('OCB','970448'))
            INSERT INTO "BankAccounts"
              ("Id","UserId","BankCode","BankBin","BankName","AccountNumber","AccountHolderName","Method",
               "CanonicalBankKey","NormalizedAccountNumber","QrCodeUrl","VerificationStatus","ActivatedAt",
               "IsDefault","IsActive","ReviewedAt","ReviewedBy","CreatedAt","UpdatedAt")
            SELECT CASE WHEN EXISTS(SELECT 1 FROM "BankAccounts" x WHERE x."Id"=m."Id")
                    THEN MD5(m."Id"::text||':mua')::uuid ELSE m."Id" END,m."MuaId",
              CASE WHEN UPPER(BTRIM(m."Method"))='MOMO' THEN 'MOMO' ELSE UPPER(BTRIM(m."BankCode")) END,
              CASE WHEN UPPER(BTRIM(m."Method"))='MOMO' THEN 'MOMO' ELSE COALESCE(bm.bin,'UNKNOWN') END,
              COALESCE(NULLIF(BTRIM(m."BankName"),''),'Chưa xác định'),
              REGEXP_REPLACE(UPPER(BTRIM(m."AccountNumber")),'[^A-Z0-9]','','g'),
              REGEXP_REPLACE(UPPER(BTRIM(m."AccountHolderName")),'\s+',' ','g'),
              CASE WHEN UPPER(BTRIM(m."Method"))='MOMO' THEN 'MOMO' ELSE 'BANK' END,
              CASE WHEN UPPER(BTRIM(m."Method"))='MOMO' THEN 'MOMO' WHEN bm.bin IS NOT NULL THEN 'BIN:'||bm.bin
                   ELSE 'CODE:'||UPPER(BTRIM(m."BankCode")) END,
              REGEXP_REPLACE(UPPER(BTRIM(m."AccountNumber")),'[^A-Z0-9]','','g'),m."QrCodeUrl",
              CASE WHEN UPPER(BTRIM(m."Method"))='MOMO' OR bm.bin IS NOT NULL THEN
                CASE UPPER(BTRIM(m."VerificationStatus")) WHEN 'APPROVED' THEN 'APPROVED' WHEN 'REJECTED' THEN 'REJECTED' ELSE 'PENDING_ADMIN' END
                ELSE 'PENDING_ADMIN' END,
              CASE WHEN UPPER(BTRIM(m."Method"))='MOMO' OR bm.bin IS NOT NULL THEN m."ActivatedAt" ELSE NULL END,
              CASE WHEN UPPER(BTRIM(m."Method"))='MOMO' OR bm.bin IS NOT NULL THEN m."IsDefault" ELSE FALSE END,
              m."IsActive",
              CASE WHEN UPPER(BTRIM(m."Method"))='MOMO' OR bm.bin IS NOT NULL THEN m."ReviewedAt" ELSE NULL END,
              CASE WHEN UPPER(BTRIM(m."Method"))='MOMO' OR bm.bin IS NOT NULL THEN m."ReviewedBy" ELSE NULL END,
              m."CreatedAt",m."UpdatedAt"
            FROM "MuaBankAccounts" m LEFT JOIN bank_map bm ON bm.code=UPPER(BTRIM(m."BankCode"))
            WHERE NOT EXISTS(SELECT 1 FROM "BankAccounts" b
              WHERE b."UserId"=m."MuaId"
                AND b."NormalizedAccountNumber"=REGEXP_REPLACE(UPPER(BTRIM(m."AccountNumber")),'[^A-Z0-9]','','g')
                AND b."Method"=CASE WHEN UPPER(BTRIM(m."Method"))='MOMO' THEN 'MOMO' ELSE 'BANK' END
                AND b."CanonicalBankKey"=CASE WHEN UPPER(BTRIM(m."Method"))='MOMO' THEN 'MOMO'
                    WHEN bm.bin IS NOT NULL THEN 'BIN:'||bm.bin ELSE 'CODE:'||UPPER(BTRIM(m."BankCode")) END);
            """);

        migrationBuilder.Sql("""
            UPDATE "BankAccounts" SET "IsDefault"=FALSE
            WHERE "IsDefault"=TRUE AND ("IsActive"=FALSE OR "VerificationStatus"<>'APPROVED'
              OR "ActivatedAt" IS NULL OR "ActivatedAt">NOW());
            WITH ranked AS (
              SELECT "Id",ROW_NUMBER() OVER(PARTITION BY "UserId" ORDER BY "CreatedAt","Id") rn
              FROM "BankAccounts" WHERE "IsDefault"=TRUE AND "IsActive"=TRUE)
            UPDATE "BankAccounts" b SET "IsDefault"=FALSE FROM ranked r WHERE b."Id"=r."Id" AND r.rn>1;
            UPDATE "Refunds" r SET "DestinationBankCode"=b."BankCode"
              FROM "BankAccounts" b WHERE r."DestinationBankAccountId"=b."Id";
            UPDATE "Payouts" SET "BankBinSnapshot"=CASE UPPER(BTRIM("BankCodeSnapshot"))
              WHEN 'MOMO' THEN 'MOMO' WHEN 'MB' THEN '970422' WHEN 'VCB' THEN '970436'
              WHEN 'TCB' THEN '970407' WHEN 'ACB' THEN '970416' WHEN 'VPB' THEN '970432'
              WHEN 'BIDV' THEN '970418' WHEN 'ICB' THEN '970415' WHEN 'VBA' THEN '970405'
              WHEN 'STB' THEN '970403' WHEN 'TPB' THEN '970423' WHEN 'VIB' THEN '970441'
              WHEN 'SHB' THEN '970443' WHEN 'HDB' THEN '970437' WHEN 'OCB' THEN '970448' ELSE NULL END;
            """);

        migrationBuilder.DropTable(name: "CustomerBankAccounts");
        migrationBuilder.DropTable(name: "MuaBankAccounts");

        migrationBuilder.CreateIndex(name: "IX_Payouts_BankAccountId", table: "Payouts", column: "BankAccountId");
        migrationBuilder.CreateIndex(name: "IX_BankAccounts_UserId_IsActive", table: "BankAccounts", columns: new[] { "UserId", "IsActive" });
        migrationBuilder.CreateIndex(name: "UX_BankAccounts_ActiveIdentity", table: "BankAccounts",
            columns: new[] { "UserId", "Method", "CanonicalBankKey", "NormalizedAccountNumber" }, unique: true,
            filter: "\"IsActive\" = TRUE");
        migrationBuilder.CreateIndex(name: "UX_BankAccounts_Default", table: "BankAccounts", column: "UserId", unique: true,
            filter: "\"IsDefault\" = TRUE AND \"IsActive\" = TRUE");
        migrationBuilder.AddForeignKey(name: "FK_Payouts_BankAccounts_BankAccountId", table: "Payouts", column: "BankAccountId",
            principalTable: "BankAccounts", principalColumn: "Id", onDelete: ReferentialAction.SetNull);
        migrationBuilder.AddForeignKey(name: "FK_Refunds_BankAccounts_DestinationBankAccountId", table: "Refunds", column: "DestinationBankAccountId",
            principalTable: "BankAccounts", principalColumn: "Id", onDelete: ReferentialAction.SetNull);
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropForeignKey(name: "FK_Payouts_BankAccounts_BankAccountId", table: "Payouts");
        migrationBuilder.DropForeignKey(name: "FK_Refunds_BankAccounts_DestinationBankAccountId", table: "Refunds");

        migrationBuilder.CreateTable(
            name: "CustomerBankAccounts",
            columns: table => new
            {
                Id = table.Column<Guid>(type: "uuid", nullable: false), CustomerId = table.Column<Guid>(type: "uuid", nullable: false),
                AccountHolderName = table.Column<string>(type: "character varying(150)", maxLength: 150, nullable: false),
                AccountNumber = table.Column<string>(type: "character varying(30)", maxLength: 30, nullable: false),
                ActivatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                BankBin = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                BankName = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                IsActive = table.Column<bool>(type: "boolean", nullable: false), IsDefault = table.Column<bool>(type: "boolean", nullable: false),
                Method = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false, defaultValue: "BANK"),
                QrCodeUrl = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: true),
                ReviewedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true), ReviewedBy = table.Column<Guid>(type: "uuid", nullable: true),
                UpdatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                VerificationStatus = table.Column<string>(type: "character varying(30)", maxLength: 30, nullable: false, defaultValue: "PENDING_ADMIN")
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_CustomerBankAccounts", x => x.Id);
                table.ForeignKey(name: "FK_CustomerBankAccounts_Users_CustomerId", column: x => x.CustomerId,
                    principalTable: "Users", principalColumn: "UserId", onDelete: ReferentialAction.Restrict);
            });

        migrationBuilder.CreateTable(
            name: "MuaBankAccounts",
            columns: table => new
            {
                Id = table.Column<Guid>(type: "uuid", nullable: false), MuaId = table.Column<Guid>(type: "uuid", nullable: false),
                AccountHolderName = table.Column<string>(type: "character varying(150)", maxLength: 150, nullable: false),
                AccountNumber = table.Column<string>(type: "character varying(30)", maxLength: 30, nullable: false),
                ActivatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                BankCode = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                BankName = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                IsActive = table.Column<bool>(type: "boolean", nullable: false), IsDefault = table.Column<bool>(type: "boolean", nullable: false),
                Method = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false, defaultValue: "BANK"),
                QrCodeUrl = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: true),
                ReviewedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true), ReviewedBy = table.Column<Guid>(type: "uuid", nullable: true),
                UpdatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                VerificationStatus = table.Column<string>(type: "character varying(30)", maxLength: 30, nullable: false, defaultValue: "PENDING_ADMIN")
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_MuaBankAccounts", x => x.Id);
                table.ForeignKey(name: "FK_MuaBankAccounts_MakeupArtistProfiles_MuaId", column: x => x.MuaId,
                    principalTable: "MakeupArtistProfiles", principalColumn: "MUAId", onDelete: ReferentialAction.Restrict);
            });

        migrationBuilder.CreateIndex(name: "IX_CustomerBankAccounts_CustomerId_IsActive", table: "CustomerBankAccounts", columns: new[] { "CustomerId", "IsActive" });
        migrationBuilder.CreateIndex(name: "UX_CustomerBankAccounts_Default", table: "CustomerBankAccounts", column: "CustomerId", unique: true, filter: "\"IsDefault\" = TRUE AND \"IsActive\" = TRUE");
        migrationBuilder.CreateIndex(name: "IX_MuaBankAccounts_MuaId_IsActive", table: "MuaBankAccounts", columns: new[] { "MuaId", "IsActive" });
        migrationBuilder.CreateIndex(name: "UX_MuaBankAccounts_Default", table: "MuaBankAccounts", column: "MuaId", unique: true, filter: "\"IsDefault\" = TRUE AND \"IsActive\" = TRUE");

        migrationBuilder.Sql("""
            INSERT INTO "CustomerBankAccounts" ("Id","CustomerId","AccountHolderName","AccountNumber","ActivatedAt","BankBin","BankName","CreatedAt","IsActive","IsDefault","Method","QrCodeUrl","ReviewedAt","ReviewedBy","UpdatedAt","VerificationStatus")
            SELECT "Id","UserId","AccountHolderName","AccountNumber","ActivatedAt","BankBin","BankName","CreatedAt","IsActive","IsDefault","Method","QrCodeUrl","ReviewedAt","ReviewedBy","UpdatedAt","VerificationStatus" FROM "BankAccounts";
            INSERT INTO "MuaBankAccounts" ("Id","MuaId","AccountHolderName","AccountNumber","ActivatedAt","BankCode","BankName","CreatedAt","IsActive","IsDefault","Method","QrCodeUrl","ReviewedAt","ReviewedBy","UpdatedAt","VerificationStatus")
            SELECT b."Id",b."UserId",b."AccountHolderName",b."AccountNumber",b."ActivatedAt",b."BankCode",b."BankName",b."CreatedAt",b."IsActive",b."IsDefault",b."Method",b."QrCodeUrl",b."ReviewedAt",b."ReviewedBy",b."UpdatedAt",b."VerificationStatus"
            FROM "BankAccounts" b WHERE EXISTS(SELECT 1 FROM "MakeupArtistProfiles" p WHERE p."MUAId"=b."UserId");
            """);

        migrationBuilder.DropTable(name: "BankAccounts");
        migrationBuilder.DropIndex(name: "IX_Payouts_BankAccountId", table: "Payouts");
        migrationBuilder.DropColumn(name: "DestinationBankCode", table: "Refunds");
        migrationBuilder.DropColumn(name: "BankAccountId", table: "Payouts");
        migrationBuilder.DropColumn(name: "BankBinSnapshot", table: "Payouts");
        migrationBuilder.AddForeignKey(name: "FK_Refunds_CustomerBankAccounts_DestinationBankAccountId", table: "Refunds",
            column: "DestinationBankAccountId", principalTable: "CustomerBankAccounts", principalColumn: "Id",
            onDelete: ReferentialAction.SetNull);
    }
}
