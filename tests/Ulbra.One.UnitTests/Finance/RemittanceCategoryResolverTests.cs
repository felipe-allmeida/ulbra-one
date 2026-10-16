using Ulbra.One.Common.Domain;
using Ulbra.One.Finance.Domain;
using Ulbra.One.Finance.Domain.Remittances;
using Xunit;

namespace Ulbra.One.UnitTests.Finance;

public class RemittanceCategoryResolverTests
{
    [Fact]
    public void Pix_maps_to_Pix()
        => Assert.Equal(RemittanceCategory.Pix, RemittanceCategoryResolver.Resolve(PaymentMethod.Pix, null));

    [Fact]
    public void Ted_maps_to_Ted()
        => Assert.Equal(RemittanceCategory.Ted, RemittanceCategoryResolver.Resolve(PaymentMethod.Ted, null));

    [Fact]
    public void Boleto_with_a_bancario_line_maps_to_BoletoBancario()
        => Assert.Equal(RemittanceCategory.BoletoBancario,
            RemittanceCategoryResolver.Resolve(PaymentMethod.Boleto, "34191790010104351004791020150008291070026000123"));

    [Fact]
    public void Boleto_with_a_convenio_line_maps_to_Convenio()
        => Assert.Equal(RemittanceCategory.Convenio,
            RemittanceCategoryResolver.Resolve(PaymentMethod.Boleto, "846700000017599200241114202410000000000000000000"));
}
