using Ulbra.One.Common.Application.Ids;
using Ulbra.One.Finance.Domain;
using Ulbra.One.Finance.Domain.Entities;

namespace Ulbra.One.Finance.Application.Contracts;

public sealed record ChartAccountResponse(
    ChartAccountId Id, ChartType ChartType, string Code, string Name,
    ChartAccountId? ParentId, AccountKind Kind, AccountNature? Nature, bool IsActive)
{
    public static ChartAccountResponse From(ChartAccount a) => new(
        new ChartAccountId(a.Id), a.ChartType, a.Code, a.Name,
        a.ParentId is { } p ? new ChartAccountId(p) : null, a.Kind, a.Nature, a.IsActive);
}
