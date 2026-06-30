using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Ulbra.One.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddFiscalWithholdingRules : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.EnsureSchema(
                name: "fiscal");

            migrationBuilder.CreateTable(
                name: "withholding_rules",
                schema: "fiscal",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    company_id = table.Column<Guid>(type: "uuid", nullable: true),
                    tax_type = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    lc116subitem = table.Column<string>(type: "character varying(10)", maxLength: 10, nullable: true),
                    vendor_regime = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: true),
                    municipality_ibge = table.Column<string>(type: "character varying(7)", maxLength: 7, nullable: true),
                    retains = table.Column<bool>(type: "boolean", nullable: false),
                    reason = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: false),
                    rate_source = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    rate = table.Column<decimal>(type: "numeric(10,6)", precision: 10, scale: 6, nullable: true),
                    base_rule = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    minimum_amount = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: true),
                    active = table.Column<bool>(type: "boolean", nullable: false),
                    deleted_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    created_by = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    updated_by = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_withholding_rules", x => x.id);
                });

            migrationBuilder.CreateIndex(
                name: "ix_withholding_rules_company_id_tax_type",
                schema: "fiscal",
                table: "withholding_rules",
                columns: new[] { "company_id", "tax_type" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "withholding_rules",
                schema: "fiscal");
        }
    }
}
