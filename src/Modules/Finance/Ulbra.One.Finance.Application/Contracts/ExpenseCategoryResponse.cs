using Ulbra.One.Common.Application.Ids;
using Ulbra.One.Finance.Domain.Entities;

namespace Ulbra.One.Finance.Application.Contracts;

public sealed record ExpenseCategoryResponse(ExpenseCategoryId Id, string Name, bool Active)
{
    public static ExpenseCategoryResponse From(ExpenseCategory c) =>
        new(new ExpenseCategoryId(c.Id), c.Name, c.Active);
}
