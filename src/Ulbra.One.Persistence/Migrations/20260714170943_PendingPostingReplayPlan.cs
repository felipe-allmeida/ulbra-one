using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Ulbra.One.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class PendingPostingReplayPlan : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "plan",
                schema: "accounting",
                table: "pending_accounting_postings",
                type: "text",
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "ix_pending_accounting_postings_company_source_key_open",
                schema: "accounting",
                table: "pending_accounting_postings",
                columns: new[] { "company_id", "source_key" },
                unique: true,
                filter: "resolved_at IS NULL");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "ix_pending_accounting_postings_company_source_key_open",
                schema: "accounting",
                table: "pending_accounting_postings");

            migrationBuilder.DropColumn(
                name: "plan",
                schema: "accounting",
                table: "pending_accounting_postings");
        }
    }
}
