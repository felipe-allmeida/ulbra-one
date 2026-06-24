using Microsoft.EntityFrameworkCore;
using Ulbra.One.Registry.Domain.Entities;
using Ulbra.One.Registry.Domain.ValueObjects;

namespace Ulbra.One.Registry.Infrastructure.Database;

public class RegistryContext(DbContextOptions<RegistryContext> options) : DbContext(options)
{
    public const string Schema = "registry";

    public DbSet<VendorEntity> Vendors => Set<VendorEntity>();
    public DbSet<CustomerEntity> Customers => Set<CustomerEntity>();
    public DbSet<EmployeeEntity> Employees => Set<EmployeeEntity>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.HasDefaultSchema(Schema);

        modelBuilder.Entity<VendorEntity>(e =>
        {
            e.HasKey(x => x.Id);
            e.Property(x => x.Name).HasMaxLength(200).IsRequired();
            e.Property(x => x.TradeName).HasMaxLength(200);
            e.Property(x => x.Document).HasMaxLength(14).IsRequired();
            e.Property(x => x.Email).HasMaxLength(200);
            e.Property(x => x.Phone).HasMaxLength(20);
            e.Property(x => x.PaymentTerms).HasMaxLength(100);
            e.OwnsOne(x => x.BankInfo, ConfigureBankInfo);
            e.Property(x => x.TaxRegime).HasConversion<string>().HasMaxLength(20);
            e.Property(x => x.CreatedBy).HasMaxLength(64);
            e.Property(x => x.UpdatedBy).HasMaxLength(64);
            e.OwnsOne(x => x.Address, ConfigureAddress);
            // Partial unique index: uniqueness applies only to live (non-soft-deleted) rows,
            // so the same document can be re-registered after a soft-delete.
            e.HasIndex(x => new { x.CompanyId, x.Document }).IsUnique().HasFilter("deleted_at IS NULL");
            e.HasQueryFilter(x => x.DeletedAt == null);
        });

        modelBuilder.Entity<CustomerEntity>(e =>
        {
            e.HasKey(x => x.Id);
            e.Property(x => x.Name).HasMaxLength(200).IsRequired();
            e.Property(x => x.TradeName).HasMaxLength(200);
            e.Property(x => x.Document).HasMaxLength(14).IsRequired();
            e.Property(x => x.Email).HasMaxLength(200);
            e.Property(x => x.Phone).HasMaxLength(20);
            e.Property(x => x.CreditLimit).HasPrecision(18, 2);
            e.Property(x => x.CreatedBy).HasMaxLength(64);
            e.Property(x => x.UpdatedBy).HasMaxLength(64);
            e.OwnsOne(x => x.Address, ConfigureAddress);
            // Partial unique index: uniqueness applies only to live (non-soft-deleted) rows,
            // so the same document can be re-registered after a soft-delete.
            e.HasIndex(x => new { x.CompanyId, x.Document }).IsUnique().HasFilter("deleted_at IS NULL");
            e.HasQueryFilter(x => x.DeletedAt == null);
        });

        modelBuilder.Entity<EmployeeEntity>(e =>
        {
            e.HasKey(x => x.Id);
            e.Property(x => x.Name).HasMaxLength(200).IsRequired();
            e.Property(x => x.Document).HasMaxLength(11).IsRequired();
            e.Property(x => x.Email).HasMaxLength(200);
            e.Property(x => x.Phone).HasMaxLength(20);
            e.Property(x => x.Role).HasMaxLength(100).IsRequired();
            e.Property(x => x.CreatedBy).HasMaxLength(64);
            e.Property(x => x.UpdatedBy).HasMaxLength(64);
            e.OwnsOne(x => x.Address, ConfigureAddress);
            // Partial unique index: uniqueness applies only to live (non-soft-deleted) rows,
            // so the same document can be re-registered after a soft-delete.
            e.HasIndex(x => new { x.CompanyId, x.Document }).IsUnique().HasFilter("deleted_at IS NULL");
            e.HasQueryFilter(x => x.DeletedAt == null);
        });

        base.OnModelCreating(modelBuilder);
    }

    private static void ConfigureAddress<T>(
        Microsoft.EntityFrameworkCore.Metadata.Builders.OwnedNavigationBuilder<T, Address> a)
        where T : class
    {
        a.Property(x => x.Cep).HasMaxLength(8);
        a.Property(x => x.Street).HasMaxLength(200);
        a.Property(x => x.Number).HasMaxLength(20);
        a.Property(x => x.Complement).HasMaxLength(100);
        a.Property(x => x.Neighborhood).HasMaxLength(100);
        a.Property(x => x.City).HasMaxLength(100);
        a.Property(x => x.State).HasMaxLength(2);
    }

    private static void ConfigureBankInfo<T>(
        Microsoft.EntityFrameworkCore.Metadata.Builders.OwnedNavigationBuilder<T, BankInfo> b)
        where T : class
    {
        b.Property(x => x.BankCode).HasMaxLength(10);
        b.Property(x => x.Branch).HasMaxLength(10);
        b.Property(x => x.Account).HasMaxLength(20);
        b.Property(x => x.AccountDigit).HasMaxLength(2);
        b.Property(x => x.PixKey).HasMaxLength(100);
    }
}
