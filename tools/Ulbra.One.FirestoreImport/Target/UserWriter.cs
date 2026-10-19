using System.Globalization;
using System.Text.Json;
using Dapper;
using Npgsql;
using Ulbra.One.FirestoreImport.Mapping;
using Ulbra.One.FirestoreImport.Source;
using Ulbra.One.FirestoreImport.Storage;

namespace Ulbra.One.FirestoreImport.Target;

/// <summary>Writes `usuarios` into `identity.users` and its scope grants. Scope is embedded in the legacy
/// as string arrays (with the <c>"all"</c> sentinel); the One models it as explicit rows (fail-closed),
/// so <c>"all"</c> fans out. User grants only reference cost centers that ALREADY exist in the companies
/// of the user's units (naturezas are created only by the budget import, which runs first) — so importing
/// users never provisions org. A nature with no cost center in the user's companies is warned, not fatal.
/// Grants are deleted and re-inserted each run so they mirror the current source.</summary>
public sealed class UserWriter(NpgsqlDataSource dataSource, ImportStore store)
{
    public const string EntityType = "user";
    private const string Actor = "firestore-migration";
    private const string AllSentinel = "all";

    private const string UpsertUserSql = """
        insert into identity.users (
            id, email, name, role, active, approval_limit, has_approval_autonomy,
            can_approve_budget_adjustment, google_subject, last_login_at,
            created_at, created_by, updated_at, updated_by)
        values (
            @id, @email, @name, @role, @active, @approvalLimit, @hasAutonomy,
            false, null, null,
            coalesce(@createdAt, now()), @actor, coalesce(@updatedAt, now()), @actor)
        on conflict (email) do update set
            name = excluded.name,
            role = excluded.role,
            active = excluded.active,
            approval_limit = excluded.approval_limit,
            has_approval_autonomy = excluded.has_approval_autonomy,
            created_at = excluded.created_at,
            updated_at = excluded.updated_at,
            updated_by = @actor
        returning id as "Id", (xmax = 0) as "Inserted"
        """;

    private const string DeleteUnitGrantsSql = "delete from identity.user_unit_scope_grant where user_id = @userId";
    private const string DeleteCcGrantsSql = "delete from identity.user_cost_center_scope_grant where user_id = @userId";

    private const string InsertUnitGrantSql = """
        insert into identity.user_unit_scope_grant (
            id, user_id, company_id, unit_id, created_at, created_by, updated_at, updated_by)
        values (@id, @userId, @companyId, @unitId, now(), @actor, now(), @actor)
        on conflict (user_id, unit_id) do nothing
        """;

    private const string InsertCcGrantSql = """
        insert into identity.user_cost_center_scope_grant (
            id, user_id, company_id, cost_center_id, created_at, created_by, updated_at, updated_by)
        values (@id, @userId, @companyId, @costCenterId, now(), @actor, now(), @actor)
        on conflict (user_id, cost_center_id) do nothing
        """;

    private const string DeleteCompanyGrantsSql = "delete from identity.user_scope_grant where user_id = @userId";

    private const string InsertCompanyGrantSql = """
        insert into identity.user_scope_grant (
            id, user_id, company_id, created_at, created_by, updated_at, updated_by)
        values (@id, @userId, @companyId, now(), @actor, now(), @actor)
        on conflict (user_id, company_id) do nothing
        """;

    public async Task<ImportSummary> ImportAsync(
        FirestoreSource source, OrgResolver org, bool dryRun, CancellationToken ct)
    {
        var counters = new ImportCounters();
        var priorMap = await store.LoadMapAsync(EntityType, ct);
        await using var conn = await dataSource.OpenConnectionAsync(ct);

        await foreach (var u in source.ReadUsersAsync(ct))
        {
            counters.CountRead();
            var payload = JsonSerializer.Serialize(u);

            var email = (u.Email ?? u.Id).Trim().ToLowerInvariant();
            if (email.Length == 0)
            {
                if (!dryRun) await store.DeadLetterAsync(EntityType, u.Id, "e-mail vazio", payload, ct);
                counters.Count(RowOutcome.DeadLettered);
                continue;
            }

            var mapped = Map(u, email, org);
            priorMap.TryGetValue(u.Id, out var prior);
            if (prior.ContentHash == mapped.Hash)
            {
                counters.Count(RowOutcome.Skipped);
                continue;
            }

            if (dryRun)
            {
                counters.Count(prior.TargetId == default ? RowOutcome.Inserted : RowOutcome.Updated);
                continue;
            }

            counters.Count(await UpsertAsync(conn, u.Id, mapped, prior, payload, ct));

            var warnings = new List<string>();
            if (mapped.UnresolvedUnits.Count > 0)
                warnings.Add("unidades não resolvidas: " + string.Join(", ", mapped.UnresolvedUnits));
            if (mapped.UnresolvedNatures.Count > 0)
                warnings.Add("naturezas sem cost center na(s) empresa(s) do usuário: " + string.Join(", ", mapped.UnresolvedNatures));
            if (warnings.Count > 0)
                await store.DeadLetterAsync("user_grant", u.Id, string.Join(" | ", warnings), payload, ct);
        }

        return counters.ToSummary("users", dryRun);
    }

    private async Task<RowOutcome> UpsertAsync(
        NpgsqlConnection conn, string legacyId, MappedUser m, IdMapEntry prior, string payload, CancellationToken ct)
    {
        await using var tx = await conn.BeginTransactionAsync(ct);
        try
        {
            var id = prior.TargetId == default ? Guid.NewGuid() : prior.TargetId;
            var result = await conn.QuerySingleAsync<UpsertResult>(new CommandDefinition(
                UpsertUserSql,
                new { id, email = m.Email, name = m.Name, role = m.Role, active = m.Active, approvalLimit = m.ApprovalLimit, hasAutonomy = m.Autonomy, createdAt = m.CreatedAt, updatedAt = m.UpdatedAt, actor = Actor },
                tx, cancellationToken: ct));

            await conn.ExecuteAsync(new CommandDefinition(DeleteUnitGrantsSql, new { userId = result.Id }, tx, cancellationToken: ct));
            await conn.ExecuteAsync(new CommandDefinition(DeleteCcGrantsSql, new { userId = result.Id }, tx, cancellationToken: ct));
            await conn.ExecuteAsync(new CommandDefinition(DeleteCompanyGrantsSql, new { userId = result.Id }, tx, cancellationToken: ct));

            // Company scope (identity.user_scope_grant) — REQUIRED for login/profile: /me and CanAccessScope
            // read company access from HERE, not from the unit grants' company_id. Without it a scoped user
            // logs in but sees no company and can access nothing (fail-closed). One row per company the user's
            // units/naturezas belong to.
            foreach (var companyId in m.CompanyGrants)
                await conn.ExecuteAsync(new CommandDefinition(InsertCompanyGrantSql,
                    new { id = Guid.NewGuid(), userId = result.Id, companyId, actor = Actor },
                    tx, cancellationToken: ct));

            foreach (var g in m.UnitGrants)
                await conn.ExecuteAsync(new CommandDefinition(InsertUnitGrantSql,
                    new { id = Guid.NewGuid(), userId = result.Id, companyId = g.CompanyId, unitId = g.UnitId, actor = Actor },
                    tx, cancellationToken: ct));

            foreach (var g in m.CostCenterGrants)
                await conn.ExecuteAsync(new CommandDefinition(InsertCcGrantSql,
                    new { id = Guid.NewGuid(), userId = result.Id, companyId = g.CompanyId, costCenterId = g.CostCenterId, actor = Actor },
                    tx, cancellationToken: ct));

            await ImportStore.UpsertMapAsync(conn, tx, EntityType, legacyId, result.Id, m.Hash, ct);
            await tx.CommitAsync(ct);
            return result.Inserted ? RowOutcome.Inserted : RowOutcome.Updated;
        }
        catch (PostgresException ex)
        {
            await tx.RollbackAsync(ct);
            await store.DeadLetterAsync(EntityType, legacyId, ex.MessageText, payload, ct);
            return RowOutcome.DeadLettered;
        }
    }

    private static MappedUser Map(UserDoc u, string email, OrgResolver org)
    {
        var name = Text.Trunc(string.IsNullOrWhiteSpace(u.Name) ? EmailLocalPart(email) : u.Name, 200);
        var role = RoleMap.ResolveBest(u.Role, u.Roles, out _);
        var approvalLimit = u.BudgetLimit is > 0 ? u.BudgetLimit : null;
        var active = u.Active ?? true;
        var autonomy = u.HasApprovalAutonomy ?? false;

        var unitsAll = u.Units.Any(IsAll);
        var explicitUnits = new Dictionary<Guid, UnitRef>();
        var unresolvedUnits = new List<string>();
        foreach (var code in u.Units.Where(t => !IsAll(t)))
        {
            if (org.TryResolveUnit(code, out var unit)) explicitUnits[unit.UnitId] = unit;
            else unresolvedUnits.Add(code);
        }

        // Unit scope: "all" fans out to every unit; otherwise the explicitly resolved ones.
        var unitGrants = unitsAll ? org.AllUnits.ToList() : [.. explicitUnits.Values];

        // Nature grants resolve within the companies of the user's EXPLICIT units; with only "all"/no
        // explicit units, the primary company. Existing cost centers only — never provisioned here.
        var companiesForNatures = explicitUnits.Values.Select(g => g.CompanyId).Distinct().ToList();
        if (companiesForNatures.Count == 0 && org.PrimaryCompanyId is { } primary)
            companiesForNatures = [primary];

        var naturesAll = u.Natures.Any(IsAll);
        var natureIntents = u.Natures.Where(t => !IsAll(t)).Select(n => n.Trim()).Where(n => n.Length > 0).Distinct().ToList();

        var ccGrants = new Dictionary<Guid, CostCenterGrant>();
        var unresolvedNatures = new List<string>();
        if (naturesAll)
            foreach (var company in companiesForNatures)
                foreach (var cc in org.CostCentersOfCompany(company))
                    ccGrants[cc] = new CostCenterGrant(cc, company);

        foreach (var nature in natureIntents)
        {
            var any = false;
            foreach (var company in companiesForNatures)
                if (org.TryResolveCostCenter(company, nature, out var cc))
                {
                    ccGrants[cc] = new CostCenterGrant(cc, company);
                    any = true;
                }
            if (!any) unresolvedNatures.Add(nature);
        }

        // Company scope = the distinct companies the granted units / cost centers belong to (login reads this).
        var companyGrants = unitGrants.Select(g => g.CompanyId)
            .Concat(ccGrants.Values.Select(g => g.CompanyId))
            .Distinct().ToList();

        var hash = Storage.Hashing.Sha256Hex(string.Join('|',
            email, name, role, active, approvalLimit?.ToString(CultureInfo.InvariantCulture) ?? "", autonomy,
            string.Join(',', unitGrants.Select(g => g.UnitId).OrderBy(x => x)),
            string.Join(',', ccGrants.Keys.OrderBy(x => x)),
            string.Join(',', companyGrants.OrderBy(x => x)),
            u.CreatedAt?.ToString("O") ?? "", u.UpdatedAt?.ToString("O") ?? ""));

        return new MappedUser(
            email, name, role, active, approvalLimit, autonomy,
            unitGrants, [.. ccGrants.Values], companyGrants, unresolvedUnits, unresolvedNatures,
            u.CreatedAt, u.UpdatedAt, hash);
    }

    private static bool IsAll(string token) => string.Equals(token, AllSentinel, StringComparison.OrdinalIgnoreCase);

    private static string EmailLocalPart(string email)
    {
        var at = email.IndexOf('@');
        return at > 0 ? email[..at] : email;
    }

    private readonly record struct UpsertResult(Guid Id, bool Inserted);

    private readonly record struct CostCenterGrant(Guid CostCenterId, Guid CompanyId);

    private sealed record MappedUser(
        string Email, string Name, string Role, bool Active, decimal? ApprovalLimit, bool Autonomy,
        List<UnitRef> UnitGrants, List<CostCenterGrant> CostCenterGrants, List<Guid> CompanyGrants,
        List<string> UnresolvedUnits, List<string> UnresolvedNatures,
        DateTimeOffset? CreatedAt, DateTimeOffset? UpdatedAt, string Hash);
}
