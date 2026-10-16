using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Ulbra.One.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class PayableCnabReturn : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "bank_reference",
                schema: "finance",
                table: "remittance_lines",
                type: "character varying(20)",
                maxLength: 20,
                nullable: true);

            migrationBuilder.AddColumn<DateOnly>(
                name: "effective_date",
                schema: "finance",
                table: "remittance_lines",
                type: "date",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "return_reason",
                schema: "finance",
                table: "remittance_lines",
                type: "character varying(200)",
                maxLength: 200,
                nullable: true);

            migrationBuilder.CreateTable(
                name: "remittance_returns",
                schema: "finance",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    company_id = table.Column<Guid>(type: "uuid", nullable: false),
                    file_storage_key = table.Column<string>(type: "character varying(300)", maxLength: 300, nullable: false),
                    file_name = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    matched_count = table.Column<int>(type: "integer", nullable: false),
                    paid_count = table.Column<int>(type: "integer", nullable: false),
                    returned_count = table.Column<int>(type: "integer", nullable: false),
                    unmatched_count = table.Column<int>(type: "integer", nullable: false),
                    divergent_count = table.Column<int>(type: "integer", nullable: false),
                    processed_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    processed_by = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    deleted_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    created_by = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    updated_by = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    xmin = table.Column<uint>(type: "xid", rowVersion: true, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_remittance_returns", x => x.id);
                });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "remittance_returns",
                schema: "finance");

            migrationBuilder.DropColumn(
                name: "bank_reference",
                schema: "finance",
                table: "remittance_lines");

            migrationBuilder.DropColumn(
                name: "effective_date",
                schema: "finance",
                table: "remittance_lines");

            migrationBuilder.DropColumn(
                name: "return_reason",
                schema: "finance",
                table: "remittance_lines");
        }
    }
}
