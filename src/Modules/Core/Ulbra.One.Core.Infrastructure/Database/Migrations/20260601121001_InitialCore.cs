using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Ulbra.One.Core.Infrastructure.Database.Migrations
{
    /// <inheritdoc />
    public partial class InitialCore : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.EnsureSchema(
                name: "core");

            migrationBuilder.CreateTable(
                name: "empresas",
                schema: "core",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    razao_social = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    cnpj = table.Column<string>(type: "character varying(14)", maxLength: 14, nullable: false),
                    ativa = table.Column<bool>(type: "boolean", nullable: false),
                    criada_em = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_empresas", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "centros_de_custo",
                schema: "core",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    empresa_id = table.Column<Guid>(type: "uuid", nullable: false),
                    codigo = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                    nome = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    orcamento_anual = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: false),
                    ativo = table.Column<bool>(type: "boolean", nullable: false),
                    criado_em = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_centros_de_custo", x => x.id);
                    table.ForeignKey(
                        name: "fk_centros_de_custo_empresas_empresa_id",
                        column: x => x.empresa_id,
                        principalSchema: "core",
                        principalTable: "empresas",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "unidades",
                schema: "core",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    empresa_id = table.Column<Guid>(type: "uuid", nullable: false),
                    nome = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    sigla = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    ativa = table.Column<bool>(type: "boolean", nullable: false),
                    criada_em = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_unidades", x => x.id);
                    table.ForeignKey(
                        name: "fk_unidades_empresas_empresa_id",
                        column: x => x.empresa_id,
                        principalSchema: "core",
                        principalTable: "empresas",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "centro_de_custo_unidades",
                schema: "core",
                columns: table => new
                {
                    centro_de_custo_id = table.Column<Guid>(type: "uuid", nullable: false),
                    unidade_id = table.Column<Guid>(type: "uuid", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_centro_de_custo_unidades", x => new { x.centro_de_custo_id, x.unidade_id });
                    table.ForeignKey(
                        name: "fk_centro_de_custo_unidades_centros_de_custo_centro_de_custo_id",
                        column: x => x.centro_de_custo_id,
                        principalSchema: "core",
                        principalTable: "centros_de_custo",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "fk_centro_de_custo_unidades_unidades_unidade_id",
                        column: x => x.unidade_id,
                        principalSchema: "core",
                        principalTable: "unidades",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "ix_centro_de_custo_unidades_unidade_id",
                schema: "core",
                table: "centro_de_custo_unidades",
                column: "unidade_id");

            migrationBuilder.CreateIndex(
                name: "ix_centros_de_custo_empresa_id_codigo",
                schema: "core",
                table: "centros_de_custo",
                columns: new[] { "empresa_id", "codigo" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_empresas_cnpj",
                schema: "core",
                table: "empresas",
                column: "cnpj",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_unidades_empresa_id_sigla",
                schema: "core",
                table: "unidades",
                columns: new[] { "empresa_id", "sigla" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "centro_de_custo_unidades",
                schema: "core");

            migrationBuilder.DropTable(
                name: "centros_de_custo",
                schema: "core");

            migrationBuilder.DropTable(
                name: "unidades",
                schema: "core");

            migrationBuilder.DropTable(
                name: "empresas",
                schema: "core");
        }
    }
}
