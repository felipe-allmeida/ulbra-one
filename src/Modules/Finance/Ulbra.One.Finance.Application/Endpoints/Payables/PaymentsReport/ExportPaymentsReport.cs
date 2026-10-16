using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Swashbuckle.AspNetCore.Annotations;
using Ulbra.One.Common.Application.Errors;
using Ulbra.One.Common.Application;
using Ulbra.One.Common.Application.Authentication;
using Ulbra.One.Common.Application.Persistence;
using Ulbra.One.Common.Domain.Errors;
using Ulbra.One.Finance.Application.Reports;

namespace Ulbra.One.Finance.Application.Endpoints.Payables.PaymentsReport;

/// <summary>Export-only options layered on top of the shared report filters.</summary>
public sealed record ExportPaymentsReportOptions
{
    [SwaggerSchema("Include the Descrição/Observação block under each row in the detail section.")]
    public bool? ShowDescription { get; set; }
}

/// <summary>
/// Exports the Relação de Pagamentos as a PDF in the institutional layout (RESUMO + DETALHAMENTO), the
/// One equivalent of the legacy "Exportar PDF". Same filters as the report list; runs over the whole
/// filtered set (never paginated).
/// </summary>
public sealed class ExportPaymentsReport : IEndpoint
{
    public void Map(IEndpointRouteBuilder builder) =>
        builder.MapGet("/export", Handle).WithName("ExportPaymentsReport").WithTags("Finance")
            .WithSummary("Exports the payments report as a PDF.")
            .WithDescription("Renders the Relação de Pagamentos to a PDF with the RESUMO (by unit) and "
                + "DETALHAMENTO (Unidade → Fornecedor → parcelas) sections, matching the legacy standard. "
                + "Takes the same filters as the report list and covers the whole filtered set.")
            .RequirePermission(Permissions.PayablesRead)
            .ProducesProblem(StatusCodes.Status403Forbidden);

    private static async Task<IResult> Handle(
        IUnitOfWork uow,
        UserContext user,
        IUserDirectory users,
        TimeProvider timeProvider,
        IConfiguration config,
        [AsParameters] PaymentsReportRequest req,
        [AsParameters] ExportPaymentsReportOptions options,
        CancellationToken ct)
    {
        if (req.CompanyId is null && !user.CanSeeConsolidated)
            return ErrorResults.Forbidden("No access to the consolidated view.", ErrorCodes.Payable.ConsolidatedForbidden);

        var rows = await PaymentsReportQuery.Build(uow, req).ToListAsync(ct);
        var items = await PaymentsReportQuery.EnrichAsync(uow, users, rows, ct);

        var generatedBy = user.UserName is { Length: > 0 } name
            ? (user.UserEmail is { Length: > 0 } email ? $"{name} ({email})" : name)
            : user.UserEmail ?? "—";
        var now = NowInBrasilia(timeProvider);
        var header = new PaymentsReportPdfHeader(generatedBy, now);
        var bytes = PaymentsReportPdf.Build(items, header, options.ShowDescription == true, ResolveWebBaseUrl(config));
        var fileName = $"relacao_pagamentos_cadastrados_{now:yyyy-MM-dd}.pdf";
        return TypedResults.File(bytes, "application/pdf", fileName);
    }

    // The PDF "Ver" links back to the SPA, so it must point at the host the user actually visits — different
    // per environment. Reuse the CORS AllowedOrigins the deployment already injects (localhost in dev,
    // dev.one.ulbra.ai / one.ulbra.ai in the cloud) instead of a parallel config to keep in sync; prefer the
    // friendly custom domain over the Azure Container Apps FQDN, and fall back to the local SPA.
    private static string ResolveWebBaseUrl(IConfiguration config)
    {
        var origins = config.GetSection("AllowedOrigins").Get<string[]>() ?? [];
        return origins.FirstOrDefault(o => o.Contains("ulbra.ai", StringComparison.OrdinalIgnoreCase))
            ?? origins.FirstOrDefault(o => !string.IsNullOrWhiteSpace(o))
            ?? "http://localhost:5173";
    }

    // The runtime image is UTC; the header/filename must read in Brasília time like the legacy report.
    private static DateTimeOffset NowInBrasilia(TimeProvider timeProvider)
    {
        var utc = timeProvider.GetUtcNow();
        foreach (var id in new[] { "America/Sao_Paulo", "E. South America Standard Time" })
        {
            try { return TimeZoneInfo.ConvertTime(utc, TimeZoneInfo.FindSystemTimeZoneById(id)); }
            catch (TimeZoneNotFoundException) { }
            catch (InvalidTimeZoneException) { }
        }
        return utc;
    }
}
