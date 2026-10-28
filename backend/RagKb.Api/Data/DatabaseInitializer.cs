using Dapper;
using Microsoft.Extensions.Options;
using Npgsql;
using RagKb.Api.Options;

namespace RagKb.Api.Data;

/// <summary>
/// Idempotent schema bootstrap, run once at startup. For a larger team, move this to a migration
/// tool (DbUp / EF migrations / Flyway); for an MVP, "CREATE ... IF NOT EXISTS" is enough.
/// </summary>
public sealed class DatabaseInitializer(
    NpgsqlDataSource dataSource,
    IOptions<OpenAiOptions> openAi,
    ILogger<DatabaseInitializer> logger) : IHostedService
{
    public async Task StartAsync(CancellationToken ct)
    {
        var dims = openAi.Value.EmbeddingDimensions;

        // Postgres can't parameterise DDL; dims is a validated int from config, so interpolation is safe.
        var sql = $"""
            CREATE EXTENSION IF NOT EXISTS vector;

            CREATE TABLE IF NOT EXISTS documents (
                id              uuid PRIMARY KEY,
                file_name       text        NOT NULL,
                file_size_bytes bigint      NOT NULL,
                content_sha256  text        NOT NULL UNIQUE,
                page_count      int         NOT NULL,
                chunk_count     int         NOT NULL DEFAULT 0,
                created_at      timestamptz NOT NULL DEFAULT now()
            );

            CREATE TABLE IF NOT EXISTS document_chunks (
                id           bigint GENERATED ALWAYS AS IDENTITY PRIMARY KEY,
                document_id  uuid   NOT NULL REFERENCES documents(id) ON DELETE CASCADE,
                chunk_index  int    NOT NULL,
                page_number  int    NOT NULL,
                content      text   NOT NULL,
                embedding    vector({dims}) NOT NULL,
                UNIQUE (document_id, chunk_index)
            );

            CREATE INDEX IF NOT EXISTS ix_document_chunks_document_id
                ON document_chunks (document_id);

            -- HNSW index for approximate nearest-neighbour search with cosine distance (<=>).
            CREATE INDEX IF NOT EXISTS ix_document_chunks_embedding_hnsw
                ON document_chunks USING hnsw (embedding vector_cosine_ops)
                WITH (m = 16, ef_construction = 64);
            """;

        const int maxAttempts = 10;
        for (var attempt = 1; ; attempt++)
        {
            try
            {
                await using var conn = await dataSource.OpenConnectionAsync(ct);
                await conn.ExecuteAsync(new CommandDefinition(sql, cancellationToken: ct));
                // The 'vector' type was just created; make Npgsql pick up its OID.
                await conn.ReloadTypesAsync();
                logger.LogInformation("Database schema ready (vector dimensions: {Dims})", dims);
                return;
            }
            catch (NpgsqlException ex) when (attempt < maxAttempts && ex.IsTransient)
            {
                logger.LogWarning("Database not reachable yet (attempt {Attempt}/{Max}): {Message}", attempt, maxAttempts, ex.Message);
                await Task.Delay(TimeSpan.FromSeconds(Math.Min(2 * attempt, 10)), ct);
            }
            catch (Exception ex) when (attempt < maxAttempts && ex is System.Net.Sockets.SocketException)
            {
                logger.LogWarning("Database socket error (attempt {Attempt}/{Max}): {Message}", attempt, maxAttempts, ex.Message);
                await Task.Delay(TimeSpan.FromSeconds(Math.Min(2 * attempt, 10)), ct);
            }
        }
    }

    public Task StopAsync(CancellationToken ct) => Task.CompletedTask;
}
