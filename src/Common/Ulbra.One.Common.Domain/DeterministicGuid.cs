using System.Security.Cryptography;
using System.Text;

namespace Ulbra.One.Common.Domain;

/// <summary>Derives a stable Guid from a string (UUIDv5-style: SHA-1 of the UTF-8 bytes, first 16 bytes with the
/// version/variant nibbles set). Gives time-based events/notifications a deterministic idempotency key, so the
/// same logical occurrence maps to one Guid across retries and periodic re-runs.</summary>
public static class DeterministicGuid
{
    public static Guid From(string value)
    {
        var hash = SHA1.HashData(Encoding.UTF8.GetBytes(value));
        Span<byte> bytes = stackalloc byte[16];
        hash.AsSpan(0, 16).CopyTo(bytes);
        bytes[6] = (byte)((bytes[6] & 0x0F) | 0x50); // version 5
        bytes[8] = (byte)((bytes[8] & 0x3F) | 0x80); // RFC 4122 variant
        return new Guid(bytes);
    }
}
