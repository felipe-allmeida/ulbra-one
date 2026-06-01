using Microsoft.EntityFrameworkCore;
using Ulbra.One.Identity.Domain.Entities;

namespace Ulbra.One.Identity.Infrastructure.Database;

public class IdentityContext(DbContextOptions<IdentityContext> options) : DbContext(options)
{
    public const string Schema = "identity";

    public DbSet<Usuario> Usuarios => Set<Usuario>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.HasDefaultSchema(Schema);

        modelBuilder.Entity<Usuario>(e =>
        {
            e.HasKey(x => x.Id);
            e.Property(x => x.Email).HasMaxLength(256).IsRequired();
            e.Property(x => x.Nome).HasMaxLength(200).IsRequired();
            e.Property(x => x.Role).HasMaxLength(50).IsRequired();
            e.Property(x => x.GoogleSubject).HasMaxLength(64);
            e.HasIndex(x => x.Email).IsUnique();
        });

        base.OnModelCreating(modelBuilder);
    }
}
