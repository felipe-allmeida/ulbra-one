using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Swashbuckle.AspNetCore.Annotations;
using Ulbra.One.Budgeting.Application.Contracts;
using Ulbra.One.Common.Application;
using Ulbra.One.Common.Application.Authentication;
using Ulbra.One.Common.Application.Ids;
using Ulbra.One.Common.Application.OpenApi;
using Ulbra.One.Common.Application.Results;
using Ulbra.One.Common.Domain.Errors;
using Ulbra.One.Common.Domain;
using Ulbra.One.Common.Domain.Validation;

namespace Ulbra.One.Budgeting.Application.Endpoints.Requisitions.DecodePixPayloadEmv;

/// <summary>Stateless preview: decodes a PIX "copia e cola" (EMV/BR Code) payload — validity, CRC, static vs
/// dynamic, and the recebedor/valor/URL — so the requisition/payable form can preview and pre-validate the
/// paste (AEL-963). Persists nothing and mutates no requisition.</summary>
public sealed class DecodePixPayloadEmv : IEndpoint
{
    public void Map(IEndpointRouteBuilder builder) =>
        builder.MapPost("/pix/decode-copy-paste", Handle)
            .WithName("DecodePixPayloadEmv").WithTags("Budgeting")
            .WithSummary("Decodes a PIX copia e cola without saving it.")
            .WithDescription("Parses an EMV/BR Code payload (structure + CRC16) and returns whether it is valid, "
                + "static or dynamic, plus the recebedor/valor/URL, to preview and pre-validate the paste before "
                + "submitting an installment. Persists nothing.")
            .RequirePermission(Permissions.RequisitionsWrite)
            .Produces<DecodedPixPayloadEmv>(StatusCodes.Status200OK)
            .ProducesProblem(StatusCodes.Status403Forbidden);

    private static IResult Handle(
        [SwaggerSchema("Company the requisition belongs to — the caller must have access to it.")]
        CompanyId companyId,
        DecodePixPayloadEmvRequest req,
        UserContext user)
    {
        if (!user.CanAccess(companyId.Value))
            return Result.Failure<DecodedPixPayloadEmv>(
                Error.Forbidden(ErrorCodes.Requisition.Forbidden, "No access to this company.")).ToOk();

        var r = PixPayloadEmvParser.Parse(req.Payload);
        var decoded = new DecodedPixPayloadEmv(
            Valid: r.IsValid,
            CrcOk: r.CrcValid,
            IsStatic: r.Initiation == PixPayloadEmvInitiation.Static,
            IsDynamic: r.Initiation == PixPayloadEmvInitiation.Dynamic,
            MerchantName: r.ReceiverName,
            MerchantCity: r.ReceiverCity,
            Amount: r.Amount,
            PixKey: r.PixKey,
            Url: r.PayloadLocationUrl,
            Txid: r.Txid);

        return Result.Success(decoded).ToOk();
    }
}

public sealed record DecodePixPayloadEmvRequest(
    [property: SwaggerSchema("The PIX copia e cola payload (EMV/BR Code) pasted by the operator.")]
    [property: SwaggerExample("00020101021226850014br.gov.bcb.pix2563pix.santander.com.br/qr/v2/...6304ABCD")]
    string Payload);
