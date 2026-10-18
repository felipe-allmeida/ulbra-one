using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Ulbra.One.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddJournalEntryCounterparty : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "counterparty_document",
                schema: "accounting",
                table: "journal_entries",
                type: "character varying(32)",
                maxLength: 32,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "counterparty_name",
                schema: "accounting",
                table: "journal_entries",
                type: "character varying(200)",
                maxLength: 200,
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "counterparty_document",
                schema: "accounting",
                table: "journal_entries");

            migrationBuilder.DropColumn(
                name: "counterparty_name",
                schema: "accounting",
                table: "journal_entries");
        }
    }
}
