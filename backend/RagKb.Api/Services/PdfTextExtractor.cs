using RagKb.Api.Infrastructure;
using RagKb.Api.Models;
using UglyToad.PdfPig;
using UglyToad.PdfPig.DocumentLayoutAnalysis.TextExtractor;

namespace RagKb.Api.Services;

public interface IPdfTextExtractor
{
    IReadOnlyList<PageText> Extract(Stream pdfStream);
}

public sealed class PdfTextExtractor(ILogger<PdfTextExtractor> logger) : IPdfTextExtractor
{
    private const int MaxPages = 2000;

    public IReadOnlyList<PageText> Extract(Stream pdfStream)
    {
        ArgumentNullException.ThrowIfNull(pdfStream);

        try
        {
            using var document = PdfDocument.Open(pdfStream, new ParsingOptions { UseLenientParsing = true });

            if (document.NumberOfPages > MaxPages)
                throw new BadRequestException($"PDF has {document.NumberOfPages} pages; the limit is {MaxPages}.");

            var pages = new List<PageText>(document.NumberOfPages);
            foreach (var page in document.GetPages())
            {
                string text;
                try
                {
                    // Content-order extraction keeps reading order and line breaks far better than page.Text.
                    text = ContentOrderTextExtractor.GetText(page);
                }
                catch (Exception ex)
                {
                    logger.LogWarning(ex, "Layout-aware extraction failed on page {Page}; falling back to raw text", page.Number);
                    text = page.Text;
                }

                pages.Add(new PageText(page.Number, text));
            }

            if (pages.All(p => string.IsNullOrWhiteSpace(p.Text)))
                throw new BadRequestException(
                    "No extractable text found. The PDF is probably scanned images — run OCR on it first.");

            return pages;
        }
        catch (BadRequestException)
        {
            throw;
        }
        catch (Exception ex) when (ex is not OutOfMemoryException)
        {
            // PdfPig throws PdfDocumentEncryptedException / PdfDocumentFormatException (and occasionally
            // generic exceptions) for broken files. Matching by name keeps us resilient across PdfPig versions.
            if (ex.GetType().Name.Contains("Encrypted", StringComparison.Ordinal))
                throw new BadRequestException("The PDF is password-protected.");

            logger.LogWarning(ex, "Failed to parse PDF");
            throw new BadRequestException("The file could not be parsed as a valid PDF.");
        }
    }
}
