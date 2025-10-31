using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using PyRagix.Net.Ingestion.Vector;

namespace PyRagix.Net.Tests.TestInfrastructure;

// Lightweight vector index used in tests to avoid native FAISS dependencies.
internal sealed class InMemoryVectorIndex : IVectorIndex
{
    private readonly Dictionary<long, float[]> _vectors = new();
    private readonly int _dimension;

    private InMemoryVectorIndex(int dimension)
    {
        _dimension = dimension;
    }

    public static InMemoryVectorIndex Create(int dimension) => new(dimension);

    public static InMemoryVectorIndex Load(string path)
    {
        if (!File.Exists(path))
        {
            return new InMemoryVectorIndex(0);
        }

        var lines = File.ReadAllLines(path);
        if (lines.Length == 0)
        {
            return new InMemoryVectorIndex(0);
        }

        var dimension = int.Parse(lines[0], CultureInfo.InvariantCulture);
        var index = new InMemoryVectorIndex(dimension);

        foreach (var line in lines.Skip(1))
        {
            if (string.IsNullOrWhiteSpace(line))
            {
                continue;
            }

            var parts = line.Split(':', 2);
            if (parts.Length != 2)
            {
                continue;
            }

            if (!long.TryParse(parts[0], NumberStyles.Any, CultureInfo.InvariantCulture, out var id))
            {
                continue;
            }

            var values = parts[1]
                .Split(',', StringSplitOptions.RemoveEmptyEntries)
                .Select(v => float.Parse(v, CultureInfo.InvariantCulture))
                .ToArray();

            if (values.Length < dimension)
            {
                Array.Resize(ref values, dimension);
            }

            index._vectors[id] = values;
        }

        return index;
    }

    public void AddWithIds(float[][] vectors, long[] ids)
    {
        for (var i = 0; i < vectors.Length && i < ids.Length; i++)
        {
            var vector = new float[_dimension == 0 ? vectors[i].Length : _dimension];
            var copyLength = Math.Min(vector.Length, vectors[i].Length);
            Array.Copy(vectors[i], vector, copyLength);
            _vectors[ids[i]] = vector;
        }
    }

    public (float[][] Distances, long[][] Indices) Search(float[][] queries, int topK)
    {
        var distances = new float[queries.Length][];
        var indices = new long[queries.Length][];

        for (var q = 0; q < queries.Length; q++)
        {
            var scored = _vectors
                .Select(entry => (Id: entry.Key, Score: DotProduct(queries[q], entry.Value)))
                .OrderByDescending(s => s.Score)
                .Take(topK)
                .ToList();

            distances[q] = scored.Select(s => s.Score).ToArray();
            indices[q] = scored.Select(s => s.Id).ToArray();
        }

        return (distances, indices);
    }

    public void Save(string path)
    {
        var dimension = _dimension > 0 ? _dimension : (_vectors.Values.FirstOrDefault()?.Length ?? 0);

        var lines = new List<string>
        {
            dimension.ToString(CultureInfo.InvariantCulture)
        };

        lines.AddRange(_vectors.Select(entry =>
            string.Join(
                ':',
                entry.Key.ToString(CultureInfo.InvariantCulture),
                string.Join(',', entry.Value.Select(v => v.ToString(CultureInfo.InvariantCulture))))));

        File.WriteAllLines(path, lines);
    }

    public long Count => _vectors.Count;

    public void Dispose()
    {
        // No unmanaged resources.
    }

    private static float DotProduct(IReadOnlyList<float> a, IReadOnlyList<float> b)
    {
        var length = Math.Min(a.Count, b.Count);
        float sum = 0;
        for (var i = 0; i < length; i++)
        {
            sum += a[i] * b[i];
        }

        return sum;
    }
}

// Factory registered inside tests to plug the in-memory index into pipeline classes.
internal sealed class InMemoryVectorIndexFactory : IVectorIndexFactory
{
    public IVectorIndex Create(int dimension) => InMemoryVectorIndex.Create(dimension);

    public IVectorIndex Load(string path) => InMemoryVectorIndex.Load(path);
}
