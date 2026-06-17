using Microsoft.EntityFrameworkCore;
using Ulbra.One.Audit.Infrastructure;
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
    public DbSet<Receivable> Receivables => Set<Receivable>();
    public DbSet<ReceivableInstallment> ReceivableInstallments => Set<ReceivableInstallment>();
    public DbSet<Receipt> Receipts => Set<Receipt>();
    public DbSet<BankStatementImport> StatementImports => Set<BankStatementImport>();
    public DbSet<BankTransaction> BankTransactions => Set<BankTransaction>();
    public DbSet<ChartAccount> ChartAccounts => Set<ChartAccount>();
    public DbSet<ClassificationPolicy> ClassificationPolicies => Set<ClassificationPolicy>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.ApplyConfigurationsFromAssembly(typeof(FinanceContext).Assembly);
        modelBuilder.MapAuditLog(ownsTable: false);
        base.OnModelCreating(modelBuilder);
    }
}
