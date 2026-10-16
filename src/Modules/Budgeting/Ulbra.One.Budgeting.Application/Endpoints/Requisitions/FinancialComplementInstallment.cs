using Swashbuckle.AspNetCore.Annotations;
using Ulbra.One.Common.Application.OpenApi;
using Ulbra.One.Common.Domain;

namespace Ulbra.One.Budgeting.Application.Endpoints.Requisitions;

/// <summary>One authored installment: a due date, the value to transfer, the payment method and — when the method
/// is Boleto — the boleto linha digitável. Shared by the financial-complement submit and the front-load
/// plan on create/update. Named uniquely (not <c>InstallmentInput</c>) to avoid an OpenAPI schemaId clash with
/// Finance's CreatePayable.InstallmentInput; one CLR type → one OpenAPI schema across all three endpoints.</summary>
public sealed record FinancialComplementInstallment(
    [property: SwaggerSchema("When this installment is due.")]
    [property: SwaggerExample("2026-08-10")]
    DateOnly DueDate,
    [property: SwaggerSchema("Value to transfer in this installment, in BRL. The installments' sum "
        + "becomes the requisition's programmed total.")]
    [property: SwaggerExample("1250.00")]
    decimal Amount,
    [property: SwaggerSchema("How this installment is paid.")]
    PaymentMethod PaymentMethod,
    [property: SwaggerSchema("Boleto linha digitável. Only meaningful when PaymentMethod is Boleto.")]
    [property: SwaggerExample("34191790010104351004791020150008291070026000")]
    string? BoletoDigitableLine = null,
    [property: SwaggerSchema("PIX key override for THIS payment (AEL-938). Only meaningful when PaymentMethod is "
        + "Pix. When set, the CNAB remittance uses it instead of the vendor's registered PIX key; blank uses the "
        + "vendor's key. The vendor's registered key is never changed.")]
    [property: SwaggerExample("pagamentos@fornecedor.com.br")]
    string? PixKeyOverride = null,
    [property: SwaggerSchema("PIX \"copia e cola\" (EMV/BR Code) for THIS payment (AEL-963). Only meaningful when "
        + "PaymentMethod is Pix, and mutually exclusive with PixKeyOverride. A static QR decodes to a key; a "
        + "dynamic QR is sent as its payload-location URL on the CNAB forma 47 remittance.")]
    [property: SwaggerExample("00020101021226850014br.gov.bcb.pix2563pix.santander.com.br/qr/v2/...6304ABCD")]
    string? PixPayloadEmv = null);
