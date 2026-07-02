using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Ulbra.One.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class RemovePayableSimplesDeclaration : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "simples_declaration_provided",
                schema: "finance",
                table: "payables");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<bool>(
                name: "simples_declaration_provided",
                schema: "finance",
                table: "payables",
                type: "boolean",
                nullable: false,
                defaultValue: false);
        }
    }
}
