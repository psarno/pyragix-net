using System;
using System.IO;
using System.Linq;

namespace PyRagix.Net.Tests.TestInfrastructure;

/// <summary>
/// Creates an isolated temporary directory for tests and cleans it up on dispose.
/// </summary>
public sealed class TempDirectory : IDisposable
{
    public string Root { get; }

    public TempDirectory()
    {
        Root = Path.Combine(System.IO.Path.GetTempPath(), "pyragix-net-tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(Root);
    }

    public string Resolve(params string[] segments)
    {
        if (segments.Length == 0)
        {
            return Root;
        }

        return Path.Combine(new[] { Root }.Concat(segments).ToArray());
    }

    public void Dispose()
    {
        try
        {
            if (Directory.Exists(Root))
            {
                Directory.Delete(Root, recursive: true);
            }
        }
        catch
        {
            // Best-effort cleanup; ignore failures so tests do not flake.
        }
    }
}
