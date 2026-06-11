using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Ulbra.One.Finance.Infrastructure.Database.Migrations
{
    /// <inheritdoc />
    public partial class AddChartOfAccounts : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<Guid>(
                name: "accounting_account_id",
                schema: "finance",
                table: "payables",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "financial_account_id",
                schema: "finance",
                table: "payables",
                type: "uuid",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "chart_accounts",
                schema: "finance",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    chart_type = table.Column<int>(type: "integer", nullable: false),
                    code = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: false),
                    name = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    parent_id = table.Column<Guid>(type: "uuid", nullable: true),
                    kind = table.Column<int>(type: "integer", nullable: false),
                    nature = table.Column<int>(type: "integer", nullable: true),
                    is_active = table.Column<bool>(type: "boolean", nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    created_by = table.Column<string>(type: "text", nullable: false),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    updated_by = table.Column<string>(type: "text", nullable: false),
                    deleted_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_chart_accounts", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "classification_policies",
                schema: "finance",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    company_id = table.Column<Guid>(type: "uuid", nullable: false),
                    require_to_approve = table.Column<bool>(type: "boolean", nullable: false),
                    require_to_pay = table.Column<bool>(type: "boolean", nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    created_by = table.Column<string>(type: "text", nullable: false),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    updated_by = table.Column<string>(type: "text", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_classification_policies", x => x.id);
                });

            migrationBuilder.CreateIndex(
                name: "ix_chart_accounts_chart_type_code",
                schema: "finance",
                table: "chart_accounts",
                columns: new[] { "chart_type", "code" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_classification_policies_company_id",
                schema: "finance",
                table: "classification_policies",
                column: "company_id",
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "chart_accounts",
                schema: "finance");

            migrationBuilder.DropTable(
                name: "classification_policies",
                schema: "finance");

            migrationBuilder.DropColumn(
                name: "accounting_account_id",
                schema: "finance",
                table: "payables");

            migrationBuilder.DropColumn(
                name: "financial_account_id",
                schema: "finance",
                table: "payables");
        }
    }
}
