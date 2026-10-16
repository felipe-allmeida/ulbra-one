using Ulbra.One.Common.Domain;
using Ulbra.One.Finance.Domain;
using Ulbra.One.Finance.Domain.Cnab;

namespace Ulbra.One.Finance.Domain.Remittances;

/// <summary>
/// Resolves the <see cref="RemittanceCategory"/> of a payment from its method and (for boleto) the linha
/// digitável. Pure business rule, shared by the generation service (partition into one file per category,
/// AEL-933) and the file writer's lote grouping — mirrors <c>SantanderCnabProfile.CategoryFor</c>.
/// </summary>
public static class RemittanceCategoryResolver
{
    public static RemittanceCategory Resolve(PaymentMethod method, string? boletoDigitableLine) => method switch
    {
        PaymentMethod.Pix => RemittanceCategory.Pix,
        PaymentMethod.Ted => RemittanceCategory.Ted,
        PaymentMethod.Boleto => BoletoBarcode.IsConvenio(boletoDigitableLine)
            ? RemittanceCategory.Convenio
            : RemittanceCategory.BoletoBancario,
        _ => throw new ArgumentOutOfRangeException(nameof(method), method, "Unsupported CNAB payment method."),
    };
}
