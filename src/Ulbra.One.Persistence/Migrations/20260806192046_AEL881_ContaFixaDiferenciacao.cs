using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Ulbra.One.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AEL881_ContaFixaDiferenciacao : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<bool>(
                name: "requires_fiscal_document",
                schema: "finance",
                table: "payables",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.CreateIndex(
                name: "ix_requisitions_fixed_expense_unique",
                schema: "budgeting",
                table: "requisitions",
                columns: new[] { "vendor_id", "unit_id", "cost_center_id" },
                unique: true,
                filter: "type = 'FixedExpense' AND status NOT IN ('Cancelled', 'Finalized')");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "ix_requisitions_fixed_expense_unique",
                schema: "budgeting",
                table: "requisitions");

            migrationBuilder.DropColumn(
                name: "requires_fiscal_document",
                schema: "finance",
                table: "payables");
        }
    }
}
