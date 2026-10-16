using System.Globalization;
using System.Runtime.CompilerServices;
using Google.Cloud.Firestore;

namespace Ulbra.One.FirestoreImport.Source;

/// <summary>Reads the legacy collections, streaming server-side (the SDK pages internally) and mapping each
/// snapshot to a typed document. Volumes are modest; batching that matters is on the write side. The
/// collection ids stay in Portuguese (they are the real Firestore names); everything mapped out is English.</summary>
public sealed class FirestoreSource(FirestoreDb db)
{
    public const string CollVendors = "fornecedores";
    public const string CollUsers = "usuarios";
    public const string CollBudgets = "orcamentos";
    public const string CollRequisitions = "requisicoes";
    public const string CollPayments = "pagamentos";
    public const string CollBankAccounts = "santander_config";
    public const string CollRemittances = "remessas";
    public const string CollAdjustments = "adequacoes";
    public const string CollProjects = "projetos";

    public IAsyncEnumerable<VendorDoc> ReadVendorsAsync(CancellationToken ct)
        => StreamAsync(CollVendors, MapVendor, ct);

    public IAsyncEnumerable<UserDoc> ReadUsersAsync(CancellationToken ct)
        => StreamAsync(CollUsers, MapUser, ct);

    public IAsyncEnumerable<BudgetDoc> ReadBudgetsAsync(CancellationToken ct)
        => StreamAsync(CollBudgets, MapBudget, ct);

    public IAsyncEnumerable<RequisitionDoc> ReadRequisitionsAsync(CancellationToken ct)
        => StreamAsync(CollRequisitions, MapRequisition, ct);

    public IAsyncEnumerable<PaymentDoc> ReadPaymentsAsync(CancellationToken ct)
        => ReadPaymentsAsync(null, ct);

    /// <summary>Streams `pagamentos`, optionally only those with <c>updatedAt</c> after <paramref name="since"/>
    /// (server-side incremental filter). The legacy stores <c>updatedAt</c> as an ISO-8601 UTC string, so the
    /// filter compares strings — which sort chronologically.</summary>
    public IAsyncEnumerable<PaymentDoc> ReadPaymentsAsync(DateTimeOffset? since, CancellationToken ct)
    {
        Query query = db.Collection(CollPayments);
        if (since is { } s)
            query = query.WhereGreaterThan("updatedAt", s.ToUniversalTime().ToString("yyyy-MM-ddTHH:mm:ss.fffZ", CultureInfo.InvariantCulture));
        return StreamQueryAsync(query, MapPayment, ct);
    }

    /// <summary>Streams the COMPLETE `pagamentos` set of the given requisitions (batched <c>WhereIn</c>,
    /// Firestore's 30-value limit per query). The incremental import uses it: when one parcela of a requisition
    /// changed, the whole group must be re-read so the payable isn't rebuilt from a single parcela.</summary>
    public async IAsyncEnumerable<PaymentDoc> ReadPaymentsForRequisitionsAsync(
        IReadOnlyCollection<string> requisitionIds, [EnumeratorCancellation] CancellationToken ct)
    {
        foreach (var batch in requisitionIds.Chunk(30))
            await foreach (var snap in db.Collection(CollPayments).WhereIn("requisitionId", batch).StreamAsync(ct))
                if (snap.Exists) yield return MapPayment(snap);
    }

    public IAsyncEnumerable<BankAccountDoc> ReadBankAccountsAsync(CancellationToken ct)
        => StreamAsync(CollBankAccounts, MapBankAccount, ct);

    public IAsyncEnumerable<RemittanceDoc> ReadRemittancesAsync(CancellationToken ct)
        => StreamAsync(CollRemittances, MapRemittance, ct);

    /// <summary>A cheap connectivity/auth check (reads at most one doc) so the import endpoint can fail FAST —
    /// before returning 202 — when Firestore is unreachable or the credential is wrong.</summary>
    public Task PingAsync(CancellationToken ct)
        => db.Collection(CollProjects).Limit(1).GetSnapshotAsync(ct);

    public IAsyncEnumerable<AdjustmentDoc> ReadAdjustmentsAsync(CancellationToken ct)
        => StreamAsync(CollAdjustments, MapAdjustment, ct);

    public IAsyncEnumerable<ProjectDoc> ReadProjectsAsync(CancellationToken ct)
        => StreamAsync(CollProjects, MapProject, ct);

    /// <summary>Streams only the requisitions that carry a non-empty <c>projectId</c> (server-side:
    /// <c>&gt; ""</c> excludes both the empty string and a missing field), for the project_id backfill.</summary>
    public async IAsyncEnumerable<RequisitionProjectLink> ReadRequisitionProjectLinksAsync(
        [EnumeratorCancellation] CancellationToken ct)
    {
        await foreach (var snap in db.Collection(CollRequisitions).WhereGreaterThan("projectId", "").StreamAsync(ct))
        {
            if (!snap.Exists) continue;
            var projectId = snap.GetStringOrNull("projectId");
            if (!string.IsNullOrWhiteSpace(projectId))
                yield return new RequisitionProjectLink(snap.Id, projectId);
        }
    }

    private async IAsyncEnumerable<T> StreamAsync<T>(
        string collection, Func<DocumentSnapshot, T> map, [EnumeratorCancellation] CancellationToken ct)
    {
        await foreach (var snap in db.Collection(collection).StreamAsync(ct))
        {
            if (snap.Exists)
                yield return map(snap);
        }
    }

    private static async IAsyncEnumerable<T> StreamQueryAsync<T>(
        Query query, Func<DocumentSnapshot, T> map, [EnumeratorCancellation] CancellationToken ct)
    {
        await foreach (var snap in query.StreamAsync(ct))
            if (snap.Exists)
                yield return map(snap);
    }

    private static VendorDoc MapVendor(DocumentSnapshot s) => new(
        s.Id,
        s.GetStringOrNull("razaoSocial"),
        s.GetStringOrNull("logradouro"),
        s.GetStringOrNull("numero"),
        s.GetStringOrNull("complemento"),
        s.GetStringOrNull("bairro"),
        s.GetStringOrNull("cidade"),
        s.GetStringOrNull("cep"),
        s.GetStringOrNull("uf"),
        s.GetStringOrNull("codigoBanco"),
        s.GetStringOrNull("agencia"),
        s.GetStringOrNull("conta"),
        s.GetStringOrNull("contaDv"),
        s.GetStringOrNull("chavePix"),
        s.GetStringOrNull("regimeTributario"),
        s.GetCreatedAt(),
        s.GetUpdatedAt());

    private static UserDoc MapUser(DocumentSnapshot s) => new(
        s.Id,
        s.GetStringOrNull("email"),
        s.GetStringOrNull("name"),
        s.GetStringOrNull("role"),
        s.GetStringList("roles"),
        s.GetStringList("units"),
        s.GetStringList("natures"),
        s.GetDecimalOrNull("budgetLimit"),
        s.GetBoolOrNull("hasApprovalAutonomy"),
        s.GetBoolOrNull("active"),
        s.GetStringOrNull("uid"),
        s.GetCreatedAt(),
        s.GetUpdatedAt());

    private static BudgetDoc MapBudget(DocumentSnapshot s) => new(
        s.Id,
        s.GetStringOrNull("unitCode"),
        s.GetStringOrNull("unitName"),
        s.GetStringOrNull("natureName"),
        s.GetStringOrNull("competency"),
        s.GetDecimalOrNull("limitValue"),
        s.GetCreatedAt(),
        s.GetUpdatedAt());

    private static RequisitionDoc MapRequisition(DocumentSnapshot s) => new(
        s.Id,
        s.GetStringOrNull("unitCode"),
        s.GetStringOrNull("natureName"),
        s.GetStringOrNull("status"),
        s.GetStringOrNull("type"),
        s.GetStringOrNull("estimateType"),
        s.GetStringOrNull("paymentSystem"),
        s.GetDecimalOrNull("estimateValue"),
        s.GetStringOrNull("startCompetency"),
        s.GetStringOrNull("endCompetency"),
        s.GetStringOrNull("providerCnpj"),
        s.GetStringOrNull("requesterEmail"),
        s.GetStringOrNull("description"),
        s.GetLongOrNull("seqNum"),
        s.GetStringOrNull("paymentFirstDueDate"),
        s.GetLongOrNull("paymentInstallmentsCount"),
        s.GetStringOrNull("paymentFrequency"),
        s.GetStringOrNull("observation"),
        s.GetCreatedAt(),
        s.GetUpdatedAt());

    private static PaymentDoc MapPayment(DocumentSnapshot s) => new(
        s.Id,
        s.GetStringOrNull("requisitionId"),
        s.GetLongOrNull("seqNum"),
        s.GetDecimalOrNull("amount"),
        s.GetDecimalOrNull("originalAmount"),
        s.GetDecimalOrNull("juros"),
        s.GetDecimalOrNull("multa"),
        s.GetStringOrNull("competency"),
        s.GetStringOrNull("dueDate"),
        s.GetStringOrNull("paymentDate"),
        s.GetStringOrNull("paymentMethod"),
        s.GetStringOrNull("status"),
        s.GetStringOrNull("returnStatus"),
        s.GetStringOrNull("providerCnpj"),
        s.GetStringOrNull("providerName"),
        s.GetStringOrNull("unitCode"),
        s.GetStringOrNull("linhaDigitavel"),
        s.GetStringOrNull("pixKey"),
        s.GetStringOrNull("pixCopyAndPaste"),
        s.GetStringOrNull("bankRemittanceId"),
        s.GetStringOrNull("fiscalDocumentUrl"),
        s.GetStringOrNull("fiscalDocumentName"),
        s.GetStringOrNull("boletoDocumentUrl"),
        s.GetStringOrNull("boletoDocumentName"),
        s.GetStringOrNull("paymentReceiptUrl"),
        s.GetStringOrNull("paymentReceiptName"),
        s.GetBoolOrNull("isExternal"),
        s.GetStringOrNull("importId"),
        s.GetStringOrNull("importType"),
        s.GetCreatedAt(),
        s.GetUpdatedAt());

    private static BankAccountDoc MapBankAccount(DocumentSnapshot s) => new(
        s.Id,
        s.GetStringOrNull("agencia"),
        s.GetStringOrNull("agenciaDv"),
        s.GetStringOrNull("conta"),
        s.GetStringOrNull("contaDv"),
        s.GetStringOrNull("cnpj"),
        s.GetStringOrNull("razaoSocial"),
        s.GetStringOrNull("convenio"),
        s.GetStringOrNull("logradouro"),
        s.GetStringOrNull("numero"),
        s.GetStringOrNull("complemento"),
        s.GetStringOrNull("cidade"),
        s.GetStringOrNull("cep"),
        s.GetStringOrNull("uf"),
        s.GetCreatedAt(),
        s.GetUpdatedAt());

    private static RemittanceDoc MapRemittance(DocumentSnapshot s) => new(
        s.Id,
        s.GetStringOrNull("santanderConfigId"),
        s.GetStringList("paymentIds"),
        s.GetLongOrNull("sequencial"),
        s.GetStringOrNull("dateStr"),
        s.GetStringOrNull("status"),
        s.GetStringOrNull("createdAt"));

    private static AdjustmentDoc MapAdjustment(DocumentSnapshot s) => new(
        s.Id,
        s.GetStringOrNull("unitCode"),
        s.GetStringOrNull("natureName"),
        s.GetStringOrNull("type"),
        s.GetStringOrNull("revisionAction"),
        s.GetStringOrNull("status"),
        s.GetStringOrNull("competency"),
        s.GetStringOrNull("sourceCompetency"),
        s.GetDecimalOrNull("value"),
        s.GetDecimalMap("competencyValues"),
        s.GetStringOrNull("justification"),
        s.GetStringOrNull("counterparts"),
        s.GetStringOrNull("requesterEmail"),
        s.GetLongOrNull("seqNum"),
        s.GetCreatedAt(),
        s.GetUpdatedAt(),
        s.GetTimestampOrNull("cancelledAt"),
        s.GetStringOrNull("cancelLog"));

    private static ProjectDoc MapProject(DocumentSnapshot s) => new(
        s.Id,
        s.GetStringOrNull("name"),
        s.GetStringOrNull("description"),
        s.GetDecimalOrNull("budgetLimit"),
        s.GetDecimalOrNull("initialConsumedValue"),
        s.GetStringOrNull("unitCode"),
        s.GetStringOrNull("status"),
        s.GetCreatedAt(),
        s.GetUpdatedAt());
}
