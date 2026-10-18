using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Ulbra.One.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddRequisitionStatusChangedAt : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "status_changed_at",
                schema: "budgeting",
                table: "requisitions",
                type: "timestamp with time zone",
                nullable: false,
                defaultValue: new DateTimeOffset(new DateTime(1, 1, 1, 0, 0, 0, 0, DateTimeKind.Unspecified), new TimeSpan(0, 0, 0, 0, 0)));

            // Backfill de linhas existentes: usa a data de criação como início do estágio atual
            // (piso razoável; o valor exato passa a ser gravado a cada transição a partir de agora).
            // Npgsql grava DateTimeOffset.MinValue (o defaultValue acima) como '-infinity'.
            migrationBuilder.Sql(
                "UPDATE budgeting.requisitions SET status_changed_at = created_at WHERE status_changed_at = '-infinity';");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "status_changed_at",
                schema: "budgeting",
                table: "requisitions");
        }
    }
}
