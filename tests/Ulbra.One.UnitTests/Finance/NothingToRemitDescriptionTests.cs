using Ulbra.One.Finance.Application.Remittances;
using Xunit;

namespace Ulbra.One.UnitTests.Finance;

public class NothingToRemitDescriptionTests
{
    private static SkippedInstallment Skip(string reason) =>
        new(Guid.NewGuid(), Guid.NewGuid(), reason);

    [Fact]
    public void Empty_skipped_says_nothing_was_due_up_to_the_cutoff()
    {
        var text = NothingToRemitDescription.Build(Array.Empty<SkippedInstallment>(), new DateOnly(2026, 9, 15));
        Assert.Equal("Nenhuma parcela vencia até 15/09/2026.", text);
    }

    [Fact]
    public void Single_skip_uses_singular_and_names_the_reason()
    {
        var text = NothingToRemitDescription.Build(new[] { Skip("no payment method") }, new DateOnly(2026, 9, 15));
        Assert.Equal("1 parcela descartada: 1× sem forma de pagamento.", text);
    }

    [Fact]
    public void Mixed_reasons_are_grouped_and_counted_most_frequent_first()
    {
        var skipped = new List<SkippedInstallment>();
        for (var i = 0; i < 8; i++) skipped.Add(Skip("no payment method"));
        for (var i = 0; i < 3; i++) skipped.Add(Skip("missing vendor PIX key"));
        skipped.Add(Skip("installment is held"));

        var text = NothingToRemitDescription.Build(skipped, new DateOnly(2026, 9, 15));

        Assert.Equal(
            "12 parcelas descartadas: 8× sem forma de pagamento, 3× chave PIX do fornecedor ausente, 1× parcela em espera.",
            text);
    }

    [Fact]
    public void Invalid_document_message_is_named_in_portuguese()
    {
        var text = NothingToRemitDescription.Build(
            new[] { Skip("vendor document must be a valid CPF (11) or CNPJ (14)") }, new DateOnly(2026, 9, 15));
        Assert.Equal("1 parcela descartada: 1× CPF/CNPJ do fornecedor inválido.", text);
    }

    // A 47-digit convênio line (lost a digit) is rejected by BoletoReason and skipped; its reason must render as
    // a boleto problem (NothingToRemitDescription buckets by the "boleto" substring, not the vendor-bank default).
    [Fact]
    public void Malformed_convenio_boleto_skip_renders_as_a_boleto_problem_in_ptBR()
    {
        var reason = CnabFieldValidation.BoletoReason("82650000007889180798000002026177663112901001043");
        Assert.NotNull(reason);

        var text = NothingToRemitDescription.Build(new[] { Skip(reason!) }, new DateOnly(2026, 8, 25));

        Assert.Contains("linha digitável do boleto", text);
        Assert.DoesNotContain("dados bancários", text);
    }
}
