using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Routing;
using Ulbra.One.Common.Infrastructure.Extensions;
using Ulbra.One.Finance.Application.Endpoints.ExpenseCategories.CreateExpenseCategory;
using Ulbra.One.Finance.Application.Endpoints.ExpenseCategories.DeleteExpenseCategory;
using Ulbra.One.Finance.Application.Endpoints.ExpenseCategories.ListExpenseCategories;
using Ulbra.One.Finance.Application.Endpoints.ExpenseCategories.UpdateExpenseCategory;
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

        return builder;
    }
}
