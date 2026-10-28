using System.ClientModel;
using Microsoft.Extensions.Options;
using OpenAI.Chat;
using OpenAI.Embeddings;
using RagKb.Api.Infrastructure;
using RagKb.Api.Options;

namespace RagKb.Api.Services;

public interface IEmbeddingService
{
    Task<IReadOnlyList<ReadOnlyMemory<float>>> EmbedAsync(IReadOnlyList<string> inputs, CancellationToken ct);
    Task<ReadOnlyMemory<float>> EmbedAsync(string input, CancellationToken ct);
}

public interface IChatService
{
    Task<(string Answer, int? PromptTokens, int? CompletionTokens)> CompleteAsync(
        IReadOnlyList<ChatMessage> messages, CancellationToken ct);
}

/// <summary>
/// Thin wrapper over the official OpenAI .NET SDK (the same SDK Azure.AI.OpenAI and Semantic Kernel
/// build on). Swapping to Azure OpenAI is a one-line change in Program.cs (AzureOpenAIClient).
/// The SDK already retries 429/5xx with exponential back-off.
/// </summary>
public sealed class OpenAiService(
    EmbeddingClient embeddingClient,
    ChatClient chatClient,
    IOptions<OpenAiOptions> options,
    ILogger<OpenAiService> logger) : IEmbeddingService, IChatService
{
    private readonly OpenAiOptions _options = options.Value;

    public async Task<ReadOnlyMemory<float>> EmbedAsync(string input, CancellationToken ct)
        => (await EmbedAsync([input], ct))[0];

    public async Task<IReadOnlyList<ReadOnlyMemory<float>>> EmbedAsync(IReadOnlyList<string> inputs, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(inputs);
        if (inputs.Count == 0) return [];

        var result = new ReadOnlyMemory<float>[inputs.Count];
        var embeddingOptions = new EmbeddingGenerationOptions { Dimensions = _options.EmbeddingDimensions };

        for (var offset = 0; offset < inputs.Count; offset += _options.EmbeddingBatchSize)
        {
            var batch = inputs.Skip(offset).Take(_options.EmbeddingBatchSize).ToList();
            try
            {
                OpenAIEmbeddingCollection response =
                    (await embeddingClient.GenerateEmbeddingsAsync(batch, embeddingOptions, ct)).Value;

                // The API returns items with an Index; never assume ordering.
                foreach (var item in response)
                {
                    result[offset + item.Index] = item.ToFloats();
                }
            }
            catch (ClientResultException ex)
            {
                logger.LogError(ex, "OpenAI embeddings call failed with status {Status}", ex.Status);
                throw new AiProviderException(MapError(ex, "embedding"), ex);
            }
        }

        for (var i = 0; i < result.Length; i++)
        {
            if (result[i].Length != _options.EmbeddingDimensions)
                throw new AiProviderException($"Embedding #{i} has {result[i].Length} dimensions, expected {_options.EmbeddingDimensions}.");
        }

        return result;
    }

    public async Task<(string Answer, int? PromptTokens, int? CompletionTokens)> CompleteAsync(
        IReadOnlyList<ChatMessage> messages, CancellationToken ct)
    {
        var chatOptions = new ChatCompletionOptions
        {
            Temperature = 0.1f,
            MaxOutputTokenCount = 1000
        };

        try
        {
            ChatCompletion completion = (await chatClient.CompleteChatAsync(messages, chatOptions, ct)).Value;

            var answer = string.Concat(completion.Content
                .Where(p => p.Kind == ChatMessageContentPartKind.Text)
                .Select(p => p.Text)).Trim();

            if (completion.FinishReason == ChatFinishReason.ContentFilter)
                answer = "The response was blocked by the provider's content filter.";

            return (answer, completion.Usage?.InputTokenCount, completion.Usage?.OutputTokenCount);
        }
        catch (ClientResultException ex)
        {
            logger.LogError(ex, "OpenAI chat call failed with status {Status}", ex.Status);
            throw new AiProviderException(MapError(ex, "chat"), ex);
        }
    }

    private static string MapError(ClientResultException ex, string operation) => ex.Status switch
    {
        401 => "OpenAI rejected the API key. Check OpenAI__ApiKey.",
        429 => "OpenAI rate limit or quota exceeded. Try again shortly or check billing.",
        >= 500 => $"OpenAI {operation} service is temporarily unavailable.",
        _ => $"OpenAI {operation} request failed (HTTP {ex.Status})."
    };
}
