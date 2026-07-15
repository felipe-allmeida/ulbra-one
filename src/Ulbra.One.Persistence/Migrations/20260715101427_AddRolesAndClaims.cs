using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

#pragma warning disable CA1814 // Prefer jagged arrays over multidimensional

namespace Ulbra.One.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddRolesAndClaims : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "role",
                schema: "identity",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    name = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_role", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "role_claim",
                schema: "identity",
                columns: table => new
                {
                    role_id = table.Column<Guid>(type: "uuid", nullable: false),
                    permission = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_role_claim", x => new { x.role_id, x.permission });
                    table.ForeignKey(
                        name: "fk_role_claim_role_role_id",
                        column: x => x.role_id,
                        principalSchema: "identity",
                        principalTable: "role",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.InsertData(
                schema: "identity",
                table: "role",
                columns: new[] { "id", "name" },
                values: new object[,]
                {
                    { new Guid("00000000-0000-0000-0000-000000000001"), "User" },
                    { new Guid("11111111-1111-1111-1111-111111111111"), "Admin" },
                    { new Guid("22222222-2222-2222-2222-222222222222"), "Financeiro" },
                    { new Guid("33333333-3333-3333-3333-333333333333"), "Controladoria" },
                    { new Guid("44444444-4444-4444-4444-444444444444"), "Contabil" }
                });

            migrationBuilder.InsertData(
                schema: "identity",
                table: "role_claim",
                columns: new[] { "permission", "role_id" },
                values: new object[,]
                {
                    { "core:read", new Guid("22222222-2222-2222-2222-222222222222") },
                    { "payables:pay", new Guid("22222222-2222-2222-2222-222222222222") },
                    { "payables:read", new Guid("22222222-2222-2222-2222-222222222222") },
                    { "payables:write", new Guid("22222222-2222-2222-2222-222222222222") },
                    { "receivables:read", new Guid("22222222-2222-2222-2222-222222222222") },
                    { "receivables:receive", new Guid("22222222-2222-2222-2222-222222222222") },
                    { "receivables:write", new Guid("22222222-2222-2222-2222-222222222222") },
                    { "registry:read", new Guid("22222222-2222-2222-2222-222222222222") },
                    { "registry:write", new Guid("22222222-2222-2222-2222-222222222222") },
                    { "reports:read", new Guid("22222222-2222-2222-2222-222222222222") },
                    { "treasury:read", new Guid("22222222-2222-2222-2222-222222222222") },
                    { "treasury:write", new Guid("22222222-2222-2222-2222-222222222222") },
                    { "accounting:read", new Guid("33333333-3333-3333-3333-333333333333") },
                    { "audit:read", new Guid("33333333-3333-3333-3333-333333333333") },
                    { "core:read", new Guid("33333333-3333-3333-3333-333333333333") },
                    { "core:write", new Guid("33333333-3333-3333-3333-333333333333") },
                    { "fiscal:read", new Guid("33333333-3333-3333-3333-333333333333") },
                    { "payables:approve", new Guid("33333333-3333-3333-3333-333333333333") },
                    { "payables:read", new Guid("33333333-3333-3333-3333-333333333333") },
                    { "receivables:read", new Guid("33333333-3333-3333-3333-333333333333") },
                    { "registry:read", new Guid("33333333-3333-3333-3333-333333333333") },
                    { "reports:read", new Guid("33333333-3333-3333-3333-333333333333") },
                    { "treasury:approve", new Guid("33333333-3333-3333-3333-333333333333") },
                    { "treasury:read", new Guid("33333333-3333-3333-3333-333333333333") },
                    { "accounting:post", new Guid("44444444-4444-4444-4444-444444444444") },
                    { "accounting:read", new Guid("44444444-4444-4444-4444-444444444444") },
                    { "accounting:write", new Guid("44444444-4444-4444-4444-444444444444") },
                    { "core:read", new Guid("44444444-4444-4444-4444-444444444444") },
                    { "fiscal:read", new Guid("44444444-4444-4444-4444-444444444444") },
                    { "fiscal:write", new Guid("44444444-4444-4444-4444-444444444444") },
                    { "payables:read", new Guid("44444444-4444-4444-4444-444444444444") },
                    { "receivables:read", new Guid("44444444-4444-4444-4444-444444444444") },
                    { "registry:read", new Guid("44444444-4444-4444-4444-444444444444") },
                    { "reports:read", new Guid("44444444-4444-4444-4444-444444444444") }
                });

            migrationBuilder.CreateIndex(
                name: "ix_role_name",
                schema: "identity",
                table: "role",
                column: "name",
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "role_claim",
                schema: "identity");

            migrationBuilder.DropTable(
                name: "role",
                schema: "identity");
        }
    }
}
