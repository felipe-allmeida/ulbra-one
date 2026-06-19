using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Routing;
using Ulbra.One.Common.Application.Extensions;
using Ulbra.One.Finance.Application.Endpoints.Aging.GetAgingItems;
using Ulbra.One.Finance.Application.Endpoints.Aging.GetAgingReport;
using Ulbra.One.Finance.Application.Endpoints.BankAccounts.CreateBankAccount;
using Ulbra.One.Finance.Application.Endpoints.CashFlow;
using Ulbra.One.Finance.Application.Endpoints.Conciliation.ConfirmMatch;
using Ulbra.One.Finance.Application.Endpoints.Conciliation.IgnoreTransaction;
using Ulbra.One.Finance.Application.Endpoints.Conciliation.ImportStatement;
using Ulbra.One.Finance.Application.Endpoints.Conciliation.MatchManually;
using Ulbra.One.Finance.Application.Endpoints.Conciliation.UnmatchTransaction;
using Ulbra.One.Finance.Application.Endpoints.Conciliation.ListBankTransactions;
using Ulbra.One.Finance.Application.Endpoints.Conciliation.ListStatementImports;
using Ulbra.One.Finance.Application.Endpoints.BankAccounts.DeleteBankAccount;
using Ulbra.One.Finance.Application.Endpoints.BankAccounts.ListBankAccounts;
using Ulbra.One.Finance.Application.Endpoints.BankAccounts.UpdateBankAccount;
using Ulbra.One.Finance.Application.Endpoints.ChartAccounts.CreateChartAccount;
using Ulbra.One.Finance.Application.Endpoints.ChartAccounts.DeleteChartAccount;
using Ulbra.One.Finance.Application.Endpoints.ChartAccounts.ListChartAccounts;
using Ulbra.One.Finance.Application.Endpoints.ChartAccounts.UpdateChartAccount;
using Ulbra.One.Finance.Application.Endpoints.ClassificationPolicies.GetClassificationPolicy;
using Ulbra.One.Finance.Application.Endpoints.ClassificationPolicies.UpdateClassificationPolicy;
using Ulbra.One.Finance.Application.Endpoints.ExpenseCategories.CreateExpenseCategory;
using Ulbra.One.Finance.Application.Endpoints.ExpenseCategories.DeleteExpenseCategory;
using Ulbra.One.Finance.Application.Endpoints.ExpenseCategories.ListExpenseCategories;
using Ulbra.One.Finance.Application.Endpoints.ExpenseCategories.UpdateExpenseCategory;
using Ulbra.One.Finance.Application.Endpoints.Payables.ApprovePayable;
using Ulbra.One.Finance.Application.Endpoints.Payables.CancelPayable;
using Ulbra.One.Finance.Application.Endpoints.Payables.ClassificationSuggestion;
using Ulbra.One.Finance.Application.Endpoints.Payables.ClassifyPayable;
using Ulbra.One.Finance.Application.Endpoints.Payables.ClassifyPayableBatch;
using Ulbra.One.Finance.Application.Endpoints.Payables.CreatePayable;
using Ulbra.One.Finance.Application.Endpoints.Payables.DeleteAttachment;
using Ulbra.One.Finance.Application.Endpoints.Payables.DownloadAttachment;
using Ulbra.One.Finance.Application.Endpoints.Payables.DownloadPaymentProof;
using Ulbra.One.Finance.Application.Endpoints.Payables.GetPayable;
using Ulbra.One.Finance.Application.Endpoints.Payables.ListPayables;
using Ulbra.One.Finance.Application.Endpoints.Payables.RegisterPayment;
using Ulbra.One.Finance.Application.Endpoints.Payables.RejectPayable;
using Ulbra.One.Finance.Application.Endpoints.Payables.ReversePayment;
using Ulbra.One.Finance.Application.Endpoints.Payables.UpdatePayable;
using Ulbra.One.Finance.Application.Endpoints.Payables.UploadAttachment;
using Ulbra.One.Finance.Application.Endpoints.Payables.UploadPaymentProof;
using Ulbra.One.Finance.Application.Endpoints.Receivables.CancelReceivable;
using Ulbra.One.Finance.Application.Endpoints.Receivables.CreateReceivable;
using Ulbra.One.Finance.Application.Endpoints.Receivables.DownloadReceiptProof;
using Ulbra.One.Finance.Application.Endpoints.Receivables.GetReceivable;
using Ulbra.One.Finance.Application.Endpoints.Receivables.ListReceivables;
using Ulbra.One.Finance.Application.Endpoints.Receivables.RegisterReceipt;
using Ulbra.One.Finance.Application.Endpoints.Receivables.ReverseReceipt;
using Ulbra.One.Finance.Application.Endpoints.Receivables.UpdateReceivable;
using Ulbra.One.Finance.Application.Endpoints.Receivables.UploadReceiptProof;
using Ulbra.One.Finance.Application.Endpoints.Transactions.ApproveTransaction;
using Ulbra.One.Finance.Application.Endpoints.Transactions.CreateTransaction;
using Ulbra.One.Finance.Application.Endpoints.Transactions.GetTransaction;
using Ulbra.One.Finance.Application.Endpoints.Transactions.ListTransactions;
using Ulbra.One.Finance.Application.Endpoints.Transactions.PayTransaction;
using Ulbra.One.Finance.Application.Endpoints.Transactions.RejectTransaction;

namespace Ulbra.One.Finance.Application;

public static class FinanceEndpoints
{
    public static IEndpointRouteBuilder MapFinanceEndpoints(this IEndpointRouteBuilder builder)
    {
        var transactions = builder.MapGroup("/transactions");
        transactions.MapEndpoint<ListTransactions>();
        transactions.MapEndpoint<GetTransaction>();
        transactions.MapEndpoint<CreateTransaction>();
        transactions.MapEndpoint<ApproveTransaction>();
        transactions.MapEndpoint<RejectTransaction>();
        transactions.MapEndpoint<PayTransaction>();

        var categories = builder.MapGroup("/expense-categories");
        categories.MapEndpoint<ListExpenseCategories>();
        categories.MapEndpoint<CreateExpenseCategory>();
        categories.MapEndpoint<UpdateExpenseCategory>();
        categories.MapEndpoint<DeleteExpenseCategory>();

        var payables = builder.MapGroup("/payables");
        payables.MapEndpoint<ListPayables>();
        payables.MapEndpoint<GetPayable>();
        payables.MapEndpoint<CreatePayable>();
        payables.MapEndpoint<UpdatePayable>();
        payables.MapEndpoint<CancelPayable>();
        payables.MapEndpoint<ApprovePayable>();
        payables.MapEndpoint<RejectPayable>();
        payables.MapEndpoint<UploadAttachment>();
        payables.MapEndpoint<DownloadAttachment>();
        payables.MapEndpoint<DeleteAttachment>();
        payables.MapEndpoint<RegisterPayment>();
        payables.MapEndpoint<ReversePayment>();
        payables.MapEndpoint<UploadPaymentProof>();
        payables.MapEndpoint<DownloadPaymentProof>();
        payables.MapEndpoint<ClassifyPayable>();
        payables.MapEndpoint<ClassifyPayableBatch>();
        payables.MapEndpoint<ClassificationSuggestion>();

        var aging = builder.MapGroup("/payables/aging");
        aging.MapEndpoint<GetAgingReport>();
        aging.MapEndpoint<GetAgingItems>();

        var receivables = builder.MapGroup("/receivables");
        receivables.MapEndpoint<ListReceivables>();
        receivables.MapEndpoint<GetReceivable>();
        receivables.MapEndpoint<CreateReceivable>();
        receivables.MapEndpoint<UpdateReceivable>();
        receivables.MapEndpoint<CancelReceivable>();
        receivables.MapEndpoint<RegisterReceipt>();
        receivables.MapEndpoint<ReverseReceipt>();
        receivables.MapEndpoint<UploadReceiptProof>();
        receivables.MapEndpoint<DownloadReceiptProof>();

        var bankAccounts = builder.MapGroup("/bank-accounts");
        bankAccounts.MapEndpoint<ListBankAccounts>();
        bankAccounts.MapEndpoint<CreateBankAccount>();
        bankAccounts.MapEndpoint<UpdateBankAccount>();
        bankAccounts.MapEndpoint<DeleteBankAccount>();
        bankAccounts.MapEndpoint<SetReconciledBalance>();

        var cashFlow = builder.MapGroup("/cash-flow");
        cashFlow.MapEndpoint<GetCashFlow>();
        cashFlow.MapEndpoint<GetCashFlowItems>();

        var chartAccounts = builder.MapGroup("/chart-accounts");
        chartAccounts.MapEndpoint<ListChartAccounts>();
        chartAccounts.MapEndpoint<CreateChartAccount>();
        chartAccounts.MapEndpoint<UpdateChartAccount>();
        chartAccounts.MapEndpoint<DeleteChartAccount>();

        var statementImports = builder.MapGroup("/statement-imports");
        statementImports.MapEndpoint<ImportStatement>();
        statementImports.MapEndpoint<ListStatementImports>();

        var classificationPolicies = builder.MapGroup("/classification-policies");
        classificationPolicies.MapEndpoint<GetClassificationPolicy>();
        classificationPolicies.MapEndpoint<UpdateClassificationPolicy>();

        var bankTransactions = builder.MapGroup("/bank-transactions");
        bankTransactions.MapEndpoint<ListBankTransactions>();
        bankTransactions.MapEndpoint<ConfirmMatch>();
        bankTransactions.MapEndpoint<MatchManually>();
        bankTransactions.MapEndpoint<IgnoreTransaction>();
        bankTransactions.MapEndpoint<UnmatchTransaction>();

        return builder;
    }
}
