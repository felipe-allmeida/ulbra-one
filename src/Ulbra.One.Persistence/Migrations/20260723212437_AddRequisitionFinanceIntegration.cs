using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Ulbra.One.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddRequisitionFinanceIntegration : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<DateOnly>(
                name: "first_due_date",
                schema: "budgeting",
                table: "requisitions",
                type: "date",
                nullable: false,
                defaultValue: new DateOnly(1, 1, 1));

            migrationBuilder.AddColumn<Guid>(
                name: "vendor_id",
                schema: "budgeting",
                table: "requisitions",
                type: "uuid",
                nullable: false,
                defaultValue: new Guid("00000000-0000-0000-0000-000000000000"));

            migrationBuilder.AddColumn<Guid>(
                name: "origin_requisition_id",
                schema: "finance",
                table: "payables",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "origin_unit_id",
                schema: "finance",
                table: "payables",
                type: "uuid",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "requisition_payment",
                schema: "budgeting",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    requisition_id = table.Column<Guid>(type: "uuid", nullable: false),
                    payment_id = table.Column<Guid>(type: "uuid", nullable: false),
                    payable_id = table.Column<Guid>(type: "uuid", nullable: false),
                    amount = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: false),
                    competency = table.Column<string>(type: "character varying(7)", maxLength: 7, nullable: false),
                    paid_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    reversed = table.Column<bool>(type: "boolean", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_requisition_payment", x => x.id);
                    table.ForeignKey(
                        name: "fk_requisition_payment_requisitions_requisition_id",
                        column: x => x.requisition_id,
                        principalSchema: "budgeting",
                        principalTable: "requisitions",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "ix_requisitions_vendor_id",
                schema: "budgeting",
                table: "requisitions",
                column: "vendor_id");

            migrationBuilder.CreateIndex(
                name: "ix_payables_origin_requisition_id",
                schema: "finance",
                table: "payables",
                column: "origin_requisition_id",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_requisition_payment_payment_id",
                schema: "budgeting",
                table: "requisition_payment",
                column: "payment_id",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_requisition_payment_requisition_id",
                schema: "budgeting",
                table: "requisition_payment",
                column: "requisition_id");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "requisition_payment",
                schema: "budgeting");

            migrationBuilder.DropIndex(
                name: "ix_requisitions_vendor_id",
                schema: "budgeting",
                table: "requisitions");

            migrationBuilder.DropIndex(
                name: "ix_payables_origin_requisition_id",
                schema: "finance",
                table: "payables");

            migrationBuilder.DropColumn(
                name: "first_due_date",
                schema: "budgeting",
                table: "requisitions");

            migrationBuilder.DropColumn(
                name: "vendor_id",
                schema: "budgeting",
                table: "requisitions");

            migrationBuilder.DropColumn(
                name: "origin_requisition_id",
                schema: "finance",
                table: "payables");

            migrationBuilder.DropColumn(
                name: "origin_unit_id",
                schema: "finance",
                table: "payables");
        }
    }
}
