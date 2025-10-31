namespace PyRagix.Net.Ingestion.Vector;

/// <summary>
/// Minimal abstraction over a vector index implementation (FAISS or managed fallback).
/// </summary>
public interface IVectorIndex : IDisposable
{
    void AddWithIds(float[][] vectors, long[] ids);
    (float[][] Distances, long[][] Indices) Search(float[][] queries, int topK);
    void Save(string path);
    long Count { get; }
}

public interface IVectorIndexFactory
{
    IVectorIndex Create(int dimension);
    IVectorIndex Load(string path);
}
