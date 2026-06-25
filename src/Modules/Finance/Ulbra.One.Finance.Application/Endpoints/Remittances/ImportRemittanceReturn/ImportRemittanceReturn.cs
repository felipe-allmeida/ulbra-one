using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;
using Ulbra.One.Common.Application;
using Ulbra.One.Common.Application.Authentication;
using Ulbra.One.Common.Application.Ids;
using Ulbra.One.Common.Domain;
using Ulbra.One.Finance.Application.Contracts;
using Ulbra.One.Finance.Application.Remittances;

namespace Ulbra.One.Finance.Application.Endpoints.Remittances.ImportRemittanceReturn;

/// <summary>
/// <c>POST /remittances/{id}/return</c> — uploads and processes a CNAB240 return (.RET) file.
/// Reads the file bytes from a multipart request, delegates parsing + conciliation to
/// <see cref="IRemittanceReturnService.ProcessReturnAsync"/> and returns the aggregate outcome.
/// The <c>{id}</c> route segment scopes the URL; the service matches lines by Reference globally.
/// </summary>
public sealed class ImportRemittanceReturn : IEndpoint
{
    public void Map(IEndpointRouteBuilder builder) =>
        builder.MapPost("/{id}/return", Handle)
            .WithName("ImportRemittanceReturn").WithTags("Finance").RequireAuthorization()
            .DisableAntiforgery()
            .Produces<RemittanceReturnOutcome>(StatusCodes.Status200OK)
            .ProducesProblem(StatusCodes.Status400BadRequest)
            .ProducesProblem(StatusCodes.Status404NotFound);

    private static async Task<IResult> Handle(
        RemittanceId id,
        IFormFile file,
        UserContext user,
        IRemittanceReturnService service,
        CancellationToken ct)
    {
        if (file is null || file.Length == 0)
            return TypedResults.Problem(detail: "File is empty.", statusCode: 400, title: "File.Empty");

        await using var stream = file.OpenReadStream();
        var bytes = new byte[file.Length];
        _ = await stream.ReadAsync(bytes, ct);

        var processedById = Guid.TryParse(user.UserId, out var uid) ? uid : SystemActor.Id;
        var processedBy = user.UserName ?? user.UserEmail ?? user.UserId;

        var result = await service.ProcessReturnAsync(bytes, file.FileName, processedBy, processedById, ct);

        if (result.IsFailure)
            return TypedResults.Problem(detail: result.Error.Description, statusCode: 400, title: result.Error.Code);

        return TypedResults.Ok(result.Value);
    }
}
