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
    private readonly TransactionData transactionData;
    private TransactionState state;
    private TimeSpan timeout;
#if NET8_0_OR_GREATER
    private Timer? timeoutTimer;
#else
    private Timer timeoutTimer;
#endif
    private bool disposed;
    private readonly object stateLock = new object();
    private readonly ManualResetEventSlim abortedEvent = new ManualResetEventSlim(false);
    private readonly CancellationTokenSource abortCancellationSource = new CancellationTokenSource();
    private bool isDeadlockVictim;

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
        get
        {
            lock (this.stateLock)
            {
                return this.state;
            }
        }

        internal set
        {
            lock (this.stateLock)
            {
                this.state = value;
                if (value == TransactionState.Aborted)
                {
                    this.abortedEvent.Set();
                    this.abortCancellationSource.Cancel();
                }
            }
        }
    }

    /// <summary>
    /// Marks this transaction as a deadlock victim.
    /// </summary>
    internal void MarkAsDeadlockVictim()
    {
        lock (this.stateLock)
        {
            this.isDeadlockVictim = true;
            this.state = TransactionState.Aborted;
            this.abortedEvent.Set();
            this.abortCancellationSource.Cancel();
        }
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
        this.transactionData = new TransactionData();

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
                try
                {
                    lockAcquired = await lockManager.AcquireReadLockAsync(this.transactionId, key, TimeSpan.FromSeconds(30), this.abortCancellationSource.Token).ConfigureAwait(false);
                }
                catch (OperationCanceledException)
                {
                    // Check if we were aborted due to deadlock
                    if (this.state == TransactionState.Aborted)
                    {
                        throw new DeadlockException(this.transactionId, new[] { this.transactionId });
                    }

                    throw new TimeoutException($"Failed to acquire read lock for key '{key}'");
                }

                if (!lockAcquired)
                {
                    // Check if we were aborted due to deadlock
                    if (this.state == TransactionState.Aborted)
                    {
                        throw new DeadlockException(this.transactionId, new[] { this.transactionId });
                    }

                    throw new TimeoutException($"Failed to acquire read lock for key '{key}'");
                }
            }

            await this.transactionLock.WaitAsync().ConfigureAwait(false);
            try
            {
                // First check if we have a pending operation for this key in the transaction
                if (this.operations.TryGetValue(key, out var operation))
                {
                    if (operation.Type == TransactionOperationType.Delete)
                    {
                        // Deleted in this transaction
                        return default;
                    }

                    if (operation.Type == TransactionOperationType.Update && operation.NewValue != null)
                    {
                        // Return the value from the operation
                        if (operation.NewValue is T typedValue)
                        {
                            return typedValue;
                        }

                        // Try to convert if it's a Document and we need a different type
                        if (operation.NewValue is Document doc && typeof(T) != typeof(Document))
                        {
                            try
                            {
                                var method = doc.GetType().GetMethod("ToObject")?.MakeGenericMethod(typeof(T));
                                if (method != null)
                                {
                                    var invokeResult = method.Invoke(doc, null);
                                    return invokeResult != null ? (T)invokeResult : default;
                                }
                            }
                            catch
                            {
                                // Ignore and try default conversion
                            }
                        }

                        // Try direct cast as last resort
                        try
                        {
                            return (T)operation.NewValue;
                        }
                        catch
                        {
                            return default;
                        }
                    }
                }

                // Check read cache only for repeatable read and serializable
                // Read committed should always see the latest committed version
                if (this.isolationLevel != IsolationLevel.ReadCommitted && this.readCache.TryGetValue(key, out var cachedValue))
                {
                    return (T)cachedValue;
                }

                // Read from storage with isolation level awareness
                var result = await this.GetWithIsolationAsync<T>(key).ConfigureAwait(false);

                // Cache the result only if it's not null and not ReadCommitted
                // Read committed should not cache to allow seeing newly committed data
                if (result != null && this.isolationLevel != IsolationLevel.ReadCommitted)
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

                return result!;
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

        // Check if we already have a lock on this key
        var lockManager = this.database.GetLockManager();
        var currentLockType = await lockManager.GetLockTypeAsync(this.transactionId, key).ConfigureAwait(false);
        var lockAcquired = false;

        if (currentLockType == LockType.Read)
        {
            // Upgrade from read to write lock
            try
            {
                lockAcquired = await lockManager.UpgradeLockAsync(this.transactionId, key, TimeSpan.FromSeconds(30), this.abortCancellationSource.Token).ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                // Check if we were aborted due to deadlock
                if (this.state == TransactionState.Aborted)
                {
                    throw new DeadlockException(this.transactionId, new[] { this.transactionId });
                }

                throw new TimeoutException($"Failed to upgrade lock for key '{key}'");
            }
        }
        else if (currentLockType == LockType.None)
        {
            // Acquire write lock - required for all isolation levels
            try
            {
                lockAcquired = await lockManager.AcquireWriteLockAsync(this.transactionId, key, TimeSpan.FromSeconds(30), this.abortCancellationSource.Token).ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                // Check if we were aborted due to deadlock
                if (this.state == TransactionState.Aborted)
                {
                    throw new DeadlockException(this.transactionId, new[] { this.transactionId });
                }

                throw new TimeoutException($"Failed to acquire write lock for key '{key}'");
            }
        }
        else
        {
            // Already have write lock
            lockAcquired = true;
        }

        if (!lockAcquired)
        {
            // Check if we were aborted due to deadlock
            if (this.state == TransactionState.Aborted)
            {
                throw new DeadlockException(this.transactionId, new[] { this.transactionId });
            }

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
                Type = TransactionOperationType.Update,
                Key = key,
                OldValue = oldValue,
                NewValue = value,
                Timestamp = DateTime.UtcNow
            };

            this.operations[key] = operation;

            // Store in transaction data
            var parts = key.Split('/');
            if (parts.Length == 2)
            {
                if (value is Document doc)
                {
                    this.transactionData.SetDocument(parts[0], parts[1], doc, TransactionOperationType.Update);
                }
                else
                {
                    // For non-Document types, we still need to track them
                    // The operations dictionary already has the value, so we just need to ensure
                    // transactionData knows about the operation
                    this.transactionData.SetDocument(parts[0], parts[1], null, TransactionOperationType.Update);
                }
            }

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

            // Update the read cache to reflect the new value within this transaction
            if (this.isolationLevel != IsolationLevel.ReadCommitted)
            {
                this.readCache[key] = value!;
            }
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
        bool lockAcquired;
        try
        {
            using (var linkedCts = CancellationTokenSource.CreateLinkedTokenSource(this.abortCancellationSource.Token))
            {
                linkedCts.CancelAfter(TimeSpan.FromSeconds(30));
                lockAcquired = await lockManager.AcquireWriteLockAsync(this.transactionId, key, TimeSpan.FromSeconds(30), linkedCts.Token).ConfigureAwait(false);
            }
        }
        catch (OperationCanceledException)
        {
            // Check if we were aborted due to deadlock
            if (this.state == TransactionState.Aborted)
            {
                throw new DeadlockException(this.transactionId, new[] { this.transactionId });
            }

            throw new TimeoutException($"Failed to acquire write lock for key '{key}'");
        }

        if (!lockAcquired)
        {
            // Check if we were aborted due to deadlock
            if (this.state == TransactionState.Aborted)
            {
                throw new DeadlockException(this.transactionId, new[] { this.transactionId });
            }

            throw new TimeoutException($"Failed to acquire write lock for key '{key}'");
        }

        await this.transactionLock.WaitAsync().ConfigureAwait(false);
        try
        {
            // Check if we already have an operation for this key
            if (this.operations.TryGetValue(key, out var existingOperation))
            {
                // If it was previously deleted, it doesn't exist
                if (existingOperation.Type == TransactionOperationType.Delete)
                {
                    return false;
                }

                // If it was previously updated, it exists
                // Continue to get the old value
            }
            else
            {
                // Check if key exists in the database
                var exists = await this.ExistsAsync(key).ConfigureAwait(false);
                if (!exists)
                {
                    return false;
                }
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
                Type = TransactionOperationType.Delete,
                Key = key,
                OldValue = oldValue,
                NewValue = null,
                Timestamp = DateTime.UtcNow
            };

            this.operations[key] = operation;

            // Store deletion in transaction data
            var parts = key.Split('/');
            if (parts.Length == 2)
            {
                this.transactionData.SetDocument(parts[0], parts[1], null, TransactionOperationType.Delete);
            }

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

        // Check if aborted (could be set by deadlock detector)
        if (this.state == TransactionState.Aborted)
        {
            if (this.isDeadlockVictim)
            {
                throw new DeadlockException(this.transactionId, new[] { this.transactionId });
            }

            throw new TransactionAbortedException(this.transactionId);
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
            // Already in terminal state - ensure locks are released
            try
            {
                var lockManager = this.database.GetLockManager();
                await lockManager.ReleaseAllLocksAsync(this.transactionId).ConfigureAwait(false);
            }
            catch
            {
                // Ignore errors during cleanup
            }

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
    /// Gets the transaction data.
    /// </summary>
    /// <returns>The transaction data.</returns>
    internal TransactionData GetTransactionData()
    {
        return this.transactionData;
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
                this.abortedEvent?.Dispose();
                this.abortCancellationSource?.Dispose();
            }

            this.disposed = true;
        }
    }

    private async Task ApplyOperationAsync(TransactionOperation operation)
    {
        // Parse the key to get collection name and document ID
        var parts = operation.Key.Split('/');
        if (parts.Length != 2)
        {
            throw new InvalidOperationException($"Invalid key format: {operation.Key}");
        }

        var collectionName = parts[0];
        var documentId = parts[1];

        // Get the collection
        var collection = this.database.GetCollection<Document>(collectionName);

        // Get version manager
        var versionManager = ((ITransactionContext)this.database).VersionManager;
        var commitTime = DateTime.UtcNow;

        switch (operation.Type)
        {
            case TransactionOperationType.Update:
                if (operation.NewValue is Document doc)
                {
                    // Check if document exists
                    var existing = await collection.FindByIdAsync(documentId).ConfigureAwait(false);
                    if (existing != null && collection is Collection<Document> internalCollection)
                    {
                        await internalCollection.UpdateAsync(documentId, doc, this.transactionId).ConfigureAwait(false);
                    }
                    else if (existing != null)
                    {
                        await collection.UpdateAsync(documentId, doc).ConfigureAwait(false);
                    }
                    else if (collection is Collection<Document> insertColl)
                    {
                        // If it doesn't exist, insert it
                        doc.Id = documentId;
                        await insertColl.InsertAsync(doc, this.transactionId).ConfigureAwait(false);
                    }
                    else
                    {
                        // If it doesn't exist, insert it
                        doc.Id = documentId;
                        await collection.InsertAsync(doc).ConfigureAwait(false);
                    }

                    // Add to version manager
                    if (versionManager != null)
                    {
                        versionManager.AddVersion(operation.Key, doc, this.transactionId, commitTime);
                    }
                }

                break;

            case TransactionOperationType.Delete:
                if (collection is Collection<Document> deleteColl)
                {
                    await deleteColl.DeleteAsync(documentId, this.transactionId).ConfigureAwait(false);
                }
                else
                {
                    await collection.DeleteAsync(documentId).ConfigureAwait(false);
                }

                // Mark as deleted in version manager
                if (versionManager != null)
                {
                    versionManager.MarkDeleted(operation.Key, this.transactionId, commitTime);
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
        // Parse the key to get collection name and document ID
        var parts = key.Split('/');
        if (parts.Length != 2)
        {
            return default;
        }

        var collectionName = parts[0];
        var documentId = parts[1];

        // Get the collection
        var collection = this.database.GetCollection<Document>(collectionName);
        if (collection == null)
        {
            return default;
        }

        // Read the document from the collection
        var document = await collection.FindByIdAsync(documentId).ConfigureAwait(false);
        if (document == null)
        {
            return default;
        }

        // Convert to the requested type
        if (typeof(T) == typeof(Document))
        {
            return (T)(object)document;
        }

        // Try to convert using Document's ToObject method if T is a reference type
        if (!typeof(T).IsValueType && typeof(T) != typeof(string))
        {
            try
            {
                var method = document.GetType().GetMethod("ToObject")?.MakeGenericMethod(typeof(T));
                if (method != null)
                {
                    var result = method.Invoke(document, null);
                    return result != null ? (T)result : default;
                }

                return default;
            }
            catch
            {
                return default;
            }
        }

        return default;
    }

#if NET8_0_OR_GREATER
    private async Task<T?> GetWithIsolationAsync<T>(string key)
#else
    private async Task<T> GetWithIsolationAsync<T>(string key)
#endif
    {
        // Parse the key to get collection name and document ID
        var parts = key.Split('/');
        if (parts.Length != 2)
        {
            return default;
        }

        var collectionName = parts[0];
        var documentId = parts[1];

        // Get the collection with transaction context
        var collection = this.database.GetCollection<Document>(collectionName);
        if (collection == null)
        {
            return default;
        }

        // Use transaction-aware read if the collection is our internal type
#if NET8_0_OR_GREATER
        Document? document = null;
#else
        Document document = null;
#endif
        if (collection is Collection<Document> internalCollection)
        {
            document = await internalCollection.FindByIdWithTransactionAsync(documentId, this.transactionId).ConfigureAwait(false);
        }
        else
        {
            document = await collection.FindByIdAsync(documentId).ConfigureAwait(false);
        }

        if (document == null)
        {
            return default;
        }

        // Record the version read for repeatable read and serializable isolation
        if (this.isolationLevel == IsolationLevel.RepeatableRead || this.isolationLevel == IsolationLevel.Serializable)
        {
            this.transactionData.RecordReadVersion(collectionName, documentId, document.Version);
        }

        // Convert to the requested type
        if (typeof(T) == typeof(Document))
        {
            return (T)(object)document;
        }

        // Try to convert using Document's ToObject method if T is a reference type
        if (!typeof(T).IsValueType && typeof(T) != typeof(string))
        {
            try
            {
                var method = document.GetType().GetMethod("ToObject")?.MakeGenericMethod(typeof(T));
                if (method != null)
                {
                    var result = method.Invoke(document, null);
                    return result != null ? (T)result : default;
                }

                return default;
            }
            catch
            {
                return default;
            }
        }

        return default;
    }

    private async Task<bool> ExistsAsync(string key)
    {
        // Check if we have a pending operation for this key
        if (this.operations.TryGetValue(key, out var operation))
        {
            // If it's deleted in this transaction, it doesn't exist
            if (operation.Type == TransactionOperationType.Delete)
            {
                return false;
            }

            // If it's updated in this transaction, it exists
            if (operation.Type == TransactionOperationType.Update)
            {
                return true;
            }
        }

        // Check if the document exists in the collection
        var doc = await this.GetAsync<Document>(key).ConfigureAwait(false);
        return doc != null;
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
    private void OnTimeout(object? state)
#else
    private void OnTimeout(object state)
#endif
    {
        lock (this.stateLock)
        {
            if (this.state == TransactionState.Active ||
                this.state == TransactionState.Preparing ||
                this.state == TransactionState.Prepared)
            {
                this.state = TransactionState.Aborted;
                this.abortedEvent.Set();
                this.abortCancellationSource.Cancel();
            }
            else
            {
                // Already in terminal state
                return;
            }
        }

        // Schedule rollback on ThreadPool to avoid blocking timer thread
        Task.Run(async () =>
        {
            try
            {
                await this.RollbackAsync().ConfigureAwait(false);
            }
            catch
            {
                // Ignore errors during timeout rollback
            }
        });
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
        lock (this.stateLock)
        {
            // Check for deadlock victim status first
            if (this.state == TransactionState.Aborted)
            {
                // Check if this was a deadlock victim to throw appropriate exception
                if (this.isDeadlockVictim)
                {
                    throw new DeadlockException(this.transactionId, new[] { this.transactionId });
                }

                throw new TransactionAbortedException(this.transactionId);
            }

            if (this.state != TransactionState.Active)
            {
                throw new InvalidOperationException($"Transaction is in {this.state} state. Expected Active state.");
            }
        }
    }

    private void ThrowIfDeadlockVictim()
    {
        lock (this.stateLock)
        {
            if (this.state == TransactionState.Aborted)
            {
                // Transaction was aborted as a deadlock victim
                throw new DeadlockException(this.transactionId, new[] { this.transactionId });
            }
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
        public TransactionOperationType Type { get; set; }

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
