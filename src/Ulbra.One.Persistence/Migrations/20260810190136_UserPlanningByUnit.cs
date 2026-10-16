using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

#pragma warning disable CA1814 // Prefer jagged arrays over multidimensional (EF InsertData 2D seed rows)

namespace Ulbra.One.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class UserPlanningByUnit : Migration
    {
        // AEL-883: rename role Superintendence → Approver, add the scoped-user tables (unit + cost-center grants),
        // the per-user can_approve_budget_adjustment flag, and seed the Presidency role (all permissions except admin:users).
        // NOTE: AEL-877/881 schema (requisition_cycle, payable columns) is intentionally NOT here — it was re-scaffolded by
        // `ef migrations add` (the WipeDevDataForReseed snapshot predates those migrations) and is created by them.

        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // Per-user grant to approve/reverse budget adjustments (adequações) — opt-in on top of the
            // Controladoria role permission (AEL-883). Existing rows default to false.
            migrationBuilder.AddColumn<bool>(
                name: "can_approve_budget_adjustment",
                schema: "identity",
                table: "users",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.CreateTable(
                name: "user_cost_center_scope_grant",
                schema: "identity",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    user_id = table.Column<Guid>(type: "uuid", nullable: false),
                    company_id = table.Column<Guid>(type: "uuid", nullable: false),
                    cost_center_id = table.Column<Guid>(type: "uuid", nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    created_by = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    updated_by = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_user_cost_center_scope_grant", x => x.id);
                    table.ForeignKey(
                        name: "fk_user_cost_center_scope_grant_users_user_id",
                        column: x => x.user_id,
                        principalSchema: "identity",
                        principalTable: "users",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "user_unit_scope_grant",
                schema: "identity",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    user_id = table.Column<Guid>(type: "uuid", nullable: false),
                    company_id = table.Column<Guid>(type: "uuid", nullable: false),
                    unit_id = table.Column<Guid>(type: "uuid", nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    created_by = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    updated_by = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_user_unit_scope_grant", x => x.id);
                    table.ForeignKey(
                        name: "fk_user_unit_scope_grant_users_user_id",
                        column: x => x.user_id,
                        principalSchema: "identity",
                        principalTable: "users",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "ix_user_cost_center_scope_grant_user_id_company_id",
                schema: "identity",
                table: "user_cost_center_scope_grant",
                columns: new[] { "user_id", "company_id" });

            migrationBuilder.CreateIndex(
                name: "ix_user_cost_center_scope_grant_user_id_cost_center_id",
                schema: "identity",
                table: "user_cost_center_scope_grant",
                columns: new[] { "user_id", "cost_center_id" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_user_unit_scope_grant_user_id_company_id",
                schema: "identity",
                table: "user_unit_scope_grant",
                columns: new[] { "user_id", "company_id" });

            migrationBuilder.CreateIndex(
                name: "ix_user_unit_scope_grant_user_id_unit_id",
                schema: "identity",
                table: "user_unit_scope_grant",
                columns: new[] { "user_id", "unit_id" },
                unique: true);

            // Rename the seeded role row (HasData keeps the fixed GUID).
            migrationBuilder.UpdateData(
                schema: "identity",
                table: "role",
                keyColumn: "id",
                keyValue: new Guid("66666666-6666-6666-6666-666666666666"),
                column: "name",
                value: "Approver");

            // Rename runtime data not covered by HasData: existing users and pending approval notifications.
            migrationBuilder.Sql("UPDATE identity.users SET role = 'Approver' WHERE role = 'Superintendence';");
            migrationBuilder.Sql("UPDATE notifications.notifications SET audience_role = 'Approver' WHERE audience_role = 'Superintendence';");

            // Seed the Presidency role + every permission except admin:users (AEL-883).
            migrationBuilder.InsertData(
                schema: "identity",
                table: "role",
                columns: new[] { "id", "name" },
                values: new object[] { new Guid("77777777-7777-7777-7777-777777777777"), "Presidency" });

            migrationBuilder.InsertData(
                schema: "identity",
                table: "role_claim",
                columns: new[] { "permission", "role_id" },
                values: new object[,]
                {
                    { "accounting:post", new Guid("77777777-7777-7777-7777-777777777777") },
                    { "accounting:read", new Guid("77777777-7777-7777-7777-777777777777") },
                    { "accounting:write", new Guid("77777777-7777-7777-7777-777777777777") },
                    { "audit:read", new Guid("77777777-7777-7777-7777-777777777777") },
                    { "budget-adjustments:approve", new Guid("77777777-7777-7777-7777-777777777777") },
                    { "budget-adjustments:read", new Guid("77777777-7777-7777-7777-777777777777") },
                    { "budget-adjustments:triage", new Guid("77777777-7777-7777-7777-777777777777") },
                    { "budget-adjustments:write", new Guid("77777777-7777-7777-7777-777777777777") },
                    { "core:read", new Guid("77777777-7777-7777-7777-777777777777") },
                    { "core:write", new Guid("77777777-7777-7777-7777-777777777777") },
                    { "fiscal:read", new Guid("77777777-7777-7777-7777-777777777777") },
                    { "fiscal:write", new Guid("77777777-7777-7777-7777-777777777777") },
                    { "payables:approve", new Guid("77777777-7777-7777-7777-777777777777") },
                    { "payables:pay", new Guid("77777777-7777-7777-7777-777777777777") },
                    { "payables:read", new Guid("77777777-7777-7777-7777-777777777777") },
                    { "payables:write", new Guid("77777777-7777-7777-7777-777777777777") },
                    { "projects:read", new Guid("77777777-7777-7777-7777-777777777777") },
                    { "projects:write", new Guid("77777777-7777-7777-7777-777777777777") },
                    { "receivables:read", new Guid("77777777-7777-7777-7777-777777777777") },
                    { "receivables:receive", new Guid("77777777-7777-7777-7777-777777777777") },
                    { "receivables:write", new Guid("77777777-7777-7777-7777-777777777777") },
                    { "registry:read", new Guid("77777777-7777-7777-7777-777777777777") },
                    { "registry:write", new Guid("77777777-7777-7777-7777-777777777777") },
                    { "reports:read", new Guid("77777777-7777-7777-7777-777777777777") },
                    { "requisitions:approve", new Guid("77777777-7777-7777-7777-777777777777") },
                    { "requisitions:read", new Guid("77777777-7777-7777-7777-777777777777") },
                    { "requisitions:sla-report", new Guid("77777777-7777-7777-7777-777777777777") },
                    { "requisitions:triage", new Guid("77777777-7777-7777-7777-777777777777") },
                    { "requisitions:write", new Guid("77777777-7777-7777-7777-777777777777") },
                    { "treasury:approve", new Guid("77777777-7777-7777-7777-777777777777") },
                    { "treasury:read", new Guid("77777777-7777-7777-7777-777777777777") },
                    { "treasury:write", new Guid("77777777-7777-7777-7777-777777777777") },
                });

            // Controladoria also triages the adequação by default (AEL-883); triage is shared with Accounting.
            migrationBuilder.InsertData(
                schema: "identity",
                table: "role_claim",
                columns: new[] { "permission", "role_id" },
                values: new object[] { "budget-adjustments:triage", new Guid("33333333-3333-3333-3333-333333333333") });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            // Remove the Presidency role + its claims, and the Controladoria triage claim (AEL-883).
            migrationBuilder.Sql("DELETE FROM identity.role_claim WHERE role_id = '77777777-7777-7777-7777-777777777777';");
            migrationBuilder.Sql("DELETE FROM identity.role WHERE id = '77777777-7777-7777-7777-777777777777';");
            migrationBuilder.DeleteData(
                schema: "identity",
                table: "role_claim",
                keyColumns: new[] { "permission", "role_id" },
                keyValues: new object[] { "budget-adjustments:triage", new Guid("33333333-3333-3333-3333-333333333333") });

            migrationBuilder.DropTable(
                name: "user_cost_center_scope_grant",
                schema: "identity");

            migrationBuilder.DropTable(
                name: "user_unit_scope_grant",
                schema: "identity");

            migrationBuilder.UpdateData(
                schema: "identity",
                table: "role",
                keyColumn: "id",
                keyValue: new Guid("66666666-6666-6666-6666-666666666666"),
                column: "name",
                value: "Superintendence");

            migrationBuilder.Sql("UPDATE identity.users SET role = 'Superintendence' WHERE role = 'Approver';");
            migrationBuilder.Sql("UPDATE notifications.notifications SET audience_role = 'Superintendence' WHERE audience_role = 'Approver';");

            migrationBuilder.DropColumn(
                name: "can_approve_budget_adjustment",
                schema: "identity",
                table: "users");
        }
    }
}
