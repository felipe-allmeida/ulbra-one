using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Ulbra.One.Common.Application;
using Ulbra.One.Common.Application.Authentication;
using Ulbra.One.Common.Application.Ids;
using Ulbra.One.Common.Application.Persistence;
using Ulbra.One.Finance.Domain.Entities;

namespace Ulbra.One.Finance.Application.Endpoints.Remittances.DownloadRemittanceFile;

public sealed class DownloadRemittanceFile : IEndpoint
{
    public void Map(IEndpointRouteBuilder builder) =>
        builder.MapGet("/{id}/file", Handle)
            .WithName("DownloadRemittanceFile").WithTags("Finance")
            .WithSummary("Downloads the remittance file.")
            .WithDescription("Streams back the generated CNAB file, which is what gets uploaded to the bank.").RequirePermission(Permissions.TreasuryRead);

    private static async Task<IResult> Handle(
        RemittanceId id,
        UserContext user,
        IUnitOfWork uow,
        IFileStorageService storage,
        CancellationToken ct)
    {
        var remittance = await uow.ReadSet<Remittance>()
            .FirstOrDefaultAsync(r => r.Id == id.Value, ct);
        // Scope by company (Remittance has no unit); denied returns the same 404. See AEL-764.
        if (remittance is null || !user.CanAccess(remittance.CompanyId) || remittance.FileStorageKey is null)
            return TypedResults.NotFound();

        var stream = await storage.GetAsync(remittance.FileStorageKey, ct);
        if (stream is null)
            return TypedResults.NotFound();

        return TypedResults.File(stream, "text/plain", remittance.FileName ?? "SANTANDER_REMESSA.txt");
    }
}
