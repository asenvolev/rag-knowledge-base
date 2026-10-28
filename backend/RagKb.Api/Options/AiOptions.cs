using System.ComponentModel.DataAnnotations;

namespace RagKb.Api.Options;

public enum AiProvider
{
    /// <summary>Google Gemini through its OpenAI-compatible endpoint. Has a free tier (data may be used by Google on the free tier).</summary>
    Gemini,

    /// <summary>OpenAI. Paid; API data is not used for training by default — the privacy-friendly option for customers.</summary>
    OpenAI
}

/// <summary>
/// One section for both providers. Both speak the OpenAI wire protocol, so the same SDK and code
/// path is used; only the endpoint, key and model names change. Empty model/endpoint values fall
/// back to the provider defaults below.
/// </summary>
public sealed class AiOptions
{
    public const string SectionName = "AI";

    public AiProvider Provider { get; set; } = AiProvider.Gemini;

    [Required(AllowEmptyStrings = false, ErrorMessage = "AI:ApiKey is required (AI__ApiKey env var).")]
    public string ApiKey { get; set; } = string.Empty;

    /// <summary>Override the API base URL (e.g. an Azure OpenAI or proxy endpoint). Empty = provider default.</summary>
    public string? Endpoint { get; set; }

    public string? ChatModel { get; set; }

    public string? EmbeddingModel { get; set; }

    /// <summary>
    /// Stored vector size. 1536 works for both providers and stays under pgvector's 2000-dim HNSW
    /// limit. Changing it after data exists requires recreating the document_chunks table.
    /// </summary>
    [Range(256, 2000)]
    public int EmbeddingDimensions { get; set; } = 1536;

    /// <summary>Chunks per embeddings request (Gemini accepts up to 100).</summary>
    [Range(1, 100)]
    public int EmbeddingBatchSize { get; set; } = 100;

    // ---- resolved values ----

    public string ResolvedEndpoint => !string.IsNullOrWhiteSpace(Endpoint)
        ? Endpoint!
        : Provider switch
        {
            AiProvider.Gemini => "https://generativelanguage.googleapis.com/v1beta/openai/",
            _ => "https://api.openai.com/v1"
        };

    public string ResolvedChatModel => !string.IsNullOrWhiteSpace(ChatModel)
        ? ChatModel!
        : Provider switch
        {
            AiProvider.Gemini => "gemini-3.8-flash",
            _ => "gpt-4o-mini"
        };

    public string ResolvedEmbeddingModel => !string.IsNullOrWhiteSpace(EmbeddingModel)
        ? EmbeddingModel!
        : Provider switch
        {
            AiProvider.Gemini => "gemini-embedding-001",
            _ => "text-embedding-3-small"
        };
}
