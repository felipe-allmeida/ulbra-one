using Swashbuckle.AspNetCore.Annotations;
using Ulbra.One.Common.Application.Ids;
using Ulbra.One.Common.Application.OpenApi;
using Ulbra.One.Finance.Domain.Entities;

namespace Ulbra.One.Finance.Application.Contracts;

[SwaggerSchema("An expense category — a management-level grouping of payables, independent of the "
    + "accounting chart.")]
public sealed record ExpenseCategoryResponse(
    ExpenseCategoryId Id,
    [property: SwaggerSchema("Category name.")]
    [property: SwaggerExample("Manutenção e reparos")]
    string Name,
    [property: SwaggerSchema("Whether the category may be chosen. An inactive one is kept on the payables "
        + "that already use it.")]
    bool Active)
{
    public static ExpenseCategoryResponse From(ExpenseCategory c) =>
        new(new ExpenseCategoryId(c.Id), c.Name, c.Active);
}
