namespace Ulbra.One.Finance.Application.Remittances;

/// <summary>
/// Per-environment NSA (file sequence number). Santander processes NSA 1-10 as a TEST file (manual note
/// G010): it passes the crítica but is never executed. Production needs NSA ≥ 11.
/// </summary>
public static class RemittanceNsa
{
    /// <summary>
    /// File NSA derived from the persisted (per-account, monotonic) sequence. Production: <c>seq + 10</c>
    /// (always ≥ 11). Test: cycles within 1-10 so the bank treats it as a test, without colliding with the
    /// unique index (the persisted sequence stays monotonic).
    /// </summary>
    public static int ForFile(int persistedSequence, bool isProduction) =>
        isProduction ? persistedSequence + 10 : ((persistedSequence - 1) % 10) + 1;
}
