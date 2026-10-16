using MassTransit;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Swashbuckle.AspNetCore.Annotations;
using Ulbra.One.Budgeting.Application.Contracts;
using Ulbra.One.Budgeting.Application.Messaging;
using Ulbra.One.Budgeting.Domain;
using Ulbra.One.Budgeting.Domain.Entities;
using Ulbra.One.Common.Application.Errors;
using Ulbra.One.Common.Application;
using Ulbra.One.Common.Application.Authentication;
using Ulbra.One.Common.Application.Ids;
using Ulbra.One.Common.Application.OpenApi;
using Ulbra.One.Common.Application.Persistence;
using Ulbra.One.Common.Application.Validation;
using Ulbra.One.Common.Domain.Errors;

namespace Ulbra.One.Budgeting.Application.Endpoints.Requisitions.UploadAttachment;

public sealed class UploadRequisitionAttachment : IEndpoint
{
    private const long MaxFileSizeBytes = 25L * 1024 * 1024;

    public void Map(IEndpointRouteBuilder builder) =>
        builder.MapPost("/{id}/attachments", Handle).WithName("UploadRequisitionAttachment").WithTags("Budgeting")
            .WithSummary("Uploads an attachment to a requisition.")
            .WithDescription("Accepts `multipart/form-data` with a single file of up to 25 MB — quotes, "
                + "fiscal documents and any supporting paperwork. A file whose kind is a fiscal document (or "
                + "an .xml) is what clears the requisition's pending-fiscal-document flag.")
            .RequirePermission(Permissions.RequisitionsWrite).DisableAntiforgery()
            .Produces<RequisitionAttachmentResponse>(StatusCodes.Status201Created)
            .ProducesProblem(StatusCodes.Status400BadRequest)
            .ProducesProblem(StatusCodes.Status404NotFound);

    private static async Task<IResult> Handle(
        RequisitionId id,
        IFormFile file,
        [SwaggerSchema("What the file is. Omitted defaults to Other; FiscalDocument marks it as a Nota "
            + "Fiscal, which is what clears the requisition's pending-fiscal-document flag. Parsed "
            + "case-insensitively; an unknown value returns 400.")]
        [SwaggerExample("FiscalDocument")]
        string? kind,
        UserContext user,
        IFileStorageService storage,
        IUnitOfWork uow,
        IPublishEndpoint publish,
        TimeProvider clock,
        CancellationToken ct)
    {
        // Optional ?kind= marker (default Other). "FiscalDocument" flags the file as a Nota Fiscal so Finance
        // promotes it to the Payable's fiscal document on approval.
        var attachmentKind = RequisitionAttachmentKind.Other;
        if (!string.IsNullOrWhiteSpace(kind) && !Enum.TryParse(kind, ignoreCase: true, out attachmentKind))
            return ErrorResults.InvalidRequest($"Unknown attachment kind '{kind}'.", ErrorCodes.Attachment.InvalidKind);

        var requisition = await uow.ReadSet<Requisition>().FirstOrDefaultAsync(r => r.Id == id.Value, ct);
        // Scoped requester (AEL-883): the full Empresa × Unidade × Centro de Custo intersection (a FiscalDocument
        // here rides onto the Payable), not just the company (AEL-764). Denied returns the same 404-hide.
        if (requisition is null || !user.CanAccessScope(requisition.CompanyId, requisition.UnitId, requisition.CostCenterId))
            return ErrorResults.NotFound("Requisition not found.", ErrorCodes.Requisition.NotFound);

        if (file.Length == 0)
            return ErrorResults.InvalidRequest("File is empty.", ErrorCodes.File.Empty);
        if (file.Length > MaxFileSizeBytes)
            return ErrorResults.InvalidRequest($"File exceeds the {MaxFileSizeBytes / (1024 * 1024)}MB limit.", ErrorCodes.File.TooLarge);

        await using var stream = file.OpenReadStream();
        var validation = FileContentValidator.Validate(file.FileName, file.ContentType, stream);
        if (validation.IsFailure)
            return ErrorResults.InvalidRequest(validation.Error.Description, ErrorCodes.File.UnsupportedType);

        var uploadedById = Guid.Parse(user.UserId);
        var key = await storage.SaveAsync(file.FileName, stream, ct);

        var attachment = new RequisitionAttachment(requisition.Id, file.FileName, file.ContentType, file.Length, key, uploadedById, clock.GetUtcNow(), attachmentKind);
        await uow.AddAsync(attachment, ct);
        // When the requisition already generated a Payable, propagate the file to it (self-service NF loop) —
        // rides the EF bus outbox, so the message commits atomically with the attachment row.
        await requisition.PublishIfPostApproval(attachment, publish, clock.GetUtcNow(), ct);
        await uow.SaveChangesAsync(ct);

        return TypedResults.Created(
            $"/api/requisitions/{id}/attachments/{new RequisitionAttachmentId(attachment.Id)}",
            RequisitionAttachmentResponse.From(attachment));
    }
}
