using Microsoft.Extensions.AI;

namespace iTerminal.Core.Services.Documents;

/// <summary>
/// Turns passages of text into the vectors the meaning index compares. The one seam between the document
/// library and an embedding model, so the library can be tested with a fake and runs without a model at all.
/// </summary>
public interface IDocumentEmbedder
{
    /// <summary>The model the vectors come from. Stored with every document, so changing it re-embeds the library.</summary>
    string ModelId { get; }

    /// <summary>False when no embedding model is configured: meaning search is off and nothing is sent.</summary>
    bool IsConfigured { get; }

    /// <summary>One vector per text, in the same order. Every vector of one model has the same length.</summary>
    Task<IReadOnlyList<float[]>> EmbedAsync(IReadOnlyList<string> texts, CancellationToken cancellationToken = default);
}

/// <summary>The embedder used when no embedding model is configured. It is never asked to embed anything.</summary>
public sealed class NullDocumentEmbedder : IDocumentEmbedder
{
    public static NullDocumentEmbedder Instance { get; } = new();

    public string ModelId => string.Empty;

    public bool IsConfigured => false;

    public Task<IReadOnlyList<float[]>> EmbedAsync(IReadOnlyList<string> texts, CancellationToken cancellationToken = default) =>
        throw new InvalidOperationException("Meaning search is off: no embedding model is configured (AI:EmbeddingModelId).");
}

/// <summary>An embedder over a <c>Microsoft.Extensions.AI</c> generator, normally the OpenAI-compatible endpoint the chat model uses.</summary>
public sealed class DocumentEmbedder(IEmbeddingGenerator<string, Embedding<float>> generator, string modelId) : IDocumentEmbedder
{
    public string ModelId { get; } = modelId;

    public bool IsConfigured => true;

    public async Task<IReadOnlyList<float[]>> EmbedAsync(IReadOnlyList<string> texts, CancellationToken cancellationToken = default)
    {
        GeneratedEmbeddings<Embedding<float>> embeddings = await generator.GenerateAsync(texts, cancellationToken: cancellationToken);

        List<float[]> vectors = new(embeddings.Count);
        foreach (Embedding<float> embedding in embeddings)
        {
            vectors.Add(embedding.Vector.ToArray());
        }

        // A server that answers with fewer vectors than texts would pair chunks with the wrong passages.
        if (vectors.Count != texts.Count)
        {
            throw new InvalidOperationException($"The embedding model returned {vectors.Count} vectors for {texts.Count} passages.");
        }

        return vectors;
    }
}
