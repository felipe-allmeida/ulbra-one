using MassTransit;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Time.Testing;
using Ulbra.One.Common.Application.Persistence;
using Ulbra.One.Common.Infrastructure.Persistence;
using Ulbra.One.Messaging.Contracts;
using Ulbra.One.Notifications.Application.Consumers;
using Ulbra.One.Notifications.Domain.Entities;
using Xunit;

namespace Ulbra.One.UnitTests.Notifications;

public class PayablePendingApprovalConsumerTests
{
    private static readonly DateTimeOffset FixedNow = new(2026, 6, 17, 12, 0, 0, TimeSpan.Zero);

    private static PayablePendingApproval MakeMessage(Guid? payableId = null) => new()
    {
        PayableId = payableId ?? Guid.NewGuid(),
        CompanyId = Guid.NewGuid(),
        CostCenterId = Guid.NewGuid(),
        VendorName = "Acme Ltda",
        TotalAmount = 1234.56m,
    };

    private static NotificationsDbContext CreateDb()
    {
        var opts = new DbContextOptionsBuilder<NotificationsDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;
        return new NotificationsDbContext(opts);
    }

    [Fact]
    public async Task Consume_creates_notification_when_none_exists()
    {
        await using var db = CreateDb();
        IUnitOfWork uow = new EfUnitOfWork<NotificationsDbContext>(db);
        var clock = new FakeTimeProvider(FixedNow);
        var consumer = new PayablePendingApprovalConsumer(uow, clock);

        var msg = MakeMessage();
        await consumer.Consume(new FakeConsumeContext<PayablePendingApproval>(msg));

        var notifications = await db.Notifications.ToListAsync();
        Assert.Single(notifications);
        var n = notifications[0];
        Assert.Equal(msg.CompanyId, n.CompanyId);
        Assert.Equal("Admin", n.AudienceRole);
        Assert.Equal("PayablePendingApproval", n.Type);
        Assert.Equal(msg.PayableId, n.ReferenceId);
        Assert.Equal(FixedNow, n.OccurredAt);
        Assert.Contains("Acme Ltda", n.Message);
    }

    [Fact]
    public async Task Consume_is_idempotent_when_notification_already_exists()
    {
        var payableId = Guid.NewGuid();
        await using var db = CreateDb();

        // Pre-seed an existing notification for the same payable.
        db.Notifications.Add(new Notification(
            Guid.NewGuid(), "Admin", "PayablePendingApproval",
            "Conta pendente de aprovação", "existing", payableId, FixedNow));
        await db.SaveChangesAsync();

        IUnitOfWork uow = new EfUnitOfWork<NotificationsDbContext>(db);
        var clock = new FakeTimeProvider(FixedNow);
        var consumer = new PayablePendingApprovalConsumer(uow, clock);

        await consumer.Consume(new FakeConsumeContext<PayablePendingApproval>(MakeMessage(payableId)));

        // Should still be exactly one notification (no duplicate created).
        var count = await db.Notifications.CountAsync();
        Assert.Equal(1, count);
    }

    // ─── Minimal DbContext for InMemory testing ──────────────────────────────

    private sealed class NotificationsDbContext(DbContextOptions<NotificationsDbContext> opts) : DbContext(opts)
    {
        public DbSet<Notification> Notifications => Set<Notification>();
    }

    // ─── Minimal ConsumeContext stub ─────────────────────────────────────────

    /// <summary>
    /// Minimal stub — only <c>Message</c> and <c>CancellationToken</c> are used by the consumer.
    /// </summary>
    private sealed class FakeConsumeContext<T>(T message) : ConsumeContext<T> where T : class
    {
        public T Message => message;
        public CancellationToken CancellationToken => CancellationToken.None;

        // ── MessageContext ──────────────────────────────────────────────────
        public Guid? MessageId => null;
        public Guid? RequestId => null;
        public Guid? CorrelationId => null;
        public Guid? ConversationId => null;
        public Guid? InitiatorId => null;
        public DateTime? ExpirationTime => null;
        public DateTime? SentTime => null;
        public Uri? SourceAddress => null;
        public Uri? DestinationAddress => null;
        public Uri? ResponseAddress => null;
        public Uri? FaultAddress => null;
        public Headers Headers => throw new NotImplementedException();
        public HostInfo Host => throw new NotImplementedException();

        // ── PipeContext ─────────────────────────────────────────────────────
        public bool HasPayloadType(Type payloadType) => false;
        public bool TryGetPayload<TPayload>([System.Diagnostics.CodeAnalysis.MaybeNullWhen(false)] out TPayload payload) where TPayload : class { payload = null; return false; }
        public TPayload GetOrAddPayload<TPayload>(PayloadFactory<TPayload> payloadFactory) where TPayload : class => throw new NotImplementedException();
        public T1 AddOrUpdatePayload<T1>(PayloadFactory<T1> addFactory, UpdatePayloadFactory<T1> updateFactory) where T1 : class => throw new NotImplementedException();

        // ── ConsumeContext ──────────────────────────────────────────────────
        public ReceiveContext ReceiveContext => throw new NotImplementedException();
        public SerializerContext SerializerContext => throw new NotImplementedException();
        public Task ConsumeCompleted => Task.CompletedTask;
        public IEnumerable<string> SupportedMessageTypes => [];
        public bool HasMessageType(Type messageType) => false;
        public bool TryGetMessage<TMessage>([System.Diagnostics.CodeAnalysis.MaybeNullWhen(false)] out ConsumeContext<TMessage> consumeContext) where TMessage : class { consumeContext = null; return false; }
        public Task RespondAsync<TMessage>(TMessage message) where TMessage : class => throw new NotImplementedException();
        public Task RespondAsync<TMessage>(TMessage message, IPipe<SendContext<TMessage>> sendPipe) where TMessage : class => throw new NotImplementedException();
        public Task RespondAsync<TMessage>(object values) where TMessage : class => throw new NotImplementedException();
        public Task RespondAsync<TMessage>(object values, IPipe<SendContext<TMessage>> sendPipe) where TMessage : class => throw new NotImplementedException();
        public Task RespondAsync<TMessage>(TMessage message, IPipe<SendContext> sendPipe) where TMessage : class => throw new NotImplementedException();
        public Task RespondAsync(object message) => throw new NotImplementedException();
        public Task RespondAsync(object message, Type messageType) => throw new NotImplementedException();
        public Task RespondAsync(object message, IPipe<SendContext> sendPipe) => throw new NotImplementedException();
        public Task RespondAsync(object message, Type messageType, IPipe<SendContext> sendPipe) => throw new NotImplementedException();
        public Task RespondAsync<TMessage>(object values, IPipe<SendContext> sendPipe) where TMessage : class => throw new NotImplementedException();
        public void Respond<TMessage>(TMessage message) where TMessage : class => throw new NotImplementedException();
        public Task<ISendEndpoint> GetSendEndpoint(Uri address) => throw new NotImplementedException();
        public Task SetPendingFaultAddress(Uri address) => throw new NotImplementedException();
        public ConnectHandle ConnectPublishObserver(IPublishObserver observer) => throw new NotImplementedException();
        public Task Publish<TMessage>(TMessage message, CancellationToken cancellationToken = default) where TMessage : class => throw new NotImplementedException();
        public Task Publish<TMessage>(TMessage message, IPipe<PublishContext<TMessage>> publishPipe, CancellationToken cancellationToken = default) where TMessage : class => throw new NotImplementedException();
        public Task Publish<TMessage>(TMessage message, IPipe<PublishContext> publishPipe, CancellationToken cancellationToken = default) where TMessage : class => throw new NotImplementedException();
        public Task Publish(object message, CancellationToken cancellationToken = default) => throw new NotImplementedException();
        public Task Publish(object message, IPipe<PublishContext> publishPipe, CancellationToken cancellationToken = default) => throw new NotImplementedException();
        public Task Publish(object message, Type messageType, CancellationToken cancellationToken = default) => throw new NotImplementedException();
        public Task Publish(object message, Type messageType, IPipe<PublishContext> publishPipe, CancellationToken cancellationToken = default) => throw new NotImplementedException();
        public Task Publish<TMessage>(object values, CancellationToken cancellationToken = default) where TMessage : class => throw new NotImplementedException();
        public Task Publish<TMessage>(object values, IPipe<PublishContext<TMessage>> publishPipe, CancellationToken cancellationToken = default) where TMessage : class => throw new NotImplementedException();
        public Task Publish<TMessage>(object values, IPipe<PublishContext> publishPipe, CancellationToken cancellationToken = default) where TMessage : class => throw new NotImplementedException();
        public ConnectHandle ConnectSendObserver(ISendObserver observer) => throw new NotImplementedException();
        public Task NotifyConsumed(TimeSpan duration, string consumerType) => Task.CompletedTask;
        public Task NotifyFaulted(TimeSpan duration, string consumerType, Exception exception) => Task.CompletedTask;
        public Task NotifyConsumed<TMessage>(ConsumeContext<TMessage> context, TimeSpan duration, string consumerType) where TMessage : class => Task.CompletedTask;
        public Task NotifyFaulted<TMessage>(ConsumeContext<TMessage> context, TimeSpan duration, string consumerType, Exception exception) where TMessage : class => Task.CompletedTask;
        public void AddConsumeTask(Task task) { }
        public Task<ConsumeContext> PushContext<T1>(T1 obj) where T1 : class => throw new NotImplementedException();
        public Task PushContext<T1>(T1 obj, IPipe<ConsumeContext<T1>> pipe, CancellationToken ct = default) where T1 : class => throw new NotImplementedException();
    }
}
