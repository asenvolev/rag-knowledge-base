using System.ComponentModel.DataAnnotations;

namespace RagKb.Api.Options;

public sealed class OpenAiOptions
{
    public const string SectionName = "OpenAI";

    [Required(AllowEmptyStrings = false)]
    public string ApiKey { get; init; } = string.Empty;

    [Required]
    public string EmbeddingModel { get; init; } = "text-embedding-3-small";

    [Required]
    public string ChatModel { get; init; } = "gpt-4o-mini";

    /// <summary>text-embedding-3-small returns 1536 dimensions. Must match the vector(N) column.</summary>
    [Range(256, 3072)]
    public int EmbeddingDimensions { get; init; } = 1536;

    /// <summary>How many chunks to send to the embeddings endpoint per request.</summary>
    [Range(1, 2048)]
    public int EmbeddingBatchSize { get; init; } = 100;
}
