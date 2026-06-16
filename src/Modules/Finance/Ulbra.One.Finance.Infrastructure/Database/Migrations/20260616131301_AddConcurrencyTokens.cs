using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Ulbra.One.Finance.Infrastructure.Database.Migrations
{
    /// <inheritdoc />
    public partial class AddConcurrencyTokens : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // No DDL: 'xmin' is a Postgres system column that already exists on every table.
            // It is mapped as a concurrency token (row-version) in the EF model only, so this
            // migration exists solely to keep the model snapshot in sync. The scaffolded
            // AddColumn/DropColumn calls were removed because the physical column already exists
            // (AddColumn would fail with "column \"xmin\" already exists").
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            // No DDL — see Up. The xmin system column cannot (and must not) be dropped.
        }
    }
}
