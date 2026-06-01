using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Ulbra.One.Financeiro.Infrastructure.Database.Migrations
{
    /// <inheritdoc />
    public partial class InitialFinanceiro : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.EnsureSchema(
                name: "financeiro");

            migrationBuilder.CreateTable(
                name: "lancamentos",
                schema: "financeiro",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    empresa_id = table.Column<Guid>(type: "uuid", nullable: false),
                    unidade_id = table.Column<Guid>(type: "uuid", nullable: false),
                    centro_de_custo_id = table.Column<Guid>(type: "uuid", nullable: false),
                    tipo = table.Column<int>(type: "integer", nullable: false),
                    descricao = table.Column<string>(type: "character varying(300)", maxLength: 300, nullable: false),
                    contraparte = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    valor = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: false),
                    vencimento = table.Column<DateOnly>(type: "date", nullable: false),
                    status = table.Column<int>(type: "integer", nullable: false),
                    motivo_rejeicao = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    criado_em = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    aprovado_em = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    pago_em = table.Column<DateTime>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_lancamentos", x => x.id);
                });

            migrationBuilder.CreateIndex(
                name: "ix_lancamentos_centro_de_custo_id",
                schema: "financeiro",
                table: "lancamentos",
                column: "centro_de_custo_id");

            migrationBuilder.CreateIndex(
                name: "ix_lancamentos_empresa_id",
                schema: "financeiro",
                table: "lancamentos",
                column: "empresa_id");

            migrationBuilder.CreateIndex(
                name: "ix_lancamentos_tipo_status",
                schema: "financeiro",
                table: "lancamentos",
                columns: new[] { "tipo", "status" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "lancamentos",
                schema: "financeiro");
        }
    }
}
