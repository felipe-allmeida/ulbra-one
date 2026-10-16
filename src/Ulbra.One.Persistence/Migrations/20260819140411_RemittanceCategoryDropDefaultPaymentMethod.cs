using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Ulbra.One.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class RemittanceCategoryDropDefaultPaymentMethod : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "default_payment_method",
                schema: "finance",
                table: "bank_accounts");

            migrationBuilder.AddColumn<string>(
                name: "category",
                schema: "finance",
                table: "remittances",
                type: "character varying(20)",
                maxLength: 20,
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "category",
                schema: "finance",
                table: "remittances");

            migrationBuilder.AddColumn<int>(
                name: "default_payment_method",
                schema: "finance",
                table: "bank_accounts",
                type: "integer",
                nullable: true);
        }
    }
}
