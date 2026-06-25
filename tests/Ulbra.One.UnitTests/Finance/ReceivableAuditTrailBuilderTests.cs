using Ulbra.One.Finance.Application.AuditTrail;
using Ulbra.One.Finance.Domain.Entities;

namespace Ulbra.One.UnitTests.Finance;

public class ReceivableAuditTrailBuilderTests
{
    private static readonly DateTimeOffset T0 = new(2026, 6, 20, 10, 0, 0, TimeSpan.Zero);

    [Fact]
    public void Build_emits_created_then_cancelled_with_actor_and_reason()
    {
        var actor = Guid.NewGuid();
        var receivable = ReceivableTestFactory.Cancelled(
            createdActorId: "system", createdAt: T0, cancelledById: actor, cancelledAt: T0.AddDays(1), reason: "acordo");
        var names = new Dictionary<Guid, string> { [actor] = "Ana" };

        var events = ReceivableAuditTrailBuilder.Build(receivable, names);

        Assert.Collection(events,
            e => Assert.Equal("Created", e.Type),
            e =>
            {
                Assert.Equal("Cancelled", e.Type);
                Assert.Equal("Ana", e.ActorName);
                Assert.Equal("acordo", e.Metadata["reason"]);
                Assert.Equal(T0.AddDays(1), e.Timestamp);
            });
    }
}
