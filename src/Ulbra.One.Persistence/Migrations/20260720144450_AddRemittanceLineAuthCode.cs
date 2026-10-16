using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Ulbra.One.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddRemittanceLineAuthCode : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "auth_code",
                schema: "finance",
                table: "remittance_lines",
                type: "character varying(64)",
                maxLength: 64,
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "auth_code",
                schema: "finance",
                table: "remittance_lines");
        }
    }
}
