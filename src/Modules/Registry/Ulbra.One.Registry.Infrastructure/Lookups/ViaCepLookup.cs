using System.Text.Json;

namespace Ulbra.One.Registry.Infrastructure.Lookups;

/// <summary>ViaCEP (https://viacep.com.br/ws/{cep}/json/). Best-effort: null on error/not-found.</summary>
public sealed class ViaCepLookup(HttpClient http) : ICepLookup
{
    public async Task<CepResult?> FindAsync(string cep, CancellationToken ct)
    {
        var digits = new string(cep.Where(char.IsDigit).ToArray());
        if (digits.Length != 8) return null;

        try
        {
            using var res = await http.GetAsync($"/ws/{digits}/json/", ct);
            if (!res.IsSuccessStatusCode) return null;
            using var doc = JsonDocument.Parse(await res.Content.ReadAsStringAsync(ct));
            var root = doc.RootElement;
            if (root.TryGetProperty("erro", out var erro) && erro.ValueKind == JsonValueKind.True) return null;

            return new CepResult(
                digits,
                root.GetProperty("logradouro").GetString() ?? "",
                root.GetProperty("bairro").GetString() ?? "",
                root.GetProperty("localidade").GetString() ?? "",
                root.GetProperty("uf").GetString() ?? "");
        }
        catch { return null; }
    }
}
