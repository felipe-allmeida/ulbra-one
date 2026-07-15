using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Routing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Ulbra.One.Common.Application;
using Ulbra.One.Common.Application.Ids;
using Ulbra.One.Common.Application.Persistence;
using Ulbra.One.Common.Application.Validation;
using Ulbra.One.Common.Application.Authentication;
using Ulbra.One.Finance.Application.Contracts;
using Ulbra.One.Finance.Domain.Entities;

namespace Ulbra.One.Finance.Application.Endpoints.Payables.UploadAttachment;

public sealed class UploadAttachment : IEndpoint
{
    private const long MaxFileSizeBytes = 25L * 1024 * 1024;

    public void Map(IEndpointRouteBuilder builder) =>
        builder.MapPost("/{id}/attachments", Handle).WithName("UploadPayableAttachment").WithTags("Finance")
            .RequirePermission(Permissions.PayablesWrite).DisableAntiforgery()
            .Produces<PayableAttachmentResponse>(StatusCodes.Status201Created)
            .ProducesProblem(StatusCodes.Status400BadRequest)
            .ProducesProblem(StatusCodes.Status404NotFound);

    private static async Task<IResult> Handle(
        PayableId id,
        IFormFile file,
        UserContext user,
        IFileStorageService storage,
        IUnitOfWork uow,
        TimeProvider clock,
        CancellationToken ct)
    {
        var payable = await uow.Set<Payable>().FirstOrDefaultAsync(p => p.Id == id.Value, ct);
        if (payable is null)
            return TypedResults.Problem(detail: "Payable not found.", statusCode: 404, title: "Payable.NotFound");

        if (file.Length == 0)
            return TypedResults.Problem(detail: "File is empty.", statusCode: 400, title: "File.Empty");
        if (file.Length > MaxFileSizeBytes)
            return TypedResults.Problem(detail: $"File exceeds the {MaxFileSizeBytes / (1024 * 1024)}MB limit.", statusCode: 400, title: "File.TooLarge");

        await using var stream = file.OpenReadStream();
        var validation = FileContentValidator.Validate(file.FileName, file.ContentType, stream);
        if (validation.IsFailure)
            return TypedResults.Problem(detail: validation.Error.Description, statusCode: 400, title: "File.UnsupportedType");

        var uploadedById = Guid.Parse(user.UserId);
        var key = await storage.SaveAsync(file.FileName, stream, ct);

        var attachment = new PayableAttachment(payable.Id, file.FileName, file.ContentType, file.Length, key, uploadedById, clock.GetUtcNow());
        await uow.AddAsync(attachment, ct);
        await uow.SaveChangesAsync(ct);

        return TypedResults.Created(
            $"/api/payables/{id}/attachments/{new PayableAttachmentId(attachment.Id)}",
            PayableAttachmentResponse.From(attachment));
    }
}
