using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Routing;
using Ulbra.One.Common.Infrastructure.Extensions;
using Ulbra.One.Finance.Application.Endpoints.BankAccounts.CreateBankAccount;
using Ulbra.One.Finance.Application.Endpoints.Conciliation.ImportStatement;
using Ulbra.One.Finance.Application.Endpoints.Conciliation.ListBankTransactions;
using Ulbra.One.Finance.Application.Endpoints.Conciliation.ListStatementImports;
using Ulbra.One.Finance.Application.Endpoints.BankAccounts.DeleteBankAccount;
using Ulbra.One.Finance.Application.Endpoints.BankAccounts.ListBankAccounts;
using Ulbra.One.Finance.Application.Endpoints.BankAccounts.UpdateBankAccount;
using Ulbra.One.Finance.Application.Endpoints.ExpenseCategories.CreateExpenseCategory;
using Ulbra.One.Finance.Application.Endpoints.ExpenseCategories.DeleteExpenseCategory;
using Ulbra.One.Finance.Application.Endpoints.ExpenseCategories.ListExpenseCategories;
using Ulbra.One.Finance.Application.Endpoints.ExpenseCategories.UpdateExpenseCategory;
using Ulbra.One.Finance.Application.Endpoints.Payables.ApprovePayable;
using Ulbra.One.Finance.Application.Endpoints.Payables.CancelPayable;
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

        var bankAccounts = builder.MapGroup("/bank-accounts");
        bankAccounts.MapEndpoint<ListBankAccounts>();
        bankAccounts.MapEndpoint<CreateBankAccount>();
        bankAccounts.MapEndpoint<UpdateBankAccount>();
        bankAccounts.MapEndpoint<DeleteBankAccount>();

        var statementImports = builder.MapGroup("/statement-imports");
        statementImports.MapEndpoint<ImportStatement>();
        statementImports.MapEndpoint<ListStatementImports>();

        var bankTransactions = builder.MapGroup("/bank-transactions");
        bankTransactions.MapEndpoint<ListBankTransactions>();

        return builder;
    }
}
