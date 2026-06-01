using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Ulbra.One.Core.Infrastructure.Database.Migrations
{
    /// <inheritdoc />
    public partial class CentroDeCustoLimiteAutoAprovacao : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<decimal>(
                name: "limite_auto_aprovacao",
                schema: "core",
                table: "centros_de_custo",
                type: "numeric(18,2)",
                precision: 18,
                scale: 2,
                nullable: false,
                defaultValue: 0m);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "limite_auto_aprovacao",
                schema: "core",
                table: "centros_de_custo");
        }
    }
}
