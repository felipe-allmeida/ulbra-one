using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Ulbra.One.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddSlaReportPermissionAndStageIndexes : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.InsertData(
                schema: "identity",
                table: "role_claim",
                columns: new[] { "permission", "role_id" },
                values: new object[] { "requisitions:sla-report", new Guid("66666666-6666-6666-6666-666666666666") });

            migrationBuilder.CreateIndex(
                name: "ix_requisitions_status_status_changed_at",
                schema: "budgeting",
                table: "requisitions",
                columns: new[] { "status", "status_changed_at" });

            migrationBuilder.CreateIndex(
                name: "ix_requisition_transition_to_status_occurred_at",
                schema: "budgeting",
                table: "requisition_transition",
                columns: new[] { "to_status", "occurred_at" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "ix_requisitions_status_status_changed_at",
                schema: "budgeting",
                table: "requisitions");

            migrationBuilder.DropIndex(
                name: "ix_requisition_transition_to_status_occurred_at",
                schema: "budgeting",
                table: "requisition_transition");

            migrationBuilder.DeleteData(
                schema: "identity",
                table: "role_claim",
                keyColumns: new[] { "permission", "role_id" },
                keyValues: new object[] { "requisitions:sla-report", new Guid("66666666-6666-6666-6666-666666666666") });
        }
    }
}
