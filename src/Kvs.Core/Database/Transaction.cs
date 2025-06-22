#if !NET472
#nullable enable
#endif

using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Data;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Kvs.Core.Serialization;
using Kvs.Core.Storage;

namespace Kvs.Core.Database;

/// <summary>
/// Represents a database transaction with ACID guarantees.
/// </summary>
public class Transaction : ITransaction
{
    private readonly string transactionId;
    private readonly Database database;
    private readonly IsolationLevel isolationLevel;
    private readonly DateTime startTime;
    private readonly ConcurrentDictionary<string, TransactionOperation> operations;
    private readonly ConcurrentDictionary<string, object> readCache;
    private readonly SemaphoreSlim transactionLock;
    private readonly ISerializer serializer;
    private readonly ConcurrentDictionary<string, long> keyToPageIdMap;
    private TransactionState state;
    private TimeSpan timeout;
#if NET8_0_OR_GREATER
    private Timer? timeoutTimer;
#else
    private Timer timeoutTimer;
#endif
    private bool disposed;

    /// <summary>
    /// Gets the unique identifier for the transaction.
    /// </summary>
    public string Id => this.transactionId;

    /// <summary>
    /// Gets a value indicating whether the transaction is read-only.
    /// </summary>
    public bool IsReadOnly => this.operations.Count == 0;

    /// <summary>
    /// Gets the isolation level of the transaction.
    /// </summary>
    public IsolationLevel IsolationLevel => this.isolationLevel;

    /// <summary>
    /// Gets the current state of the transaction.
    /// </summary>
    public TransactionState State
    {
        get => this.state;
        internal set => this.state = value;
    }

    /// <summary>
    /// Gets the timestamp when the transaction started.
    /// </summary>
    public DateTime StartTime => this.startTime;

    /// <summary>
    /// Initializes a new instance of the <see cref="Transaction"/> class.
    /// </summary>
    /// <param name="transactionId">The transaction ID.</param>
    /// <param name="database">The database instance.</param>
    /// <param name="isolationLevel">The isolation level.</param>
    internal Transaction(string transactionId, Database database, IsolationLevel isolationLevel)
    {
        this.transactionId = transactionId ?? throw new ArgumentNullException(nameof(transactionId));
        this.database = database ?? throw new ArgumentNullException(nameof(database));
        this.isolationLevel = isolationLevel;
        this.startTime = DateTime.UtcNow;
        this.operations = new ConcurrentDictionary<string, TransactionOperation>();
        this.readCache = new ConcurrentDictionary<string, object>();
        this.transactionLock = new SemaphoreSlim(1, 1);
        this.serializer = new BinarySerializer();
        this.state = TransactionState.Active;
        this.timeout = TimeSpan.FromMinutes(5);
        this.keyToPageIdMap = new ConcurrentDictionary<string, long>();

        this.StartTimeoutTimer();
    }

    /// <summary>
    /// Sets the timeout for the transaction.
    /// </summary>
    /// <param name="timeout">The timeout duration.</param>
    public void SetTimeout(TimeSpan timeout)
    {
        this.ThrowIfNotActive();

        this.timeout = timeout;
        this.RestartTimeoutTimer();
    }

    /// <summary>
    /// Reads a value from the database.
    /// </summary>
    /// <typeparam name="T">The type of the value.</typeparam>
    /// <param name="key">The key to read.</param>
    /// <returns>A task that represents the asynchronous operation. The task result contains the value, or null if not found.</returns>
#if NET472
    public async Task<T> ReadAsync<T>(string key)
#else
    public async Task<T?> ReadAsync<T>(string key)
#endif
    {
        this.ThrowIfDisposed();
        this.ThrowIfNotActive();

        if (string.IsNullOrEmpty(key))
        {
            throw new ArgumentException("Key cannot be null or empty.", nameof(key));
        }

        this.RestartTimeoutTimer();

        // Acquire read lock based on isolation level
        var lockManager = this.database.GetLockManager();
        var lockAcquired = false;

        try
        {
            if (this.isolationLevel == IsolationLevel.Serializable)
            {
                // Serializable requires read locks to be held until commit
                lockAcquired = await lockManager.AcquireReadLockAsync(this.transactionId, key, TimeSpan.FromSeconds(30)).ConfigureAwait(false);
                if (!lockAcquired)
                {
                    throw new TimeoutException($"Failed to acquire read lock for key '{key}'");
                }
            }

            await this.transactionLock.WaitAsync().ConfigureAwait(false);
            try
            {
                // Check if we have a pending write for this key
                if (this.operations.TryGetValue(key, out var operation))
                {
                    if (operation.Type == OperationType.Delete)
                    {
                        // Deleted in this transaction
                        return default;
                    }

                    if (operation.Type == OperationType.Update)
                    {
                        if (operation.NewValue is T value)
                        {
                            return value;
                        }

                        // Type mismatch or null - return default
                        return default;
                    }
                }

                // Check read cache
                if (this.readCache.TryGetValue(key, out var cachedValue))
                {
                    return (T)cachedValue;
                }

                // Read from storage
                var result = await this.GetAsync<T>(key).ConfigureAwait(false);

                // Cache the result only if it's not null
                if (result != null)
                {
                    this.readCache[key] = result;
                }

                // Log the read operation
                var entry = new TransactionLogEntry
                {
                    TransactionId = this.transactionId,
                    Type = TransactionLogEntryType.Read,
                    Key = key,
                    Timestamp = DateTime.UtcNow
                };

                var wal = this.database.GetDatabaseWAL();
                await wal.WriteEntryAsync(entry).ConfigureAwait(false);

            return result;
            }
            finally
            {
                this.transactionLock.Release();
            }
        }
        finally
        {
            // For Read Committed, release read lock immediately after reading
            if (lockAcquired && this.isolationLevel == IsolationLevel.ReadCommitted)
            {
                await lockManager.ReleaseLockAsync(this.transactionId, key).ConfigureAwait(false);
            }
        }
    }

    /// <summary>
    /// Writes a value to the database.
    /// </summary>
    /// <typeparam name="T">The type of the value.</typeparam>
    /// <param name="key">The key to write.</param>
    /// <param name="value">The value to write.</param>
    /// <returns>A task that represents the asynchronous operation.</returns>
    public async Task WriteAsync<T>(string key, T value)
    {
        this.ThrowIfDisposed();
        this.ThrowIfNotActive();

        if (string.IsNullOrEmpty(key))
        {
            throw new ArgumentException("Key cannot be null or empty.", nameof(key));
        }

        if (value == null)
        {
            throw new ArgumentNullException(nameof(value));
        }

        this.RestartTimeoutTimer();

        // Acquire write lock - required for all isolation levels
        var lockManager = this.database.GetLockManager();
        var lockAcquired = await lockManager.AcquireWriteLockAsync(this.transactionId, key, TimeSpan.FromSeconds(30)).ConfigureAwait(false);
        if (!lockAcquired)
        {
            throw new TimeoutException($"Failed to acquire write lock for key '{key}'");
        }

        await this.transactionLock.WaitAsync().ConfigureAwait(false);
        try
        {
            // Get the old value for rollback
#if NET8_0_OR_GREATER
            object? oldValue = null;
#else
            object oldValue = null;
#endif
            if (this.operations.TryGetValue(key, out var existingOp))
            {
                oldValue = existingOp.OldValue;
            }
            else
            {
                oldValue = await this.GetOldValueAsync<T>(key).ConfigureAwait(false);
            }

            // Create operation
            var operation = new TransactionOperation
            {
                Type = OperationType.Update,
                Key = key,
                OldValue = oldValue,
                NewValue = value,
                Timestamp = DateTime.UtcNow
            };

            this.operations[key] = operation;

            // Log the write operation
            var entry = new TransactionLogEntry
            {
                TransactionId = this.transactionId,
                Type = TransactionLogEntryType.Write,
                Key = key,
                Data = this.serializer.Serialize(value),
                Timestamp = DateTime.UtcNow
            };

            var wal = this.database.GetDatabaseWAL();
            await wal.WriteEntryAsync(entry).ConfigureAwait(false);
        }
        finally
        {
            this.transactionLock.Release();
        }
    }

    /// <summary>
    /// Deletes a value from the database.
    /// </summary>
    /// <param name="key">The key to delete.</param>
    /// <returns>A task that represents the asynchronous operation. The task result indicates whether the deletion was successful.</returns>
    public async Task<bool> DeleteAsync(string key)
    {
        this.ThrowIfDisposed();
        this.ThrowIfNotActive();

        if (string.IsNullOrEmpty(key))
        {
            throw new ArgumentException("Key cannot be null or empty.", nameof(key));
        }

        this.RestartTimeoutTimer();

        // Acquire write lock for delete operation
        var lockManager = this.database.GetLockManager();
        var lockAcquired = await lockManager.AcquireWriteLockAsync(this.transactionId, key, TimeSpan.FromSeconds(30)).ConfigureAwait(false);
        if (!lockAcquired)
        {
            throw new TimeoutException($"Failed to acquire write lock for key '{key}'");
        }

        await this.transactionLock.WaitAsync().ConfigureAwait(false);
        try
        {
            // Check if key exists
            var exists = await this.ExistsAsync(key).ConfigureAwait(false);
            if (!exists)
            {
                return false;
            }

            // Get the old value for rollback
#if NET8_0_OR_GREATER
            object? oldValue = null;
#else
            object oldValue = null;
#endif
            if (this.operations.TryGetValue(key, out var existingOp))
            {
                oldValue = existingOp.OldValue;
            }
            else
            {
                oldValue = await this.GetOldValueAsync<object>(key).ConfigureAwait(false);
            }

            // Create operation
            var operation = new TransactionOperation
            {
                Type = OperationType.Delete,
                Key = key,
                OldValue = oldValue,
                NewValue = null,
                Timestamp = DateTime.UtcNow
            };

            this.operations[key] = operation;

            // Remove from read cache since it's deleted
            this.readCache.TryRemove(key, out _);

            // Log the delete operation
            var entry = new TransactionLogEntry
            {
                TransactionId = this.transactionId,
                Type = TransactionLogEntryType.Delete,
                Key = key,
                Timestamp = DateTime.UtcNow
            };

            var wal = this.database.GetDatabaseWAL();
            await wal.WriteEntryAsync(entry).ConfigureAwait(false);

            return true;
        }
        finally
        {
            this.transactionLock.Release();
        }
    }

    /// <summary>
    /// Commits the transaction.
    /// </summary>
    /// <returns>A task that represents the asynchronous operation.</returns>
    public async Task CommitAsync()
    {
        this.ThrowIfDisposed();

        // Ensure idempotency - multiple commits should not fail
        if (this.state == TransactionState.Committed)
        {
            return;
        }

        if (this.state != TransactionState.Active)
        {
            throw new InvalidOperationException($"Transaction is in {this.state} state. Expected Active state.");
        }

        await this.transactionLock.WaitAsync().ConfigureAwait(false);
        try
        {
            this.state = TransactionState.Preparing;

            // Log prepare
            var prepareEntry = new TransactionLogEntry
            {
                TransactionId = this.transactionId,
                Type = TransactionLogEntryType.Prepare,
                Timestamp = DateTime.UtcNow
            };

            var wal = this.database.GetDatabaseWAL();
            await wal.WriteEntryAsync(prepareEntry).ConfigureAwait(false);

            this.state = TransactionState.Prepared;

            // Apply all operations
            foreach (var operation in this.operations.Values)
            {
                await this.ApplyOperationAsync(operation).ConfigureAwait(false);
            }

            this.state = TransactionState.Committing;

            // Log commit
            var commitEntry = new TransactionLogEntry
            {
                TransactionId = this.transactionId,
                Type = TransactionLogEntryType.Commit,
                Timestamp = DateTime.UtcNow
            };

            await wal.WriteEntryAsync(commitEntry).ConfigureAwait(false);
            await wal.FlushAsync().ConfigureAwait(false);

            this.state = TransactionState.Committed;

            this.StopTimeoutTimer();

            // Release all locks held by this transaction
            var lockManager = this.database.GetLockManager();
            await lockManager.ReleaseAllLocksAsync(this.transactionId).ConfigureAwait(false);

            this.database.RemoveTransaction(this.transactionId);
        }
        catch
        {
            await this.RollbackAsync().ConfigureAwait(false);
            throw;
        }
        finally
        {
            this.transactionLock.Release();
        }
    }

    /// <summary>
    /// Rolls back the transaction.
    /// </summary>
    /// <returns>A task that represents the asynchronous operation.</returns>
    public async Task RollbackAsync()
    {
        this.ThrowIfDisposed();

        if (this.state == TransactionState.Committed || this.state == TransactionState.Aborted)
        {
            return;
        }

        await this.transactionLock.WaitAsync().ConfigureAwait(false);
        try
        {
            this.state = TransactionState.Aborting;

            // Log rollback
            var rollbackEntry = new TransactionLogEntry
            {
                TransactionId = this.transactionId,
                Type = TransactionLogEntryType.Rollback,
                Timestamp = DateTime.UtcNow
            };

            var wal = this.database.GetDatabaseWAL();
            await wal.WriteEntryAsync(rollbackEntry).ConfigureAwait(false);

            this.state = TransactionState.Aborted;

            this.StopTimeoutTimer();

            // Release all locks held by this transaction
            var lockManager = this.database.GetLockManager();
            await lockManager.ReleaseAllLocksAsync(this.transactionId).ConfigureAwait(false);

            this.database.RemoveTransaction(this.transactionId);
        }
        finally
        {
            this.transactionLock.Release();
        }
    }

    /// <summary>
    /// Disposes the transaction.
    /// </summary>
    public void Dispose()
    {
        this.Dispose(true);
        GC.SuppressFinalize(this);
    }

    /// <summary>
    /// Disposes the transaction.
    /// </summary>
    /// <param name="disposing">true to release both managed and unmanaged resources; false to release only unmanaged resources.</param>
    protected virtual void Dispose(bool disposing)
    {
        if (!this.disposed)
        {
            if (disposing)
            {
                if (this.state == TransactionState.Active ||
                    this.state == TransactionState.Preparing ||
                    this.state == TransactionState.Prepared)
                {
                    this.RollbackAsync().GetAwaiter().GetResult();
                }

                this.StopTimeoutTimer();
                this.transactionLock?.Dispose();
            }

            this.disposed = true;
        }
    }

    private async Task ApplyOperationAsync(TransactionOperation operation)
    {
        var pageManager = this.database.GetPageManager();

        switch (operation.Type)
        {
            case OperationType.Update:
                var page = await this.AllocateOrGetPageForKeyAsync(operation.Key).ConfigureAwait(false);
                var data = this.serializer.Serialize(operation.NewValue);
                page.WriteData(data.Span);
                await pageManager.WritePageAsync(page).ConfigureAwait(false);
                break;

            case OperationType.Delete:
                var pageId = this.GetPageIdForKey(operation.Key);
                if (pageId > 0)
                {
                    // In a real implementation, we would deallocate the page
                    // For now, we just remove the mapping
                    this.keyToPageIdMap.TryRemove(operation.Key, out _);
                }

                break;
        }
    }

#if NET8_0_OR_GREATER
    private async Task<T?> GetAsync<T>(string key)
#else
    private async Task<T> GetAsync<T>(string key)
#endif
    {
        var pageManager = this.database.GetPageManager();
        var pageId = this.GetPageIdForKey(key);
        if (pageId <= 0)
        {
            return default;
        }

        // For now, return default since we're not implementing real storage
        // In a real implementation, this would read from the collection's storage
        _ = pageManager; // Suppress warning
        await Task.CompletedTask.ConfigureAwait(false); // Suppress async warning
        return default;
    }

    private async Task<bool> ExistsAsync(string key)
    {
        // Check if we have a pending operation for this key
        if (this.operations.TryGetValue(key, out var operation))
        {
            // If it's deleted in this transaction, it doesn't exist
            if (operation.Type == OperationType.Delete)
            {
                return false;
            }

            // If it's updated in this transaction, it exists
            if (operation.Type == OperationType.Update)
            {
                return true;
            }
        }

        // For now, assume keys don't exist in storage
        await Task.CompletedTask.ConfigureAwait(false);
        return false;
    }

#if NET8_0_OR_GREATER
    private async Task<T?> GetOldValueAsync<T>(string key)
#else
    private async Task<T> GetOldValueAsync<T>(string key)
#endif
    {
        if (this.operations.TryGetValue(key, out var existingOp))
        {
#if NET8_0_OR_GREATER
            return (T?)existingOp.OldValue;
#else
            return (T)existingOp.OldValue;
#endif
        }

        return await this.GetAsync<T>(key).ConfigureAwait(false);
    }

    private long GetPageIdForKey(string key)
    {
        if (this.keyToPageIdMap.TryGetValue(key, out var pageId))
        {
            return pageId;
        }

        // For now, use a fixed page ID for transaction data
        // In a real implementation, this would look up the key in an index
        return 1;
    }

    private async Task<Page> AllocateOrGetPageForKeyAsync(string key)
    {
        var pageManager = this.database.GetPageManager();
        var pageId = this.GetPageIdForKey(key);

        // Check if we already have a page for this key
        if (this.keyToPageIdMap.TryGetValue(key, out var existingPageId))
        {
            try
            {
                return await pageManager.GetPageAsync(existingPageId).ConfigureAwait(false);
            }
            catch (InvalidOperationException)
            {
                // Page doesn't exist, allocate a new one
            }
        }

        // Allocate a new page for this transaction
        var page = await pageManager.AllocatePageAsync(PageType.Data).ConfigureAwait(false);
        this.keyToPageIdMap[key] = page.PageId;
        return page;
    }

    private void StartTimeoutTimer()
    {
        this.timeoutTimer = new Timer(this.OnTimeout, null, this.timeout, Timeout.InfiniteTimeSpan);
    }

    private void RestartTimeoutTimer()
    {
        this.StopTimeoutTimer();
        this.StartTimeoutTimer();
    }

    private void StopTimeoutTimer()
    {
        this.timeoutTimer?.Dispose();
        this.timeoutTimer = null;
    }

#if NET8_0_OR_GREATER
    private async void OnTimeout(object? state)
#else
    private async void OnTimeout(object state)
#endif
    {
        if (this.state == TransactionState.Active ||
            this.state == TransactionState.Preparing ||
            this.state == TransactionState.Prepared)
        {
            this.state = TransactionState.Aborted;
            await this.RollbackAsync().ConfigureAwait(false);
        }
    }

    private void ThrowIfDisposed()
    {
        if (this.disposed)
        {
            throw new ObjectDisposedException(nameof(Transaction));
        }
    }

    private void ThrowIfNotActive()
    {
        if (this.state != TransactionState.Active)
        {
            throw new InvalidOperationException($"Transaction is in {this.state} state. Expected Active state.");
        }
    }

    /// <summary>
    /// Represents a transaction operation.
    /// </summary>
    private sealed class TransactionOperation
    {
        /// <summary>
        /// Gets or sets the operation type.
        /// </summary>
        public OperationType Type { get; set; }

        /// <summary>
        /// Gets or sets the key.
        /// </summary>
        public string Key { get; set; } = string.Empty;

        /// <summary>
        /// Gets or sets the old value.
        /// </summary>
#if NET8_0_OR_GREATER
        public object? OldValue { get; set; }
#else
        public object OldValue { get; set; }
#endif

        /// <summary>
        /// Gets or sets the new value.
        /// </summary>
#if NET8_0_OR_GREATER
        public object? NewValue { get; set; }
#else
        public object NewValue { get; set; }
#endif

        /// <summary>
        /// Gets or sets the timestamp.
        /// </summary>
        public DateTime Timestamp { get; set; }
    }
}
