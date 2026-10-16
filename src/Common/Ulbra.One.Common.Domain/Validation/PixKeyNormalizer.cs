namespace Ulbra.One.Common.Domain.Validation;

/// <summary>Normaliza a chave PIX à sua forma essencial conforme o formato detectado:
/// e-mail só recebe trim; chave aleatória (EVP/UUID) fica intacta; CPF/CNPJ/celular
/// ficam só com dígitos (o que também remove o '+' do celular).</summary>
public static class PixKeyNormalizer
{
    public static string Normalize(string? key)
    {
        var s = (key ?? string.Empty).Trim();
        if (s.Length == 0) return string.Empty;
        if (s.Contains('@')) return s;                   // e-mail → só trim
        if (Guid.TryParseExact(s, "D", out _)) return s; // chave aleatória → intacta
        return DocumentValidator.Normalize(s);           // celular/CPF/CNPJ → só dígitos
    }
}
