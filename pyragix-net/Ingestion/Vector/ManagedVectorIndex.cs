using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace PyRagix.Net.Ingestion.Vector;

/// <summary>
/// Pure managed vector index that falls back to exhaustive inner-product search when native FAISS bindings are unavailable.
/// Designed primarily for development environments (e.g., WSL or CI) where FaissNet is not supported.
/// </summary>
internal sealed class ManagedVectorIndex : IVectorIndex
{
    private readonly int _dimension;
    private readonly List<float[]> _vectors = new();
    private readonly List<long> _ids = new();
    private readonly object _syncRoot = new();

    private ManagedVectorIndex(int dimension)
    {
        if (dimension <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(dimension), "Embedding dimension must be positive.");
        }

        _dimension = dimension;
    }

    /// <summary>
    /// Factory helper used by <see cref="ManagedVectorIndexFactory"/> when loading persisted indexes.
    /// </summary>
    public static ManagedVectorIndex Create(int dimension) => new(dimension);

    /// <inheritdoc />
    public long Count
    {
        get
        {
            lock (_syncRoot)
            {
                return _ids.Count;
            }
        }
    }

    /// <inheritdoc />
    public void AddWithIds(float[][] vectors, long[] ids)
    {
        if (vectors.Length != ids.Length)
        {
            throw new ArgumentException("Vectors and IDs length mismatch.", nameof(ids));
        }

        lock (_syncRoot)
        {
            for (int i = 0; i < vectors.Length; i++)
            {
                var vector = vectors[i];
                if (vector == null || vector.Length != _dimension)
                {
                    throw new ArgumentException($"Vector at index {i} has incorrect dimension. Expected {_dimension}, received {vector?.Length ?? 0}.");
                }

                // Store a defensive copy so callers cannot mutate the underlying state.
                var copy = new float[_dimension];
                Array.Copy(vector, copy, _dimension);

                _vectors.Add(copy);
                _ids.Add(ids[i]);
            }
        }
    }

    /// <inheritdoc />
    public (float[][] Distances, long[][] Indices) Search(float[][] queries, int topK)
    {
        if (queries == null)
        {
            throw new ArgumentNullException(nameof(queries));
        }

        if (topK <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(topK), "topK must be greater than zero.");
        }

        lock (_syncRoot)
        {
            var distances = new float[queries.Length][];
            var indices = new long[queries.Length][];

            for (int qi = 0; qi < queries.Length; qi++)
            {
                var query = queries[qi];
                if (query == null || query.Length != _dimension)
                {
                    throw new ArgumentException($"Query at index {qi} has incorrect dimension. Expected {_dimension}, received {query?.Length ?? 0}.", nameof(queries));
                }

                var results = new List<(float Score, long Id)>(_vectors.Count);
                for (int vi = 0; vi < _vectors.Count; vi++)
                {
                    var score = DotProduct(query, _vectors[vi]);
                    results.Add((score, _ids[vi]));
                }

                var topResults = results
                    .OrderByDescending(r => r.Score)
                    .Take(topK)
                    .ToList();

                var distanceRow = new float[topK];
                var indexRow = new long[topK];

                int ri = 0;
                for (; ri < topResults.Count; ri++)
                {
                    distanceRow[ri] = topResults[ri].Score;
                    indexRow[ri] = topResults[ri].Id;
                }

                for (; ri < topK; ri++)
                {
                    distanceRow[ri] = 0f;
                    indexRow[ri] = -1;
                }

                distances[qi] = distanceRow;
                indices[qi] = indexRow;
            }

            return (distances, indices);
        }
    }

    /// <inheritdoc />
    public void Save(string path)
    {
        if (string.IsNullOrWhiteSpace(path))
        {
            throw new ArgumentException("Invalid path.", nameof(path));
        }

        lock (_syncRoot)
        {
            var directory = Path.GetDirectoryName(path);
            if (!string.IsNullOrEmpty(directory))
            {
                Directory.CreateDirectory(directory);
            }

            using var stream = File.Open(path, FileMode.Create, FileAccess.Write, FileShare.None);
            using var writer = new BinaryWriter(stream);

            writer.Write(1); // format version
            writer.Write(_dimension);
            writer.Write(_vectors.Count);

            for (int i = 0; i < _vectors.Count; i++)
            {
                writer.Write(_ids[i]);
                var vector = _vectors[i];
                for (int d = 0; d < vector.Length; d++)
                {
                    writer.Write(vector[d]);
                }
            }
        }
    }

    /// <summary>
    /// Loads a managed vector index from the specified file.
    /// </summary>
    public static ManagedVectorIndex Load(string path)
    {
        if (!File.Exists(path))
        {
            throw new FileNotFoundException("Vector index file not found.", path);
        }

        using var stream = File.OpenRead(path);
        using var reader = new BinaryReader(stream);

        var version = reader.ReadInt32();
        if (version != 1)
        {
            throw new NotSupportedException($"Unsupported vector index version: {version}");
        }

        var dimension = reader.ReadInt32();
        var count = reader.ReadInt32();

        var index = new ManagedVectorIndex(dimension);
        for (int i = 0; i < count; i++)
        {
            var id = reader.ReadInt64();
            var vector = new float[dimension];
            for (int d = 0; d < dimension; d++)
            {
                vector[d] = reader.ReadSingle();
            }

            index._ids.Add(id);
            index._vectors.Add(vector);
        }

        return index;
    }

    /// <inheritdoc />
    public void Dispose()
    {
        // No unmanaged resources being held.
    }

    private static float DotProduct(float[] a, float[] b)
    {
        var sum = 0f;
        for (int i = 0; i < a.Length; i++)
        {
            sum += a[i] * b[i];
        }

        return sum;
    }
}

/// <summary>
/// Factory that creates managed vector indexes or loads them from disk.
/// </summary>
internal sealed class ManagedVectorIndexFactory : IVectorIndexFactory
{
    public static ManagedVectorIndexFactory Instance { get; } = new();

    private ManagedVectorIndexFactory()
    {
    }

    public IVectorIndex Create(int dimension) => ManagedVectorIndex.Create(dimension);

    public IVectorIndex Load(string path) => ManagedVectorIndex.Load(path);
}
