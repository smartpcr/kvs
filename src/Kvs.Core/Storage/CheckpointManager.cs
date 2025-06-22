using System;
using System.Threading;
using System.Threading.Tasks;

namespace Kvs.Core.Storage;

/// <summary>
/// Defines the contract for managing database checkpoints.
/// </summary>
public interface ICheckpointManager : IDisposable
{
    /// <summary>
    /// Creates a new checkpoint to compact the write-ahead log.
    /// </summary>
    /// <returns>A task that represents the asynchronous operation. The task result indicates whether the checkpoint succeeded.</returns>
    Task<bool> CreateCheckpointAsync();

    /// <summary>
    /// Gets the log sequence number (LSN) of the last checkpoint.
    /// </summary>
    /// <returns>A task that represents the asynchronous operation. The task result contains the last checkpoint LSN.</returns>
    Task<long> GetLastCheckpointLsnAsync();

    /// <summary>
    /// Determines whether a checkpoint is needed based on configured thresholds.
    /// </summary>
    /// <returns>A task that represents the asynchronous operation. The task result indicates whether a checkpoint is needed.</returns>
    Task<bool> IsCheckpointNeededAsync();

    /// <summary>
    /// Occurs when a checkpoint operation completes.
    /// </summary>
    event EventHandler<CheckpointCompletedEventArgs> CheckpointCompleted;
}

/// <summary>
/// Provides data for the <see cref="ICheckpointManager.CheckpointCompleted"/> event.
/// </summary>
public class CheckpointCompletedEventArgs(long lsn, TimeSpan duration, bool success) : EventArgs
{
    /// <summary>
    /// Gets the log sequence number of the checkpoint.
    /// </summary>
    public long Lsn { get; } = lsn;

    /// <summary>
    /// Gets the duration of the checkpoint operation.
    /// </summary>
    public TimeSpan Duration { get; } = duration;

    /// <summary>
    /// Gets a value indicating whether the checkpoint operation succeeded.
    /// </summary>
    public bool Success { get; } = success;
}

/// <summary>
/// Manages periodic checkpoints to compact the write-ahead log.
/// </summary>
public class CheckpointManager : ICheckpointManager
{
    private readonly ITransactionLog transactionLog;
    private readonly IPageManager pageManager;
    private readonly Timer checkpointTimer;
    private readonly SemaphoreSlim checkpointSemaphore;
    private readonly TimeSpan checkpointInterval;
    private readonly long walSizeThreshold;
    private long lastCheckpointLsn;
    private bool disposed;

#if NET472
    /// <inheritdoc />
    public event EventHandler<CheckpointCompletedEventArgs> CheckpointCompleted;
#else
    /// <inheritdoc />
    public event EventHandler<CheckpointCompletedEventArgs>? CheckpointCompleted;
#endif

    /// <summary>
    /// Initializes a new instance of the <see cref="CheckpointManager"/> class.
    /// </summary>
    /// <param name="transactionLog">The transaction log to manage checkpoints for.</param>
    /// <param name="pageManager">The page manager for flushing pages.</param>
    /// <param name="checkpointInterval">The interval between automatic checkpoints.</param>
    /// <param name="walSizeThreshold">The WAL size threshold that triggers checkpoints.</param>
    public CheckpointManager(
        ITransactionLog transactionLog,
        IPageManager pageManager,
        TimeSpan checkpointInterval = default,
        long walSizeThreshold = 64 * 1024 * 1024) // 64MB default
    {
        this.transactionLog = transactionLog ?? throw new ArgumentNullException(nameof(transactionLog));
        this.pageManager = pageManager ?? throw new ArgumentNullException(nameof(pageManager));
        this.checkpointInterval = checkpointInterval == default ? TimeSpan.FromMinutes(5) : checkpointInterval;
        this.walSizeThreshold = walSizeThreshold;
        this.checkpointSemaphore = new SemaphoreSlim(1, 1);

        // Start periodic checkpoint timer
        this.checkpointTimer = new Timer(this.OnCheckpointTimer, null, this.checkpointInterval, this.checkpointInterval);
    }

    /// <inheritdoc />
    public async Task<bool> CreateCheckpointAsync()
    {
        if (this.disposed)
        {
            return false;
        }

        if (!await this.checkpointSemaphore.WaitAsync(TimeSpan.FromSeconds(30)))
        {
            return false; // Timeout waiting for checkpoint lock
        }

        var startTime = DateTime.UtcNow;

        try
        {
            // Get current LSN
            var currentLsn = await this.transactionLog.GetLastLsnAsync();

            if (currentLsn <= this.lastCheckpointLsn)
            {
                return true; // No new transactions since last checkpoint
            }

            // Flush all dirty pages to disk
            await this.pageManager.FlushAsync();

            // Flush WAL to ensure all entries are persisted
            await this.transactionLog.FlushAsync();

            // Write checkpoint record to WAL
            await this.transactionLog.CheckpointAsync(currentLsn);

            // Update last checkpoint LSN
            this.lastCheckpointLsn = currentLsn;

            var duration = DateTime.UtcNow - startTime;
            this.OnCheckpointCompleted(new CheckpointCompletedEventArgs(currentLsn, duration, true));

            return true;
        }
        catch (Exception)
        {
            var duration = DateTime.UtcNow - startTime;
            this.OnCheckpointCompleted(new CheckpointCompletedEventArgs(this.lastCheckpointLsn, duration, false));
            return false;
        }
        finally
        {
            this.checkpointSemaphore.Release();
        }
    }

    /// <inheritdoc />
    public Task<long> GetLastCheckpointLsnAsync()
    {
        return Task.FromResult(this.lastCheckpointLsn);
    }

    /// <inheritdoc />
    public async Task<bool> IsCheckpointNeededAsync()
    {
        if (this.disposed)
        {
            return false;
        }

        try
        {
            var currentLsn = await this.transactionLog.GetLastLsnAsync();
            var firstLsn = await this.transactionLog.GetFirstLsnAsync();

            // Check if WAL has grown beyond threshold
            var walSize = (currentLsn - Math.Max(firstLsn, this.lastCheckpointLsn)) * EstimatedEntrySize;

            return walSize > this.walSizeThreshold;
        }
        catch
        {
            return false;
        }
    }

#if NET472
    private async void OnCheckpointTimer(object state)
#else
    private async void OnCheckpointTimer(object? state)
#endif
    {
        if (this.disposed)
        {
            return;
        }

        try
        {
            if (await this.IsCheckpointNeededAsync())
            {
                await this.CreateCheckpointAsync();
            }
        }
        catch
        {
            // Log error but don't throw from timer callback
        }
    }

    private void OnCheckpointCompleted(CheckpointCompletedEventArgs args)
    {
        var handler = this.CheckpointCompleted;
        handler?.Invoke(this, args);
    }

    // Estimated average size per WAL entry (for size calculations)
    private const int EstimatedEntrySize = 256;

    /// <inheritdoc />
    public void Dispose()
    {
        if (this.disposed)
        {
            return;
        }

        this.disposed = true;

        this.checkpointTimer?.Dispose();
        this.checkpointSemaphore?.Dispose();
    }

/* Unmerged change from project 'Kvs.Core(net8.0)'
Before:
}
After:
}
*/
}
