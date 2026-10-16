using Microsoft.Extensions.Time.Testing;
using Ulbra.One.Common.Domain.Auditing;
using Ulbra.One.Common.Infrastructure.Auditing;

namespace Ulbra.One.UnitTests.Auditing;

public class AuditInterceptorTests
{
    private sealed class Sample : IAuditable
    {
        public DateTimeOffset CreatedAt { get; private set; }
        public string CreatedBy { get; private set; } = "";
        public DateTimeOffset UpdatedAt { get; private set; }
        public string UpdatedBy { get; private set; } = "";
        public void MarkCreated(string userId, DateTimeOffset at) { CreatedBy = userId; CreatedAt = at; MarkUpdated(userId, at); }
        public void MarkUpdated(string userId, DateTimeOffset at) { UpdatedBy = userId; UpdatedAt = at; }
    }

    [Fact]
    public void ApplyCreated_Sets_All_Fields_From_TimeProvider()
    {
        var time = new FakeTimeProvider(new DateTimeOffset(2026, 6, 1, 12, 0, 0, TimeSpan.Zero));
        var entity = new Sample();

        AuditInterceptor.ApplyCreated(entity, "user-123", time);

        Assert.Equal("user-123", entity.CreatedBy);
        Assert.Equal(time.GetUtcNow(), entity.CreatedAt);
        Assert.Equal(time.GetUtcNow(), entity.UpdatedAt);
    }

    [Fact]
    public void ApplyUpdated_Touches_Only_Updated_Fields()
    {
        var time = new FakeTimeProvider(new DateTimeOffset(2026, 6, 1, 12, 0, 0, TimeSpan.Zero));
        var entity = new Sample();
        AuditInterceptor.ApplyCreated(entity, "creator", time);

        time.Advance(TimeSpan.FromHours(1));
        AuditInterceptor.ApplyUpdated(entity, "editor", time);

        Assert.Equal("creator", entity.CreatedBy);
        Assert.Equal("editor", entity.UpdatedBy);
        Assert.Equal(time.GetUtcNow(), entity.UpdatedAt);
    }
}
