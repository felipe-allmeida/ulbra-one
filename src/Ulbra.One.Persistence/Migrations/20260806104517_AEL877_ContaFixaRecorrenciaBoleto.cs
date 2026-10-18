using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Ulbra.One.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AEL877_ContaFixaRecorrenciaBoleto : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "ix_payables_origin_requisition_id",
                schema: "finance",
                table: "payables");

            migrationBuilder.AddColumn<Guid>(
                name: "base_payable_id",
                schema: "budgeting",
                table: "requisitions",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "boleto_digitable_line",
                schema: "budgeting",
                table: "requisition_installment",
                type: "character varying(48)",
                maxLength: 48,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "origin_cycle_competency",
                schema: "finance",
                table: "payables",
                type: "character varying(7)",
                maxLength: 7,
                nullable: true);

            migrationBuilder.CreateTable(
                name: "requisition_cycle",
                schema: "budgeting",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    requisition_id = table.Column<Guid>(type: "uuid", nullable: false),
                    sequence = table.Column<int>(type: "integer", nullable: false),
                    competency = table.Column<string>(type: "character varying(7)", maxLength: 7, nullable: false),
                    amount = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: false),
                    due_date = table.Column<DateOnly>(type: "date", nullable: false),
                    payment_method = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false),
                    boleto_digitable_line = table.Column<string>(type: "character varying(48)", maxLength: 48, nullable: true),
                    payable_id = table.Column<Guid>(type: "uuid", nullable: true),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    created_by = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_requisition_cycle", x => x.id);
                    table.ForeignKey(
                        name: "fk_requisition_cycle_requisitions_requisition_id",
                        column: x => x.requisition_id,
                        principalSchema: "budgeting",
                        principalTable: "requisitions",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "ix_payables_origin_requisition_id",
                schema: "finance",
                table: "payables",
                column: "origin_requisition_id",
                unique: true,
                filter: "origin_requisition_id IS NOT NULL AND origin_cycle_competency IS NULL");

            migrationBuilder.CreateIndex(
                name: "ix_payables_origin_requisition_id_origin_cycle_competency",
                schema: "finance",
                table: "payables",
                columns: new[] { "origin_requisition_id", "origin_cycle_competency" },
                unique: true,
                filter: "origin_cycle_competency IS NOT NULL AND status <> 'Cancelled'");

            migrationBuilder.CreateIndex(
                name: "ix_requisition_cycle_requisition_id_competency",
                schema: "budgeting",
                table: "requisition_cycle",
                columns: new[] { "requisition_id", "competency" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "requisition_cycle",
                schema: "budgeting");

            migrationBuilder.DropIndex(
                name: "ix_payables_origin_requisition_id",
                schema: "finance",
                table: "payables");

            migrationBuilder.DropIndex(
                name: "ix_payables_origin_requisition_id_origin_cycle_competency",
                schema: "finance",
                table: "payables");

            migrationBuilder.DropColumn(
                name: "base_payable_id",
                schema: "budgeting",
                table: "requisitions");

            migrationBuilder.DropColumn(
                name: "boleto_digitable_line",
                schema: "budgeting",
                table: "requisition_installment");

            migrationBuilder.DropColumn(
                name: "origin_cycle_competency",
                schema: "finance",
                table: "payables");

            migrationBuilder.CreateIndex(
                name: "ix_payables_origin_requisition_id",
                schema: "finance",
                table: "payables",
                column: "origin_requisition_id",
                unique: true);
        }
    }
}
