using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Ulbra.One.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddWithholdingRuleVendorId : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<Guid>(
                name: "vendor_id",
                schema: "fiscal",
                table: "withholding_rules",
                type: "uuid",
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "ix_withholding_rules_vendor_id_tax_type",
                schema: "fiscal",
                table: "withholding_rules",
                columns: new[] { "vendor_id", "tax_type" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "ix_withholding_rules_vendor_id_tax_type",
                schema: "fiscal",
                table: "withholding_rules");

            migrationBuilder.DropColumn(
                name: "vendor_id",
                schema: "fiscal",
                table: "withholding_rules");
        }
    }
}
