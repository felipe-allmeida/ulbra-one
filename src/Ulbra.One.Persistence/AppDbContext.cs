using System.Globalization;
using EFCore.NamingConventions.Internal;
using MassTransit;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata;
using Ulbra.One.Common.Infrastructure.Persistence;

namespace Ulbra.One.Persistence;

/// <summary>
/// The application's single DbContext. It knows no module: the model is assembled from the
/// <see cref="IModuleModelConfiguration"/> contributions injected by DI. Entities are reached
/// through <see cref="DbContext.Set{TEntity}"/> — usually via IUnitOfWork — never through
/// DbSet properties declared here.
/// </summary>
public class AppDbContext(
    DbContextOptions<AppDbContext> options,
    IEnumerable<IModuleModelConfiguration>? moduleConfigurations = null) : DbContext(options)
{
    private readonly IReadOnlyCollection<IModuleModelConfiguration> _moduleConfigurations =
        (moduleConfigurations ?? []).ToList();

    /// <summary>
    /// The same rewriter <c>UseSnakeCaseNamingConvention()</c> installs (see PersistenceModule), so
    /// recomputed constraint names come out byte-identical to the ones the convention produces.
    /// Internal (not private) so <c>NameRewriterTests</c> can pin its output to the deployed names.
    /// </summary>
    internal static readonly INameRewriter NameRewriter = new SnakeCaseNameRewriter(CultureInfo.InvariantCulture);

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        if (_moduleConfigurations.Count == 0)
            throw new InvalidOperationException(
                "AppDbContext received no IModuleModelConfiguration contributions — the model would " +
                "only contain the MassTransit outbox entities. Every module's AddXModule must call " +
                "services.AddModelConfiguration(...) (runtime), and the design-time factory must pass " +
                "ModuleModelConfigurations.Create() (Ulbra.One.Api.Database).");

        foreach (var module in _moduleConfigurations)
            module.Apply(modelBuilder);

        // MassTransit's outbox entities ship in the package, so no module configuration declares a
        // TableName for them: only the schema is set here and the table name comes from the type name.
        modelBuilder.AddInboxStateEntity(cfg => cfg.Metadata.SetSchema("masstransit"));
        modelBuilder.AddOutboxMessageEntity(cfg => cfg.Metadata.SetSchema("masstransit"));
        modelBuilder.AddOutboxStateEntity(cfg => cfg.Metadata.SetSchema("masstransit"));

        base.OnModelCreating(modelBuilder);

        RecomputeConventionForeignKeyConstraintNames(modelBuilder);
    }

    /// <summary>
    /// Recomputes every convention-named FK constraint from the final table names.
    /// </summary>
    /// <remarks>
    /// The naming convention bakes a constraint name into an annotation the moment a relationship is
    /// discovered, using the principal's table name at that instant. Without DbSet properties that instant
    /// precedes the <c>TableName</c> declared by BaseEntityConfiguration, so the baked name follows the CLR
    /// type name (singular) instead of the declared table (plural) — renaming 13 existing constraints. The
    /// convention only refreshes a constraint when the <em>dependent</em>'s table name changes, so it never
    /// catches up on its own. Running after every configuration has been applied, this pass redoes exactly
    /// what the convention does — rewrite EF's default name — but against the final table names, which makes
    /// it order-independent: it covers future relationships without anyone having to remember. Names set
    /// explicitly (for example via HasConstraintName) keep their configured value.
    /// </remarks>
    private static void RecomputeConventionForeignKeyConstraintNames(ModelBuilder modelBuilder)
    {
        foreach (var entityType in modelBuilder.Model.GetEntityTypes())
        {
            foreach (var foreignKey in entityType.GetDeclaredForeignKeys())
            {
                var conventionForeignKey = (IConventionForeignKey)foreignKey;

                if (conventionForeignKey.GetConstraintNameConfigurationSource() is { } source
                    && source != ConfigurationSource.Convention)
                    continue;

                if (foreignKey.GetDefaultName() is { } defaultName)
                    conventionForeignKey.SetConstraintName(NameRewriter.RewriteName(defaultName));
            }
        }
    }
}
