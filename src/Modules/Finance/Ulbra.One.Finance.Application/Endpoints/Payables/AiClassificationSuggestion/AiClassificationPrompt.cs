using System.Globalization;
using System.Text;
using Ulbra.One.Common.Application.Ai;

namespace Ulbra.One.Finance.Application.Endpoints.Payables.AiClassificationSuggestion;

public static class AiClassificationPrompt
{
    private const string System =
        "You are an accounting assistant for a Brazilian ERP. Given a payable and the company's chart of " +
        "accounts, choose the single best accounting account, selecting ONLY from the provided list by its " +
        "exact Code. If you are unsure, set the code to null and lower your confidence (0..1). Respond only " +
        "with the requested structure.";

    public static AiPrompt Build(
        string description,
        string counterpartyName,
        string? documentNumber,
        decimal totalAmount,
        IReadOnlyList<(string Code, string Name)> accountingAccounts,
        IReadOnlyList<string>? cfops = null,
        IReadOnlyList<string>? ncms = null)
    {
        var sb = new StringBuilder();
        sb.AppendLine($"Payable description: {description}");
        sb.AppendLine($"Counterparty: {counterpartyName}");
        if (!string.IsNullOrWhiteSpace(documentNumber))
            sb.AppendLine($"Document number: {documentNumber}");
        sb.AppendLine($"Total amount: {totalAmount.ToString("0.00", CultureInfo.InvariantCulture)}");
        if (cfops is { Count: > 0 } || ncms is { Count: > 0 })
            sb.AppendLine($"Fiscal codes — CFOP: {string.Join(", ", cfops ?? [])}; NCM: {string.Join(", ", ncms ?? [])}");
        sb.AppendLine();
        sb.AppendLine("Accounting accounts (Code — Name):");
        foreach (var (code, name) in accountingAccounts)
            sb.AppendLine($"- {code} — {name}");

        return new AiPrompt(System, sb.ToString());
    }
}
