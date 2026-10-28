using RagKb.Api.Infrastructure;
using RagKb.Api.Models;
using RagKb.Api.Services;
using Xunit;

namespace RagKb.Tests;

public class TextChunkerTests
{
    private readonly TextChunker _chunker = new(chunkSize: 1000, overlap: 200);

    private static string Paragraphs(int count, int sentencesEach = 8)
    {
        var rnd = new Random(7);
        var paragraphs = Enumerable.Range(0, count).Select(_ =>
            string.Join(' ', Enumerable.Range(0, sentencesEach).Select(_ =>
                string.Join(' ', Enumerable.Range(0, rnd.Next(6, 16)).Select(_ => $"word{rnd.Next(1000)}")) + ".")));
        return string.Join("\n\n", paragraphs);
    }

    [Fact]
    public void Empty_input_returns_no_chunks()
    {
        Assert.Empty(_chunker.Chunk([]));
        Assert.Empty(_chunker.Chunk([new PageText(1, "  \n\t ")]));
    }

    [Fact]
    public void Short_text_is_a_single_chunk_on_its_page()
    {
        var chunks = _chunker.Chunk([new PageText(3, "Hello world.")]);
        var chunk = Assert.Single(chunks);
        Assert.Equal("Hello world.", chunk.Content);
        Assert.Equal(3, chunk.PageNumber);
    }

    [Fact]
    public void Chunks_respect_max_size_and_are_reasonably_full()
    {
        var chunks = _chunker.Chunk([new PageText(1, Paragraphs(40))]);

        Assert.True(chunks.Count > 5);
        Assert.All(chunks, c => Assert.InRange(c.Content.Length, 1, 1000));
        Assert.All(chunks.SkipLast(1), c => Assert.True(c.Content.Length >= 450, $"chunk too small: {c.Content.Length}"));
    }

    [Fact]
    public void Consecutive_chunks_overlap()
    {
        var chunks = _chunker.Chunk([new PageText(1, Paragraphs(30))]);

        for (var i = 0; i + 1 < chunks.Count; i++)
        {
            var tail = chunks[i].Content[^40..];
            Assert.Contains(tail[..20], chunks[i + 1].Content);
        }
    }

    [Fact]
    public void Chunks_never_split_words()
    {
        var text = Paragraphs(30);
        var vocabulary = text.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries).ToHashSet();

        var words = _chunker.Chunk([new PageText(1, text)])
            .SelectMany(c => c.Content.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries));

        Assert.All(words, w => Assert.Contains(w, vocabulary));
    }

    [Fact]
    public void Chunks_are_tagged_with_their_starting_page()
    {
        var pages = Enumerable.Range(1, 4).Select(p => new PageText(p, Paragraphs(6))).ToList();
        var chunks = _chunker.Chunk(pages);

        Assert.Equal(1, chunks[0].PageNumber);
        Assert.Equal(4, chunks[^1].PageNumber);
        Assert.True(chunks.Select(c => c.PageNumber).SequenceEqual(chunks.Select(c => c.PageNumber).Order()));
    }

    [Fact]
    public void Unbreakable_text_is_hard_cut_with_overlap()
    {
        var chunks = _chunker.Chunk([new PageText(1, new string('x', 2500))]);
        Assert.Equal([1000, 1000, 900], chunks.Select(c => c.Content.Length));
    }

    [Fact]
    public void Normalize_joins_hyphenated_words_and_collapses_whitespace()
    {
        Assert.Equal("information is key\n\nnext", TextChunker.Normalize("infor-\nmation  is \t key\r\n\r\n\r\n\r\nnext"));
    }

    [Theory]
    [InlineData(1000, 600)]
    [InlineData(0, 0)]
    public void Invalid_settings_throw(int size, int overlap)
        => Assert.Throws<ArgumentOutOfRangeException>(() => new TextChunker(size, overlap));
}

public class ConnectionStringResolverTests
{
    [Fact]
    public void Converts_postgres_url_with_tls()
    {
        var cs = ConnectionStringResolver.Normalize("postgres://u%40x:p%3Ass@db.example.com:6543/rag?sslmode=require");
        Assert.Contains("Host=db.example.com", cs);
        Assert.Contains("Port=6543", cs);
        Assert.Contains("Database=rag", cs);
        Assert.Contains("Username=u@x", cs);
        Assert.Contains("Password=p:ss", cs);
        Assert.Contains("SSL Mode=Require", cs);
    }

    [Fact]
    public void Leaves_keyvalue_strings_untouched()
    {
        const string cs = "Host=localhost;Database=ragkb";
        Assert.Equal(cs, ConnectionStringResolver.Normalize(cs));
    }
}
