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
        IReadOnlyList<string>? ncms = null,
        IReadOnlyList<(string Code, string Name)>? vendorAssociatedAccounts = null,
        string? lc116Subitem = null)
    {
        var sb = new StringBuilder();
        sb.AppendLine($"Payable description: {description}");
        sb.AppendLine($"Counterparty: {counterpartyName}");
        if (!string.IsNullOrWhiteSpace(documentNumber))
            sb.AppendLine($"Document number: {documentNumber}");
        sb.AppendLine($"Total amount: {totalAmount.ToString("0.00", CultureInfo.InvariantCulture)}");
        if (cfops is { Count: > 0 } || ncms is { Count: > 0 } || !string.IsNullOrWhiteSpace(lc116Subitem))
        {
            var codes = new List<string>();
            if (cfops is { Count: > 0 }) codes.Add($"CFOP: {string.Join(", ", cfops)}");
            if (ncms is { Count: > 0 }) codes.Add($"NCM: {string.Join(", ", ncms)}");
            if (!string.IsNullOrWhiteSpace(lc116Subitem)) codes.Add($"LC116: {lc116Subitem}");
            sb.AppendLine($"Fiscal codes — {string.Join("; ", codes)}");
        }
        sb.AppendLine();
        sb.AppendLine("Accounting accounts (Code — Name):");
        foreach (var (code, name) in accountingAccounts)
            sb.AppendLine($"- {code} — {name}");

        // Accounts this vendor is usually booked to (from the vendor registry) — a strong prior. Prefer one of
        // these when it fits the payable, but they are not exhaustive: pick a better-matching account if the
        // description/fiscal codes clearly indicate one.
        if (vendorAssociatedAccounts is { Count: > 0 })
        {
            sb.AppendLine();
            sb.AppendLine("Ledger accounts associated with this vendor — prefer one of these when compatible with the payable:");
            foreach (var (code, name) in vendorAssociatedAccounts)
                sb.AppendLine($"- {code} — {name}");
        }

        return new AiPrompt(System, sb.ToString());
    }
}
