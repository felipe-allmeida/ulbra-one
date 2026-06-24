using Microsoft.Extensions.AI;
using Ulbra.One.Common.Application.Ai;
using Ulbra.One.Common.Infrastructure.Ai;

namespace Ulbra.One.UnitTests.Ai;

public class MeaiAiCompletionTests
{
    // Hand-written IChatClient fake (no mocking library, per repo convention).
    private sealed class FakeChatClient(Func<ChatResponse> responder) : IChatClient
    {
        public Task<ChatResponse> GetResponseAsync(
            IEnumerable<ChatMessage> messages, ChatOptions? options = null, CancellationToken ct = default)
            => Task.FromResult(responder());

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
        var sut = new MeaiAiCompletion(new FakeChatClient(() => Json("""{"Value":"ok"}""")));

        var result = await sut.CompleteAsync<Probe>(new AiPrompt("s", "u"));

        Assert.True(result.IsSuccess);
        Assert.Equal("ok", result.Value.Value);
    }

    [Fact]
    public async Task Unparseable_response_maps_to_SchemaMismatch()
    {
        var sut = new MeaiAiCompletion(new FakeChatClient(() => Json("not json at all")));

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
        var sut = new MeaiAiCompletion(new FakeChatClient(() => refused));

        var result = await sut.CompleteAsync<Probe>(new AiPrompt("s", "u"));

        Assert.False(result.IsSuccess);
        Assert.Equal(AiErrors.Refused, result.Error.Code);
    }

    [Fact]
    public async Task Provider_exception_maps_to_ProviderError()
    {
        var sut = new MeaiAiCompletion(new FakeChatClient(
            () => throw new InvalidOperationException("boom")));

        var result = await sut.CompleteAsync<Probe>(new AiPrompt("s", "u"));

        Assert.False(result.IsSuccess);
        Assert.Equal(AiErrors.ProviderError, result.Error.Code);
    }
}
