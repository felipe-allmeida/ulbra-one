using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

#pragma warning disable CA1814 // Prefer jagged arrays over multidimensional

namespace Ulbra.One.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class RequisitionInitial : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "requisitions",
                schema: "budgeting",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    code = table.Column<int>(type: "integer", nullable: false),
                    company_id = table.Column<Guid>(type: "uuid", nullable: false),
                    unit_id = table.Column<Guid>(type: "uuid", nullable: false),
                    cost_center_id = table.Column<Guid>(type: "uuid", nullable: false),
                    type = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    description = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: false),
                    observation = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: true),
                    estimate_value = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: false),
                    estimate_type = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false),
                    start_competency = table.Column<string>(type: "character varying(7)", maxLength: 7, nullable: false),
                    end_competency = table.Column<string>(type: "character varying(7)", maxLength: 7, nullable: true),
                    payment_frequency = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: true),
                    payment_installments_count = table.Column<int>(type: "integer", nullable: true),
                    status = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    created_by = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    updated_by = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_requisitions", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "requisition_attachment",
                schema: "budgeting",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    requisition_id = table.Column<Guid>(type: "uuid", nullable: false),
                    file_name = table.Column<string>(type: "character varying(260)", maxLength: 260, nullable: false),
                    content_type = table.Column<string>(type: "character varying(120)", maxLength: 120, nullable: false),
                    size_bytes = table.Column<long>(type: "bigint", nullable: false),
                    storage_path = table.Column<string>(type: "character varying(300)", maxLength: 300, nullable: false),
                    uploaded_by_id = table.Column<Guid>(type: "uuid", nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_requisition_attachment", x => x.id);
                    table.ForeignKey(
                        name: "fk_requisition_attachment_requisitions_requisition_id",
                        column: x => x.requisition_id,
                        principalSchema: "budgeting",
                        principalTable: "requisitions",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.InsertData(
                schema: "identity",
                table: "role_claim",
                columns: new[] { "permission", "role_id" },
                values: new object[,]
                {
                    { "requisitions:read", new Guid("22222222-2222-2222-2222-222222222222") },
                    { "requisitions:write", new Guid("22222222-2222-2222-2222-222222222222") },
                    { "requisitions:read", new Guid("33333333-3333-3333-3333-333333333333") },
                    { "requisitions:write", new Guid("33333333-3333-3333-3333-333333333333") },
                    { "requisitions:read", new Guid("44444444-4444-4444-4444-444444444444") }
                });

            migrationBuilder.CreateIndex(
                name: "ix_requisition_attachment_requisition_id",
                schema: "budgeting",
                table: "requisition_attachment",
                column: "requisition_id");

            migrationBuilder.CreateIndex(
                name: "ix_requisitions_company_id_code",
                schema: "budgeting",
                table: "requisitions",
                columns: new[] { "company_id", "code" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "requisition_attachment",
                schema: "budgeting");

            migrationBuilder.DropTable(
                name: "requisitions",
                schema: "budgeting");

            migrationBuilder.DeleteData(
                schema: "identity",
                table: "role_claim",
                keyColumns: new[] { "permission", "role_id" },
                keyValues: new object[] { "requisitions:read", new Guid("22222222-2222-2222-2222-222222222222") });

            migrationBuilder.DeleteData(
                schema: "identity",
                table: "role_claim",
                keyColumns: new[] { "permission", "role_id" },
                keyValues: new object[] { "requisitions:write", new Guid("22222222-2222-2222-2222-222222222222") });

            migrationBuilder.DeleteData(
                schema: "identity",
                table: "role_claim",
                keyColumns: new[] { "permission", "role_id" },
                keyValues: new object[] { "requisitions:read", new Guid("33333333-3333-3333-3333-333333333333") });

            migrationBuilder.DeleteData(
                schema: "identity",
                table: "role_claim",
                keyColumns: new[] { "permission", "role_id" },
                keyValues: new object[] { "requisitions:write", new Guid("33333333-3333-3333-3333-333333333333") });

            migrationBuilder.DeleteData(
                schema: "identity",
                table: "role_claim",
                keyColumns: new[] { "permission", "role_id" },
                keyValues: new object[] { "requisitions:read", new Guid("44444444-4444-4444-4444-444444444444") });
        }
    }
}
