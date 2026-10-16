using Ulbra.One.Audit.Domain;
using Ulbra.One.Audit.Domain.Entities;
using Xunit;

namespace Ulbra.One.UnitTests.Audit;

public class AuditLogTests
{
    [Fact]
    public void Ctor_sets_all_fields()
    {
        var at = new DateTimeOffset(2026, 6, 16, 12, 0, 0, TimeSpan.Zero);
        var log = new AuditLog(AuditAction.Update, "Payable", "finance", "payables", "p1",
            oldValues: "{\"Status\":\"Approved\"}", newValues: "{\"Status\":\"Settled\"}",
            affectedColumns: "[\"Status\"]", actorUserId: "u1", traceId: "trace-abc", occurredAt: at);

        Assert.NotEqual(Guid.Empty, log.Id);
        Assert.Equal(AuditAction.Update, log.Action);
        Assert.Equal("Payable", log.EntityType);
        Assert.Equal("finance", log.Schema);
        Assert.Equal("payables", log.TableName);
        Assert.Equal("p1", log.PrimaryKey);
        Assert.Equal("[\"Status\"]", log.AffectedColumns);
        Assert.Equal("u1", log.ActorUserId);
        Assert.Equal("trace-abc", log.TraceId);
        Assert.Equal(at, log.OccurredAt);
    }
}
