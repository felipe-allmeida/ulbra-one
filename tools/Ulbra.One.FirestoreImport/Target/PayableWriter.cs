using System.Globalization;
using System.Text;
using System.Text.Json;
using Dapper;
using Npgsql;
using Ulbra.One.FirestoreImport.Mapping;
using Ulbra.One.FirestoreImport.Source;
using Ulbra.One.FirestoreImport.Storage;

namespace Ulbra.One.FirestoreImport.Target;

/// <summary>Writes the legacy `pagamentos` (1 doc per installment) into Finance as **1 base Payable per
/// requisition** (`finance.payables`) + N `finance.installments` + `finance.payments` (baixa for the paid
/// ones), and mirrors the realized amount into `budgeting.requisition_payment` so the budget's *realizado*
/// comes from the REAL payments (principal by competency). Scope: only workflow payments (`isExternal != true`);
/// the `EXT_REQ_IMP_*` external buckets are skipped. Unit/vendor/natureza come from the REQUISITION (the
/// pagamento often leaves them blank). Writes happen in CHUNKS via multi-row `unnest` (one statement per table
/// per chunk instead of one per row); if a chunk's bulk write fails it falls back to per-group writes so one
/// bad row doesn't lose the chunk. Files (fiscal document / attachments) are handled by the blob step.</summary>
public sealed class PayableWriter(NpgsqlDataSource dataSource, ImportStore store)
{
    public const string EntityType = "payable";
    private const string Actor = "firestore-migration";
    private static readonly Guid SystemActorId = new("00000000-0000-0000-0000-0000000005ed");
    private const string DefaultBankConfigId = "default";
    private const int ChunkSize = 250;
    private const string SyncCollection = "pagamentos"; // incremental-read cursor key (ImportStore.sync_state)

    // ---- Bulk (chunk) statements: one multi-row insert per table via unnest of parallel arrays. ----

    private const string BulkUpsertPayableSql = """
        insert into finance.payables (
            id, company_id, origin, vendor_id, counterparty_name, document_number, issue_date, description,
            cost_center_id, origin_requisition_id, origin_unit_id, requires_fiscal_document, requires_tax_analysis,
            total_amount, status, type, payment_method, created_at, created_by, updated_at, updated_by)
        select u.id, u.company_id, 1, u.vendor_id, u.counterparty_name, null, u.issue_date::date, u.description,
            u.cost_center_id, u.origin_requisition_id, u.origin_unit_id, u.requires_fiscal, u.requires_tax,
            u.total_amount, u.status, u.type, u.payment_method,
            coalesce(u.created_at::timestamptz, now()), @actor, coalesce(u.updated_at::timestamptz, now()), @actor
        from unnest(
            @ids::uuid[], @companyIds::uuid[], @vendorIds::uuid[], @counterpartyNames::text[], @issueDates::text[],
            @descriptions::text[], @costCenterIds::uuid[], @originReqIds::uuid[], @originUnitIds::uuid[],
            @requiresFiscals::boolean[], @requiresTaxes::boolean[], @totalAmounts::numeric[], @statuses::text[],
            @types::text[], @paymentMethods::integer[], @createdAts::text[], @updatedAts::text[])
          as u(id, company_id, vendor_id, counterparty_name, issue_date, description, cost_center_id,
               origin_requisition_id, origin_unit_id, requires_fiscal, requires_tax, total_amount, status, type,
               payment_method, created_at, updated_at)
        on conflict (origin_requisition_id) where origin_requisition_id is not null and origin_cycle_competency is null
        do update set
            company_id = excluded.company_id, origin_unit_id = excluded.origin_unit_id,
            vendor_id = excluded.vendor_id, counterparty_name = excluded.counterparty_name, issue_date = excluded.issue_date,
            description = excluded.description, cost_center_id = excluded.cost_center_id,
            requires_fiscal_document = excluded.requires_fiscal_document, requires_tax_analysis = excluded.requires_tax_analysis,
            total_amount = excluded.total_amount, status = excluded.status, type = excluded.type,
            payment_method = excluded.payment_method,
            created_at = excluded.created_at, updated_at = excluded.updated_at, updated_by = @actor
        """;

    private const string BulkInsertInstallmentSql = """
        insert into finance.installments (
            id, payable_id, sequence, due_date, amount, net_amount, paid_amount, status,
            scheduled_payment_date, is_held, payment_method, boleto_digitable_line, pix_key_override)
        select u.id, u.payable_id, u.sequence, u.due_date::date, u.amount, u.amount, u.paid_amount, u.status,
            u.due_date::date, false, u.method, u.boleto, u.pix
        from unnest(@ids::uuid[], @payableIds::uuid[], @sequences::integer[], @dueDates::text[], @amounts::numeric[],
            @paidAmounts::numeric[], @statuses::integer[], @methods::integer[], @boletos::text[], @pixes::text[])
          as u(id, payable_id, sequence, due_date, amount, paid_amount, status, method, boleto, pix)
        """;

    private const string BulkInsertPaymentSql = """
        insert into finance.payments (
            id, installment_id, payable_id, payment_date, amount, bank_account_id, method,
            interest, fine, discount, is_reversed, created_at, created_by_id)
        select u.id, u.installment_id, u.payable_id, u.payment_date::date, u.amount, u.bank_account_id, u.method,
            u.interest, u.fine, 0, u.is_reversed, coalesce(u.payment_date::timestamptz, now()), @actorId
        from unnest(@ids::uuid[], @installmentIds::uuid[], @payableIds::uuid[], @paymentDates::text[], @amounts::numeric[],
            @bankAccountIds::uuid[], @methods::integer[], @interests::numeric[], @fines::numeric[], @isReverseds::boolean[])
          as u(id, installment_id, payable_id, payment_date, amount, bank_account_id, method, interest, fine, is_reversed)
        """;

    private const string BulkInsertReqPaymentSql = """
        insert into budgeting.requisition_payment (
            id, requisition_id, payment_id, payable_id, amount, competency, paid_at, reversed)
        select u.id, u.requisition_id, u.payment_id, u.payable_id, u.amount, u.competency, u.paid_at::timestamptz, u.reversed
        from unnest(@ids::uuid[], @reqIds::uuid[], @paymentIds::uuid[], @payableIds::uuid[], @amounts::numeric[],
            @competencies::text[], @paidAts::text[], @reverseds::boolean[])
          as u(id, requisition_id, payment_id, payable_id, amount, competency, paid_at, reversed)
        """;

    private const string DeletePaymentsSql = "delete from finance.payments where payable_id = any(@payableIds)";
    private const string DeleteInstallmentsSql = "delete from finance.installments where payable_id = any(@payableIds)";
    private const string DeleteReqPaymentsSql = "delete from budgeting.requisition_payment where requisition_id = any(@reqIds)";

    public async Task<ImportSummary> ImportAsync(
        FirestoreSource source, OrgResolver org, bool full, bool dryRun, CancellationToken ct)
    {
        var counters = new ImportCounters();
        var priorMap = await store.LoadMapAsync(EntityType, ct);
        var requisitionMap = await store.LoadMapAsync(RequisitionWriter.EntityType, ct);
        var bankAccounts = await store.LoadMapAsync(BankAccountWriter.EntityType, ct);
        var defaultBank = bankAccounts.TryGetValue(DefaultBankConfigId, out var db) ? db.TargetId : (Guid?)null;

        var runStart = DateTimeOffset.UtcNow;
        var since = full ? null : await store.GetSyncCursorAsync(SyncCollection, ct);

        // Payment groups to process: full = every in-scope requisition; incremental = the COMPLETE parcela set
        // of only the requisitions whose payments changed since the last run (a no-change re-run reads nothing).
        var groups = await BuildGroupsAsync(source, since, ct);
        if (groups.Count == 0)
        {
            if (!dryRun) await store.SetSyncCursorAsync(SyncCollection, runStart, ct);
            return counters.ToSummary("payables", dryRun);
        }

        // Cross-reference reads, only once there is work: remittances → payer bank account; requisition head
        // fields (unit/vendor/natureza) that the pagamento leaves blank.
        var remittanceIndex = new Dictionary<string, string>(StringComparer.Ordinal);
        await foreach (var r in source.ReadRemittancesAsync(ct))
            foreach (var pid in r.PaymentIds)
                if (r.BankAccountConfigId is { Length: > 0 }) remittanceIndex[pid] = r.BankAccountConfigId;

        var reqById = new Dictionary<string, RequisitionDoc>(StringComparer.Ordinal);
        await foreach (var rq in source.ReadRequisitionsAsync(ct)) reqById[rq.Id] = rq;

        // Phase 1 (in memory): map + resolve cost centers; collect the write set. Skips/dead-letters here
        // touch only the bookkeeping schema (no business writes), so the heavy path stays batchable.
        var pending = new List<PendingPayable>();
        foreach (var reqId in groups.Keys.OrderBy(k => k, StringComparer.Ordinal))
        {
            counters.CountRead();
            var payments = groups[reqId];
            payments.Sort((a, b) => (a.Sequence ?? 0).CompareTo(b.Sequence ?? 0));

            var group = MapGroup(reqId, payments, reqById, requisitionMap, org, remittanceIndex, bankAccounts, defaultBank, out var error);
            if (group is null)
            {
                if (!dryRun) await store.DeadLetterAsync(EntityType, reqId, error!, "{}", ct);
                counters.Count(RowOutcome.DeadLettered);
                continue;
            }

            priorMap.TryGetValue(reqId, out var prior);
            if (prior.ContentHash == group.Hash) { counters.Count(RowOutcome.Skipped); continue; }
            if (dryRun) { counters.Count(prior.TargetId == default ? RowOutcome.Inserted : RowOutcome.Updated); continue; }

            Guid costCenterId;
            try { costCenterId = await org.EnsureCostCenterAsync(group.CompanyId, group.NatureName, ct); }
            catch (PostgresException ex)
            {
                await store.DeadLetterAsync(EntityType, reqId, ex.MessageText, "{}", ct);
                counters.Count(RowOutcome.DeadLettered);
                continue;
            }

            var payableId = prior.TargetId == default ? Guid.NewGuid() : prior.TargetId;
            pending.Add(new PendingPayable(reqId, payableId, costCenterId, prior.TargetId != default, group));
        }

        // Phase 2: bulk-write in chunks; on a bulk failure, fall back to per-group so one bad row is isolated.
        if (!dryRun && pending.Count > 0)
        {
            await using var conn = await dataSource.OpenConnectionAsync(ct);
            for (var i = 0; i < pending.Count; i += ChunkSize)
            {
                var chunk = pending.GetRange(i, Math.Min(ChunkSize, pending.Count - i));
                try
                {
                    await WriteChunkAsync(conn, chunk, ct);
                    foreach (var p in chunk) counters.Count(p.IsUpdate ? RowOutcome.Updated : RowOutcome.Inserted);
                }
                catch (Exception ex) when (ex is PostgresException or NpgsqlException)
                {
                    foreach (var p in chunk) counters.Count(await WriteOneAsync(conn, p, ct));
                }
            }
        }

        // Advance the incremental cursor to the run's START (docs updated mid-run are re-read next time).
        if (!dryRun) await store.SetSyncCursorAsync(SyncCollection, runStart, ct);
        return counters.ToSummary("payables", dryRun);
    }

    // Full read (every in-scope requisition) or incremental: the payments changed since the cursor give the
    // AFFECTED requisitions; their COMPLETE parcela sets are then re-read so a payable is never rebuilt from a
    // single changed parcela. External (`EXT_REQ_IMP_*` / isExternal) is always excluded.
    private static async Task<Dictionary<string, List<PaymentDoc>>> BuildGroupsAsync(
        FirestoreSource source, DateTimeOffset? since, CancellationToken ct)
    {
        var groups = new Dictionary<string, List<PaymentDoc>>(StringComparer.Ordinal);
        void AddInScope(PaymentDoc p)
        {
            if (p.IsExternal == true) return;
            var reqId = (p.RequisitionId ?? string.Empty).Trim();
            if (reqId.Length == 0 || reqId.StartsWith("EXT_REQ_IMP_", StringComparison.Ordinal)) return;
            (groups.TryGetValue(reqId, out var list) ? list : groups[reqId] = []).Add(p);
        }

        if (since is null)
        {
            await foreach (var p in source.ReadPaymentsAsync((DateTimeOffset?)null, ct)) AddInScope(p);
            return groups;
        }

        var affected = new HashSet<string>(StringComparer.Ordinal);
        await foreach (var p in source.ReadPaymentsAsync(since, ct))
        {
            if (p.IsExternal == true) continue;
            var reqId = (p.RequisitionId ?? string.Empty).Trim();
            if (reqId.Length > 0 && !reqId.StartsWith("EXT_REQ_IMP_", StringComparison.Ordinal)) affected.Add(reqId);
        }
        if (affected.Count > 0)
            await foreach (var p in source.ReadPaymentsForRequisitionsAsync(affected, ct)) AddInScope(p);
        return groups;
    }

    private async Task WriteChunkAsync(NpgsqlConnection conn, List<PendingPayable> chunk, CancellationToken ct)
    {
        var inst = new InstallmentBatch();
        var pay = new PaymentBatch();
        var rp = new ReqPaymentBatch();
        foreach (var p in chunk)
            foreach (var l in p.Group.Lines)
            {
                var installmentId = Guid.NewGuid();
                inst.Add(installmentId, p.PayableId, l);
                if (!l.Paid) continue;
                var paymentId = Guid.NewGuid();
                pay.Add(paymentId, installmentId, p.PayableId, l);
                rp.Add(p.Group.RequisitionTargetId, paymentId, p.PayableId, l);
            }

        var payableIds = chunk.Select(p => p.PayableId).ToArray();
        var reqIds = chunk.Select(p => p.Group.RequisitionTargetId).ToArray();

        await using var tx = await conn.BeginTransactionAsync(ct);
        await ExecBulkAsync(conn, tx, BulkUpsertPayableSql, HeadParams(chunk), ct);
        await ExecBulkAsync(conn, tx, DeletePaymentsSql, new() { ["payableIds"] = payableIds }, ct);
        await ExecBulkAsync(conn, tx, DeleteInstallmentsSql, new() { ["payableIds"] = payableIds }, ct);
        await ExecBulkAsync(conn, tx, DeleteReqPaymentsSql, new() { ["reqIds"] = reqIds }, ct);
        await ExecBulkAsync(conn, tx, BulkInsertInstallmentSql, inst.Params(), ct);
        if (pay.Count > 0) await ExecBulkAsync(conn, tx, BulkInsertPaymentSql, pay.Params(SystemActorId), ct);
        if (rp.Count > 0) await ExecBulkAsync(conn, tx, BulkInsertReqPaymentSql, rp.Params(), ct);
        await ImportStore.UpsertMapBatchAsync(conn, tx, EntityType,
            chunk.Select(p => p.ReqId).ToArray(), payableIds, chunk.Select(p => p.Group.Hash).ToArray(), ct);
        await tx.CommitAsync(ct);
    }

    // Raw command so array parameters go as real Postgres arrays — Dapper would expand @param into (@p1,@p2,…),
    // which breaks unnest()/any().
    private static async Task ExecBulkAsync(
        NpgsqlConnection conn, NpgsqlTransaction tx, string sql, Dictionary<string, object> ps, CancellationToken ct)
    {
        await using var cmd = new NpgsqlCommand(sql, conn, tx);
        foreach (var (name, value) in ps) cmd.Parameters.AddWithValue(name, value);
        await cmd.ExecuteNonQueryAsync(ct);
    }

    // Per-group fallback: same writes, one payable at a time, so a single failing row is dead-lettered
    // instead of failing its whole chunk. Uses the already-resolved payable id + cost center.
    private async Task<RowOutcome> WriteOneAsync(NpgsqlConnection conn, PendingPayable p, CancellationToken ct)
    {
        var single = new List<PendingPayable> { p };
        try
        {
            await WriteChunkAsync(conn, single, ct);
            return p.IsUpdate ? RowOutcome.Updated : RowOutcome.Inserted;
        }
        catch (Exception ex) when (ex is PostgresException or NpgsqlException)
        {
            await store.DeadLetterAsync(EntityType, p.ReqId, (ex as PostgresException)?.MessageText ?? ex.Message, "{}", ct);
            return RowOutcome.DeadLettered;
        }
    }

    private static Dictionary<string, object> HeadParams(List<PendingPayable> chunk) => new()
    {
        ["actor"] = Actor,
        ["ids"] = chunk.Select(p => p.PayableId).ToArray(),
        ["companyIds"] = chunk.Select(p => p.Group.CompanyId).ToArray(),
        ["vendorIds"] = chunk.Select(p => p.Group.VendorId).ToArray(),
        ["counterpartyNames"] = chunk.Select(p => p.Group.CounterpartyName).ToArray(),
        ["issueDates"] = chunk.Select(p => p.Group.IssueDate).ToArray(),
        ["descriptions"] = chunk.Select(p => p.Group.Description).ToArray(),
        ["costCenterIds"] = chunk.Select(p => p.CostCenterId).ToArray(),
        ["originReqIds"] = chunk.Select(p => p.Group.RequisitionTargetId).ToArray(),
        ["originUnitIds"] = chunk.Select(p => p.Group.UnitId).ToArray(),
        ["requiresFiscals"] = chunk.Select(p => p.Group.RequiresFiscal).ToArray(),
        ["requiresTaxes"] = chunk.Select(p => p.Group.RequiresTax).ToArray(),
        ["totalAmounts"] = chunk.Select(p => p.Group.TotalAmount).ToArray(),
        ["statuses"] = chunk.Select(p => p.Group.Status).ToArray(),
        ["types"] = chunk.Select(p => p.Group.PayableType).ToArray(),
        ["paymentMethods"] = chunk.Select(p => p.Group.PaymentMethod).ToArray(),
        ["createdAts"] = chunk.Select(p => p.Group.CreatedAt?.ToString("O")).ToArray(),
        ["updatedAts"] = chunk.Select(p => p.Group.UpdatedAt?.ToString("O")).ToArray(),
    };

    private static PayableGroup? MapGroup(
        string reqId, List<PaymentDoc> payments, IReadOnlyDictionary<string, RequisitionDoc> reqById,
        IReadOnlyDictionary<string, IdMapEntry> requisitionMap, OrgResolver org,
        IReadOnlyDictionary<string, string> remittanceIndex, IReadOnlyDictionary<string, IdMapEntry> bankAccounts,
        Guid? defaultBank, out string? error)
    {
        error = null;
        reqById.TryGetValue(reqId, out var rq);
        if (!requisitionMap.TryGetValue(reqId, out var reqEntry) || reqEntry.TargetId == default)
        {
            // Surface WHY: an ORPHAN payment (its requisition does not exist in the source) vs a requisition
            // that exists but was not imported (its stage was not run, or it failed — e.g. Conta Fixa uniqueness).
            error = rq is null
                ? $"requisição '{reqId}' ÓRFÃ: não existe na coleção `requisicoes` de origem (pagamento sem requisição)"
                : $"requisição '{reqId}' não importada — status origem '{rq.Status}' → '{RequisitionMapping.MapStatus(rq.Status)}' (rode o estágio, ou ela falhou, ex.: unicidade de Conta Fixa)";
            return null;
        }

        var head = payments[0];

        // Unit / vendor / natureza come from the REQUISITION (authoritative) — the legacy `pagamento` often
        // leaves unitCode/providerCnpj empty, which was dead-lettering most groups as "unidade '' …".
        var unitCode = !string.IsNullOrWhiteSpace(rq?.UnitCode) ? rq!.UnitCode! : head.UnitCode;
        if (!org.TryResolveUnit(unitCode, out var unit))
        {
            error = $"unidade '{unitCode}' não encontrada no org-alvo";
            return null;
        }

        var providerCnpj = !string.IsNullOrWhiteSpace(rq?.ProviderCnpj) ? rq!.ProviderCnpj! : head.ProviderCnpj;
        var vendorDoc = VendorMapping.ResolveDocument(providerCnpj ?? string.Empty).Document;
        if (vendorDoc.Length == 0 || !org.TryResolveVendor(vendorDoc, out var vendorId))
        {
            error = $"fornecedor '{providerCnpj}' não encontrado (import fornecedores primeiro)";
            return null;
        }

        var natureName = rq?.NatureName ?? string.Empty;
        var reqType = rq is not null ? RequisitionMapping.MapType(rq.Type) : "Purchase";
        var payableType = reqType == "Purchase" ? "Product" : "Service";
        var description = Text.Trunc(
            !string.IsNullOrWhiteSpace(rq?.Description) ? rq!.Description! : $"Migrado {reqId}", 300);

        var anyPaid = payments.Any(p => PaymentMapping.IsPaid(p.Status));
        var installmentStatuses = payments.Select(p => p.Status).ToList();
        var requiresTax = PaymentMapping.RequiresTaxAnalysis(payableType, anyPaid);
        var status = PaymentMapping.MapPayableStatus(installmentStatuses, requiresTax);

        var lines = new List<PayableLine>(payments.Count);
        var seq = 0;
        foreach (var p in payments)
        {
            seq++;
            if (PaymentMapping.IsCancelled(p.Status)) continue;
            var principal = p.OriginalAmount ?? p.Amount ?? 0m;
            var cash = p.Amount ?? principal;
            var paid = PaymentMapping.IsPaid(p.Status);
            Guid? bank = null;
            if (paid)
            {
                bank = remittanceIndex.TryGetValue(p.Id, out var scid) && bankAccounts.TryGetValue(scid, out var ba)
                    ? ba.TargetId
                    : defaultBank;
                if (bank is null)
                {
                    error = "conta bancária não resolvida (rode bank-accounts primeiro; sem conta 'default')";
                    return null;
                }
            }
            var due = DateText(p.DueDate) ?? CompetencyFirstDay(p.Competency);
            lines.Add(new PayableLine(
                Sequence: (int)(p.Sequence ?? seq),
                DueDate: due,
                PrincipalAmount: principal,
                CashAmount: cash,
                Paid: paid,
                Reversed: PaymentMapping.IsReturned(p.Status),
                InstallmentStatus: PaymentMapping.MapInstallmentStatus(p.Status),
                Method: PaymentMapping.MapMethod(p.PaymentMethod),
                PaymentDate: DateText(p.PaymentDate) ?? due,
                // competency is varchar(7) NOT NULL — normalize to "YYYY-MM" (derive from the paid/due date
                // when the source is blank or malformed) so it never overflows and buckets realizado correctly.
                Competency: CompetencyRange.IsValid(p.Competency) ? p.Competency!.Trim() : (DateText(p.PaymentDate) ?? due)[..7],
                Interest: p.Interest ?? 0m,
                Fine: p.Fine ?? 0m,
                BankAccountId: bank,
                Boleto: Text.Trunc(p.DigitableLine, 48),
                Pix: Text.Trunc(string.IsNullOrWhiteSpace(p.PixKey) ? p.PixCopyAndPaste : p.PixKey, 100)));
        }

        if (lines.Count == 0 && status != PaymentMapping.PayableCancelled)
        {
            error = "nenhuma parcela válida (todas canceladas?)";
            return null;
        }

        var total = lines.Sum(l => l.PrincipalAmount);
        // created_at / issue_date come from the legacy pagamento createdAt (decision #9), preserving the real
        // creation date; updated_at is the most recent parcela change. Never falls back to "today".
        var createdAt = payments.Select(p => p.CreatedAt).FirstOrDefault(x => x is not null);
        var updates = payments.Where(p => p.UpdatedAt is not null).Select(p => p.UpdatedAt!.Value).ToList();
        DateTimeOffset? updatedAt = updates.Count > 0 ? updates.Max() : createdAt;
        var issueDate = createdAt?.ToString("yyyy-MM-dd") ?? CompetencyFirstDay(head.Competency);

        return new PayableGroup(
            RequisitionTargetId: reqEntry.TargetId,
            CompanyId: unit.CompanyId,
            UnitId: unit.UnitId,
            VendorId: vendorId,
            NatureName: natureName,
            CounterpartyName: Text.Trunc(head.ProviderName ?? natureName, 200),
            Description: description,
            IssueDate: issueDate,
            PayableType: payableType,
            RequiresFiscal: payments.Any(p => !string.IsNullOrWhiteSpace(p.FiscalDocumentUrl)),
            RequiresTax: requiresTax,
            TotalAmount: total,
            Status: status,
            PaymentMethod: PaymentMapping.MapMethod(head.PaymentMethod),
            CreatedAt: createdAt,
            UpdatedAt: updatedAt,
            Lines: lines);
    }

    private static string? DateText(string? raw)
    {
        if (string.IsNullOrWhiteSpace(raw)) return null;
        var s = raw.Trim();
        return s.Length >= 10 && DateOnly.TryParse(s[..10], CultureInfo.InvariantCulture, DateTimeStyles.None, out var d)
            ? d.ToString("yyyy-MM-dd")
            : null;
    }

    private static string CompetencyFirstDay(string? competency)
        => CompetencyRange.IsValid(competency) ? $"{competency!.Trim()}-01" : "2020-01-01";

    /// <summary>A resolved payable ready to write (target id + cost center already resolved).</summary>
    private readonly record struct PendingPayable(
        string ReqId, Guid PayableId, Guid CostCenterId, bool IsUpdate, PayableGroup Group);

    // Column-array accumulators for the chunk's child rows (Dapper reads the array properties by @param name).
    private sealed class InstallmentBatch
    {
        private readonly List<Guid> _ids = [], _payableIds = [];
        private readonly List<int> _sequences = [], _statuses = [], _methods = [];
        private readonly List<string> _dueDates = [], _boletos = [], _pixes = [];
        private readonly List<decimal> _amounts = [], _paidAmounts = [];

        public void Add(Guid id, Guid payableId, PayableLine l)
        {
            _ids.Add(id); _payableIds.Add(payableId); _sequences.Add(l.Sequence); _dueDates.Add(l.DueDate);
            _amounts.Add(l.PrincipalAmount); _paidAmounts.Add(l.Paid ? l.CashAmount : 0m);
            _statuses.Add(l.InstallmentStatus); _methods.Add(l.Method); _boletos.Add(l.Boleto); _pixes.Add(l.Pix);
        }

        public Dictionary<string, object> Params() => new()
        {
            ["ids"] = _ids.ToArray(), ["payableIds"] = _payableIds.ToArray(), ["sequences"] = _sequences.ToArray(),
            ["dueDates"] = _dueDates.ToArray(), ["amounts"] = _amounts.ToArray(), ["paidAmounts"] = _paidAmounts.ToArray(),
            ["statuses"] = _statuses.ToArray(), ["methods"] = _methods.ToArray(), ["boletos"] = _boletos.ToArray(), ["pixes"] = _pixes.ToArray(),
        };
    }

    private sealed class PaymentBatch
    {
        private readonly List<Guid> _ids = [], _installmentIds = [], _payableIds = [], _bankAccountIds = [];
        private readonly List<int> _methods = [];
        private readonly List<string> _paymentDates = [];
        private readonly List<decimal> _amounts = [], _interests = [], _fines = [];
        private readonly List<bool> _isReverseds = [];

        public int Count => _ids.Count;

        public void Add(Guid id, Guid installmentId, Guid payableId, PayableLine l)
        {
            _ids.Add(id); _installmentIds.Add(installmentId); _payableIds.Add(payableId);
            _bankAccountIds.Add(l.BankAccountId!.Value); _methods.Add(l.Method);
            _paymentDates.Add(l.PaymentDate ?? l.DueDate); _amounts.Add(l.CashAmount);
            _interests.Add(l.Interest); _fines.Add(l.Fine); _isReverseds.Add(l.Reversed);
        }

        public Dictionary<string, object> Params(Guid actorId) => new()
        {
            ["actorId"] = actorId, ["ids"] = _ids.ToArray(), ["installmentIds"] = _installmentIds.ToArray(), ["payableIds"] = _payableIds.ToArray(),
            ["paymentDates"] = _paymentDates.ToArray(), ["amounts"] = _amounts.ToArray(), ["bankAccountIds"] = _bankAccountIds.ToArray(),
            ["methods"] = _methods.ToArray(), ["interests"] = _interests.ToArray(), ["fines"] = _fines.ToArray(), ["isReverseds"] = _isReverseds.ToArray(),
        };
    }

    private sealed class ReqPaymentBatch
    {
        private readonly List<Guid> _ids = [], _reqIds = [], _paymentIds = [], _payableIds = [];
        private readonly List<string> _competencies = [], _paidAts = [];
        private readonly List<decimal> _amounts = [];
        private readonly List<bool> _reverseds = [];

        public int Count => _ids.Count;

        public void Add(Guid reqTargetId, Guid paymentId, Guid payableId, PayableLine l)
        {
            _ids.Add(Guid.NewGuid()); _reqIds.Add(reqTargetId); _paymentIds.Add(paymentId); _payableIds.Add(payableId);
            _amounts.Add(l.PrincipalAmount); _competencies.Add(l.Competency); _paidAts.Add(l.PaymentDate ?? l.DueDate);
            _reverseds.Add(l.Reversed);
        }

        public Dictionary<string, object> Params() => new()
        {
            ["ids"] = _ids.ToArray(), ["reqIds"] = _reqIds.ToArray(), ["paymentIds"] = _paymentIds.ToArray(), ["payableIds"] = _payableIds.ToArray(),
            ["amounts"] = _amounts.ToArray(), ["competencies"] = _competencies.ToArray(), ["paidAts"] = _paidAts.ToArray(), ["reverseds"] = _reverseds.ToArray(),
        };
    }
}

/// <summary>One installment/baixa line derived from a `pagamento` doc.</summary>
public sealed record PayableLine(
    int Sequence, string DueDate, decimal PrincipalAmount, decimal CashAmount, bool Paid, bool Reversed,
    int InstallmentStatus, int Method, string? PaymentDate, string Competency, decimal Interest, decimal Fine,
    Guid? BankAccountId, string Boleto, string Pix);

/// <summary>A whole Payable (one requisition) plus its lines and the idempotency hash.</summary>
public sealed record PayableGroup(
    Guid RequisitionTargetId, Guid CompanyId, Guid UnitId, Guid VendorId, string NatureName,
    string CounterpartyName, string Description, string IssueDate, string PayableType, bool RequiresFiscal,
    bool RequiresTax, decimal TotalAmount, string Status, int PaymentMethod,
    DateTimeOffset? CreatedAt, DateTimeOffset? UpdatedAt, IReadOnlyList<PayableLine> Lines)
{
    public string Hash
    {
        get
        {
            // Invariant: every field the writer persists (head columns + child columns) must be in this hash,
            // else a single-field correction leaves the hash unchanged and the group is wrongly skipped on re-run.
            var sb = new StringBuilder();
            sb.Append(CompanyId).Append('|').Append(UnitId).Append('|').Append(VendorId).Append('|')
              .Append(NatureKey.Of(NatureName)).Append('|').Append(CounterpartyName).Append('|').Append(Description)
              .Append('|').Append(IssueDate).Append('|').Append(PayableType).Append('|').Append(RequiresFiscal)
              .Append('|').Append(RequiresTax).Append('|').Append(TotalAmount.ToString(CultureInfo.InvariantCulture))
              .Append('|').Append(Status).Append('|').Append(PaymentMethod)
              .Append('|').Append(CreatedAt?.ToString("O") ?? "").Append('|').Append(UpdatedAt?.ToString("O") ?? "");
            foreach (var l in Lines)
                sb.Append("||").Append(l.Sequence).Append(':').Append(l.DueDate).Append(':')
                  .Append(l.PrincipalAmount.ToString(CultureInfo.InvariantCulture)).Append(':')
                  .Append(l.CashAmount.ToString(CultureInfo.InvariantCulture)).Append(':').Append(l.Paid)
                  .Append(':').Append(l.Reversed).Append(':').Append(l.InstallmentStatus).Append(':')
                  .Append(l.Method).Append(':').Append(l.PaymentDate).Append(':').Append(l.Competency).Append(':')
                  .Append(l.Interest.ToString(CultureInfo.InvariantCulture)).Append(':')
                  .Append(l.Fine.ToString(CultureInfo.InvariantCulture)).Append(':').Append(l.BankAccountId)
                  .Append(':').Append(l.Boleto).Append(':').Append(l.Pix);
            return Storage.Hashing.Sha256Hex(sb.ToString());
        }
    }
}
