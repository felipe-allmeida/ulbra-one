using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Ulbra.One.Finance.Infrastructure.Database.Migrations
{
    /// <inheritdoc />
    public partial class AddBankAccountsAndPayments : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "bank_accounts",
                schema: "finance",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    company_id = table.Column<Guid>(type: "uuid", nullable: false),
                    bank_code = table.Column<string>(type: "character varying(3)", maxLength: 3, nullable: false),
                    bank_name = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    agency = table.Column<string>(type: "character varying(10)", maxLength: 10, nullable: false),
                    account_number = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    account_digit = table.Column<string>(type: "character varying(2)", maxLength: 2, nullable: false),
                    account_type = table.Column<int>(type: "integer", nullable: false),
                    alias = table.Column<string>(type: "character varying(60)", maxLength: 60, nullable: false),
                    opening_balance = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: true),
                    active = table.Column<bool>(type: "boolean", nullable: false),
                    deleted_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    created_by = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    updated_by = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_bank_accounts", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "payments",
                schema: "finance",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    installment_id = table.Column<Guid>(type: "uuid", nullable: false),
                    payable_id = table.Column<Guid>(type: "uuid", nullable: false),
                    payment_date = table.Column<DateOnly>(type: "date", nullable: false),
                    amount = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: false),
                    bank_account_id = table.Column<Guid>(type: "uuid", nullable: false),
                    method = table.Column<int>(type: "integer", nullable: false),
                    interest = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: false),
                    fine = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: false),
                    discount = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: false),
                    notes = table.Column<string>(type: "character varying(300)", maxLength: 300, nullable: true),
                    is_reversed = table.Column<bool>(type: "boolean", nullable: false),
                    reversal_reason = table.Column<string>(type: "character varying(300)", maxLength: 300, nullable: true),
                    proof_file_name = table.Column<string>(type: "character varying(260)", maxLength: 260, nullable: true),
                    proof_content_type = table.Column<string>(type: "character varying(120)", maxLength: 120, nullable: true),
                    proof_storage_path = table.Column<string>(type: "character varying(300)", maxLength: 300, nullable: true),
                    proof_size_bytes = table.Column<long>(type: "bigint", nullable: true),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    created_by_id = table.Column<Guid>(type: "uuid", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_payments", x => x.id);
                    table.ForeignKey(
                        name: "fk_payments_installments_installment_id",
                        column: x => x.installment_id,
                        principalSchema: "finance",
                        principalTable: "installments",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "ix_bank_accounts_company_id",
                schema: "finance",
                table: "bank_accounts",
                column: "company_id");

            migrationBuilder.CreateIndex(
                name: "ix_bank_accounts_company_id_bank_code_agency_account_number_ac",
                schema: "finance",
                table: "bank_accounts",
                columns: new[] { "company_id", "bank_code", "agency", "account_number", "account_digit" },
                unique: true,
                filter: "deleted_at IS NULL");

            migrationBuilder.CreateIndex(
                name: "ix_payments_bank_account_id",
                schema: "finance",
                table: "payments",
                column: "bank_account_id");

            migrationBuilder.CreateIndex(
                name: "ix_payments_installment_id",
                schema: "finance",
                table: "payments",
                column: "installment_id");

            migrationBuilder.CreateIndex(
                name: "ix_payments_payable_id",
                schema: "finance",
                table: "payments",
                column: "payable_id");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "bank_accounts",
                schema: "finance");

            migrationBuilder.DropTable(
                name: "payments",
                schema: "finance");
        }
    }
}
