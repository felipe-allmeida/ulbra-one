using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Ulbra.One.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class PayablePaymentSource : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "ix_bank_accounts_company_id",
                schema: "finance",
                table: "bank_accounts");

            migrationBuilder.AddColumn<Guid>(
                name: "payment_bank_account_id",
                schema: "finance",
                table: "payables",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "payment_method",
                schema: "finance",
                table: "payables",
                type: "integer",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "default_payment_method",
                schema: "finance",
                table: "bank_accounts",
                type: "integer",
                nullable: true);

            migrationBuilder.AddColumn<bool>(
                name: "is_default_for_payments",
                schema: "finance",
                table: "bank_accounts",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.CreateIndex(
                name: "ix_bank_accounts_company_default_payer",
                schema: "finance",
                table: "bank_accounts",
                column: "company_id",
                unique: true,
                filter: "is_default_for_payments AND deleted_at IS NULL");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "ix_bank_accounts_company_default_payer",
                schema: "finance",
                table: "bank_accounts");

            migrationBuilder.DropColumn(
                name: "payment_bank_account_id",
                schema: "finance",
                table: "payables");

            migrationBuilder.DropColumn(
                name: "payment_method",
                schema: "finance",
                table: "payables");

            migrationBuilder.DropColumn(
                name: "default_payment_method",
                schema: "finance",
                table: "bank_accounts");

            migrationBuilder.DropColumn(
                name: "is_default_for_payments",
                schema: "finance",
                table: "bank_accounts");

            migrationBuilder.CreateIndex(
                name: "ix_bank_accounts_company_id",
                schema: "finance",
                table: "bank_accounts",
                column: "company_id");
        }
    }
}
