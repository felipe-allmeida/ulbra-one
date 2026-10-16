using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Ulbra.One.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class PixPayloadEmvDuplicateIndex : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateIndex(
                name: "ix_requisition_installment_pix_payload_emv",
                schema: "budgeting",
                table: "requisition_installment",
                column: "pix_payload_emv");

            migrationBuilder.CreateIndex(
                name: "ix_requisition_cycle_pix_payload_emv",
                schema: "budgeting",
                table: "requisition_cycle",
                column: "pix_payload_emv");

            migrationBuilder.CreateIndex(
                name: "ix_installments_pix_payload_emv",
                schema: "finance",
                table: "installments",
                column: "pix_payload_emv");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "ix_requisition_installment_pix_payload_emv",
                schema: "budgeting",
                table: "requisition_installment");

            migrationBuilder.DropIndex(
                name: "ix_requisition_cycle_pix_payload_emv",
                schema: "budgeting",
                table: "requisition_cycle");

            migrationBuilder.DropIndex(
                name: "ix_installments_pix_payload_emv",
                schema: "finance",
                table: "installments");
        }
    }
}
