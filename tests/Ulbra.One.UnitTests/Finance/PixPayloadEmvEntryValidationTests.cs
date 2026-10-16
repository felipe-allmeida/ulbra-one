using Ulbra.One.Finance.Application.Endpoints.Payables;
using Xunit;

namespace Ulbra.One.UnitTests.Finance;

/// <summary>Payable-entry validation of the PIX copia-e-cola (AEL-963): mutual exclusion with the key override
/// (parity with the requisition path) + structure/CRC on a provided payload.</summary>
public class PixPayloadEmvEntryValidationTests
{
    private const string Valid =
        "00020101021226770014BR.GOV.BCB.PIX2555api.itau/pix/qr/v2/2b4a01c0-cb2a-4d71-8338-d3a4d8f5c8af5204000053039865802BR5915TUNA PAGAMENTOS6013FLORIANOPOLIS62070503***63049F31";

    [Fact]
    public void Both_key_override_and_copia_e_cola_is_rejected()
        => Assert.NotNull(PixPayloadEmvEntryValidation.Problem(new (string?, string?)[] { ("pagamentos@fornecedor.com.br", Valid) }));

    [Fact]
    public void Only_a_valid_copia_e_cola_passes()
        => Assert.Null(PixPayloadEmvEntryValidation.Problem(new (string?, string?)[] { (null, Valid) }));

    [Fact]
    public void Only_a_plain_key_passes()
        => Assert.Null(PixPayloadEmvEntryValidation.Problem(new (string?, string?)[] { ("pagamentos@fornecedor.com.br", null) }));

    [Fact]
    public void A_malformed_copia_e_cola_is_rejected()
        => Assert.NotNull(PixPayloadEmvEntryValidation.Problem(new (string?, string?)[] { (null, "000201thisisnotavalidbrcodexxx") }));
}
