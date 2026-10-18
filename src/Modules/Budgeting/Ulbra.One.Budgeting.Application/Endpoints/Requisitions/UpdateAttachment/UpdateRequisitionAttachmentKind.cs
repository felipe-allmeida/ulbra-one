using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.EntityFrameworkCore;
using Swashbuckle.AspNetCore.Annotations;
using Ulbra.One.Budgeting.Application.Contracts;
using Ulbra.One.Budgeting.Domain;
using Ulbra.One.Budgeting.Domain.Entities;
using Ulbra.One.Common.Application.Errors;
using Ulbra.One.Common.Application;
using Ulbra.One.Common.Application.Authentication;
using Ulbra.One.Common.Application.Ids;
using Ulbra.One.Common.Application.OpenApi;
using Ulbra.One.Common.Application.Persistence;
using Ulbra.One.Common.Domain.Errors;

namespace Ulbra.One.Budgeting.Application.Endpoints.Requisitions.UpdateAttachment;

/// <summary>The type a requester/controllership assigns to an existing attachment when reclassifying it.</summary>
[SwaggerSchema("New kind for the attachment (Nota Fiscal / Boleto / Fatura / Outro).")]
public sealed record UpdateAttachmentKindRequest(
    [property: SwaggerSchema("What the file is. Parsed case-insensitively; an unknown value returns 400.")]
    [property: SwaggerExample("BankSlip")]
    string Kind);

/// <summary>Reclassifies an existing requisition attachment's kind — the Solicitante or the Controladoria fixing a
/// mis-typed file (e.g. a boleto stored as Outro) while editing/triaging the requisition (AEL-959). Metadata-only:
/// it changes the requisition attachment's Kind, which the copier reads on approval, so fixing it before approval
/// propagates to the Payable. It does not retro-fix an already-materialized Payable's copy.</summary>
public sealed class UpdateRequisitionAttachmentKind : IEndpoint
{
    public void Map(IEndpointRouteBuilder builder) =>
        builder.MapPatch("/{id}/attachments/{attachmentId}", Handle)
            .WithName("UpdateRequisitionAttachmentKind").WithTags("Budgeting")
            .WithSummary("Reclassifies a requisition attachment's kind.")
            .WithDescription("Changes the type of one attachment (Nota Fiscal / Boleto / Fatura / Outro). Available "
                + "to the requester (requisitions:write) and the controllership (requisitions:triage). Returns 400 "
                + "for an unknown kind and 404 when the requisition or the attachment does not exist.")
            .RequireAnyPermission(Permissions.RequisitionsWrite, Permissions.RequisitionsTriage)
            .Produces<RequisitionAttachmentResponse>(StatusCodes.Status200OK)
            .ProducesProblem(StatusCodes.Status400BadRequest)
            .ProducesProblem(StatusCodes.Status404NotFound);

    private static async Task<IResult> Handle(
        RequisitionId id,
        RequisitionAttachmentId attachmentId,
        UpdateAttachmentKindRequest body,
        UserContext user,
        IUnitOfWork uow,
        CancellationToken ct)
    {
        if (!Enum.TryParse<RequisitionAttachmentKind>(body.Kind, ignoreCase: true, out var kind))
            return ErrorResults.InvalidRequest($"Unknown attachment kind '{body.Kind}'.", ErrorCodes.Attachment.InvalidKind);

        var a = await uow.Set<RequisitionAttachment>()
            .FirstOrDefaultAsync(x => x.Id == attachmentId.Value && x.RequisitionId == id.Value, ct);
        if (a is null) return ErrorResults.NotFound("Attachment not found.", ErrorCodes.Requisition.AttachmentNotFound);

        // Scope by the parent requisition's full Empresa × Unidade × Centro de Custo (AEL-883); denied → 404-hide.
        var dims = await uow.ReadSet<Requisition>()
            .Where(r => r.Id == id.Value)
            .Select(r => new { r.CompanyId, r.UnitId, r.CostCenterId }).FirstOrDefaultAsync(ct);
        if (dims is null || !user.CanAccessScope(dims.CompanyId, dims.UnitId, dims.CostCenterId))
            return ErrorResults.NotFound("Attachment not found.", ErrorCodes.Requisition.AttachmentNotFound);

        a.SetKind(kind);
        await uow.SaveChangesAsync(ct);
        return TypedResults.Ok(RequisitionAttachmentResponse.From(a));
    }
}
