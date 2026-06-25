using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Ulbra.One.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class PayableAccountingPosting : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "source_key",
                schema: "accounting",
                table: "journal_entries",
                type: "character varying(120)",
                maxLength: 120,
                nullable: true);

            migrationBuilder.CreateTable(
                name: "pending_accounting_postings",
                schema: "accounting",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    company_id = table.Column<Guid>(type: "uuid", nullable: false),
                    source_key = table.Column<string>(type: "character varying(120)", maxLength: 120, nullable: false),
                    kind = table.Column<int>(type: "integer", nullable: false),
                    amount = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: false),
                    reason = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    intended_codes = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    resolved_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_pending_accounting_postings", x => x.id);
                });

            migrationBuilder.CreateIndex(
                name: "ix_journal_entries_company_source_key",
                schema: "accounting",
                table: "journal_entries",
                columns: new[] { "company_id", "source_key" },
                unique: true,
                filter: "source_key IS NOT NULL AND deleted_at IS NULL");

            migrationBuilder.CreateIndex(
                name: "ix_pending_accounting_postings_company_id_resolved_at",
                schema: "accounting",
                table: "pending_accounting_postings",
                columns: new[] { "company_id", "resolved_at" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "pending_accounting_postings",
                schema: "accounting");

            migrationBuilder.DropIndex(
                name: "ix_journal_entries_company_source_key",
                schema: "accounting",
                table: "journal_entries");

            migrationBuilder.DropColumn(
                name: "source_key",
                schema: "accounting",
                table: "journal_entries");
        }
    }
}
