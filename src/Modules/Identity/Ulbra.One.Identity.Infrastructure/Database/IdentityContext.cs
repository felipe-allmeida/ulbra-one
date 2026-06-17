using Microsoft.EntityFrameworkCore;
using Ulbra.One.Audit.Infrastructure;
using Ulbra.One.Identity.Domain.Entities;

namespace Ulbra.One.Identity.Infrastructure.Database;

public class IdentityContext(DbContextOptions<IdentityContext> options) : DbContext(options)
{
    public const string Schema = "identity";

    public DbSet<User> Users => Set<User>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.ApplyConfigurationsFromAssembly(typeof(IdentityContext).Assembly);
        modelBuilder.MapAuditLog(ownsTable: false);
        base.OnModelCreating(modelBuilder);
    }
}
