using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Ulbra.One.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddInstallmentPixKeyOverride : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "pix_key_override",
                schema: "budgeting",
                table: "requisition_installment",
                type: "character varying(100)",
                maxLength: 100,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "pix_key_override",
                schema: "budgeting",
                table: "requisition_cycle",
                type: "character varying(100)",
                maxLength: 100,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "pix_key_override",
                schema: "finance",
                table: "installments",
                type: "character varying(100)",
                maxLength: 100,
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "pix_key_override",
                schema: "budgeting",
                table: "requisition_installment");

            migrationBuilder.DropColumn(
                name: "pix_key_override",
                schema: "budgeting",
                table: "requisition_cycle");

            migrationBuilder.DropColumn(
                name: "pix_key_override",
                schema: "finance",
                table: "installments");
        }
    }
}
