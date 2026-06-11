using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Ulbra.One.Finance.Infrastructure.Database.Migrations
{
    /// <inheritdoc />
    public partial class AddChartAccountUniqueIndexFilter : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "ix_chart_accounts_chart_type_code",
                schema: "finance",
                table: "chart_accounts");

            migrationBuilder.CreateIndex(
                name: "ix_chart_accounts_chart_type_code",
                schema: "finance",
                table: "chart_accounts",
                columns: new[] { "chart_type", "code" },
                unique: true,
                filter: "deleted_at IS NULL");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "ix_chart_accounts_chart_type_code",
                schema: "finance",
                table: "chart_accounts");

            migrationBuilder.CreateIndex(
                name: "ix_chart_accounts_chart_type_code",
                schema: "finance",
                table: "chart_accounts",
                columns: new[] { "chart_type", "code" },
                unique: true);
        }
    }
}
