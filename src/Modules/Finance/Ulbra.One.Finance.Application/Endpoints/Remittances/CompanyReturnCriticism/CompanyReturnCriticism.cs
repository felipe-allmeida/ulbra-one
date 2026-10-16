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
using Ulbra.One.Finance.Application.Contracts;
using Ulbra.One.Finance.Application.Remittances;
using Ulbra.One.Finance.Domain.Entities;

namespace Ulbra.One.Finance.Application.Endpoints.Remittances.CompanyReturnCriticism;

/// <summary>
/// <c>POST /remittances/return/criticism/{payingBankAccountId}</c> — reads a Santander CRÍTICA file
/// (accept/reject). Unlike the movimento conciliation, a crítica is not a payment: this endpoint only
/// reports the bank's verdict per record and applies NO baixa. Read-only — nothing is persisted.
/// </summary>
public sealed class CompanyReturnCriticism : IEndpoint
{
    public void Map(IEndpointRouteBuilder builder) =>
        builder.MapPost("/return/criticism/{payingBankAccountId}", Handle)
            .WithName("CompanyReturnCriticism").WithTags("Finance")
            .WithSummary("Reads a bank criticism (crítica) file without applying anything.")
            .WithDescription("Uploads a CNAB240 CRÍTICA file (the bank's validation feedback) and reports which "
                + "titles were accepted (acatados) or rejected, WITHOUT any settlement. The crítica never pays — "
                + "it only says whether the remessa was accepted. Read permission only; nothing is persisted.")
            .RequirePermission(Permissions.TreasuryRead)
            .DisableAntiforgery()
            .Produces<RemittanceCriticismResponse>(StatusCodes.Status200OK)
            .ProducesProblem(StatusCodes.Status400BadRequest)
            .ProducesProblem(StatusCodes.Status404NotFound);

    private static async Task<IResult> Handle(
        [SwaggerSchema("Paying account whose remessa the criticism refers to.")]
        BankAccountId payingBankAccountId,
        [SwaggerSchema("The CNAB criticism file sent by the bank (.RET or .txt).")]
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

        await using var stream = file.OpenReadStream();
        using var ms = new MemoryStream((int)file.Length);
        await stream.CopyToAsync(ms, ct);

        var result = await service.PreviewCriticismAsync(payingBankAccountId.Value, ms.ToArray(), ct);

        return result.IsFailure
            ? ErrorResults.InvalidRequest(result.Error.Description, result.Error.Code)
            : TypedResults.Ok(result.Value);
    }
}
