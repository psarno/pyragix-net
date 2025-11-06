using System;
using System.IO;
using System.Net.Http;
using System.Threading.Tasks;
using Polly;
using Polly.Retry;

namespace PyRagix.Net.Core.Resilience;

/// <summary>
/// Centralises retry policy construction so ingestion and retrieval components can handle transient failures consistently.
/// </summary>
internal static class RetryPolicies
{
    public static AsyncRetryPolicy CreateHttpPolicy(string operationName, int retryCount = 3)
    {
        return Policy
            .Handle<HttpRequestException>()
            .Or<TaskCanceledException>()
            .Or<TimeoutException>()
            .Or<IOException>()
            .WaitAndRetryAsync(
                retryCount,
                attempt => TimeSpan.FromSeconds(Math.Pow(2, attempt - 1)),
                (exception, delay, attempt, _) =>
                {
                    Console.WriteLine($"Retrying {operationName} (attempt {attempt}/{retryCount}) in {delay.TotalSeconds:F1}s due to: {exception.Message}");
                });
    }

    public static AsyncRetryPolicy CreateAsyncPolicy(string operationName, int retryCount = 3)
    {
        return Policy
            .Handle<Exception>()
            .WaitAndRetryAsync(
                retryCount,
                attempt => TimeSpan.FromMilliseconds(200 * Math.Pow(2, attempt - 1)),
                (exception, delay, attempt, _) =>
                {
                    Console.WriteLine($"Retrying {operationName} (attempt {attempt}/{retryCount}) in {delay.TotalMilliseconds:F0}ms due to: {exception.Message}");
                });
    }

    public static RetryPolicy CreateSyncPolicy(string operationName, int retryCount = 3)
    {
        return Policy
            .Handle<Exception>()
            .WaitAndRetry(
                retryCount,
                attempt => TimeSpan.FromMilliseconds(200 * Math.Pow(2, attempt - 1)),
                (exception, delay, attempt, _) =>
                {
                    Console.WriteLine($"Retrying {operationName} (attempt {attempt}/{retryCount}) in {delay.TotalMilliseconds:F0}ms due to: {exception.Message}");
                });
    }
}
