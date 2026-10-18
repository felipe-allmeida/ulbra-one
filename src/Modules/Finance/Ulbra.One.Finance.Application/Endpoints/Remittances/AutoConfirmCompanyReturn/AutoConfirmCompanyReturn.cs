using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Swashbuckle.AspNetCore.Annotations;
using Ulbra.One.Common.Application.Errors;
using Ulbra.One.Common.Application;
using Ulbra.One.Common.Application.Authentication;
using Ulbra.One.Common.Application.Ids;
using Ulbra.One.Common.Application.Persistence;
using Ulbra.One.Common.Domain.Errors;
using Ulbra.One.Common.Domain;
using Ulbra.One.Finance.Application.Contracts;
using Ulbra.One.Finance.Application.Remittances;
using Ulbra.One.Finance.Domain.Entities;

namespace Ulbra.One.Finance.Application.Endpoints.Remittances.AutoConfirmCompanyReturn;

/// <summary>
/// <c>POST /remittances/return/auto-confirm/{payingBankAccountId}</c> — uploads a CNAB240 return
/// (.RET/.txt/.csv) and <b>auto-confirms the "Paid" titles</b> (baixa aplicada), returning the refreshed
/// preview so the operator handles only the remaining buckets (divergent/rejected/unmatched) manually.
/// Write permission. When the trailer diverges nothing is applied — it degrades to a plain preview.
/// </summary>
public sealed class AutoConfirmCompanyReturn : IEndpoint
{
    public void Map(IEndpointRouteBuilder builder) =>
        builder.MapPost("/return/auto-confirm/{payingBankAccountId}", Handle)
            .WithName("AutoConfirmCompanyReturn").WithTags("Finance")
            .WithSummary("Applies a bank return, confirming the paid titles.")
            .WithDescription("Same upload as the preview, but it APPLIES the settlement for every line the bank reported "
                + "as paid, and returns the refreshed preview so the operator only handles what is left - "
                + "divergent, rejected or unmatched. One safety rule: if the file trailer does not check out, "
                + "nothing is applied and the call degrades to a plain preview rather than half-applying a "
                + "suspect file. Either way the uploaded file is kept and shows up in the return history.").RequirePermission(Permissions.TreasuryWrite)
            .DisableAntiforgery()
            .Produces<RemittanceReturnPreviewResponse>(StatusCodes.Status200OK)
            .ProducesProblem(StatusCodes.Status400BadRequest)
            .ProducesProblem(StatusCodes.Status404NotFound);

    private static async Task<IResult> Handle(
        [SwaggerSchema("Paying account whose pending titles the return refers to.")]
        BankAccountId payingBankAccountId,
        [SwaggerSchema("The CNAB return file sent by the bank (.RET, .txt or .csv).")]
        IFormFile file,
        UserContext user,
        IUnitOfWork uow,
        IRemittanceReturnService service,
        CancellationToken ct)
    {
        if (file is null || file.Length == 0)
            return ErrorResults.InvalidRequest("File is empty.", ErrorCodes.File.Empty);

        // Company scope: the account must exist and be accessible to the caller (AEL-764 pattern).
        var bankAccount = await uow.ReadSet<BankAccount>()
            .FirstOrDefaultAsync(b => b.Id == payingBankAccountId.Value, ct);
        if (bankAccount is null || !user.CanAccess(bankAccount.CompanyId))
            return ErrorResults.NotFound("Bank account not found.", ErrorCodes.BankAccount.NotFound);

        var processedById = Guid.TryParse(user.UserId, out var uid) ? uid : SystemActor.Id;
        var processedBy = user.UserName ?? user.UserEmail ?? user.UserId;

        await using var stream = file.OpenReadStream();
        using var ms = new MemoryStream((int)file.Length);
        await stream.CopyToAsync(ms, ct);

        var result = await service.AutoConfirmPaidAndPreviewAsync(
            bankAccount.CompanyId, payingBankAccountId.Value, ms.ToArray(), file.FileName,
            processedBy, processedById, ct);

        return result.IsFailure
            ? ErrorResults.InvalidRequest(result.Error.Description, result.Error.Code)
            : TypedResults.Ok(result.Value);
    }
}
