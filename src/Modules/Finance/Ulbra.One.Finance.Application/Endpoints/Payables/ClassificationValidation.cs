using Microsoft.EntityFrameworkCore;
using Ulbra.One.Common.Application.Persistence;
using Ulbra.One.Finance.Domain;
using Ulbra.One.Finance.Domain.Entities;

namespace Ulbra.One.Finance.Application.Endpoints.Payables;

/// <summary>Shared validation for chart-account ids used to classify a payable. Returns a Problem-Details
/// code (suitable for the <c>title</c>) for the FIRST invalid id, or null when all supplied ids are valid.</summary>
internal static class ClassificationValidation
{
    public static async Task<string?> ValidateAsync(IUnitOfWork uow, Guid? accId, Guid? finId, CancellationToken ct)
    {
        if (accId is { } a)
        {
            var error = await ValidateOneAsync(uow, a, ChartType.Accounting, ct);
            if (error is not null) return error;
        }
        if (finId is { } f)
        {
            var error = await ValidateOneAsync(uow, f, ChartType.Financial, ct);
            if (error is not null) return error;
        }
        return null;
    }

    private static async Task<string?> ValidateOneAsync(IUnitOfWork uow, Guid id, ChartType expectedType, CancellationToken ct)
    {
        var account = await uow.ReadSet<ChartAccount>().FirstOrDefaultAsync(c => c.Id == id, ct);
        if (account is null) return "ChartAccount.NotFound";
        if (!account.CanReceivePayable) return "ChartAccount.NotAnalytical";
        if (account.ChartType != expectedType) return "ChartAccount.WrongType";
        return null;
    }
}
