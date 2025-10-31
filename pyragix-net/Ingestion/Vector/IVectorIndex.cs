namespace PyRagix.Net.Ingestion.Vector;

/// <summary>
/// Minimal abstraction over a vector index implementation (FAISS or managed fallback).
/// </summary>
public interface IVectorIndex : IDisposable
{
    /// <summary>
    /// Adds the provided vectors to the index, associating each vector with a stable identifier.
    /// </summary>
    void AddWithIds(float[][] vectors, long[] ids);

    /// <summary>
    /// Searches the index using the supplied query embeddings and returns distances plus vector IDs.
    /// </summary>
    (float[][] Distances, long[][] Indices) Search(float[][] queries, int topK);

    /// <summary>
    /// Persists the index to disk so it can be reloaded in future sessions.
    /// </summary>
    void Save(string path);

    /// <summary>
    /// Gets the number of vectors currently tracked by the index.
    /// </summary>
    long Count { get; }
}

/// <summary>
/// Factory abstraction so tests can replace the underlying vector index implementation.
/// </summary>
public interface IVectorIndexFactory
{
    /// <summary>
    /// Creates a new, empty index with the given vector dimensionality.
    /// </summary>
    IVectorIndex Create(int dimension);

    /// <summary>
    /// Loads an index from the provided path.
    /// </summary>
    IVectorIndex Load(string path);
}
