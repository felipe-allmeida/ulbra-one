using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Ulbra.One.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddRequisitionAuthoredInstallments : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<DateOnly>(
                name: "issue_date",
                schema: "budgeting",
                table: "requisitions",
                type: "date",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "requisition_installment",
                schema: "budgeting",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    requisition_id = table.Column<Guid>(type: "uuid", nullable: false),
                    sequence = table.Column<int>(type: "integer", nullable: false),
                    due_date = table.Column<DateOnly>(type: "date", nullable: false),
                    amount = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: false),
                    payment_method = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_requisition_installment", x => x.id);
                    table.ForeignKey(
                        name: "fk_requisition_installment_requisitions_requisition_id",
                        column: x => x.requisition_id,
                        principalSchema: "budgeting",
                        principalTable: "requisitions",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "ix_requisition_installment_requisition_id",
                schema: "budgeting",
                table: "requisition_installment",
                column: "requisition_id");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "requisition_installment",
                schema: "budgeting");

            migrationBuilder.DropColumn(
                name: "issue_date",
                schema: "budgeting",
                table: "requisitions");
        }
    }
}
