using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Ulbra.One.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class RequisitionStageCountIndexes : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateIndex(
                name: "ix_requisitions_company_id_created_by",
                schema: "budgeting",
                table: "requisitions",
                columns: new[] { "company_id", "created_by" });

            migrationBuilder.CreateIndex(
                name: "ix_requisitions_company_id_status",
                schema: "budgeting",
                table: "requisitions",
                columns: new[] { "company_id", "status" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "ix_requisitions_company_id_created_by",
                schema: "budgeting",
                table: "requisitions");

            migrationBuilder.DropIndex(
                name: "ix_requisitions_company_id_status",
                schema: "budgeting",
                table: "requisitions");
        }
    }
}
