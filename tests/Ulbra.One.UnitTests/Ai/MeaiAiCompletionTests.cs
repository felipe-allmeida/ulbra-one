using Microsoft.Extensions.AI;
using Ulbra.One.Common.Application.Ai;
using Ulbra.One.Common.Infrastructure.Ai;

namespace Ulbra.One.UnitTests.Ai;

public class MeaiAiCompletionTests
{
    // Hand-written IChatClient fake (no mocking library, per repo convention).
    private sealed class FakeChatClient(Func<IEnumerable<ChatMessage>, ChatResponse> responder) : IChatClient
    {
        public Task<ChatResponse> GetResponseAsync(
            IEnumerable<ChatMessage> messages, ChatOptions? options = null, CancellationToken ct = default)
            => Task.FromResult(responder(messages));

        public IAsyncEnumerable<ChatResponseUpdate> GetStreamingResponseAsync(
            IEnumerable<ChatMessage> messages, ChatOptions? options = null, CancellationToken ct = default)
            => throw new NotSupportedException();

        public object? GetService(Type serviceType, object? serviceKey = null) => null;
        public void Dispose() { }
    }

    private static ChatResponse Json(string text) =>
        new(new ChatMessage(ChatRole.Assistant, text));

    [Fact]
    public async Task Valid_json_maps_to_success()
    {
        var sut = new MeaiAiCompletion(new FakeChatClient(_ =>Json("""{"Value":"ok"}""")));

        var result = await sut.CompleteAsync<Probe>(new AiPrompt("s", "u"));

        Assert.True(result.IsSuccess);
        Assert.Equal("ok", result.Value.Value);
    }

    [Fact]
    public async Task Unparseable_response_maps_to_SchemaMismatch()
    {
        var sut = new MeaiAiCompletion(new FakeChatClient(_ =>Json("not json at all")));

        var result = await sut.CompleteAsync<Probe>(new AiPrompt("s", "u"));

        Assert.False(result.IsSuccess);
        Assert.Equal(AiErrors.SchemaMismatch, result.Error.Code);
    }

    [Fact]
    public async Task Content_filter_finish_reason_maps_to_Refused()
    {
        var refused = new ChatResponse(new ChatMessage(ChatRole.Assistant, ""))
        {
            FinishReason = ChatFinishReason.ContentFilter,
        };
        var sut = new MeaiAiCompletion(new FakeChatClient(_ =>refused));

        var result = await sut.CompleteAsync<Probe>(new AiPrompt("s", "u"));

        Assert.False(result.IsSuccess);
        Assert.Equal(AiErrors.Refused, result.Error.Code);
    }

    [Fact]
    public async Task Provider_exception_maps_to_ProviderError()
    {
        var sut = new MeaiAiCompletion(new FakeChatClient(
            _ => throw new InvalidOperationException("boom")));

        var result = await sut.CompleteAsync<Probe>(new AiPrompt("s", "u"));

        Assert.False(result.IsSuccess);
        Assert.Equal(AiErrors.ProviderError, result.Error.Code);
    }

    [Fact]
    public async Task Cancellation_propagates_and_is_not_mapped_to_ProviderError()
    {
        var sut = new MeaiAiCompletion(new FakeChatClient(_ => throw new OperationCanceledException()));

        await Assert.ThrowsAsync<OperationCanceledException>(
            () => sut.CompleteAsync<Probe>(new AiPrompt("s", "u")));
    }

    [Fact]
    public async Task Attachment_is_sent_as_DataContent()
    {
        IReadOnlyList<ChatMessage>? captured = null;
        var fake = new FakeChatClient(msgs => { captured = msgs.ToList(); return Json("""{"Value":"ok"}"""); });
        var sut = new MeaiAiCompletion(fake);

        var pdf = new byte[] { 1, 2, 3 };
        var prompt = new AiPrompt("sys", "read this", [new AiAttachment(pdf, "application/pdf")]);
        var result = await sut.CompleteAsync<Probe>(prompt);

        Assert.True(result.IsSuccess);
        var userMsg = captured!.Single(m => m.Role == ChatRole.User);
#pragma warning disable MEAI001
        Assert.Contains(userMsg.Contents, c => c is DataContent d && d.MediaType == "application/pdf");
#pragma warning restore MEAI001
    }
}
