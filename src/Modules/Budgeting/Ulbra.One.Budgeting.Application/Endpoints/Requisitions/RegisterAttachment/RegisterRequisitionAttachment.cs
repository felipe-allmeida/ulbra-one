using MassTransit;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.EntityFrameworkCore;
using Swashbuckle.AspNetCore.Annotations;
using Ulbra.One.Budgeting.Application.Contracts;
using Ulbra.One.Budgeting.Application.FiscalDocuments;
using Ulbra.One.Budgeting.Application.Messaging;
using Ulbra.One.Budgeting.Domain;
using Ulbra.One.Budgeting.Domain.Entities;
using Ulbra.One.Common.Application;
using Ulbra.One.Common.Application.Authentication;
using Ulbra.One.Common.Application.Errors;
using Ulbra.One.Common.Application.Ids;
using Ulbra.One.Common.Application.OpenApi;
using Ulbra.One.Common.Application.Persistence;
using Ulbra.One.Common.Application.Storage;
using Ulbra.One.Common.Domain.Errors;

namespace Ulbra.One.Budgeting.Application.Endpoints.Requisitions.RegisterAttachment;

/// <summary>Registers a blob already in storage (uploaded through the boleto presigned URL) as a requisition
/// attachment, by key — no second upload (AEL-938). Mirrors the payable's UploadFiscalDocument: the reader PUTs
/// the file once for the AI parse, and this reuses that same blob instead of sending the bytes again.</summary>
public sealed class RegisterRequisitionAttachment : IEndpoint
{
    public void Map(IEndpointRouteBuilder builder) =>
        builder.MapPost("/{id}/attachments/from-key", Handle)
            .WithName("RegisterRequisitionAttachment").WithTags("Budgeting")
            .WithSummary("Registers an already-uploaded blob as a requisition attachment.")
            .WithDescription("Links a file already in object storage (uploaded via the boleto presigned URL) to a "
                + "requisition, referencing the storage Key — no second upload. The key must belong to the "
                + "requisition's company. Used by the boleto reader so the read document is kept as an attachment "
                + "without sending the bytes twice.")
            .RequirePermission(Permissions.RequisitionsWrite)
            .Produces<RequisitionAttachmentResponse>(StatusCodes.Status201Created)
            .ProducesProblem(StatusCodes.Status400BadRequest)
            .ProducesProblem(StatusCodes.Status404NotFound);

    private static async Task<IResult> Handle(
        RequisitionId id,
        RegisterRequisitionAttachmentRequest req,
        UserContext user,
        IUnitOfWork uow,
        IPublishEndpoint publish,
        TimeProvider clock,
        CancellationToken ct)
    {
        if (req.SizeBytes <= 0 || string.IsNullOrWhiteSpace(req.ContentType) || string.IsNullOrWhiteSpace(req.FileName))
            return ErrorResults.InvalidRequest("fileName, contentType and sizeBytes are required.", ErrorCodes.Attachment.InvalidMetadata);

        var attachmentKind = RequisitionAttachmentKind.Other;
        if (!string.IsNullOrWhiteSpace(req.Kind) && !Enum.TryParse(req.Kind, ignoreCase: true, out attachmentKind))
            return ErrorResults.InvalidRequest($"Unknown attachment kind '{req.Kind}'.", ErrorCodes.Attachment.InvalidKind);

        var requisition = await uow.ReadSet<Requisition>().FirstOrDefaultAsync(r => r.Id == id.Value, ct);
        // Same scope check + 404-hide as the multipart upload (AEL-883/764).
        if (requisition is null || !user.CanAccessScope(requisition.CompanyId, requisition.UnitId, requisition.CostCenterId))
            return ErrorResults.NotFound("Requisition not found.", ErrorCodes.Requisition.NotFound);

        // The key must live in this company's boleto namespace — blocks linking another company's/arbitrary blob.
        if (!ScopedBlobKey.BelongsTo(req.Key, RequisitionBoletoBlob.KeyRoot, requisition.CompanyId))
            return ErrorResults.NotFound("Requisition not found.", ErrorCodes.Requisition.NotFound);

        var uploadedById = Guid.Parse(user.UserId);
        var attachment = new RequisitionAttachment(
            requisition.Id, req.FileName, req.ContentType, req.SizeBytes, req.Key, uploadedById, clock.GetUtcNow(), attachmentKind);
        await uow.AddAsync(attachment, ct);
        // Propagate to the Payable when already post-approval (self-service loop) — rides the outbox, commits atomically.
        await requisition.PublishIfPostApproval(attachment, publish, clock.GetUtcNow(), ct);
        await uow.SaveChangesAsync(ct);

        return TypedResults.Created(
            $"/api/requisitions/{id}/attachments/{new RequisitionAttachmentId(attachment.Id)}",
            RequisitionAttachmentResponse.From(attachment));
    }
}

public sealed record RegisterRequisitionAttachmentRequest(
    [property: SwaggerSchema("Storage key of the blob already uploaded through the boleto presigned URL.")]
    string Key,
    [property: SwaggerSchema("Original file name, kept as the attachment's display name.")]
    [property: SwaggerExample("boleto-08-2026.pdf")]
    string FileName,
    [property: SwaggerSchema("MIME type of the uploaded file.")]
    [property: SwaggerExample("application/pdf")]
    string ContentType,
    [property: SwaggerSchema("Size of the uploaded file in bytes.")]
    [property: SwaggerExample("48213")]
    long SizeBytes,
    [property: SwaggerSchema("What the file is; defaults to Other. Parsed case-insensitively.")]
    [property: SwaggerExample("Other")]
    string? Kind);
