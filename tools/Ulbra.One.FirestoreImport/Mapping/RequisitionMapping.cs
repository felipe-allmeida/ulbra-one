using System.Globalization;
using Ulbra.One.FirestoreImport.Source;

namespace Ulbra.One.FirestoreImport.Mapping;

/// <summary>Maps a legacy `requisicoes` document onto the One's Requisition so BudgetControlService derives
/// the same per-competency consumption AND the installment plan (parcelas) is reproduced faithfully.
/// Status/type/frequency are enum-name strings. Recurrence: legacy <c>estimateType='integral'</c> = total
/// (One <c>Full</c>, on the start month); <c>'mensal'</c> = per-installment (One <c>Monthly</c>);
/// <c>paymentSystem='Recorrente'</c> or a year ≥ 2100 end = open-ended. The installment schedule is
/// <c>paymentInstallmentsCount</c> parcelas of the per-installment value, stepped from
/// <c>paymentFirstDueDate</c> by <c>paymentFrequency</c> (Mensal=+1 month, Quinzenal=+15 days) — so
/// Quinzenal correctly folds two parcelas into the same competency (the old month-span heuristic halved
/// those totals).</summary>
public static class RequisitionMapping
{
    private const int OpenEndedYear = 2100;

    // Which One statuses each import "stage" flag brings. Cancelled (and unknown-but-mapped-to-Triage
    // extras) fall through: Cancelled is in no set (never imported); the pre-approval queue is Approval.
    public static readonly IReadOnlySet<string> StagePaid =
        new HashSet<string>(StringComparer.Ordinal) { "Paid", "Finalized" };
    public static readonly IReadOnlySet<string> StageInPayment =
        new HashSet<string>(StringComparer.Ordinal) { "InPayment", "AwaitingFiscalDocument" };
    public static readonly IReadOnlySet<string> StageApproval =
        new HashSet<string>(StringComparer.Ordinal) { "Triage", "Approval", "Complement" };
    // Cancelled requisitions ARE imported (history + so their payments can attach to them). They contribute
    // nothing to budget consumption and are excluded from the Conta Fixa uniqueness index by design.
    public static readonly IReadOnlySet<string> StageCancelled =
        new HashSet<string>(StringComparer.Ordinal) { "Cancelled" };

    public static string MapStatus(string? legacy) => (legacy ?? string.Empty).Trim() switch
    {
        "Finalizada" => "Finalized",
        "PAGO" => "Paid",
        "Em Pagamento" => "InPayment",
        "AGUARDANDO DOCUMENTO FISCAL" => "AwaitingFiscalDocument",
        "Cancelado" => "Cancelled",
        "Triagem" => "Triage",
        "Aprovação" => "Approval",
        "Complementação" => "Complement",
        _ => "Triage", // unknown → pre-approval queue (safe: contributes only as forecast, not consumido)
    };

    public static string MapType(string? legacy) => (legacy ?? string.Empty).Trim() switch
    {
        "Conta Fixa" => "FixedExpense",
        "Compra" => "Purchase",
        "Prestação de Serviço" => "ServiceProvision",
        _ => "Purchase",
    };

    public static string MapFrequency(string? legacy) => (legacy ?? string.Empty).Trim() switch
    {
        "Quinzenal" => "Biweekly",
        "Semestral" => "Semiannual",
        "Anual" => "Annual",
        _ => "Monthly", // Mensal / empty / null
    };

    /// <summary>Paid/Finalized carry realized value — they need synthetic payments to count (else ZERO).</summary>
    public static bool IsRealized(string oneStatus) => StagePaid.Contains(oneStatus);

    /// <summary>Resolves the estimate fields AND the installment schedule for one legacy requisition. The
    /// installments' amounts always sum to <see cref="RequisitionPlan.EstimateValue"/> (One's domain
    /// invariant), and the realized payment rows are derived from them (competency = due-date month).</summary>
    public static RequisitionPlan ResolvePlan(RequisitionDoc d)
    {
        var type = MapType(d.Type);
        var perValue = d.EstimateValue ?? 0m;
        var start = (d.StartCompetency ?? string.Empty).Trim();
        var legacyEnd = CompetencyRange.IsValid(d.EndCompetency) ? d.EndCompetency!.Trim() : null;
        var mensal = string.Equals(d.EstimateType, "mensal", StringComparison.OrdinalIgnoreCase);
        var open = string.Equals(d.PaymentSystem, "Recorrente", StringComparison.OrdinalIgnoreCase)
                   || (legacyEnd is not null && int.Parse(legacyEnd[..4]) >= OpenEndedYear);
        var biweekly = string.Equals((d.PaymentFrequency ?? string.Empty).Trim(), "Quinzenal",
            StringComparison.OrdinalIgnoreCase);
        var first = ParseFirstDue(d.FirstDueDate, start);

        // Recurring (Conta Fixa) or open-ended monthly: a single per-month TEMPLATE installment (One forbids
        // >1 installment on a recurring requisition). EstimateValue stays per-month; the window stays open.
        if (type == "FixedExpense" || (mensal && open))
            return Single(type, "Monthly", perValue, start, null, d, first);

        // integral / Único: the whole value is the total, one installment on the first due date (One Full
        // ignores the end competency; realized lands on the start competency, the budget cell).
        if (!mensal)
            return Single(type, "Full", perValue, start, null, d, first);

        // mensal + closed (Parcelado): N parcelas of the per-installment value; total = perValue × N (NOT ×
        // month-span). Competency steps by frequency anchored at the start competency — Mensal advances a
        // month each parcela, Quinzenal bills two per calendar month (floor(i/2)) — matching the legacy
        // window, so the estimate splits back over exactly the months the parcelas land in.
        var n = Math.Max((int)(d.PaymentInstallmentsCount ?? 0), 1);
        var installments = new List<Installment>(n);
        var payments = new List<(string, decimal)>(n);
        for (var i = 0; i < n; i++)
        {
            var due = biweekly ? first.AddDays(15 * i) : first.AddMonths(i);
            installments.Add(new(i + 1, due, perValue));
            payments.Add((CompetencyRange.Add(start, biweekly ? i / 2 : i), perValue));
        }
        var end = CompetencyRange.Add(start, biweekly ? (n - 1) / 2 : n - 1);
        return new(type, "Monthly", perValue * n, start, end,
            HeadFrequency(d), HeadCount(d), installments, payments);
    }

    // A single-installment plan (Único, or the recurring/open template): the realized payment lands on the
    // start competency (the budget cell), and the one installment carries the whole value on the first due.
    private static RequisitionPlan Single(
        string type, string estimateType, decimal value, string start, string? end, RequisitionDoc d, DateOnly first)
        => new(type, estimateType, value, start, end, HeadFrequency(d), HeadCount(d),
            [new(1, first, value)], [(start, value)]);

    // Head columns mirror the source: null when the source has no count/frequency (a single Único payment).
    private static string? HeadFrequency(RequisitionDoc d)
        => string.IsNullOrWhiteSpace(d.PaymentFrequency) ? null : MapFrequency(d.PaymentFrequency);

    private static int? HeadCount(RequisitionDoc d)
        => d.PaymentInstallmentsCount is > 0 and var c ? (int)c : null;

    /// <summary>The first due date, falling back to the first day of the start competency when unparseable.</summary>
    public static DateOnly ParseFirstDue(string? raw, string startCompetency)
        => DateOnly.TryParse(raw, CultureInfo.InvariantCulture, DateTimeStyles.None, out var d)
            ? d
            : DateOnly.ParseExact($"{startCompetency}-01", "yyyy-MM-dd", CultureInfo.InvariantCulture);
}

/// <summary>One parcela of the payment plan (One's <c>requisition_installment</c>).</summary>
public sealed record Installment(int Sequence, DateOnly DueDate, decimal Amount);

/// <summary>The One-side requisition fields plus the installment plan. The installment amounts sum to
/// <see cref="EstimateValue"/> (domain invariant), and <see cref="Payments"/> (the realized rows, written
/// only for Paid/Finalized) are the same schedule bucketed by competency — Quinzenal folds two parcelas
/// into one competency.</summary>
public sealed record RequisitionPlan(
    string Type, string EstimateType, decimal EstimateValue,
    string StartCompetency, string? EndCompetency,
    string? PaymentFrequency, int? PaymentInstallmentsCount,
    IReadOnlyList<Installment> Installments,
    IReadOnlyList<(string Competency, decimal Amount)> Payments);
