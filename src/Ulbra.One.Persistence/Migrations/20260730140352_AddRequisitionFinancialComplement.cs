using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Ulbra.One.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddRequisitionFinancialComplement : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<decimal>(
                name: "triage_approved_value",
                schema: "budgeting",
                table: "requisitions",
                type: "numeric(18,2)",
                precision: 18,
                scale: 2,
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "triage_approved_vendor_id",
                schema: "budgeting",
                table: "requisitions",
                type: "uuid",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "triage_approved_value",
                schema: "budgeting",
                table: "requisitions");

            migrationBuilder.DropColumn(
                name: "triage_approved_vendor_id",
                schema: "budgeting",
                table: "requisitions");
        }
    }
}
