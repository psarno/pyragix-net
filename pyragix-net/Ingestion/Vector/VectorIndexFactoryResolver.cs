using System;

namespace PyRagix.Net.Ingestion.Vector;

/// <summary>
/// Discovers the most capable vector index implementation for the current operating system.
/// </summary>
internal static class VectorIndexFactoryResolver
{
    /// <summary>
    /// Returns the default vector index factory for the current platform.
    /// Prefers FaissNet on Windows where native binaries are available, otherwise falls back to the managed implementation.
    /// </summary>
    public static IVectorIndexFactory GetDefault()
    {
        if (OperatingSystem.IsWindows())
        {
            return FaissVectorIndexFactory.Instance;
        }

        return ManagedVectorIndexFactory.Instance;
    }
}
