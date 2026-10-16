using Swashbuckle.AspNetCore.Annotations;
using Ulbra.One.Common.Application.OpenApi;

namespace Ulbra.One.Budgeting.Application.Contracts;

/// <summary>What the AI vision extractor read out of a boleto PDF/image, to pre-fill one installment. Nothing is
/// persisted — a preview; the length/convênio/DV flags are computed server-side from the digits. A null line
/// means the AI could not read it and the form opens for manual fill.</summary>
[SwaggerSchema("What the AI read from a boleto PDF/image, to pre-fill one installment. Nothing is persisted — a "
    + "preview. The length/convênio/DV flags are computed server-side from the digits. A null line means the AI "
    + "could not read it and the form opens for manual fill.")]
public sealed record ParsedBoleto(
    [property: SwaggerSchema("Digitable line, digits only (47 bancário / 48 convênio). Null when the AI read nothing.")]
    [property: SwaggerExample("34191790010104351004791020150008291070026000123")]
    string? DigitableLine,
    [property: SwaggerSchema("True when the line is arrecadação/convênio (starts with 8).")]
    bool IsConvenio,
    [property: SwaggerSchema("True when the line has 47 (bancário) or 48 (convênio) digits — the canonical validation rule.")]
    bool LengthValid,
    [property: SwaggerSchema("Advisory (non-blocking): the verifier digits (mod-10/11) match. False suggests an "
        + "OCR-misread digit — the UI asks for review.")]
    bool CheckDigitsOk,
    [property: SwaggerSchema("Due date read from the boleto.")]
    [property: SwaggerExample("2026-09-10")]
    DateOnly? DueDate,
    [property: SwaggerSchema("Issue/processing date read, when present on the boleto.")]
    DateOnly? IssueDate,
    [property: SwaggerSchema("Boleto amount, in BRL, when present.")]
    [property: SwaggerExample("1500.00")]
    decimal? Amount,
    [property: SwaggerSchema("Overall model-reported confidence, 0..1.")]
    double Confidence);
