using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.Extensions.Options;
using RagKb.Api.Data;
using RagKb.Api.Infrastructure;
using RagKb.Api.Models;
using RagKb.Api.Options;
using RagKb.Api.Services;

namespace RagKb.Api.Controllers;

[ApiController]
[Route("api/documents")]
[Produces("application/json")]
public sealed class DocumentController(
    IRagService rag,
    IDocumentRepository repository,
    IOptions<RagOptions> ragOptions) : ControllerBase
{
    private static ReadOnlySpan<byte> PdfMagic => "%PDF-"u8;

    /// <summary>Upload a PDF; it is parsed, chunked, embedded and stored in pgvector.</summary>
    [HttpPost("upload")]
    [Consumes("multipart/form-data")]
    [RequestSizeLimit(105 * 1024 * 1024)] // hard ceiling; the configurable limit is checked below
    [EnableRateLimiting(RateLimitPolicies.Upload)]
    [ProducesResponseType<UploadResponse>(StatusCodes.Status200OK)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status400BadRequest)]
    public async Task<ActionResult<UploadResponse>> Upload(IFormFile? file, CancellationToken ct)
    {
        if (file is null || file.Length == 0)
            throw new BadRequestException("Attach a PDF in the 'file' form field.");

        var maxBytes = ragOptions.Value.MaxUploadSizeMb * 1024L * 1024L;
        if (file.Length > maxBytes)
            throw new BadRequestException($"File is too large. Maximum size is {ragOptions.Value.MaxUploadSizeMb} MB.");

        if (!Path.GetExtension(file.FileName).Equals(".pdf", StringComparison.OrdinalIgnoreCase))
            throw new BadRequestException("Only .pdf files are supported.");

        await using var stream = file.OpenReadStream();

        // Don't trust the extension or Content-Type — check the magic bytes.
        var header = new byte[5];
        var read = await stream.ReadAtLeastAsync(header, header.Length, throwOnEndOfStream: false, ct);
        if (read < header.Length || !header.AsSpan().SequenceEqual(PdfMagic))
            throw new BadRequestException("The file is not a valid PDF.");
        stream.Position = 0;

        var result = await rag.IngestPdfAsync(stream, SanitizeFileName(file.FileName), file.Length, ct);
        return Ok(result);
    }

    /// <summary>Ask a question; answers are grounded in the most similar chunks (cosine similarity).</summary>
    [HttpPost("chat")]
    [Consumes("application/json")]
    [EnableRateLimiting(RateLimitPolicies.Chat)]
    [ProducesResponseType<ChatResponse>(StatusCodes.Status200OK)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status400BadRequest)]
    public async Task<ActionResult<ChatResponse>> Chat([FromBody] ChatRequest request, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(request.Message))
            throw new BadRequestException("Message must not be empty.");

        return Ok(await rag.AskAsync(request, ct));
    }

    [HttpGet]
    [ProducesResponseType<IReadOnlyList<DocumentRecord>>(StatusCodes.Status200OK)]
    public async Task<ActionResult<IReadOnlyList<DocumentRecord>>> List(CancellationToken ct)
        => Ok(await repository.ListAsync(ct));

    [HttpDelete("{id:guid}")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> Delete(Guid id, CancellationToken ct)
    {
        if (!await repository.DeleteAsync(id, ct))
            throw new NotFoundException($"Document {id} was not found.");
        return NoContent();
    }

    private static string SanitizeFileName(string fileName)
    {
        var name = Path.GetFileName(fileName);
        var invalid = Path.GetInvalidFileNameChars();
        var cleaned = new string(name.Select(c => invalid.Contains(c) || char.IsControl(c) ? '_' : c).ToArray()).Trim();
        if (cleaned.Length > 200) cleaned = cleaned[..196] + ".pdf";
        return cleaned.Length == 0 ? "document.pdf" : cleaned;
    }
}
