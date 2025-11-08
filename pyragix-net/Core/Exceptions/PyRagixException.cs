namespace PyRagix.Net.Core.Exceptions;

/// <summary>
/// Base exception for domain-specific failures surfaced by the PyRagix.Net engine.
/// </summary>
public class PyRagixException : Exception
{
    public PyRagixException(string message)
        : base(message)
    {
    }

    public PyRagixException(string message, Exception innerException)
        : base(message, innerException)
    {
    }
}

/// <summary>
/// Thrown when GPU execution was requested but the CUDA provider is not available on the host.
/// </summary>
public class GpuExecutionProviderUnavailableException : PyRagixException
{
    public GpuExecutionProviderUnavailableException(string message)
        : base(message)
    {
    }
}

/// <summary>
/// Thrown when the CUDA execution provider is detected but could not be initialised (e.g. driver issues).
/// </summary>
public class GpuExecutionProviderInitializationException : PyRagixException
{
    public GpuExecutionProviderInitializationException(string message, Exception innerException)
        : base(message, innerException)
    {
    }
}
