namespace Ulbra.One.Finance.Infrastructure.Messaging;

/// <summary>Thrown by PayableReceivedConsumer when classification produced nothing — signals MassTransit
/// to retry/redeliver (escalating) until data/AI is available or the attempt budget is exhausted.</summary>
public sealed class ClassificationUnavailableException(Guid payableId)
    : Exception($"Could not classify payable {payableId}; retrying.");
