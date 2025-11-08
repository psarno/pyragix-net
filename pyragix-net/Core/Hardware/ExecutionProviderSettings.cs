using Microsoft.ML.OnnxRuntime;
using PyRagix.Net.Config;
using PyRagix.Net.Core.Exceptions;

namespace PyRagix.Net.Core.Hardware;

/// <summary>
/// Configurable execution provider preference that controls whether ONNX Runtime targets CPU, GPU, or auto-detection.
/// </summary>
public enum ExecutionProviderPreference
{
    Auto,
    Cpu,
    Gpu
}

/// <summary>
/// Represents the concrete provider selected after probing the host.
/// </summary>
public enum ExecutionProviderType
{
    Cpu,
    Gpu
}

/// <summary>
/// Captures the outcome of probing for GPU support alongside the resolved provider that will be used.
/// </summary>
public sealed class ExecutionProviderStatus
{
    public ExecutionProviderStatus(
        ExecutionProviderPreference preference,
        ExecutionProviderType selectedProvider,
        bool gpuAvailable,
        bool fallbackToCpu,
        string? note = null)
    {
        Preference = preference;
        SelectedProvider = selectedProvider;
        GpuAvailable = gpuAvailable;
        FallbackToCpu = fallbackToCpu;
        Note = note;
    }

    public ExecutionProviderPreference Preference { get; }

    public ExecutionProviderType SelectedProvider { get; }

    public bool GpuAvailable { get; }

    public bool FallbackToCpu { get; }

    public string? Note { get; }

    public bool UsingGpu => SelectedProvider == ExecutionProviderType.Gpu;

    public bool ShouldWarnCpuPreference => Preference == ExecutionProviderPreference.Cpu && GpuAvailable;

    public static ExecutionProviderStatus CpuOnly(ExecutionProviderPreference preference, bool gpuAvailable, bool fallbackToCpu, string? note = null)
    {
        return new ExecutionProviderStatus(preference, ExecutionProviderType.Cpu, gpuAvailable, fallbackToCpu, note);
    }
}

/// <summary>
/// Central helper that probes CUDA availability and creates pre-configured session options for ONNX Runtime consumers.
/// </summary>
public static class OnnxExecutionProviderResolver
{
    private static readonly object StatusLock = new();

    public static ExecutionProviderStatus EnsureStatus(PyRagixConfig config)
    {
        if (config.ExecutionProviderStatus is not null)
        {
            return config.ExecutionProviderStatus;
        }

        lock (StatusLock)
        {
            if (config.ExecutionProviderStatus is not null)
            {
                return config.ExecutionProviderStatus;
            }

            var status = Probe(config);
            config.ExecutionProviderStatus = status;
            return status;
        }
    }

    public static SessionOptions CreateSessionOptions(PyRagixConfig config, string consumerName)
    {
        var status = EnsureStatus(config);
        var options = new SessionOptions();
        if (status.SelectedProvider == ExecutionProviderType.Gpu)
        {
            try
            {
                options.AppendExecutionProvider_CUDA(config.GpuDeviceId);
            }
            catch (Exception ex)
            {
                throw new GpuExecutionProviderInitializationException(
                    $"{consumerName} attempted to initialise the CUDA execution provider but failed: {ex.Message}",
                    ex);
            }
        }

        return options;
    }

    private static ExecutionProviderStatus Probe(PyRagixConfig config)
    {
        var preference = config.ExecutionProviderPreference;
        var gpuAvailable = ProbeCudaProvider(config.GpuDeviceId);

        if (preference == ExecutionProviderPreference.Gpu && !gpuAvailable)
        {
            throw new GpuExecutionProviderUnavailableException(
                "GPU execution was requested but no CUDA-capable device or drivers were detected.");
        }

        if (preference == ExecutionProviderPreference.Cpu)
        {
            return ExecutionProviderStatus.CpuOnly(preference, gpuAvailable, fallbackToCpu: false, note: null);
        }

        if ((preference == ExecutionProviderPreference.Gpu || preference == ExecutionProviderPreference.Auto) && gpuAvailable)
        {
            return new ExecutionProviderStatus(preference, ExecutionProviderType.Gpu, gpuAvailable, fallbackToCpu: false);
        }

        var note = preference == ExecutionProviderPreference.Gpu
            ? "GPU preference ignored because CUDA is not available."
            : "GPU acceleration not detected; falling back to CPU execution.";
        var fallback = preference != ExecutionProviderPreference.Cpu;
        return ExecutionProviderStatus.CpuOnly(preference, gpuAvailable, fallback, note);
    }

    private static bool ProbeCudaProvider(int deviceId)
    {
        try
        {
            using var options = new SessionOptions();
            options.AppendExecutionProvider_CUDA(deviceId);
            return true;
        }
        catch
        {
            return false;
        }
    }
}
