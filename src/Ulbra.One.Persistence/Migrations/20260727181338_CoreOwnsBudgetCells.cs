using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

#pragma warning disable CA1814 // Prefer jagged arrays over multidimensional

namespace Ulbra.One.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class CoreOwnsBudgetCells : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "budgets",
                schema: "budgeting");

            migrationBuilder.DeleteData(
                schema: "identity",
                table: "role_claim",
                keyColumns: new[] { "permission", "role_id" },
                keyValues: new object[] { "budgeting:read", new Guid("22222222-2222-2222-2222-222222222222") });

            migrationBuilder.DeleteData(
                schema: "identity",
                table: "role_claim",
                keyColumns: new[] { "permission", "role_id" },
                keyValues: new object[] { "budgeting:read", new Guid("33333333-3333-3333-3333-333333333333") });

            migrationBuilder.DeleteData(
                schema: "identity",
                table: "role_claim",
                keyColumns: new[] { "permission", "role_id" },
                keyValues: new object[] { "budgeting:write", new Guid("33333333-3333-3333-3333-333333333333") });

            migrationBuilder.DeleteData(
                schema: "identity",
                table: "role_claim",
                keyColumns: new[] { "permission", "role_id" },
                keyValues: new object[] { "budgeting:read", new Guid("44444444-4444-4444-4444-444444444444") });

            migrationBuilder.DropColumn(
                name: "annual_budget",
                schema: "core",
                table: "cost_centers");

            migrationBuilder.CreateTable(
                name: "cost_center_unit_budgets",
                schema: "core",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    cost_center_id = table.Column<Guid>(type: "uuid", nullable: false),
                    unit_id = table.Column<Guid>(type: "uuid", nullable: false),
                    company_id = table.Column<Guid>(type: "uuid", nullable: false),
                    competency = table.Column<string>(type: "character varying(7)", maxLength: 7, nullable: false),
                    monthly_limit = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: false),
                    active = table.Column<bool>(type: "boolean", nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    created_by = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    updated_by = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_cost_center_unit_budgets", x => x.id);
                    table.ForeignKey(
                        name: "fk_cost_center_unit_budgets_cost_centers_cost_center_id",
                        column: x => x.cost_center_id,
                        principalSchema: "core",
                        principalTable: "cost_centers",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "fk_cost_center_unit_budgets_units_unit_id",
                        column: x => x.unit_id,
                        principalSchema: "core",
                        principalTable: "units",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.InsertData(
                schema: "identity",
                table: "role_claim",
                columns: new[] { "permission", "role_id" },
                values: new object[] { "requisitions:override-budget", new Guid("33333333-3333-3333-3333-333333333333") });

            migrationBuilder.CreateIndex(
                name: "ix_cost_center_unit_budgets_company_id",
                schema: "core",
                table: "cost_center_unit_budgets",
                column: "company_id");

            migrationBuilder.CreateIndex(
                name: "ix_cost_center_unit_budgets_cost_center_id_unit_id_competency",
                schema: "core",
                table: "cost_center_unit_budgets",
                columns: new[] { "cost_center_id", "unit_id", "competency" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_cost_center_unit_budgets_unit_id",
                schema: "core",
                table: "cost_center_unit_budgets",
                column: "unit_id");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "cost_center_unit_budgets",
                schema: "core");

            migrationBuilder.DeleteData(
                schema: "identity",
                table: "role_claim",
                keyColumns: new[] { "permission", "role_id" },
                keyValues: new object[] { "requisitions:override-budget", new Guid("33333333-3333-3333-3333-333333333333") });

            migrationBuilder.AddColumn<decimal>(
                name: "annual_budget",
                schema: "core",
                table: "cost_centers",
                type: "numeric(18,2)",
                precision: 18,
                scale: 2,
                nullable: false,
                defaultValue: 0m);

            migrationBuilder.CreateTable(
                name: "budgets",
                schema: "budgeting",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    active = table.Column<bool>(type: "boolean", nullable: false),
                    company_id = table.Column<Guid>(type: "uuid", nullable: false),
                    competency = table.Column<string>(type: "character varying(7)", maxLength: 7, nullable: false),
                    cost_center_id = table.Column<Guid>(type: "uuid", nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    created_by = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    limit_value = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: false),
                    unit_id = table.Column<Guid>(type: "uuid", nullable: false),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    updated_by = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_budgets", x => x.id);
                });

            migrationBuilder.InsertData(
                schema: "identity",
                table: "role_claim",
                columns: new[] { "permission", "role_id" },
                values: new object[,]
                {
                    { "budgeting:read", new Guid("22222222-2222-2222-2222-222222222222") },
                    { "budgeting:read", new Guid("33333333-3333-3333-3333-333333333333") },
                    { "budgeting:write", new Guid("33333333-3333-3333-3333-333333333333") },
                    { "budgeting:read", new Guid("44444444-4444-4444-4444-444444444444") }
                });

            migrationBuilder.CreateIndex(
                name: "ix_budgets_unit_id_cost_center_id_competency",
                schema: "budgeting",
                table: "budgets",
                columns: new[] { "unit_id", "cost_center_id", "competency" },
                unique: true);
        }
    }
}
