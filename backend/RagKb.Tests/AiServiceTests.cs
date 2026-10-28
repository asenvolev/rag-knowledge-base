using RagKb.Api.Options;
using RagKb.Api.Services;
using Xunit;

namespace RagKb.Tests;

public class AiServiceTests
{
    [Fact]
    public void FitToDimensions_keeps_vectors_of_the_right_size()
    {
        var v = new float[] { 0.6f, 0.8f };
        Assert.Equal(v, AiService.FitToDimensions(v, 2).ToArray());
    }

    [Fact]
    public void FitToDimensions_truncates_and_renormalises_longer_vectors()
    {
        var v = new float[] { 3f, 4f, 100f, -50f };
        var fitted = AiService.FitToDimensions(v, 2).ToArray();

        Assert.Equal(2, fitted.Length);
        Assert.Equal(0.6f, fitted[0], 5);
        Assert.Equal(0.8f, fitted[1], 5);
    }

    [Theory]
    [InlineData(AiProvider.Gemini, "gemini-embedding-001", "generativelanguage.googleapis.com")]
    [InlineData(AiProvider.OpenAI, "text-embedding-3-small", "api.openai.com")]
    public void Provider_defaults_resolve(AiProvider provider, string embeddingModel, string host)
    {
        var o = new AiOptions { Provider = provider, ApiKey = "x" };
        Assert.Equal(embeddingModel, o.ResolvedEmbeddingModel);
        Assert.Contains(host, o.ResolvedEndpoint);
        Assert.False(string.IsNullOrWhiteSpace(o.ResolvedChatModel));
    }

    [Fact]
    public void Explicit_models_override_defaults()
    {
        var o = new AiOptions { Provider = AiProvider.Gemini, ChatModel = "gemini-custom", Endpoint = "https://proxy.local/v1" };
        Assert.Equal("gemini-custom", o.ResolvedChatModel);
        Assert.Equal("https://proxy.local/v1", o.ResolvedEndpoint);
    }
}
