using iTerminal.Core.Services.Documents;

namespace iTerminal.Core.Test.Services.Documents;

[TestClass]
public class DocumentChunkerTest
{
    [TestMethod]
    public void EmptyTextHasNoPassages()
    {
        CollectionAssert.AreEqual(Array.Empty<int>(), DocumentChunker.ChunkStarts(string.Empty).ToArray());
        CollectionAssert.AreEqual(Array.Empty<int>(), DocumentChunker.ChunkStarts(null).ToArray());
    }

    [TestMethod]
    public void TextThatFitsIsOnePassage()
    {
        CollectionAssert.AreEqual(new[] { 0 }, DocumentChunker.ChunkStarts(new string('a', DocumentChunker.ChunkLength)).ToArray());
    }

    [TestMethod]
    public void ConsecutivePassagesOverlapAndTheLastOneReachesTheEnd()
    {
        string text = new('a', DocumentChunker.ChunkLength + 1);

        // The second window starts one step on, so it holds the one character the first window missed.
        CollectionAssert.AreEqual(new[] { 0, DocumentChunker.ChunkLength - DocumentChunker.Overlap }, DocumentChunker.ChunkStarts(text).ToArray());
    }

    [TestMethod]
    public void TheCountIsCappedSoALongDocumentCostsAFixedNumberOfRequests()
    {
        string text = new('a', 100_000);

        IReadOnlyList<int> starts = DocumentChunker.ChunkStarts(text);

        Assert.AreEqual(DocumentChunker.MaxChunks, starts.Count);
        Assert.AreEqual((DocumentChunker.MaxChunks - 1) * (DocumentChunker.ChunkLength - DocumentChunker.Overlap), starts[^1]);
    }

    [TestMethod]
    public void ChunkAtIsClippedToTheEndOfTheText()
    {
        string text = new('x', 1500);

        Assert.AreEqual(DocumentChunker.ChunkLength, DocumentChunker.ChunkAt(text, 0).Length);
        Assert.AreEqual(700, DocumentChunker.ChunkAt(text, 800).Length);
    }

    [TestMethod]
    public void AWindowCutThroughASurrogatePairDropsTheLoneHalf()
    {
        // The emoji is two UTF-16 units, at 999 and 1000: the first window ends on its high surrogate.
        string text = new string('a', 999) + "😀" + new string('b', 10);

        string window = DocumentChunker.ChunkAt(text, 0);

        Assert.AreEqual(999, window.Length);
        Assert.IsFalse(char.IsHighSurrogate(window[^1]));
    }

    [TestMethod]
    public void TheWindowsCoverTheWholeTextWhenItIsNotCapped()
    {
        string text = string.Concat(Enumerable.Range(0, 2600).Select(index => (char)('a' + index % 26)));

        IReadOnlyList<int> starts = DocumentChunker.ChunkStarts(text);
        int covered = starts.Max() + DocumentChunker.ChunkAt(text, starts.Max()).Length;

        Assert.AreEqual(text.Length, covered);
    }
}
