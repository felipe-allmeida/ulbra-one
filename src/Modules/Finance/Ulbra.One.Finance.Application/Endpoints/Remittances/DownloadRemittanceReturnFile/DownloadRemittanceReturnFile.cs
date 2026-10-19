using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Swashbuckle.AspNetCore.Annotations;
using Ulbra.One.Common.Application;
using Ulbra.One.Common.Application.Authentication;
using Ulbra.One.Common.Application.Ids;
using Ulbra.One.Common.Application.Persistence;
using Ulbra.One.Finance.Domain.Entities;

namespace Ulbra.One.Finance.Application.Endpoints.Remittances.DownloadRemittanceReturnFile;

/// <summary>
/// <c>GET /remittances/return/history/{returnId}/file</c> — streams back the .RET exactly as the bank sent
/// it and the operator uploaded it. Counterpart of <c>DownloadRemittanceFile</c>, which serves the outbound
/// remittance.
/// </summary>
public sealed class DownloadRemittanceReturnFile : IEndpoint
{
    public void Map(IEndpointRouteBuilder builder) =>
        builder.MapGet("/return/history/{returnId}/file", Handle)
            .WithName("DownloadRemittanceReturnFile").WithTags("Finance")
            .WithSummary("Downloads a stored bank return file.")
            .WithDescription("Streams back the CNAB return the bank sent, byte for byte as it was uploaded. It is the "
                + "bank's own document, so it is what settles an argument about a baixa - or about a file "
                + "the system refused.").RequirePermission(Permissions.TreasuryRead);

    private static async Task<IResult> Handle(
        [SwaggerSchema("Processed return whose stored file to download.")]
        RemittanceReturnId returnId,
        UserContext user,
        IUnitOfWork uow,
        IFileStorageService storage,
        CancellationToken ct)
    {
        var record = await uow.ReadSet<RemittanceReturn>()
            .FirstOrDefaultAsync(r => r.Id == returnId.Value, ct);
        // Scope by company; denied returns the same 404 as missing. See AEL-764.
        if (record is null || !user.CanAccess(record.CompanyId))
            return TypedResults.NotFound();

        var stream = await storage.GetAsync(record.FileStorageKey, ct);
        if (stream is null)
            return TypedResults.NotFound();

        return TypedResults.File(stream, "text/plain", record.FileName);
    }
}
