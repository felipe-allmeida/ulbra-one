using Microsoft.EntityFrameworkCore;
using Ulbra.One.Finance.Domain.Entities;

namespace Ulbra.One.Finance.Infrastructure.Database;

public class FinanceContext(DbContextOptions<FinanceContext> options) : DbContext(options)
{
    public const string Schema = "finance";

    public DbSet<Transaction> Transactions => Set<Transaction>();
    public DbSet<Payable> Payables => Set<Payable>();
    public DbSet<Installment> Installments => Set<Installment>();
    public DbSet<PayableAttachment> PayableAttachments => Set<PayableAttachment>();
    public DbSet<ExpenseCategory> ExpenseCategories => Set<ExpenseCategory>();
    public DbSet<BankAccount> BankAccounts => Set<BankAccount>();
    public DbSet<Payment> Payments => Set<Payment>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.HasDefaultSchema(Schema);

        modelBuilder.Entity<Transaction>(e =>
        {
            e.HasKey(x => x.Id);
            e.Property(x => x.Description).HasMaxLength(300).IsRequired();
            e.Property(x => x.CounterpartyName).HasMaxLength(200);
            e.Property(x => x.Amount).HasPrecision(18, 2);
            e.Property(x => x.RejectionReason).HasMaxLength(500);
            // CompanyId/UnitId/CostCenterId/VendorId/CustomerId: logical references to Core/Registry (no cross-context FK)
            e.HasIndex(x => x.CompanyId);
            e.HasIndex(x => x.CostCenterId);
            e.HasIndex(x => x.VendorId);
            e.HasIndex(x => x.CustomerId);
            e.HasIndex(x => new { x.Type, x.Status });
        });

        modelBuilder.Entity<Payable>(e =>
        {
            e.HasKey(x => x.Id);
            e.Property(x => x.CounterpartyName).HasMaxLength(200).IsRequired();
            e.Property(x => x.DocumentNumber).HasMaxLength(60);
            e.Property(x => x.Description).HasMaxLength(300).IsRequired();
            e.Property(x => x.TotalAmount).HasPrecision(18, 2);
            e.Property(x => x.RejectionReason).HasMaxLength(500);
            e.Property(x => x.CancelReason).HasMaxLength(500);
            e.Property(x => x.ApprovedBy).HasMaxLength(64);
            e.Property(x => x.RejectedBy).HasMaxLength(64);
            e.Property(x => x.CreatedBy).HasMaxLength(64);
            e.Property(x => x.UpdatedBy).HasMaxLength(64);
            e.HasMany(x => x.Installments).WithOne().HasForeignKey(i => i.PayableId).OnDelete(DeleteBehavior.Cascade);
            e.HasMany(x => x.Attachments).WithOne().HasForeignKey(a => a.PayableId).OnDelete(DeleteBehavior.Cascade);
            e.Navigation(x => x.Installments).AutoInclude();
            e.HasIndex(x => x.CompanyId);
            e.HasIndex(x => x.CostCenterId);
            e.HasIndex(x => x.VendorId);
            e.HasIndex(x => x.Status);
            e.HasQueryFilter(x => x.DeletedAt == null);
        });

        modelBuilder.Entity<Installment>(e =>
        {
            e.HasKey(x => x.Id);
            e.Property(x => x.Amount).HasPrecision(18, 2);
            e.Property(x => x.PaidAmount).HasPrecision(18, 2);
            e.HasIndex(x => x.PayableId);
            e.HasIndex(x => x.DueDate);
            e.HasIndex(x => x.Status);
            e.HasMany(x => x.Payments).WithOne().HasForeignKey(p => p.InstallmentId).OnDelete(DeleteBehavior.Cascade);
            e.Navigation(x => x.Payments).AutoInclude();
        });

        modelBuilder.Entity<PayableAttachment>(e =>
        {
            e.HasKey(x => x.Id);
            e.Property(x => x.FileName).HasMaxLength(260).IsRequired();
            e.Property(x => x.ContentType).HasMaxLength(120).IsRequired();
            e.Property(x => x.StoragePath).HasMaxLength(300).IsRequired();
            e.HasIndex(x => x.PayableId);
        });

        modelBuilder.Entity<ExpenseCategory>(e =>
        {
            e.HasKey(x => x.Id);
            e.Property(x => x.Name).HasMaxLength(120).IsRequired();
            e.Property(x => x.CreatedBy).HasMaxLength(64);
            e.Property(x => x.UpdatedBy).HasMaxLength(64);
            e.HasIndex(x => x.Name).IsUnique().HasFilter("deleted_at IS NULL");
            e.HasQueryFilter(x => x.DeletedAt == null);
        });

        modelBuilder.Entity<BankAccount>(e =>
        {
            e.HasKey(x => x.Id);
            e.Property(x => x.BankCode).HasMaxLength(3).IsRequired();
            e.Property(x => x.BankName).HasMaxLength(100).IsRequired();
            e.Property(x => x.Agency).HasMaxLength(10).IsRequired();
            e.Property(x => x.AccountNumber).HasMaxLength(20).IsRequired();
            e.Property(x => x.AccountDigit).HasMaxLength(2);
            e.Property(x => x.Alias).HasMaxLength(60).IsRequired();
            e.Property(x => x.OpeningBalance).HasPrecision(18, 2);
            e.Property(x => x.CreatedBy).HasMaxLength(64);
            e.Property(x => x.UpdatedBy).HasMaxLength(64);
            e.HasIndex(x => x.CompanyId);
            e.HasIndex(x => new { x.CompanyId, x.BankCode, x.Agency, x.AccountNumber, x.AccountDigit })
                .IsUnique().HasFilter("deleted_at IS NULL");
            e.HasQueryFilter(x => x.DeletedAt == null);
        });

        modelBuilder.Entity<Payment>(e =>
        {
            e.HasKey(x => x.Id);
            e.Property(x => x.Amount).HasPrecision(18, 2);
            e.Property(x => x.Interest).HasPrecision(18, 2);
            e.Property(x => x.Fine).HasPrecision(18, 2);
            e.Property(x => x.Discount).HasPrecision(18, 2);
            e.Property(x => x.Notes).HasMaxLength(300);
            e.Property(x => x.ReversalReason).HasMaxLength(300);
            e.Property(x => x.ProofFileName).HasMaxLength(260);
            e.Property(x => x.ProofContentType).HasMaxLength(120);
            e.Property(x => x.ProofStoragePath).HasMaxLength(300);
            e.HasIndex(x => x.InstallmentId);
            e.HasIndex(x => x.PayableId);
            e.HasIndex(x => x.BankAccountId);
        });

        base.OnModelCreating(modelBuilder);
    }
}
