using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;
using Swashbuckle.AspNetCore.Annotations;
using Ulbra.One.Common.Application.Errors;
using Ulbra.One.Common.Application;
using Ulbra.One.Common.Application.Authentication;
using Ulbra.One.Common.Application.Ids;
using Ulbra.One.Common.Domain.Errors;
using Ulbra.One.Common.Domain;
using Ulbra.One.Finance.Application.Contracts;
using Ulbra.One.Finance.Application.Remittances;

namespace Ulbra.One.Finance.Application.Endpoints.Remittances.ImportRemittanceReturn;

/// <summary>
/// <c>POST /remittances/{id}/return</c> — uploads and processes a CNAB240 return (.RET) file.
/// Reads the file bytes from a multipart request, delegates parsing + conciliation to
/// <see cref="IRemittanceReturnService.ProcessReturnAsync"/> and returns the aggregate outcome.
/// The <c>{id}</c> route segment scopes the line-matching to this remittance only (multitenant safety).
/// </summary>
public sealed class ImportRemittanceReturn : IEndpoint
{
    public void Map(IEndpointRouteBuilder builder) =>
        builder.MapPost("/{id}/return", Handle)
            .WithName("ImportRemittanceReturn").WithTags("Finance")
            .WithSummary("Imports the bank return for one remittance.")
            .WithDescription("Uploads the CNAB return (.RET) the bank sends back and applies it: paid lines register their "
                + "payments and rejected ones release the installment back to the queue. The {id} in the path "
                + "scopes the matching to that remittance alone, which is what keeps a return file from "
                + "touching another company\u0027s lines.").RequirePermission(Permissions.TreasuryWrite)
            .DisableAntiforgery()
            .Produces<RemittanceReturnOutcome>(StatusCodes.Status200OK)
            .ProducesProblem(StatusCodes.Status400BadRequest)
            .ProducesProblem(StatusCodes.Status404NotFound);

    private static async Task<IResult> Handle(
        RemittanceId id,
        [SwaggerSchema("The CNAB return file sent by the bank (.RET).")]
        IFormFile file,
        UserContext user,
        IRemittanceReturnService service,
        CancellationToken ct)
    {
        if (file is null || file.Length == 0)
            return ErrorResults.InvalidRequest("File is empty.", ErrorCodes.File.Empty);

        await using var stream = file.OpenReadStream();
        using var ms = new MemoryStream((int)file.Length);
        await stream.CopyToAsync(ms, ct);
        var bytes = ms.ToArray();

        var processedById = Guid.TryParse(user.UserId, out var uid) ? uid : SystemActor.Id;
        var processedBy = user.UserName ?? user.UserEmail ?? user.UserId;

        var result = await service.ProcessReturnAsync(id.Value, bytes, file.FileName, processedBy, processedById, ct);

        if (result.IsFailure)
            return ErrorResults.InvalidRequest(result.Error.Description, result.Error.Code);

        return TypedResults.Ok(result.Value);
    }
}
