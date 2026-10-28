using System.ComponentModel.DataAnnotations;

namespace RagKb.Api.Options;

public sealed class RagOptions
{
    public const string SectionName = "Rag";

    [Range(200, 8000)]
    public int ChunkSize { get; init; } = 1000;

    [Range(0, 4000)]
    public int ChunkOverlap { get; init; } = 200;

    [Range(1, 20)]
    public int TopK { get; init; } = 5;

    /// <summary>Chunks below this cosine similarity are ignored (0..1).</summary>
    [Range(0.0, 1.0)]
    public double MinSimilarity { get; init; } = 0.25;

    [Range(1, 100)]
    public int MaxUploadSizeMb { get; init; } = 20;

    [Range(0, 20)]
    public int MaxHistoryMessages { get; init; } = 6;
}

public sealed class SecurityOptions
{
    public const string SectionName = "Security";

    /// <summary>Optional shared API key. When set, every /api request must send header X-Api-Key.</summary>
    public string? ApiKey { get; init; }

    public string[] AllowedOrigins { get; init; } = ["http://localhost:5173"];
}
