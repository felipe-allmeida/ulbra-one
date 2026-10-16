using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Ulbra.One.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class Ael794NotificationsAndAttachmentKind : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.EnsureSchema(
                name: "notifications");

            migrationBuilder.AddColumn<string>(
                name: "kind",
                schema: "budgeting",
                table: "requisition_attachment",
                type: "character varying(32)",
                maxLength: 32,
                nullable: false,
                defaultValue: "Other");

            migrationBuilder.CreateTable(
                name: "notifications",
                schema: "notifications",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    company_id = table.Column<Guid>(type: "uuid", nullable: false),
                    audience_role = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: true),
                    audience_user_id = table.Column<Guid>(type: "uuid", nullable: true),
                    type = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    title = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    message = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: false),
                    reference_id = table.Column<Guid>(type: "uuid", nullable: true),
                    source_transition_id = table.Column<Guid>(type: "uuid", nullable: false),
                    occurred_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    read_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_notifications", x => x.id);
                });

            migrationBuilder.CreateIndex(
                name: "ix_notifications_audience_role_occurred_at",
                schema: "notifications",
                table: "notifications",
                columns: new[] { "audience_role", "occurred_at" });

            migrationBuilder.CreateIndex(
                name: "ix_notifications_audience_user_id_occurred_at",
                schema: "notifications",
                table: "notifications",
                columns: new[] { "audience_user_id", "occurred_at" });

            migrationBuilder.CreateIndex(
                name: "ix_notifications_company_id",
                schema: "notifications",
                table: "notifications",
                column: "company_id");

            migrationBuilder.CreateIndex(
                name: "ix_notifications_source_transition_id_type",
                schema: "notifications",
                table: "notifications",
                columns: new[] { "source_transition_id", "type" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "notifications",
                schema: "notifications");

            migrationBuilder.DropColumn(
                name: "kind",
                schema: "budgeting",
                table: "requisition_attachment");
        }
    }
}
