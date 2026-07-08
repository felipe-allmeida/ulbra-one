using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Ulbra.One.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class ConsolidateGlobalLedger : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // ── AEL-684 data migration — runs BEFORE the schema changes below ──
            // The ledger chart becomes global (one row per code). Dev data is seeded per company today,
            // so collapse duplicate codes into a single surviving account and repoint references to it.
            // FK journal_lines → ledger_accounts is Restrict, so remap references before deleting dupes.

            // 1a) Reparent children to the surviving parent (lowest id per code).
            migrationBuilder.Sql(@"
                UPDATE accounting.ledger_accounts AS child
                SET parent_id = ps.keep_id
                FROM accounting.ledger_accounts AS oldp
                JOIN (SELECT DISTINCT ON (code) code, id AS keep_id FROM accounting.ledger_accounts WHERE deleted_at IS NULL ORDER BY code, id) AS ps
                  ON ps.code = oldp.code
                WHERE child.parent_id = oldp.id AND child.parent_id <> ps.keep_id;");

            // 1b) Repoint journal lines to the surviving account.
            migrationBuilder.Sql(@"
                UPDATE accounting.journal_lines AS jl
                SET ledger_account_id = s.keep_id
                FROM accounting.ledger_accounts AS la
                JOIN (SELECT DISTINCT ON (code) code, id AS keep_id FROM accounting.ledger_accounts WHERE deleted_at IS NULL ORDER BY code, id) AS s
                  ON s.code = la.code
                WHERE jl.ledger_account_id = la.id AND jl.ledger_account_id <> s.keep_id;");

            // 1c) Delete the non-surviving duplicates.
            migrationBuilder.Sql(@"
                DELETE FROM accounting.ledger_accounts AS la
                USING (SELECT DISTINCT ON (code) code, id AS keep_id FROM accounting.ledger_accounts WHERE deleted_at IS NULL ORDER BY code, id) AS s
                WHERE la.code = s.code AND la.id <> s.keep_id;");

            // 2) Repoint payable classification: accounting_account_id / suggested_accounting_account_id hold
            //    ChartAccount ids → map to the matching global LedgerAccount id (by code); null the unmatched.
            migrationBuilder.Sql(@"
                UPDATE finance.payables AS p
                SET accounting_account_id = la.id
                FROM finance.chart_accounts AS ca
                JOIN accounting.ledger_accounts AS la ON la.code = ca.code AND la.deleted_at IS NULL
                WHERE p.accounting_account_id = ca.id;");
            migrationBuilder.Sql(@"
                UPDATE finance.payables AS p
                SET suggested_accounting_account_id = la.id
                FROM finance.chart_accounts AS ca
                JOIN accounting.ledger_accounts AS la ON la.code = ca.code AND la.deleted_at IS NULL
                WHERE p.suggested_accounting_account_id = ca.id;");
            migrationBuilder.Sql(@"
                UPDATE finance.payables AS p
                SET accounting_account_id = NULL
                WHERE EXISTS (SELECT 1 FROM finance.chart_accounts AS ca WHERE ca.id = p.accounting_account_id);");
            migrationBuilder.Sql(@"
                UPDATE finance.payables AS p
                SET suggested_accounting_account_id = NULL
                WHERE EXISTS (SELECT 1 FROM finance.chart_accounts AS ca WHERE ca.id = p.suggested_accounting_account_id);");

            migrationBuilder.DropTable(
                name: "chart_accounts",
                schema: "finance");

            migrationBuilder.DropIndex(
                name: "ix_ledger_accounts_company_id",
                schema: "accounting",
                table: "ledger_accounts");

            migrationBuilder.DropIndex(
                name: "ix_ledger_accounts_company_id_code",
                schema: "accounting",
                table: "ledger_accounts");

            migrationBuilder.DropColumn(
                name: "company_id",
                schema: "accounting",
                table: "ledger_accounts");

            migrationBuilder.CreateIndex(
                name: "ix_ledger_accounts_code",
                schema: "accounting",
                table: "ledger_accounts",
                column: "code",
                unique: true,
                filter: "deleted_at IS NULL");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "ix_ledger_accounts_code",
                schema: "accounting",
                table: "ledger_accounts");

            migrationBuilder.AddColumn<Guid>(
                name: "company_id",
                schema: "accounting",
                table: "ledger_accounts",
                type: "uuid",
                nullable: false,
                defaultValue: new Guid("00000000-0000-0000-0000-000000000000"));

            migrationBuilder.CreateTable(
                name: "chart_accounts",
                schema: "finance",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    chart_type = table.Column<int>(type: "integer", nullable: false),
                    code = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    created_by = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    deleted_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    is_active = table.Column<bool>(type: "boolean", nullable: false),
                    kind = table.Column<int>(type: "integer", nullable: false),
                    name = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    nature = table.Column<int>(type: "integer", nullable: true),
                    parent_id = table.Column<Guid>(type: "uuid", nullable: true),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    updated_by = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_chart_accounts", x => x.id);
                });

            migrationBuilder.CreateIndex(
                name: "ix_ledger_accounts_company_id",
                schema: "accounting",
                table: "ledger_accounts",
                column: "company_id");

            migrationBuilder.CreateIndex(
                name: "ix_ledger_accounts_company_id_code",
                schema: "accounting",
                table: "ledger_accounts",
                columns: new[] { "company_id", "code" },
                unique: true,
                filter: "deleted_at IS NULL");

            migrationBuilder.CreateIndex(
                name: "ix_chart_accounts_chart_type_code",
                schema: "finance",
                table: "chart_accounts",
                columns: new[] { "chart_type", "code" },
                unique: true,
                filter: "deleted_at IS NULL");
        }
    }
}
