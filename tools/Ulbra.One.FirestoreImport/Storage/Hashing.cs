using System.Security.Cryptography;
using System.Text;

namespace Ulbra.One.FirestoreImport.Storage;

/// <summary>Content hashing for idempotency: a stable hash over a row's mapped target values decides
/// whether a re-run should skip (unchanged) or update (changed).</summary>
public static class Hashing
{
    public static string Sha256Hex(string value)
        => Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(value)));
}
