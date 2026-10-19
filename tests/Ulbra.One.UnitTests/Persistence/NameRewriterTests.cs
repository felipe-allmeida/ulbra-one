using Ulbra.One.Persistence;

namespace Ulbra.One.UnitTests.Persistence;

/// <summary>
/// Pins <see cref="AppDbContext.NameRewriter"/> to the constraint names that are already in the
/// database. The rewriter is a second, silent declaration of the snake_case choice made by
/// <c>UseSnakeCaseNamingConvention()</c> in <c>PersistenceModule</c> and <c>AppDbContextFactory</c>:
/// it renames every convention-named FK constraint. Change the convention (or the culture) in one
/// place only and the names diverge — which today surfaces late, as a failed startup migration.
/// </summary>
public class NameRewriterTests
{
    // EF's raw default for BudgetAdjustmentTransition -> BudgetAdjustment: it exceeds Postgres'
    // 63-char identifier limit, so EF truncates it and marks the cut with '~'. The rewriter drops
    // the '~' (non-alphanumeric) and lowercases the "FK" prefix, which is the non-obvious part.
    private const string EfDefaultConstraintName =
        "FK_budget_adjustment_transition_budget_adjustments_budget_adju~";

    // Read verbatim from Migrations/AppDbContextModelSnapshot.cs (HasConstraintName on the
    // BudgetAdjustmentTransition relationship) — i.e. the name that exists in the deployed database.
    private const string DeployedConstraintName =
        "fk_budget_adjustment_transition_budget_adjustments_budget_adju";

    [Fact]
    public void RewriteName_ProducesTheDeployedForeignKeyConstraintName()
    {
        var rewritten = AppDbContext.NameRewriter.RewriteName(EfDefaultConstraintName);

        Assert.True(
            rewritten == DeployedConstraintName,
            $"AppDbContext.NameRewriter rewrote '{EfDefaultConstraintName}' to '{rewritten}', but the "
            + $"database has '{DeployedConstraintName}'. PersistenceModule/AppDbContextFactory "
            + "(UseSnakeCaseNamingConvention) and AppDbContext.NameRewriter must declare the same "
            + "naming convention and culture; whoever changes one has to change the other, and a "
            + "genuine convention change needs a migration that renames every FK constraint.");
    }
}
