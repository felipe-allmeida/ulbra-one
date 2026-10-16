using Microsoft.Extensions.Logging;
using Ulbra.One.Common.Domain.Validation;
using Ulbra.One.Registry.Application.Lookups;
using Ulbra.One.Registry.Domain.Entities;
using System.Net;
using System.Text.Json;

namespace Ulbra.One.Registry.Infrastructure.Lookups;

/// <summary>BrasilAPI CNPJ lookup — typed outcome so a real 404 is distinct from an upstream failure (logged).</summary>
public sealed class BrasilApiCnpjLookup(HttpClient http, ILogger<BrasilApiCnpjLookup> logger) : ICnpjLookup
{
    public async Task<CnpjLookupOutcome> FindAsync(string cnpj, CancellationToken ct)
    {
        var normalized = DocumentValidator.NormalizeCnpj(cnpj);
        if (normalized.Length != 14) return CnpjLookupOutcome.NotFound;

        try
        {
            using var res = await http.GetAsync($"/api/cnpj/v1/{normalized}", ct);
            if (res.StatusCode == HttpStatusCode.NotFound) return CnpjLookupOutcome.NotFound;
            if (!res.IsSuccessStatusCode)
            {
                logger.LogWarning("BrasilAPI CNPJ lookup returned {StatusCode} for {Cnpj}", (int)res.StatusCode, normalized);
                return CnpjLookupOutcome.Unavailable;
            }

            using var doc = JsonDocument.Parse(await res.Content.ReadAsStringAsync(ct));
            var r = doc.RootElement;

            string Get(string name) => r.TryGetProperty(name, out var v) ? v.GetString() ?? "" : "";

            // CNAE fiscal is a fixed 7-digit code; zero-pad so section A/B codes keep their leading zero.
            var cnaeCode = r.TryGetProperty("cnae_fiscal", out var cn) && cn.ValueKind == JsonValueKind.Number
                ? cn.GetInt64().ToString("D7")
                : null;
            var cnaeDesc = r.TryGetProperty("cnae_fiscal_descricao", out var cd) ? cd.GetString() : null;

            return CnpjLookupOutcome.Found(new CnpjResult(
                Get("razao_social"),
                r.TryGetProperty("nome_fantasia", out var nf) ? nf.GetString() : null,
                new CnpjAddress(Get("cep"), Get("logradouro"), Get("numero"), Get("bairro"), Get("municipio"), Get("uf")),
                MapTaxRegime(r),
                string.IsNullOrWhiteSpace(cnaeCode) ? null : cnaeCode,
                string.IsNullOrWhiteSpace(cnaeDesc) ? null : cnaeDesc));
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "BrasilAPI CNPJ lookup failed for {Cnpj}", normalized);
            return CnpjLookupOutcome.Unavailable;
        }
    }

    // Best-effort map of BrasilAPI tax fields to our TaxRegime; null when the data doesn't clearly indicate
    // one (Simples/MEI opt-in is often null, and regime_tributario reflects ECF filings — it won't cover
    // Simples). The caller uses it only as a form default, never overwriting a user choice.
    private static TaxRegime? MapTaxRegime(JsonElement r)
    {
        if (GetNullableBool(r, "opcao_pelo_mei") == true) return TaxRegime.Mei;
        if (GetNullableBool(r, "opcao_pelo_simples") == true) return TaxRegime.SimplesNacional;

        if (!r.TryGetProperty("regime_tributario", out var rt) || rt.ValueKind != JsonValueKind.Array
            || rt.GetArrayLength() == 0)
            return null;

        // The most recent year wins.
        JsonElement latest = default;
        var maxYear = int.MinValue;
        var found = false;
        foreach (var e in rt.EnumerateArray())
        {
            var year = e.TryGetProperty("ano", out var a) && a.ValueKind == JsonValueKind.Number && a.TryGetInt32(out var y)
                ? y : int.MinValue;
            if (year >= maxYear) { maxYear = year; latest = e; found = true; }
        }
        var forma = found && latest.TryGetProperty("forma_de_tributacao", out var f) ? f.GetString() : null;
        return forma?.Trim().ToUpperInvariant() switch
        {
            "LUCRO REAL" => TaxRegime.LucroReal,
            "LUCRO PRESUMIDO" => TaxRegime.LucroPresumido,
            "SIMPLES NACIONAL" => TaxRegime.SimplesNacional,
            _ => null, // ISENTO / IMUNE / ARBITRADO / unknown → leave for manual selection
        };
    }

    private static bool? GetNullableBool(JsonElement r, string name) =>
        r.TryGetProperty(name, out var v) && v.ValueKind is JsonValueKind.True or JsonValueKind.False
            ? v.GetBoolean()
            : null;
}
