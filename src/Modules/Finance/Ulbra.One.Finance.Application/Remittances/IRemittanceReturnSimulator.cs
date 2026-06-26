namespace Ulbra.One.Finance.Application.Remittances;

/// <summary>
/// Development-only service that synthesises a CNAB240 return (.RET) file from an in-flight remittance,
/// marking all its Pending lines as paid. Used to drive end-to-end testing of the return conciliation flow
/// without a real bank return file.
///
/// Implementations are gated behind an environment check in the endpoint itself — this service is only
/// called when the application is running in Development mode.
/// </summary>
public interface IRemittanceReturnSimulator
{
    /// <summary>
    /// Builds a synthetic .RET file from the given remittance's Pending lines, each marked with the
    /// "BD" (Santander: Pagamento Efetuado) occurrence code and today's date, matching the actual line amount.
    /// The file round-trips through <see cref="ICnabReturnParser"/> → <see cref="IReturnOutcomeResolver"/>
    /// with outcome = Paid for every line.
    /// </summary>
    /// <param name="remittanceId">Id of the remittance to simulate a return for.</param>
    /// <param name="today">Simulated effective payment date (typically today).</param>
    /// <returns>
    /// Raw CNAB240 bytes encoded in Latin-1, or <c>null</c> if the remittance is not found or has no Pending lines.
    /// </returns>
    Task<(byte[] Bytes, string FileName)?> SimulateAsync(Guid remittanceId, DateOnly today, CancellationToken ct);
}
