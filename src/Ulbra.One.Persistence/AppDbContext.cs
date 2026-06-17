using Microsoft.EntityFrameworkCore;
using Ulbra.One.Audit.Infrastructure;
using Ulbra.One.Core.Domain.Entities;
using Ulbra.One.Core.Infrastructure.Database;
using Ulbra.One.Finance.Domain.Entities;
using Ulbra.One.Finance.Infrastructure.Database;
using Ulbra.One.Identity.Domain.Entities;
using Ulbra.One.Identity.Infrastructure.Database;
using Ulbra.One.Registry.Domain.Entities;
using Ulbra.One.Registry.Infrastructure.Database;

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
    // Identity
    public DbSet<User> Users => Set<User>();
    // Registry
    public DbSet<VendorEntity> Vendors => Set<VendorEntity>();
    public DbSet<CustomerEntity> Customers => Set<CustomerEntity>();
    public DbSet<EmployeeEntity> Employees => Set<EmployeeEntity>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.ApplyConfigurationsFromAssembly(typeof(CoreContext).Assembly);
        modelBuilder.ApplyConfigurationsFromAssembly(typeof(FinanceContext).Assembly);
        modelBuilder.ApplyConfigurationsFromAssembly(typeof(IdentityContext).Assembly);
        modelBuilder.ApplyConfigurationsFromAssembly(typeof(RegistryContext).Assembly);
        modelBuilder.MapAuditLog(ownsTable: false);
        base.OnModelCreating(modelBuilder);
    }
}
