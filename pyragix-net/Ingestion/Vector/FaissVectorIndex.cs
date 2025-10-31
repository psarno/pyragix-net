using FaissNet;

namespace PyRagix.Net.Ingestion.Vector;

/// <summary>
/// Thin wrapper around <see cref="FaissNet.Index"/> so the rest of the codebase can depend on the shared <see cref="IVectorIndex"/> abstraction.
/// </summary>
internal sealed class FaissVectorIndex : IVectorIndex
{
    private readonly FaissNet.Index _inner;

    public FaissVectorIndex(FaissNet.Index inner)
    {
        _inner = inner;
    }

    /// <inheritdoc />
    public void AddWithIds(float[][] vectors, long[] ids) => _inner.AddWithIds(vectors, ids);

    /// <inheritdoc />
    public (float[][] Distances, long[][] Indices) Search(float[][] queries, int topK)
    {
        var result = _inner.Search(queries, topK);
        return (result.Item1, result.Item2);
    }

    /// <inheritdoc />
    public void Save(string path) => _inner.Save(path);

    /// <inheritdoc />
    public long Count => _inner.Count;

    /// <inheritdoc />
    public void Dispose() => _inner.Dispose();
}

/// <summary>
/// Factory responsible for creating or loading FAISS-backed vector indexes.
/// </summary>
internal sealed class FaissVectorIndexFactory : IVectorIndexFactory
{
    public static FaissVectorIndexFactory Instance { get; } = new();

    private FaissVectorIndexFactory()
    {
    }

    /// <inheritdoc />
    public IVectorIndex Create(int dimension) => new FaissVectorIndex(FaissNet.Index.CreateDefault(dimension, MetricType.METRIC_INNER_PRODUCT));

    /// <inheritdoc />
    public IVectorIndex Load(string path) => new FaissVectorIndex(FaissNet.Index.Load(path));
}
