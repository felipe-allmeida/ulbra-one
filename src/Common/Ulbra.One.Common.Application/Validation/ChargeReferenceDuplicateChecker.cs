using Microsoft.AspNetCore.Http;
using Ulbra.One.Common.Application.Errors;
using Ulbra.One.Common.Domain.Validation;

namespace Ulbra.One.Common.Application.Validation;

/// <summary>The kinds of charge reference the system can queue for payment. Both identify ONE issued charge;
/// neither is an address. A PIX <b>static</b> BR Code is deliberately absent — it wraps the vendor's key and is
/// reusable, so it is filtered out before it ever reaches a source.</summary>
public enum ChargeReferenceKind
{
    /// <summary>Boleto/arrecadação linha digitável (47/48 digits), compared on digits only.</summary>
    BoletoLine,

    /// <summary>PIX Copia e Cola (EMV/BR Code), dynamic only — it carries its own payload URL / txid per
    /// cobrança (AEL-963).</summary>
    PixPayloadEmv,
}

/// <summary>Where one module already uses a charge reference, and under what name the user knows it.
/// <paramref name="Value"/> is the canonical (comparable) form of the reference.</summary>
public sealed record ChargeReferenceUsage(string Value, string Origin, string Reference);

/// <summary>
/// A module's answer to "does anything of mine already use these charge references?".
/// <para><b>A charge reference identifies one issued charge</b> — today the boleto linha digitável, tomorrow the
/// PIX Copia e Cola (BR Code). It is NOT a payment address: a vendor's PIX key, agency/account or any other
/// reusable destination must never be checked here, because repeating one across payments is the normal case,
/// not a duplicate.</para>
/// Finance and Budgeting each store references on their own entities and neither references the other's Domain,
/// so the check is assembled from the modules the way the EF model is — every module registers its
/// contribution and <see cref="ChargeReferenceDuplicateChecker"/> asks them all. Implementations decide what
/// counts: a cancelled title's reference is free to reuse, an open one's is not.
/// </summary>
public interface IChargeReferenceSource
{
    /// <summary>The first usage found among <paramref name="references"/>, or <c>null</c>.
    /// <paramref name="excludeOwnerIds"/> are the aggregates whose own references must not count as duplicates
    /// of themselves — the payable or requisition being edited, plus (for a requisition-sourced title) the
    /// requisition it came from, which legitimately holds the very same reference.</summary>
    Task<ChargeReferenceUsage?> FindAsync(
        ChargeReferenceKind kind, IReadOnlyCollection<string> references,
        IReadOnlyCollection<Guid> excludeOwnerIds, CancellationToken ct);
}

/// <summary>
/// Guards against paying the same charge twice. A boleto linha digitável identifies one issued slip (bank, due
/// date, amount and campo livre are all encoded in it), so the same reference on two installments means the
/// slip is queued for payment twice — the money leaves the account twice and only one of them is a debt.
/// <para>Two questions, because they differ: <see cref="DuplicateWithin"/> is pure and looks at the request
/// being submitted; <see cref="FindExistingAsync"/> asks every <see cref="IChargeReferenceSource"/> what is
/// already stored. <see cref="ProblemAsync"/> asks both and returns the caller's envelope error.</para>
/// Comparison is on the canonical form (<see cref="BoletoLine.Digits"/>) — the masked line the form sends and
/// the bare one an AI extraction produces are the same boleto.
/// </summary>
public sealed class ChargeReferenceDuplicateChecker(IEnumerable<IChargeReferenceSource> sources)
{
    /// <summary>The envelope error when one of <paramref name="references"/> is already queued for payment,
    /// or <c>null</c>. <paramref name="errorCode"/> is the caller's aggregate code, so the requisition and the
    /// payable each report the duplicate in their own vocabulary.</summary>
    public async Task<IResult?> ProblemAsync(
        ChargeReferenceKind kind, IReadOnlyList<string?> references, IReadOnlyCollection<Guid> excludeOwnerIds,
        string errorCode, CancellationToken ct)
    {
        var what = Describe(kind);

        if (DuplicateWithin(kind, references) is not null)
            return ErrorResults.InvalidState(
                $"The same {what} is on more than one installment of this request.", errorCode);

        if (await FindExistingAsync(kind, references, excludeOwnerIds, ct) is { } usage)
            return ErrorResults.InvalidState(
                $"This {what} is already registered on {usage.Reference}.", errorCode);

        return null;
    }

    private static string Describe(ChargeReferenceKind kind) => kind switch
    {
        ChargeReferenceKind.BoletoLine => "boleto linha digitável",
        ChargeReferenceKind.PixPayloadEmv => "PIX Copia e Cola",
        _ => "charge reference",
    };

    /// <summary>The comparable form of a reference, or <c>null</c> when the value is not a one-time charge at
    /// all — a blank, or a STATIC BR Code, which is the vendor's key in QR clothing and repeats legitimately.</summary>
    private static string? Canonical(ChargeReferenceKind kind, string? value) => kind switch
    {
        ChargeReferenceKind.BoletoLine => BoletoLine.Canonical(value),
        ChargeReferenceKind.PixPayloadEmv => IsDynamicQr(value) ? value!.Trim() : null,
        _ => null,
    };

    private static bool IsDynamicQr(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
            return false;
        var parsed = PixPayloadEmvParser.Parse(value);
        return parsed.IsValid && parsed.Initiation == PixPayloadEmvInitiation.Dynamic;
    }

    /// <summary>The first reference that appears twice in the same request, as the caller supplied it, or
    /// <c>null</c>. Blank is not a reference: PIX/TED installments carry none and must never collide.</summary>
    public static string? DuplicateWithin(ChargeReferenceKind kind, IReadOnlyList<string?> references)
    {
        var seen = new HashSet<string>(StringComparer.Ordinal);
        foreach (var reference in references)
        {
            if (Canonical(kind, reference) is not { } canonical)
                continue;
            if (!seen.Add(canonical))
                return reference;
        }
        return null;
    }

    /// <summary>The first usage of any of <paramref name="references"/> already stored by some module, or
    /// <c>null</c>. <paramref name="excludeOwnerIds"/> are the aggregates being edited (and, for a title born
    /// of a requisition, that requisition), so re-saving without touching the boleto is not a self-conflict.</summary>
    public async Task<ChargeReferenceUsage?> FindExistingAsync(
        ChargeReferenceKind kind, IReadOnlyList<string?> references, IReadOnlyCollection<Guid> excludeOwnerIds,
        CancellationToken ct)
    {
        var canonical = references
            .Select(r => Canonical(kind, r))
            .OfType<string>()
            .ToHashSet(StringComparer.Ordinal);

        if (canonical.Count == 0)
            return null;

        foreach (var source in sources)
        {
            if (await source.FindAsync(kind, canonical, excludeOwnerIds, ct) is { } usage)
                return usage;
        }
        return null;
    }
}
