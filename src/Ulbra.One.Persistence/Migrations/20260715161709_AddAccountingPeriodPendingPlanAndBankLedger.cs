using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Ulbra.One.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddAccountingPeriodPendingPlanAndBankLedger : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "plan",
                schema: "accounting",
                table: "pending_accounting_postings",
                type: "text",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "ledger_account_id",
                schema: "finance",
                table: "bank_accounts",
                type: "uuid",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "accounting_periods",
                schema: "accounting",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    company_id = table.Column<Guid>(type: "uuid", nullable: false),
                    fiscal_year = table.Column<int>(type: "integer", nullable: false),
                    month = table.Column<int>(type: "integer", nullable: false),
                    status = table.Column<int>(type: "integer", nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    created_by = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    updated_by = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_accounting_periods", x => x.id);
                });

            migrationBuilder.CreateIndex(
                name: "ix_pending_accounting_postings_company_source_key_open",
                schema: "accounting",
                table: "pending_accounting_postings",
                columns: new[] { "company_id", "source_key" },
                unique: true,
                filter: "resolved_at IS NULL");

            migrationBuilder.CreateIndex(
                name: "ix_accounting_periods_company_id_fiscal_year_month",
                schema: "accounting",
                table: "accounting_periods",
                columns: new[] { "company_id", "fiscal_year", "month" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "accounting_periods",
                schema: "accounting");

            migrationBuilder.DropIndex(
                name: "ix_pending_accounting_postings_company_source_key_open",
                schema: "accounting",
                table: "pending_accounting_postings");

            migrationBuilder.DropColumn(
                name: "plan",
                schema: "accounting",
                table: "pending_accounting_postings");

            migrationBuilder.DropColumn(
                name: "ledger_account_id",
                schema: "finance",
                table: "bank_accounts");
        }
    }
}
