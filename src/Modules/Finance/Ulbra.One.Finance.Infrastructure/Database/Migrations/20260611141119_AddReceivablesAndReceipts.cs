using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Ulbra.One.Finance.Infrastructure.Database.Migrations
{
    /// <inheritdoc />
    public partial class AddReceivablesAndReceipts : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "receivables",
                schema: "finance",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    company_id = table.Column<Guid>(type: "uuid", nullable: false),
                    origin = table.Column<int>(type: "integer", nullable: false),
                    payer_id = table.Column<Guid>(type: "uuid", nullable: false),
                    payer_name = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    document_number = table.Column<string>(type: "character varying(60)", maxLength: 60, nullable: true),
                    issue_date = table.Column<DateOnly>(type: "date", nullable: true),
                    description = table.Column<string>(type: "character varying(300)", maxLength: 300, nullable: false),
                    cost_center_id = table.Column<Guid>(type: "uuid", nullable: false),
                    revenue_category_id = table.Column<Guid>(type: "uuid", nullable: true),
                    ledger_account_id = table.Column<Guid>(type: "uuid", nullable: true),
                    total_amount = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: false),
                    status = table.Column<int>(type: "integer", nullable: false),
                    cancel_reason = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    write_off_reason = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    deleted_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    created_by = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    updated_by = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_receivables", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "receivable_installments",
                schema: "finance",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    receivable_id = table.Column<Guid>(type: "uuid", nullable: false),
                    sequence = table.Column<int>(type: "integer", nullable: false),
                    due_date = table.Column<DateOnly>(type: "date", nullable: false),
                    amount = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: false),
                    status = table.Column<int>(type: "integer", nullable: false),
                    received_amount = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_receivable_installments", x => x.id);
                    table.ForeignKey(
                        name: "fk_receivable_installments_receivables_receivable_id",
                        column: x => x.receivable_id,
                        principalSchema: "finance",
                        principalTable: "receivables",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "receipts",
                schema: "finance",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    installment_id = table.Column<Guid>(type: "uuid", nullable: false),
                    receivable_id = table.Column<Guid>(type: "uuid", nullable: false),
                    receipt_date = table.Column<DateOnly>(type: "date", nullable: false),
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
                    created_by_id = table.Column<Guid>(type: "uuid", nullable: false),
                    reversed_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    reversed_by_id = table.Column<Guid>(type: "uuid", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_receipts", x => x.id);
                    table.ForeignKey(
                        name: "fk_receipts_receivable_installments_installment_id",
                        column: x => x.installment_id,
                        principalSchema: "finance",
                        principalTable: "receivable_installments",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "ix_receipts_bank_account_id",
                schema: "finance",
                table: "receipts",
                column: "bank_account_id");

            migrationBuilder.CreateIndex(
                name: "ix_receipts_installment_id",
                schema: "finance",
                table: "receipts",
                column: "installment_id");

            migrationBuilder.CreateIndex(
                name: "ix_receipts_receivable_id",
                schema: "finance",
                table: "receipts",
                column: "receivable_id");

            migrationBuilder.CreateIndex(
                name: "ix_receivable_installments_due_date",
                schema: "finance",
                table: "receivable_installments",
                column: "due_date");

            migrationBuilder.CreateIndex(
                name: "ix_receivable_installments_receivable_id",
                schema: "finance",
                table: "receivable_installments",
                column: "receivable_id");

            migrationBuilder.CreateIndex(
                name: "ix_receivable_installments_status",
                schema: "finance",
                table: "receivable_installments",
                column: "status");

            migrationBuilder.CreateIndex(
                name: "ix_receivables_company_id",
                schema: "finance",
                table: "receivables",
                column: "company_id");

            migrationBuilder.CreateIndex(
                name: "ix_receivables_cost_center_id",
                schema: "finance",
                table: "receivables",
                column: "cost_center_id");

            migrationBuilder.CreateIndex(
                name: "ix_receivables_payer_id",
                schema: "finance",
                table: "receivables",
                column: "payer_id");

            migrationBuilder.CreateIndex(
                name: "ix_receivables_status",
                schema: "finance",
                table: "receivables",
                column: "status");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "receipts",
                schema: "finance");

            migrationBuilder.DropTable(
                name: "receivable_installments",
                schema: "finance");

            migrationBuilder.DropTable(
                name: "receivables",
                schema: "finance");
        }
    }
}
