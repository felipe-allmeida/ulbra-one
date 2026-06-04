namespace Ulbra.One.Registry.Infrastructure.Lookups;

public interface ICnpjLookup
{
    Task<CnpjResult?> FindAsync(string cnpj, CancellationToken ct);
}
