using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

#pragma warning disable CA1814 // Prefer jagged arrays over multidimensional

namespace Ulbra.One.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class BudgetingInitial : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.EnsureSchema(
                name: "budgeting");

            migrationBuilder.CreateTable(
                name: "orcamentos",
                schema: "budgeting",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    company_id = table.Column<Guid>(type: "uuid", nullable: false),
                    unit_id = table.Column<Guid>(type: "uuid", nullable: false),
                    cost_center_id = table.Column<Guid>(type: "uuid", nullable: false),
                    competency = table.Column<string>(type: "character varying(7)", maxLength: 7, nullable: false),
                    limit_value = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: false),
                    active = table.Column<bool>(type: "boolean", nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    created_by = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    updated_by = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_orcamentos", x => x.id);
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
                name: "ix_orcamentos_unit_id_cost_center_id_competency",
                schema: "budgeting",
                table: "orcamentos",
                columns: new[] { "unit_id", "cost_center_id", "competency" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "orcamentos",
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
        }
    }
}
