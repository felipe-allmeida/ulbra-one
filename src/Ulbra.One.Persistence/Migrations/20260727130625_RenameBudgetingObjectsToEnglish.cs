using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Ulbra.One.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class RenameBudgetingObjectsToEnglish : Migration
    {
        // Standardizes the Budgeting module's persisted names to English (Orcamento→Budget,
        // OrcamentoAdequacao→BudgetAdjustment, Projeto→Project). EF scaffolds a table rename as drop+create;
        // rewritten here as in-place RENAMEs so existing rows survive (the module is already deployed).
        // Postgres keeps constraint/index names when a table is renamed, so PKs/indexes/FK are renamed
        // explicitly to match the model's convention (otherwise has-pending-model-changes would flag drift).

        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.RenameTable(name: "orcamentos", schema: "budgeting", newName: "budgets");
            migrationBuilder.RenameTable(name: "orcamento_adequacoes", schema: "budgeting", newName: "budget_adjustments");
            migrationBuilder.RenameTable(name: "projetos", schema: "budgeting", newName: "projects");
            migrationBuilder.RenameTable(name: "orcamento_adequacao_transition", schema: "budgeting", newName: "budget_adjustment_transition");

            migrationBuilder.RenameColumn(name: "adequacao_id", schema: "budgeting", table: "budget_adjustment_transition", newName: "budget_adjustment_id");

            migrationBuilder.RenameIndex(name: "ix_orcamentos_unit_id_cost_center_id_competency", schema: "budgeting", table: "budgets", newName: "ix_budgets_unit_id_cost_center_id_competency");
            migrationBuilder.RenameIndex(name: "ix_orcamento_adequacoes_company_id_code", schema: "budgeting", table: "budget_adjustments", newName: "ix_budget_adjustments_company_id_code");
            migrationBuilder.RenameIndex(name: "ix_projetos_company_id_name", schema: "budgeting", table: "projects", newName: "ix_projects_company_id_name");
            migrationBuilder.RenameIndex(name: "ix_projetos_unit_id", schema: "budgeting", table: "projects", newName: "ix_projects_unit_id");
            migrationBuilder.RenameIndex(name: "ix_orcamento_adequacao_transition_adequacao_id", schema: "budgeting", table: "budget_adjustment_transition", newName: "ix_budget_adjustment_transition_budget_adjustment_id");

            // PK/FK constraints have no rename op in MigrationBuilder — rename via SQL to the convention names.
            migrationBuilder.Sql("ALTER TABLE budgeting.budgets RENAME CONSTRAINT pk_orcamentos TO pk_budgets;");
            migrationBuilder.Sql("ALTER TABLE budgeting.budget_adjustments RENAME CONSTRAINT pk_orcamento_adequacoes TO pk_budget_adjustments;");
            migrationBuilder.Sql("ALTER TABLE budgeting.projects RENAME CONSTRAINT pk_projetos TO pk_projects;");
            migrationBuilder.Sql("ALTER TABLE budgeting.budget_adjustment_transition RENAME CONSTRAINT pk_orcamento_adequacao_transition TO pk_budget_adjustment_transition;");
            migrationBuilder.Sql("ALTER TABLE budgeting.budget_adjustment_transition RENAME CONSTRAINT \"fk_orcamento_adequacao_transition_orcamento_adequacoes_adequac\" TO \"fk_budget_adjustment_transition_budget_adjustments_budget_adju\";");

            // Role permission claims (seed reference rows) — rename the value in place, e.g. adequacoes:read → budget-adjustments:read.
            migrationBuilder.Sql("UPDATE identity.role_claim SET permission = 'budget-adjustments:' || split_part(permission, ':', 2) WHERE permission LIKE 'adequacoes:%';");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("UPDATE identity.role_claim SET permission = 'adequacoes:' || split_part(permission, ':', 2) WHERE permission LIKE 'budget-adjustments:%';");

            migrationBuilder.Sql("ALTER TABLE budgeting.budget_adjustment_transition RENAME CONSTRAINT \"fk_budget_adjustment_transition_budget_adjustments_budget_adju\" TO \"fk_orcamento_adequacao_transition_orcamento_adequacoes_adequac\";");
            migrationBuilder.Sql("ALTER TABLE budgeting.budget_adjustment_transition RENAME CONSTRAINT pk_budget_adjustment_transition TO pk_orcamento_adequacao_transition;");
            migrationBuilder.Sql("ALTER TABLE budgeting.projects RENAME CONSTRAINT pk_projects TO pk_projetos;");
            migrationBuilder.Sql("ALTER TABLE budgeting.budget_adjustments RENAME CONSTRAINT pk_budget_adjustments TO pk_orcamento_adequacoes;");
            migrationBuilder.Sql("ALTER TABLE budgeting.budgets RENAME CONSTRAINT pk_budgets TO pk_orcamentos;");

            migrationBuilder.RenameIndex(name: "ix_budget_adjustment_transition_budget_adjustment_id", schema: "budgeting", table: "budget_adjustment_transition", newName: "ix_orcamento_adequacao_transition_adequacao_id");
            migrationBuilder.RenameIndex(name: "ix_projects_unit_id", schema: "budgeting", table: "projects", newName: "ix_projetos_unit_id");
            migrationBuilder.RenameIndex(name: "ix_projects_company_id_name", schema: "budgeting", table: "projects", newName: "ix_projetos_company_id_name");
            migrationBuilder.RenameIndex(name: "ix_budget_adjustments_company_id_code", schema: "budgeting", table: "budget_adjustments", newName: "ix_orcamento_adequacoes_company_id_code");
            migrationBuilder.RenameIndex(name: "ix_budgets_unit_id_cost_center_id_competency", schema: "budgeting", table: "budgets", newName: "ix_orcamentos_unit_id_cost_center_id_competency");

            migrationBuilder.RenameColumn(name: "budget_adjustment_id", schema: "budgeting", table: "budget_adjustment_transition", newName: "adequacao_id");

            migrationBuilder.RenameTable(name: "budget_adjustment_transition", schema: "budgeting", newName: "orcamento_adequacao_transition");
            migrationBuilder.RenameTable(name: "projects", schema: "budgeting", newName: "projetos");
            migrationBuilder.RenameTable(name: "budget_adjustments", schema: "budgeting", newName: "orcamento_adequacoes");
            migrationBuilder.RenameTable(name: "budgets", schema: "budgeting", newName: "orcamentos");
        }
    }
}
