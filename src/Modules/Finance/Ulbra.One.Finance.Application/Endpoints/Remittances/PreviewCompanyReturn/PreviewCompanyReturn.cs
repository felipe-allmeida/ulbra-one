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

namespace Ulbra.One.Finance.Application.Endpoints.Remittances.PreviewCompanyReturn;

/// <summary>
/// <c>POST /remittances/return/preview/{payingBankAccountId}</c> — dry-run. Uploads a CNAB240 return
/// (.RET/.txt/.csv) and previews which of the paying account's pending titles the bank paid/rejected,
/// WITHOUT applying any baixa. The operator then confirms line by line. Read permission only — nothing
/// is persisted.
/// </summary>
public sealed class PreviewCompanyReturn : IEndpoint
{
    public void Map(IEndpointRouteBuilder builder) =>
        builder.MapPost("/return/preview/{payingBankAccountId}", Handle)
            .WithName("PreviewCompanyReturn").WithTags("Finance")
            .WithSummary("Previews a bank return without applying it.")
            .WithDescription("Dry run over a whole paying account: uploads a CNAB return and shows which pending titles "
                + "the bank paid, rejected, diverged on or could not be matched - WITHOUT applying any "
                + "settlement. Nothing is persisted, which is why read permission is enough. The operator then "
                + "confirms line by line, or uses auto-confirm.").RequirePermission(Permissions.TreasuryRead)
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

        await using var stream = file.OpenReadStream();
        using var ms = new MemoryStream((int)file.Length);
        await stream.CopyToAsync(ms, ct);

        var result = await service.PreviewCompanyReturnAsync(payingBankAccountId.Value, ms.ToArray(), ct);

        return result.IsFailure
            ? ErrorResults.InvalidRequest(result.Error.Description, result.Error.Code)
            : TypedResults.Ok(result.Value);
    }
}
