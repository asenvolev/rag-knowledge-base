using System.ComponentModel.DataAnnotations;

namespace RagKb.Api.Models;

// ---------- Domain ----------

public sealed record PageText(int PageNumber, string Text);

public sealed record TextChunk(int Index, int PageNumber, string Content);

public sealed record ChunkForInsert(int Index, int PageNumber, string Content, ReadOnlyMemory<float> Embedding);

public sealed record DocumentRecord(
    Guid Id,
    string FileName,
    long FileSizeBytes,
    int PageCount,
    int ChunkCount,
    DateTime CreatedAt); // timestamptz is read by Npgsql as DateTime (Kind=Utc)

public sealed record ChunkSearchResult(
    long ChunkId,
    Guid DocumentId,
    string FileName,
    int PageNumber,
    int ChunkIndex,
    string Content,
    double Similarity);

// ---------- API contracts ----------

public sealed record UploadResponse(
    Guid DocumentId,
    string FileName,
    int PageCount,
    int ChunkCount,
    long ElapsedMs);

public sealed record ChatHistoryItem(
    [Required, RegularExpression("^(user|assistant)$")] string Role,
    [Required, StringLength(8000)] string Content);

public sealed record ChatRequest(
    [Required, StringLength(4000, MinimumLength = 1)] string Message,
    Guid? DocumentId = null,
    IReadOnlyList<ChatHistoryItem>? History = null);

public sealed record SourceReference(
    Guid DocumentId,
    string FileName,
    int PageNumber,
    double Similarity,
    string Snippet);

public sealed record ChatResponse(
    string Answer,
    IReadOnlyList<SourceReference> Sources,
    int? PromptTokens,
    int? CompletionTokens);
