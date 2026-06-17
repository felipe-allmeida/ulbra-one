using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Ulbra.One.Finance.Infrastructure.Database.Migrations
{
    /// <inheritdoc />
    public partial class PaymentConcurrencyAndAppendOnly : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "fk_payments_installments_installment_id",
                schema: "finance",
                table: "payments");

            migrationBuilder.AddColumn<uint>(
                name: "xmin",
                schema: "finance",
                table: "payables",
                type: "xid",
                rowVersion: true,
                nullable: false,
                defaultValue: 0u);

            migrationBuilder.AddColumn<uint>(
                name: "xmin",
                schema: "finance",
                table: "installments",
                type: "xid",
                rowVersion: true,
                nullable: false,
                defaultValue: 0u);

            migrationBuilder.AddForeignKey(
                name: "fk_payments_installments_installment_id",
                schema: "finance",
                table: "payments",
                column: "installment_id",
                principalSchema: "finance",
                principalTable: "installments",
                principalColumn: "id",
                onDelete: ReferentialAction.Restrict);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "fk_payments_installments_installment_id",
                schema: "finance",
                table: "payments");

            migrationBuilder.DropColumn(
                name: "xmin",
                schema: "finance",
                table: "payables");

            migrationBuilder.DropColumn(
                name: "xmin",
                schema: "finance",
                table: "installments");

            migrationBuilder.AddForeignKey(
                name: "fk_payments_installments_installment_id",
                schema: "finance",
                table: "payments",
                column: "installment_id",
                principalSchema: "finance",
                principalTable: "installments",
                principalColumn: "id",
                onDelete: ReferentialAction.Cascade);
        }
    }
}
