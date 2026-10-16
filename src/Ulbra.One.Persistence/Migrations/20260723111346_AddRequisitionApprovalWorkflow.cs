using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

#pragma warning disable CA1814 // Prefer jagged arrays over multidimensional

namespace Ulbra.One.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddRequisitionApprovalWorkflow : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<decimal>(
                name: "approval_limit",
                schema: "identity",
                table: "users",
                type: "numeric(18,2)",
                precision: 18,
                scale: 2,
                nullable: true);

            migrationBuilder.AddColumn<bool>(
                name: "has_approval_autonomy",
                schema: "identity",
                table: "users",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.CreateTable(
                name: "requisition_transition",
                schema: "budgeting",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    requisition_id = table.Column<Guid>(type: "uuid", nullable: false),
                    from_status = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    to_status = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    action = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    actor_id = table.Column<Guid>(type: "uuid", nullable: false),
                    occurred_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    reason = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_requisition_transition", x => x.id);
                    table.ForeignKey(
                        name: "fk_requisition_transition_requisitions_requisition_id",
                        column: x => x.requisition_id,
                        principalSchema: "budgeting",
                        principalTable: "requisitions",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.InsertData(
                schema: "identity",
                table: "role_claim",
                columns: new[] { "permission", "role_id" },
                values: new object[,]
                {
                    { "requisitions:approve", new Guid("33333333-3333-3333-3333-333333333333") },
                    { "requisitions:triage", new Guid("44444444-4444-4444-4444-444444444444") }
                });

            migrationBuilder.CreateIndex(
                name: "ix_requisition_transition_requisition_id",
                schema: "budgeting",
                table: "requisition_transition",
                column: "requisition_id");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "requisition_transition",
                schema: "budgeting");

            migrationBuilder.DeleteData(
                schema: "identity",
                table: "role_claim",
                keyColumns: new[] { "permission", "role_id" },
                keyValues: new object[] { "requisitions:approve", new Guid("33333333-3333-3333-3333-333333333333") });

            migrationBuilder.DeleteData(
                schema: "identity",
                table: "role_claim",
                keyColumns: new[] { "permission", "role_id" },
                keyValues: new object[] { "requisitions:triage", new Guid("44444444-4444-4444-4444-444444444444") });

            migrationBuilder.DropColumn(
                name: "approval_limit",
                schema: "identity",
                table: "users");

            migrationBuilder.DropColumn(
                name: "has_approval_autonomy",
                schema: "identity",
                table: "users");
        }
    }
}
