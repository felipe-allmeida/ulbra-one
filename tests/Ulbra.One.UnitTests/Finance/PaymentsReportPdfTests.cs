using Ulbra.One.Common.Application.Ids;
using Ulbra.One.Finance.Application.Contracts;
using Ulbra.One.Finance.Application.Reports;
using Ulbra.One.Finance.Domain;
using Xunit;

namespace Ulbra.One.UnitTests.Finance;

public class PaymentsReportPdfTests
{
    private static PaymentReportItemResponse Row(
        string unit, string nature, string vendor, decimal amount, string statusLabel, PayableStatus status) =>
        new(PayableId.New(), InstallmentId.New(), CompanyId.New(),
            16916, "Nayara da Rocha Silveira", UnitId.New(), unit, CostCenterId.New(), nature,
            "Cristina Caberlon", DateTimeOffset.UtcNow, vendor, "09.721.504/0001-75",
            amount, new DateOnly(2026, 8, 22), 1, 1, status, InstallmentStatus.Open, statusLabel,
            "Pgto. Único", "Descrição de teste com acentuação (ç, ã, õ).", "Observação de teste.");

    [Fact]
    public void Build_produces_a_valid_pdf_with_the_embedded_font()
    {
        var rows = new[]
        {
            Row("Ulbra S.A", "SUPORTE", "CIGAME COMERCIO DE MATERIAL ELETRICO LTDA", 4732.82m, "Fila Pagamento", PayableStatus.Scheduled),
            Row("Canoas/RS", "ENSINO", "CASTELLI COMERCIO DE PRODUTOS LTDA", 1201.00m, "Em Triagem", PayableStatus.PendingClassification),
        };
        var header = new PaymentsReportPdfHeader("Administrador (dev@localhost)", DateTimeOffset.UtcNow);

        var bytes = PaymentsReportPdf.Build(rows, header, showDescription: true, webBaseUrl: "http://localhost:5173");

        // Non-trivial output starting with the "%PDF" magic — proves the font resolved and MigraDoc rendered.
        Assert.True(bytes.Length > 1000);
        Assert.Equal(new byte[] { 0x25, 0x50, 0x44, 0x46 }, bytes[..4]);
    }
}
