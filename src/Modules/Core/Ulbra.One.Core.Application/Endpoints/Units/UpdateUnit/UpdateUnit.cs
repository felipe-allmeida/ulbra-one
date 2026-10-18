using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Swashbuckle.AspNetCore.Annotations;
using Ulbra.One.Common.Application.Errors;
using Ulbra.One.Common.Application.Validation;
using Ulbra.One.Common.Application;
using Ulbra.One.Common.Application.Authentication;
using Ulbra.One.Common.Application.Ids;
using Ulbra.One.Common.Application.OpenApi;
using Ulbra.One.Common.Application.Persistence;
using Ulbra.One.Common.Application.Normalization;
using Ulbra.One.Common.Domain.Errors;
using Ulbra.One.Common.Domain.Validation;
using Ulbra.One.Core.Application.Contracts;
using Ulbra.One.Core.Domain.Entities;

namespace Ulbra.One.Core.Application.Endpoints.Units.UpdateUnit;

public sealed class UpdateUnit : IEndpoint
{
    public sealed record Request
    {
        [SwaggerSchema("Unit name.")]
        [SwaggerExample("Campus Canoas")]
        [NormalizeText] public string Name { get; set; } = string.Empty;

        [SwaggerSchema("Short form used wherever the full name does not fit.")]
        [SwaggerExample("CANOAS")]
        [NormalizeText] public string Acronym { get; set; } = string.Empty;

        /// <summary>Optional legacy ERP code (stable natural key from the legacy system).</summary>
        [SwaggerSchema("Code carried over from the legacy ERP. Optional, and the stable natural key that "
            + "lets an import match this unit to its legacy counterpart.")]
        [SwaggerExample("0142")]
        public string? Code { get; set; }

        /// <summary>Optional establishment CNPJ (accounting tracking). Validated (mod-11) and stored unformatted when present.</summary>
        [SwaggerSchema("CNPJ of the establishment, for accounting tracking. Optional; when supplied it is "
            + "check-digit validated and stored unformatted.")]
        [SwaggerExample("88.332.580/0002-70")]
        [NormalizeCnpj] public string? Cnpj { get; set; }

        [SwaggerSchema("Whether this unit is the company's headquarters (matriz).")]
        public bool IsMatriz { get; set; }
    }

    public void Map(IEndpointRouteBuilder builder)
    {
        builder.MapPut("/{id}", Handle)
            .WithName("UpdateUnit")
            .WithTags("Core")
            .WithSummary("Updates a unit.")
            .WithDescription("Replaces the unit's editable fields. A full replace, not a patch — omitting "
                + "Code or Cnpj clears them. The owning company is immutable.")
            .RequirePermission(Permissions.CoreWrite)
            .WithRequestNormalizer<Request>()
            .WithValidation<Request>()
            .Produces<UnitResponse>(StatusCodes.Status200OK)
            .Produces(StatusCodes.Status404NotFound)
            .ProducesProblem(StatusCodes.Status400BadRequest);
    }

    private static async Task<IResult> Handle(
        UnitId id,
        Request req,
        IUnitOfWork uow,
        CancellationToken ct)
    {
        var unit = await uow.Set<Unit>().FirstOrDefaultAsync(u => u.Id == id.Value, ct);
        if (unit is null) return TypedResults.NotFound();

        var cnpj = string.IsNullOrWhiteSpace(req.Cnpj) ? null : req.Cnpj;

        unit.Update(req.Name, req.Acronym, cnpj, req.IsMatriz, req.Code);
        await uow.SaveChangesAsync(ct);
        return TypedResults.Ok(UnitResponse.From(unit));
    }
}
