using Microsoft.EntityFrameworkCore;
using Ulbra.One.Core.Domain.Entities;

namespace Ulbra.One.Core.Infrastructure.Database;

public class CoreContext(DbContextOptions<CoreContext> options) : DbContext(options)
{
    public const string Schema = "core";

    public DbSet<Company> Companies => Set<Company>();
    public DbSet<Unit> Units => Set<Unit>();
    public DbSet<CostCenter> CostCenters => Set<CostCenter>();
    public DbSet<CostCenterUnit> CostCenterUnits => Set<CostCenterUnit>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.HasDefaultSchema(Schema);

        modelBuilder.Entity<Company>(e =>
        {
            e.HasKey(x => x.Id);
            e.Property(x => x.LegalName).HasMaxLength(200).IsRequired();
            e.Property(x => x.Cnpj).HasMaxLength(14).IsRequired();
            e.HasIndex(x => x.Cnpj).IsUnique();
        });

        modelBuilder.Entity<Unit>(e =>
        {
            e.HasKey(x => x.Id);
            e.Property(x => x.Name).HasMaxLength(200).IsRequired();
            e.Property(x => x.Acronym).HasMaxLength(20).IsRequired();
            e.HasOne<Company>().WithMany().HasForeignKey(x => x.CompanyId).OnDelete(DeleteBehavior.Restrict);
            e.HasIndex(x => new { x.CompanyId, x.Acronym }).IsUnique();
        });

        modelBuilder.Entity<CostCenter>(e =>
        {
            e.HasKey(x => x.Id);
            e.Property(x => x.Code).HasMaxLength(50).IsRequired();
            e.Property(x => x.Name).HasMaxLength(200).IsRequired();
            e.Property(x => x.AnnualBudget).HasPrecision(18, 2);
            e.Property(x => x.AutoApprovalLimit).HasPrecision(18, 2);
            e.HasOne<Company>().WithMany().HasForeignKey(x => x.CompanyId).OnDelete(DeleteBehavior.Restrict);
            e.HasIndex(x => new { x.CompanyId, x.Code }).IsUnique();
        });

        modelBuilder.Entity<CostCenterUnit>(e =>
        {
            e.HasKey(x => new { x.CostCenterId, x.UnitId });
            e.HasOne<CostCenter>().WithMany().HasForeignKey(x => x.CostCenterId).OnDelete(DeleteBehavior.Cascade);
            e.HasOne<Unit>().WithMany().HasForeignKey(x => x.UnitId).OnDelete(DeleteBehavior.Cascade);
        });

        base.OnModelCreating(modelBuilder);
    }
}
