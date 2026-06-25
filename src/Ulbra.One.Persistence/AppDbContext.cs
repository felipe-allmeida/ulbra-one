using MassTransit;
using Microsoft.EntityFrameworkCore;
using Ulbra.One.Accounting.Domain.Entities;
using Ulbra.One.Accounting.Infrastructure.Database.Configurations;
using Ulbra.One.Audit.Infrastructure;
using Ulbra.One.Core.Domain.Entities;
using Ulbra.One.Core.Infrastructure.Database.Configurations;
using Ulbra.One.Finance.Domain.Entities;
using Ulbra.One.Finance.Infrastructure.Database.Configurations;
using Ulbra.One.Identity.Domain.Entities;
using Ulbra.One.Identity.Infrastructure.Database.Configurations;
using Ulbra.One.Registry.Domain.Entities;
using Ulbra.One.Registry.Infrastructure.Database.Configurations;

namespace Ulbra.One.Persistence;

public class AppDbContext(DbContextOptions<AppDbContext> options) : DbContext(options)
{
    // Core
    public DbSet<Company> Companies => Set<Company>();
    public DbSet<Unit> Units => Set<Unit>();
    public DbSet<CostCenter> CostCenters => Set<CostCenter>();
    public DbSet<CostCenterUnit> CostCenterUnits => Set<CostCenterUnit>();
    // Finance
    public DbSet<Transaction> Transactions => Set<Transaction>();
    public DbSet<Payable> Payables => Set<Payable>();
    public DbSet<Installment> Installments => Set<Installment>();
    public DbSet<PayableAttachment> PayableAttachments => Set<PayableAttachment>();
    public DbSet<FiscalDocument> FiscalDocuments => Set<FiscalDocument>();
    public DbSet<ExpenseCategory> ExpenseCategories => Set<ExpenseCategory>();
    public DbSet<BankAccount> BankAccounts => Set<BankAccount>();
    public DbSet<Payment> Payments => Set<Payment>();
    public DbSet<Receivable> Receivables => Set<Receivable>();
    public DbSet<ReceivableInstallment> ReceivableInstallments => Set<ReceivableInstallment>();
    public DbSet<Receipt> Receipts => Set<Receipt>();
    public DbSet<BankStatementImport> StatementImports => Set<BankStatementImport>();
    public DbSet<BankTransaction> BankTransactions => Set<BankTransaction>();
    public DbSet<ChartAccount> ChartAccounts => Set<ChartAccount>();
    public DbSet<ClassificationPolicy> ClassificationPolicies => Set<ClassificationPolicy>();
    public DbSet<Remittance> Remittances => Set<Remittance>();
    public DbSet<RemittanceLine> RemittanceLines => Set<RemittanceLine>();
    // Accounting
    public DbSet<LedgerAccount> LedgerAccounts => Set<LedgerAccount>();
    public DbSet<JournalEntry> JournalEntries => Set<JournalEntry>();
    public DbSet<JournalLine> JournalLines => Set<JournalLine>();
    public DbSet<PendingAccountingPosting> PendingAccountingPostings => Set<PendingAccountingPosting>();
    // Identity
    public DbSet<User> Users => Set<User>();
    // Registry
    public DbSet<VendorEntity> Vendors => Set<VendorEntity>();
    public DbSet<CustomerEntity> Customers => Set<CustomerEntity>();
    public DbSet<EmployeeEntity> Employees => Set<EmployeeEntity>();
    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.ApplyConfigurationsFromAssembly(typeof(CompanyConfiguration).Assembly);
        modelBuilder.ApplyConfigurationsFromAssembly(typeof(PayableConfiguration).Assembly);
        modelBuilder.ApplyConfigurationsFromAssembly(typeof(UserConfiguration).Assembly);
        modelBuilder.ApplyConfigurationsFromAssembly(typeof(VendorConfiguration).Assembly);
        modelBuilder.ApplyConfigurationsFromAssembly(typeof(LedgerAccountConfiguration).Assembly);
        modelBuilder.MapAuditLog(ownsTable: false);

        // Schema only (table names come from the snake_case convention) — the project's .ToTable anti-pattern.
        modelBuilder.AddInboxStateEntity(cfg => cfg.Metadata.SetSchema("masstransit"));
        modelBuilder.AddOutboxMessageEntity(cfg => cfg.Metadata.SetSchema("masstransit"));
        modelBuilder.AddOutboxStateEntity(cfg => cfg.Metadata.SetSchema("masstransit"));

        base.OnModelCreating(modelBuilder);
    }
}
