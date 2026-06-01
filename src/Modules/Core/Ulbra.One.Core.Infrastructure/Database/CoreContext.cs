using Microsoft.EntityFrameworkCore;
using Ulbra.One.Core.Domain.Entities;

namespace Ulbra.One.Core.Infrastructure.Database;

public class CoreContext(DbContextOptions<CoreContext> options) : DbContext(options)
{
    public const string Schema = "core";

    public DbSet<Empresa> Empresas => Set<Empresa>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.HasDefaultSchema(Schema);

        modelBuilder.Entity<Empresa>(e =>
        {
            e.HasKey(x => x.Id);
            e.Property(x => x.RazaoSocial).HasMaxLength(200).IsRequired();
            e.Property(x => x.Cnpj).HasMaxLength(14).IsRequired();
            e.HasIndex(x => x.Cnpj).IsUnique();
        });

        base.OnModelCreating(modelBuilder);
    }
}
