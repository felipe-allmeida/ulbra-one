namespace Ulbra.One.Registry.Application.Lookups;

public interface ICnpjLookup
{
    Task<CnpjResult?> FindAsync(string cnpj, CancellationToken ct);
}
