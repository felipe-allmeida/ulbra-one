namespace Ulbra.One.Accounting.Application.CostCenters;

/// <summary>Minimal projection of a Core cost center used for cross-module validation and report labels.
/// Accounting references Core only by loose <c>Guid</c>; this keeps the Application layer free of a Core
/// dependency (the implementation lives in Infrastructure, which may reference Core.Domain).</summary>
public sealed record CostCenterInfo(Guid Id, Guid CompanyId, bool Active, string Code, string Name);

/// <summary>Port that resolves cost centers (Core module) by id — for the journal-line guard and for report
/// labels (Livro Razão CSV).</summary>
public interface ICostCenterLookup
{
    /// <summary>Returns the requested cost centers keyed by id; ids that don't exist are simply absent.</summary>
    Task<IReadOnlyDictionary<Guid, CostCenterInfo>> GetAsync(IReadOnlyCollection<Guid> ids, CancellationToken ct);
}
