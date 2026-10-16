using System.Globalization;

namespace Ulbra.One.Finance.Application.Remittances;

/// <summary>
/// Builds the user-facing pt-BR description for a <c>Remittance.NothingToRemit</c> failure. The
/// frontend shows this <c>detail</c> verbatim for this code, so the text is pt-BR (not a translatable
/// code) — it names <em>why</em> each installment was dropped so the operator can act without guessing.
/// </summary>
public static class NothingToRemitDescription
{
    public static string Build(IReadOnlyList<SkippedInstallment> skipped, DateOnly dueCutoff)
    {
        // No skips at all: nothing was even due — distinct from "everything was filtered out".
        if (skipped.Count == 0)
            return $"Nenhuma parcela vencia até {dueCutoff.ToString("dd/MM/yyyy", CultureInfo.InvariantCulture)}.";

        var groups = skipped
            .GroupBy(s => Translate(s.Reason))
            .Select(g => (Text: g.Key, Count: g.Count()))
            .OrderByDescending(g => g.Count)
            .ThenBy(g => g.Text, StringComparer.Ordinal)
            .ToList();

        var parts = string.Join(", ", groups.Select(g => $"{g.Count}× {g.Text}"));
        var noun = skipped.Count == 1 ? "parcela descartada" : "parcelas descartadas";
        return $"{skipped.Count} {noun}: {parts}.";
    }

    /// <summary>Maps the internal (English) skip reason to a pt-BR phrase. Static reasons match exactly;
    /// the dynamic <see cref="CnabFieldValidation"/> messages are bucketed by a stable substring.</summary>
    private static string Translate(string reason) => reason switch
    {
        "no payment method" => "sem forma de pagamento",
        "method not supported by CNAB" => "forma de pagamento não suportada no CNAB",
        "vendor not found" => "fornecedor não encontrado",
        "missing vendor bank account" => "dados bancários do fornecedor ausentes",
        "missing vendor PIX key" => "chave PIX do fornecedor ausente",
        "installment is not open" => "parcela não está em aberto",
        "installment is held" => "parcela em espera",
        "installment amount must be greater than zero" => "valor da parcela deve ser maior que zero",
        "missing boleto linha digitável" => "linha digitável do boleto ausente",
        PixPayloadEmvResolver.ReasonInvalid => "PIX copia e cola inválido",
        PixPayloadEmvResolver.ReasonCrc => "PIX copia e cola com CRC inválido",
        PixPayloadEmvResolver.ReasonUrlTooLong => "URL do PIX copia e cola excede o limite CNAB",
        PixPayloadEmvResolver.ReasonQrDisabled => "PIX copia e cola (QR dinâmico) ainda não habilitado",
        "installment not found among Scheduled payables of this company"
            => "parcela não encontrada entre os títulos agendados da empresa",
        _ => TranslateDynamic(reason),
    };

    private static string TranslateDynamic(string reason)
    {
        if (reason.Contains("document", StringComparison.OrdinalIgnoreCase))
            return "CPF/CNPJ do fornecedor inválido";
        if (reason.Contains("boleto", StringComparison.OrdinalIgnoreCase))
            return "linha digitável do boleto inválida";
        if (reason.Contains("PIX", StringComparison.OrdinalIgnoreCase))
            return "chave PIX do fornecedor fora do padrão CNAB";
        return "dados bancários do fornecedor fora do padrão CNAB";
    }
}
