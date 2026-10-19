using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Ulbra.One.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class PayableOriginRequisitionCode : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "origin_requisition_code",
                schema: "finance",
                table: "payables",
                type: "integer",
                nullable: true);

            // Backfill existing requisition-sourced payables with the origin requisition's friendly code.
            // Same physical DB (cross-module read by loose id, no FK). New payables are stamped at creation.
            migrationBuilder.Sql(@"
                UPDATE finance.payables p
                SET origin_requisition_code = r.code
                FROM budgeting.requisitions r
                WHERE p.origin_requisition_id = r.id;");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "origin_requisition_code",
                schema: "finance",
                table: "payables");
        }
    }
}
