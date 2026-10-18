using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Routing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Ulbra.One.Common.Application.Errors;
using Ulbra.One.Common.Application;
using Ulbra.One.Common.Application.Ids;
using Ulbra.One.Common.Application.Persistence;
using Ulbra.One.Common.Application.Validation;
using Ulbra.One.Common.Application.Authentication;
using Ulbra.One.Common.Domain.Errors;
using Ulbra.One.Finance.Application.Contracts;
using Ulbra.One.Finance.Domain;
using Ulbra.One.Finance.Domain.Entities;
using Swashbuckle.AspNetCore.Annotations;
using Ulbra.One.Common.Application.OpenApi;

namespace Ulbra.One.Finance.Application.Endpoints.Payables.UploadAttachment;

public sealed class UploadAttachment : IEndpoint
{
    private const long MaxFileSizeBytes = 25L * 1024 * 1024;

    public void Map(IEndpointRouteBuilder builder) =>
        builder.MapPost("/{id}/attachments", Handle).WithName("UploadPayableAttachment").WithTags("Finance")
            .WithSummary("Uploads an attachment to a payable.")
            .WithDescription("Accepts multipart/form-data with a single file of up to 25 MB - contracts, quotes, "
                + "correspondence. An attachment whose kind is FiscalDocument counts as a Nota Fiscal and clears "
                + "the 'NF pendente' gate; for a parseable NF-e/NFS-e XML use the fiscal-document routes instead: "
                + "only those drive the fiscal fields.")
            .RequirePermission(Permissions.PayablesWrite).DisableAntiforgery()
            .Produces<PayableAttachmentResponse>(StatusCodes.Status201Created)
            .ProducesProblem(StatusCodes.Status400BadRequest)
            .ProducesProblem(StatusCodes.Status404NotFound);

    private static async Task<IResult> Handle(
        PayableId id,
        IFormFile file,
        [SwaggerSchema("What the file is. Omitted defaults to Other; FiscalDocument marks it as a Nota Fiscal, "
            + "which clears the payable's 'NF pendente' gate. Parsed case-insensitively; an unknown value returns 400.")]
        [SwaggerExample("FiscalDocument")]
        string? kind,
        UserContext user,
        IFileStorageService storage,
        IUnitOfWork uow,
        TimeProvider clock,
        CancellationToken ct)
    {
        // FiscalDocument is the only kind that clears the NF gate at tax analysis / accounting.
        var attachmentKind = PayableAttachmentKind.Other;
        if (!string.IsNullOrWhiteSpace(kind) && !Enum.TryParse(kind, ignoreCase: true, out attachmentKind))
            return ErrorResults.InvalidRequest($"Unknown attachment kind '{kind}'.", ErrorCodes.Attachment.InvalidKind);

        var payable = await uow.Set<Payable>().FirstOrDefaultAsync(p => p.Id == id.Value, ct);
        if (payable is null || !user.CanAccess(payable.CompanyId))
            return ErrorResults.NotFound("Payable not found.", ErrorCodes.Payable.NotFound);

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

        var attachment = new PayableAttachment(payable.Id, file.FileName, file.ContentType, file.Length, key, uploadedById, clock.GetUtcNow(), attachmentKind);
        await uow.AddAsync(attachment, ct);
        await uow.SaveChangesAsync(ct);

        return TypedResults.Created(
            $"/api/payables/{id}/attachments/{new PayableAttachmentId(attachment.Id)}",
            PayableAttachmentResponse.From(attachment));
    }
}
