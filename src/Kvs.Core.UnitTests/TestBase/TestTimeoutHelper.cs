using System;
using System.Threading;
using System.Threading.Tasks;

namespace Kvs.Core.UnitTests.TestBase;

/// <summary>
/// Helper class for test timeouts.
/// </summary>
public static class TestTimeoutHelper
{
    /// <summary>
    /// Default timeout for tests.
    /// </summary>
    public static readonly TimeSpan DefaultTimeout = TimeSpan.FromSeconds(5);

    /// <summary>
    /// Maximum timeout for any test.
    /// </summary>
    public static readonly TimeSpan MaxTimeout = TimeSpan.FromSeconds(10);

    /// <summary>
    /// Runs an async operation with a timeout.
    /// </summary>
    /// <typeparam name="T">The type of the result.</typeparam>
    /// <param name="operation">The operation to run.</param>
    /// <param name="timeout">The timeout duration (optional, defaults to DefaultTimeout).</param>
    /// <returns>The result of the operation.</returns>
    public static async Task<T> RunWithTimeoutAsync<T>(Func<CancellationToken, Task<T>> operation, TimeSpan? timeout = null)
    {
        var actualTimeout = timeout ?? DefaultTimeout;
        if (actualTimeout > MaxTimeout)
        {
            actualTimeout = MaxTimeout;
        }

        using var cts = new CancellationTokenSource(actualTimeout);
        try
        {
            return await operation(cts.Token).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (cts.IsCancellationRequested)
        {
            throw new TimeoutException($"Operation timed out after {actualTimeout.TotalSeconds} seconds");
        }
    }

    /// <summary>
    /// Runs an async operation with a timeout.
    /// </summary>
    /// <param name="operation">The operation to run.</param>
    /// <param name="timeout">The timeout duration (optional, defaults to DefaultTimeout).</param>
    /// <returns>A task representing the operation.</returns>
    public static async Task RunWithTimeoutAsync(Func<CancellationToken, Task> operation, TimeSpan? timeout = null)
    {
        var actualTimeout = timeout ?? DefaultTimeout;
        if (actualTimeout > MaxTimeout)
        {
            actualTimeout = MaxTimeout;
        }

        using var cts = new CancellationTokenSource(actualTimeout);
        try
        {
            await operation(cts.Token).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (cts.IsCancellationRequested)
        {
            throw new TimeoutException($"Operation timed out after {actualTimeout.TotalSeconds} seconds");
        }
    }
}