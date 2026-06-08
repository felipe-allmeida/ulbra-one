using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Ulbra.One.Finance.Infrastructure.Database.Migrations
{
    /// <inheritdoc />
    public partial class CreatePayableAggregate : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "expense_categories",
                schema: "finance",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    name = table.Column<string>(type: "character varying(120)", maxLength: 120, nullable: false),
                    active = table.Column<bool>(type: "boolean", nullable: false),
                    deleted_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    created_by = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    updated_by = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_expense_categories", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "payables",
                schema: "finance",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    company_id = table.Column<Guid>(type: "uuid", nullable: false),
                    origin = table.Column<int>(type: "integer", nullable: false),
                    vendor_id = table.Column<Guid>(type: "uuid", nullable: false),
                    counterparty_name = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    document_number = table.Column<string>(type: "character varying(60)", maxLength: 60, nullable: true),
                    issue_date = table.Column<DateOnly>(type: "date", nullable: true),
                    description = table.Column<string>(type: "character varying(300)", maxLength: 300, nullable: false),
                    cost_center_id = table.Column<Guid>(type: "uuid", nullable: false),
                    category_id = table.Column<Guid>(type: "uuid", nullable: true),
                    total_amount = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: false),
                    status = table.Column<int>(type: "integer", nullable: false),
                    rejection_reason = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    cancel_reason = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    deleted_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    created_by = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    updated_by = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_payables", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "installments",
                schema: "finance",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    payable_id = table.Column<Guid>(type: "uuid", nullable: false),
                    sequence = table.Column<int>(type: "integer", nullable: false),
                    due_date = table.Column<DateOnly>(type: "date", nullable: false),
                    amount = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: false),
                    status = table.Column<int>(type: "integer", nullable: false),
                    paid_amount = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_installments", x => x.id);
                    table.ForeignKey(
                        name: "fk_installments_payables_payable_id",
                        column: x => x.payable_id,
                        principalSchema: "finance",
                        principalTable: "payables",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "payable_attachments",
                schema: "finance",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    payable_id = table.Column<Guid>(type: "uuid", nullable: false),
                    file_name = table.Column<string>(type: "character varying(260)", maxLength: 260, nullable: false),
                    content_type = table.Column<string>(type: "character varying(120)", maxLength: 120, nullable: false),
                    size_bytes = table.Column<long>(type: "bigint", nullable: false),
                    storage_path = table.Column<string>(type: "character varying(300)", maxLength: 300, nullable: false),
                    uploaded_by_id = table.Column<Guid>(type: "uuid", nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_payable_attachments", x => x.id);
                    table.ForeignKey(
                        name: "fk_payable_attachments_payables_payable_id",
                        column: x => x.payable_id,
                        principalSchema: "finance",
                        principalTable: "payables",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "ix_expense_categories_name",
                schema: "finance",
                table: "expense_categories",
                column: "name",
                unique: true,
                filter: "deleted_at IS NULL");

            migrationBuilder.CreateIndex(
                name: "ix_installments_due_date",
                schema: "finance",
                table: "installments",
                column: "due_date");

            migrationBuilder.CreateIndex(
                name: "ix_installments_payable_id",
                schema: "finance",
                table: "installments",
                column: "payable_id");

            migrationBuilder.CreateIndex(
                name: "ix_installments_status",
                schema: "finance",
                table: "installments",
                column: "status");

            migrationBuilder.CreateIndex(
                name: "ix_payable_attachments_payable_id",
                schema: "finance",
                table: "payable_attachments",
                column: "payable_id");

            migrationBuilder.CreateIndex(
                name: "ix_payables_company_id",
                schema: "finance",
                table: "payables",
                column: "company_id");

            migrationBuilder.CreateIndex(
                name: "ix_payables_cost_center_id",
                schema: "finance",
                table: "payables",
                column: "cost_center_id");

            migrationBuilder.CreateIndex(
                name: "ix_payables_status",
                schema: "finance",
                table: "payables",
                column: "status");

            migrationBuilder.CreateIndex(
                name: "ix_payables_vendor_id",
                schema: "finance",
                table: "payables",
                column: "vendor_id");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "expense_categories",
                schema: "finance");

            migrationBuilder.DropTable(
                name: "installments",
                schema: "finance");

            migrationBuilder.DropTable(
                name: "payable_attachments",
                schema: "finance");

            migrationBuilder.DropTable(
                name: "payables",
                schema: "finance");
        }
    }
}
