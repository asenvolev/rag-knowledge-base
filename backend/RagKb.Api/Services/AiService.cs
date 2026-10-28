using System.ClientModel;
using Microsoft.Extensions.Options;
using OpenAI.Chat;
using OpenAI.Embeddings;
using RagKb.Api.Infrastructure;
using RagKb.Api.Options;

namespace RagKb.Api.Services;

public interface IEmbeddingService
{
    /// <summary>Identifies the vector space; chunks embedded by another model are not comparable.</summary>
    string ModelId { get; }
    Task<IReadOnlyList<ReadOnlyMemory<float>>> EmbedAsync(IReadOnlyList<string> inputs, CancellationToken ct);
    Task<ReadOnlyMemory<float>> EmbedAsync(string input, CancellationToken ct);
}

public interface IChatService
{
    Task<(string Answer, int? PromptTokens, int? CompletionTokens)> CompleteAsync(
        IReadOnlyList<ChatMessage> messages, CancellationToken ct);
}

/// <summary>
/// Embeddings + chat over the OpenAI wire protocol. Works with OpenAI directly and with Google
/// Gemini's OpenAI-compatible endpoint; Program.cs points the SDK clients at the right base URL.
/// The SDK already retries 429/5xx with exponential back-off.
/// </summary>
public sealed class AiService(
    EmbeddingClient embeddingClient,
    ChatClient chatClient,
    IOptions<AiOptions> options,
    ILogger<AiService> logger) : IEmbeddingService, IChatService
{
    private readonly AiOptions _options = options.Value;
    private string ProviderName => _options.Provider.ToString();

    public string ModelId => $"{_options.Provider}:{_options.ResolvedEmbeddingModel}:{_options.EmbeddingDimensions}".ToLowerInvariant();

    public async Task<ReadOnlyMemory<float>> EmbedAsync(string input, CancellationToken ct)
        => (await EmbedAsync([input], ct))[0];

    public async Task<IReadOnlyList<ReadOnlyMemory<float>>> EmbedAsync(IReadOnlyList<string> inputs, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(inputs);
        if (inputs.Count == 0) return [];

        var dims = _options.EmbeddingDimensions;
        var result = new ReadOnlyMemory<float>[inputs.Count];
        var embeddingOptions = new EmbeddingGenerationOptions { Dimensions = dims };

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
                    result[offset + item.Index] = FitToDimensions(item.ToFloats(), dims);
                }
            }
            catch (ClientResultException ex)
            {
                logger.LogError(ex, "{Provider} embeddings call failed with status {Status}", ProviderName, ex.Status);
                throw new AiProviderException(MapError(ex, "embedding"), ex);
            }
        }

        for (var i = 0; i < result.Length; i++)
        {
            if (result[i].Length != dims)
                throw new AiProviderException($"Embedding #{i} has {result[i].Length} dimensions, expected {dims}.");
        }

        return result;
    }

    /// <summary>
    /// gemini-embedding-001 and text-embedding-3 are Matryoshka models: the first N values of a longer
    /// vector are a valid N-dim embedding once re-normalised. If a provider ignores the requested
    /// size, truncate + L2-normalise instead of failing.
    /// </summary>
    internal static ReadOnlyMemory<float> FitToDimensions(ReadOnlyMemory<float> vector, int dims)
    {
        if (vector.Length <= dims) return vector;

        var truncated = vector[..dims].ToArray();
        double sumSquares = 0;
        foreach (var v in truncated) sumSquares += (double)v * v;
        var norm = (float)Math.Sqrt(sumSquares);
        if (norm > 0)
        {
            for (var i = 0; i < truncated.Length; i++) truncated[i] /= norm;
        }
        return truncated;
    }

    public async Task<(string Answer, int? PromptTokens, int? CompletionTokens)> CompleteAsync(
        IReadOnlyList<ChatMessage> messages, CancellationToken ct)
    {
        var chatOptions = new ChatCompletionOptions { Temperature = 0.1f };

        if (_options.Provider == AiProvider.Gemini)
        {
            // Gemini 3 models always "think"; thinking tokens count against output limits, so we set a
            // low effort instead of a hard token cap (a cap can cut the answer to nothing).
#pragma warning disable OPENAI001 // ReasoningEffortLevel is marked experimental in the SDK
            chatOptions.ReasoningEffortLevel = ChatReasoningEffortLevel.Low;
#pragma warning restore OPENAI001
        }
        else
        {
            chatOptions.MaxOutputTokenCount = 1000;
        }

        try
        {
            ChatCompletion completion = (await chatClient.CompleteChatAsync(messages, chatOptions, ct)).Value;

            var answer = string.Concat(completion.Content
                .Where(p => p.Kind == ChatMessageContentPartKind.Text)
                .Select(p => p.Text)).Trim();

            if (completion.FinishReason == ChatFinishReason.ContentFilter)
                answer = "The response was blocked by the provider's content filter.";
            else if (answer.Length == 0)
                answer = "The model returned an empty answer. Please try again.";

            return (answer, completion.Usage?.InputTokenCount, completion.Usage?.OutputTokenCount);
        }
        catch (ClientResultException ex)
        {
            logger.LogError(ex, "{Provider} chat call failed with status {Status}", ProviderName, ex.Status);
            throw new AiProviderException(MapError(ex, "chat"), ex);
        }
    }

    private string MapError(ClientResultException ex, string operation)
    {
        var p = ProviderName;
        return ex.Status switch
        {
            401 or 403 => $"{p} rejected the API key. Check the AI__ApiKey setting.",
            // Gemini reports a bad key as 400 "API key not valid".
            400 when ex.Message.Contains("API key", StringComparison.OrdinalIgnoreCase)
                => $"{p} rejected the API key. Check the AI__ApiKey setting.",
            404 => $"{p} does not recognise the configured {operation} model. Check AI__ChatModel / AI__EmbeddingModel.",
            429 => $"{p} rate limit or free-tier quota reached. Wait a minute and try again.",
            >= 500 => $"{p} {operation} service is temporarily unavailable.",
            _ => $"{p} {operation} request failed (HTTP {ex.Status})."
        };
    }
}
