using Dapper;
using Npgsql;
using NpgsqlTypes;
using Pgvector;
using RagKb.Api.Models;

namespace RagKb.Api.Data;

public interface IDocumentRepository
{
    Task<DocumentRecord?> FindByHashAsync(string sha256, CancellationToken ct);
    Task<DocumentRecord> InsertDocumentWithChunksAsync(
        DocumentRecord document, string sha256, IReadOnlyList<ChunkForInsert> chunks, CancellationToken ct);
    Task<IReadOnlyList<DocumentRecord>> ListAsync(CancellationToken ct);
    Task<bool> DeleteAsync(Guid id, CancellationToken ct);
    Task<IReadOnlyList<ChunkSearchResult>> SearchAsync(
        ReadOnlyMemory<float> queryEmbedding, int topK, double minSimilarity, Guid? documentId, CancellationToken ct);
}

/// <summary>
/// Dapper + Npgsql repository. Chunks are bulk-loaded with binary COPY (fast for hundreds of rows)
/// inside the same transaction as the parent document, so a failed upload never leaves orphans.
/// </summary>
public sealed class DocumentRepository(NpgsqlDataSource dataSource) : IDocumentRepository
{
    private const string DocumentColumns = """
        id              AS Id,
        file_name       AS FileName,
        file_size_bytes AS FileSizeBytes,
        page_count      AS PageCount,
        chunk_count     AS ChunkCount,
        created_at      AS CreatedAt
        """;

    public async Task<DocumentRecord?> FindByHashAsync(string sha256, CancellationToken ct)
    {
        await using var conn = await dataSource.OpenConnectionAsync(ct);
        return await conn.QuerySingleOrDefaultAsync<DocumentRecord>(new CommandDefinition(
            $"SELECT {DocumentColumns} FROM documents WHERE content_sha256 = @sha256",
            new { sha256 }, cancellationToken: ct));
    }

    public async Task<DocumentRecord> InsertDocumentWithChunksAsync(
        DocumentRecord document, string sha256, IReadOnlyList<ChunkForInsert> chunks, CancellationToken ct)
    {
        await using var conn = await dataSource.OpenConnectionAsync(ct);
        await using var tx = await conn.BeginTransactionAsync(ct);

        await conn.ExecuteAsync(new CommandDefinition("""
            INSERT INTO documents (id, file_name, file_size_bytes, content_sha256, page_count, chunk_count)
            VALUES (@Id, @FileName, @FileSizeBytes, @Sha256, @PageCount, @ChunkCount)
            """,
            new
            {
                document.Id,
                document.FileName,
                document.FileSizeBytes,
                Sha256 = sha256,
                document.PageCount,
                ChunkCount = chunks.Count
            },
            transaction: tx, cancellationToken: ct));

        await using (var writer = await conn.BeginBinaryImportAsync(
            "COPY document_chunks (document_id, chunk_index, page_number, content, embedding) FROM STDIN (FORMAT BINARY)", ct))
        {
            foreach (var chunk in chunks)
            {
                await writer.StartRowAsync(ct);
                await writer.WriteAsync(document.Id, NpgsqlDbType.Uuid, ct);
                await writer.WriteAsync(chunk.Index, NpgsqlDbType.Integer, ct);
                await writer.WriteAsync(chunk.PageNumber, NpgsqlDbType.Integer, ct);
                await writer.WriteAsync(chunk.Content, NpgsqlDbType.Text, ct);
                await writer.WriteAsync(new Vector(chunk.Embedding), ct);
            }

            await writer.CompleteAsync(ct);
        }

        var saved = await conn.QuerySingleAsync<DocumentRecord>(new CommandDefinition(
            $"SELECT {DocumentColumns} FROM documents WHERE id = @Id",
            new { document.Id }, transaction: tx, cancellationToken: ct));

        await tx.CommitAsync(ct);
        return saved;
    }

    public async Task<IReadOnlyList<DocumentRecord>> ListAsync(CancellationToken ct)
    {
        await using var conn = await dataSource.OpenConnectionAsync(ct);
        var rows = await conn.QueryAsync<DocumentRecord>(new CommandDefinition(
            $"SELECT {DocumentColumns} FROM documents ORDER BY created_at DESC LIMIT 500",
            cancellationToken: ct));
        return rows.AsList();
    }

    public async Task<bool> DeleteAsync(Guid id, CancellationToken ct)
    {
        await using var conn = await dataSource.OpenConnectionAsync(ct);
        var affected = await conn.ExecuteAsync(new CommandDefinition(
            "DELETE FROM documents WHERE id = @id", new { id }, cancellationToken: ct));
        return affected > 0;
    }

    /// <summary>
    /// Cosine similarity search. pgvector's &lt;=&gt; operator returns cosine DISTANCE (0 = identical,
    /// 2 = opposite), so similarity = 1 - distance. Ordering by the raw operator expression lets
    /// Postgres use the HNSW index; the similarity threshold is applied on the small top-N set.
    /// </summary>
    public async Task<IReadOnlyList<ChunkSearchResult>> SearchAsync(
        ReadOnlyMemory<float> queryEmbedding, int topK, double minSimilarity, Guid? documentId, CancellationToken ct)
    {
        const string sql = """
            WITH nearest AS (
                SELECT  c.id,
                        c.document_id,
                        c.page_number,
                        c.chunk_index,
                        c.content,
                        c.embedding <=> @embedding AS distance
                FROM    document_chunks c
                WHERE   (@documentId::uuid IS NULL OR c.document_id = @documentId::uuid)
                ORDER BY c.embedding <=> @embedding
                LIMIT   @candidates
            )
            SELECT  n.id           AS ChunkId,
                    n.document_id  AS DocumentId,
                    d.file_name    AS FileName,
                    n.page_number  AS PageNumber,
                    n.chunk_index  AS ChunkIndex,
                    n.content      AS Content,
                    1 - n.distance AS Similarity
            FROM    nearest n
            JOIN    documents d ON d.id = n.document_id
            WHERE   1 - n.distance >= @minSimilarity
            ORDER BY n.distance
            LIMIT   @topK;
            """;

        await using var conn = await dataSource.OpenConnectionAsync(ct);
        await using var tx = await conn.BeginTransactionAsync(ct);

        // Wider HNSW search beam => better recall; SET LOCAL scopes it to this transaction.
        await conn.ExecuteAsync(new CommandDefinition("SET LOCAL hnsw.ef_search = 100", transaction: tx, cancellationToken: ct));

        var rows = await conn.QueryAsync<ChunkSearchResult>(new CommandDefinition(sql, new
        {
            embedding = new Vector(queryEmbedding),
            documentId,
            candidates = Math.Max(topK * 4, 20),
            minSimilarity,
            topK
        }, transaction: tx, cancellationToken: ct));

        await tx.CommitAsync(ct);
        return rows.AsList();
    }
}
