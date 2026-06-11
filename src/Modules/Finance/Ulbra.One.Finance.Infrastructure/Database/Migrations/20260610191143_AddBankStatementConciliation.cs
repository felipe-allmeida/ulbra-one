using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Ulbra.One.Finance.Infrastructure.Database.Migrations
{
    /// <inheritdoc />
    public partial class AddBankStatementConciliation : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "bank_transactions",
                schema: "finance",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    import_id = table.Column<Guid>(type: "uuid", nullable: false),
                    company_id = table.Column<Guid>(type: "uuid", nullable: false),
                    bank_account_id = table.Column<Guid>(type: "uuid", nullable: false),
                    external_id = table.Column<string>(type: "character varying(120)", maxLength: 120, nullable: false),
                    date = table.Column<DateOnly>(type: "date", nullable: false),
                    amount = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: false),
                    direction = table.Column<int>(type: "integer", nullable: false),
                    description = table.Column<string>(type: "character varying(300)", maxLength: 300, nullable: false),
                    match_status = table.Column<int>(type: "integer", nullable: false),
                    suggested_installment_id = table.Column<Guid>(type: "uuid", nullable: true),
                    matched_payment_id = table.Column<Guid>(type: "uuid", nullable: true),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_bank_transactions", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "statement_imports",
                schema: "finance",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    company_id = table.Column<Guid>(type: "uuid", nullable: false),
                    bank_account_id = table.Column<Guid>(type: "uuid", nullable: false),
                    source = table.Column<int>(type: "integer", nullable: false),
                    file_name = table.Column<string>(type: "character varying(260)", maxLength: 260, nullable: false),
                    period_start = table.Column<DateOnly>(type: "date", nullable: true),
                    period_end = table.Column<DateOnly>(type: "date", nullable: true),
                    transaction_count = table.Column<int>(type: "integer", nullable: false),
                    new_count = table.Column<int>(type: "integer", nullable: false),
                    duplicate_count = table.Column<int>(type: "integer", nullable: false),
                    status = table.Column<int>(type: "integer", nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    created_by = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    updated_by = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_statement_imports", x => x.id);
                });

            migrationBuilder.CreateIndex(
                name: "ix_bank_transactions_bank_account_id_external_id",
                schema: "finance",
                table: "bank_transactions",
                columns: new[] { "bank_account_id", "external_id" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_bank_transactions_company_id_match_status",
                schema: "finance",
                table: "bank_transactions",
                columns: new[] { "company_id", "match_status" });

            migrationBuilder.CreateIndex(
                name: "ix_bank_transactions_date",
                schema: "finance",
                table: "bank_transactions",
                column: "date");

            migrationBuilder.CreateIndex(
                name: "ix_bank_transactions_import_id",
                schema: "finance",
                table: "bank_transactions",
                column: "import_id");

            migrationBuilder.CreateIndex(
                name: "ix_statement_imports_company_id_bank_account_id",
                schema: "finance",
                table: "statement_imports",
                columns: new[] { "company_id", "bank_account_id" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "bank_transactions",
                schema: "finance");

            migrationBuilder.DropTable(
                name: "statement_imports",
                schema: "finance");
        }
    }
}
