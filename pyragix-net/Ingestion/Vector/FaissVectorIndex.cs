using FaissNet;

namespace PyRagix.Net.Ingestion.Vector;

internal sealed class FaissVectorIndex : IVectorIndex
{
    private readonly FaissNet.Index _inner;

    public FaissVectorIndex(FaissNet.Index inner)
    {
        _inner = inner;
    }

    public void AddWithIds(float[][] vectors, long[] ids) => _inner.AddWithIds(vectors, ids);

    public (float[][] Distances, long[][] Indices) Search(float[][] queries, int topK)
    {
        var result = _inner.Search(queries, topK);
        return (result.Item1, result.Item2);
    }

    public void Save(string path) => _inner.Save(path);

    public long Count => _inner.Count;

    public void Dispose() => _inner.Dispose();
}

internal sealed class FaissVectorIndexFactory : IVectorIndexFactory
{
    public static FaissVectorIndexFactory Instance { get; } = new();

    private FaissVectorIndexFactory()
    {
    }

    public IVectorIndex Create(int dimension) => new FaissVectorIndex(FaissNet.Index.CreateDefault(dimension, MetricType.METRIC_INNER_PRODUCT));

    public IVectorIndex Load(string path) => new FaissVectorIndex(FaissNet.Index.Load(path));
}
