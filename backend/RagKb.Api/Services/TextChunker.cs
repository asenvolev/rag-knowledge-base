using System.Text;
using System.Text.RegularExpressions;
using Microsoft.Extensions.Options;
using RagKb.Api.Models;
using RagKb.Api.Options;

namespace RagKb.Api.Services;

public interface ITextChunker
{
    IReadOnlyList<TextChunk> Chunk(IReadOnlyList<PageText> pages);
}

/// <summary>
/// Sliding-window chunker (default 1000 chars, 200 overlap) that prefers to cut on natural
/// boundaries — paragraph, line, sentence, then word — and never starts a chunk mid-word.
/// Pages are concatenated so chunks can span page breaks; each chunk is tagged with the page
/// where it starts, which is what we cite back to the user.
/// </summary>
public sealed partial class TextChunker : ITextChunker
{
    private const string PageSeparator = "\n\n";

    // Ordered from strongest to weakest boundary.
    private static readonly string[] BoundaryTokens = ["\n\n", "\n", ". ", "? ", "! ", "; ", ", ", " "];

    private readonly int _chunkSize;
    private readonly int _overlap;

    public TextChunker(IOptions<RagOptions> options)
        : this(options.Value.ChunkSize, options.Value.ChunkOverlap) { }

    public TextChunker(int chunkSize, int overlap)
    {
        if (chunkSize <= 0) throw new ArgumentOutOfRangeException(nameof(chunkSize));
        if (overlap < 0 || overlap >= chunkSize / 2 + 1)
            throw new ArgumentOutOfRangeException(nameof(overlap), "Overlap must be >= 0 and at most half of the chunk size.");

        _chunkSize = chunkSize;
        _overlap = overlap;
    }

    public IReadOnlyList<TextChunk> Chunk(IReadOnlyList<PageText> pages)
    {
        ArgumentNullException.ThrowIfNull(pages);

        var (text, pageStarts, pageNumbers) = Concatenate(pages);
        var chunks = new List<TextChunk>();
        if (text.Length == 0) return chunks;

        var start = 0;
        while (start < text.Length)
        {
            var hardEnd = Math.Min(start + _chunkSize, text.Length);
            var end = hardEnd == text.Length ? hardEnd : FindBreak(text, start, hardEnd);

            var content = text[start..end].Trim();
            if (content.Length > 0)
            {
                chunks.Add(new TextChunk(chunks.Count, PageAt(start, pageStarts, pageNumbers), content));
            }

            if (end >= text.Length) break;

            var next = AlignToWordStart(text, end - _overlap, end);
            start = next > start ? next : end; // guarantee forward progress
        }

        return chunks;
    }

    /// <summary>
    /// Looks for the strongest boundary in the second half of the window so chunks stay reasonably
    /// full. Returns the index just after the boundary token.
    /// </summary>
    private int FindBreak(string text, int start, int hardEnd)
    {
        var minEnd = start + _chunkSize / 2;
        var window = text.AsSpan(start, hardEnd - start);

        foreach (var token in BoundaryTokens)
        {
            var idx = window.LastIndexOf(token.AsSpan(), StringComparison.Ordinal);
            if (idx < 0) continue;

            var candidate = start + idx + token.Length;
            if (candidate > minEnd) return candidate;
        }

        return hardEnd; // one giant "word" (e.g. a URL or base64 blob) — hard cut
    }

    /// <summary>Moves the overlap start forward to the next word start, without passing <paramref name="limit"/>.</summary>
    private static int AlignToWordStart(string text, int position, int limit)
    {
        position = Math.Max(0, position);
        if (position == 0 || char.IsWhiteSpace(text[position - 1])) return position;

        var i = position;
        while (i < limit && !char.IsWhiteSpace(text[i])) i++;
        while (i < limit && char.IsWhiteSpace(text[i])) i++;
        return i < limit ? i : position;
    }

    private static int PageAt(int offset, List<int> pageStarts, List<int> pageNumbers)
    {
        var idx = pageStarts.BinarySearch(offset);
        if (idx < 0) idx = ~idx - 1;
        return pageNumbers[Math.Clamp(idx, 0, pageNumbers.Count - 1)];
    }

    private static (string Text, List<int> PageStarts, List<int> PageNumbers) Concatenate(IReadOnlyList<PageText> pages)
    {
        var sb = new StringBuilder();
        var starts = new List<int>(pages.Count);
        var numbers = new List<int>(pages.Count);

        foreach (var page in pages)
        {
            var normalized = Normalize(page.Text);
            if (normalized.Length == 0) continue;

            if (sb.Length > 0) sb.Append(PageSeparator);
            starts.Add(sb.Length);
            numbers.Add(page.PageNumber);
            sb.Append(normalized);
        }

        return (sb.ToString(), starts, numbers);
    }

    internal static string Normalize(string? raw)
    {
        if (string.IsNullOrWhiteSpace(raw)) return string.Empty;

        var text = raw.Replace("\r\n", "\n").Replace('\r', '\n').Replace(' ', ' ');
        text = HyphenatedLineBreak().Replace(text, "$1$2");   // "infor-\nmation" -> "information"
        text = HorizontalWhitespace().Replace(text, " ");
        text = SpaceAroundNewline().Replace(text, "\n");
        text = ManyNewlines().Replace(text, "\n\n");
        return text.Trim();
    }

    [GeneratedRegex(@"(\p{L})-\n(\p{Ll})")]
    private static partial Regex HyphenatedLineBreak();

    [GeneratedRegex(@"[ \t\f\v]+")]
    private static partial Regex HorizontalWhitespace();

    [GeneratedRegex(@" *\n *")]
    private static partial Regex SpaceAroundNewline();

    [GeneratedRegex(@"\n{3,}")]
    private static partial Regex ManyNewlines();
}
