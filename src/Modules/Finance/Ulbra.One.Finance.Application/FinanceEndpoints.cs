using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Routing;
using Ulbra.One.Common.Infrastructure.Extensions;
using Ulbra.One.Finance.Application.Endpoints.BankAccounts.CreateBankAccount;
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

        return builder;
    }
}
