using Ulbra.One.Registry.Application.Lookups;
using System.Text.Json;

namespace Ulbra.One.Registry.Infrastructure.Lookups;

/// <summary>BrasilAPI (https://brasilapi.com.br/api/cnpj/v1/{cnpj}). Best-effort: null on error/not-found.</summary>
public sealed class BrasilApiCnpjLookup(HttpClient http) : ICnpjLookup
{
    public async Task<CnpjResult?> FindAsync(string cnpj, CancellationToken ct)
    {
        var digits = new string(cnpj.Where(char.IsDigit).ToArray());
        if (digits.Length != 14) return null;

        try
        {
            using var res = await http.GetAsync($"/api/cnpj/v1/{digits}", ct);
            if (!res.IsSuccessStatusCode) return null;
            using var doc = JsonDocument.Parse(await res.Content.ReadAsStringAsync(ct));
            var r = doc.RootElement;

            string Get(string name) => r.TryGetProperty(name, out var v) ? v.GetString() ?? "" : "";

            return new CnpjResult(
                Get("razao_social"),
                r.TryGetProperty("nome_fantasia", out var nf) ? nf.GetString() : null,
                new CnpjAddress(Get("cep"), Get("logradouro"), Get("numero"), Get("bairro"), Get("municipio"), Get("uf")));
        }
        catch { return null; }
    }
}
