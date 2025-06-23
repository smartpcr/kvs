using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace Kvs.Core.Database;

/// <summary>
/// Manages locks for database transactions with deadlock detection.
/// </summary>
public class LockManager : ILockManager
{
    private readonly ConcurrentDictionary<string, ResourceLock> resourceLocks;
    private readonly ConcurrentDictionary<string, HashSet<string>> transactionLocks;
    private readonly DeadlockDetector deadlockDetector;
    private readonly SemaphoreSlim lockSemaphore;
    private bool disposed;

    /// <summary>
    /// Initializes a new instance of the <see cref="LockManager"/> class.
    /// </summary>
    /// <param name="deadlockDetector">The deadlock detector.</param>
    public LockManager(DeadlockDetector deadlockDetector)
    {
        this.deadlockDetector = deadlockDetector ?? throw new ArgumentNullException(nameof(deadlockDetector));
        this.resourceLocks = new ConcurrentDictionary<string, ResourceLock>();
        this.transactionLocks = new ConcurrentDictionary<string, HashSet<string>>();
        this.lockSemaphore = new SemaphoreSlim(1, 1);

        // Subscribe to deadlock detection
        this.deadlockDetector.DeadlockDetected += this.OnDeadlockDetected;
    }

    /// <summary>
    /// Occurs when a deadlock is detected.
    /// </summary>
#if NET8_0_OR_GREATER
    public event EventHandler<DeadlockEventArgs>? DeadlockDetected;
#else
    public event EventHandler<DeadlockEventArgs> DeadlockDetected;
#endif

    /// <summary>
    /// Acquires a read lock on a resource for a transaction.
    /// </summary>
    /// <param name="transactionId">The ID of the transaction requesting the lock.</param>
    /// <param name="resourceId">The ID of the resource to lock.</param>
    /// <param name="timeout">The maximum time to wait for acquiring the lock.</param>
    /// <returns>A <see cref="Task"/> representing the asynchronous operation.</returns>
    public Task<bool> AcquireReadLockAsync(string transactionId, string resourceId, TimeSpan timeout)
    {
        return this.AcquireReadLockAsync(transactionId, resourceId, timeout, CancellationToken.None);
    }

    /// <summary>
    /// Acquires a read lock on a resource for a transaction.
    /// </summary>
    /// <param name="transactionId">The ID of the transaction requesting the lock.</param>
    /// <param name="resourceId">The ID of the resource to lock.</param>
    /// <param name="timeout">The maximum time to wait for acquiring the lock.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>A <see cref="Task"/> representing the asynchronous operation.</returns>
    public async Task<bool> AcquireReadLockAsync(string transactionId, string resourceId, TimeSpan timeout, CancellationToken cancellationToken)
    {
        this.ThrowIfDisposed();

        if (string.IsNullOrEmpty(transactionId))
        {
            throw new ArgumentException("Transaction ID cannot be null or empty.", nameof(transactionId));
        }

        if (string.IsNullOrEmpty(resourceId))
        {
            throw new ArgumentException("Resource ID cannot be null or empty.", nameof(resourceId));
        }

        var resourceLock = this.resourceLocks.GetOrAdd(resourceId, id => new ResourceLock(id));

        using (var cts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken))
        {
            cts.CancelAfter(timeout);
            try
            {
                // Add to wait-for graph before trying to acquire
                if (resourceLock.WriteLockHolder != null)
                {
                    await this.deadlockDetector.AddWaitForAsync(transactionId, resourceLock.WriteLockHolder).ConfigureAwait(false);
                }

                var acquired = await resourceLock.AcquireReadLockAsync(transactionId, cts.Token).ConfigureAwait(false);

                if (acquired)
                {
                    // Remove from wait-for graph after acquiring
                    if (resourceLock.WriteLockHolder != null)
                    {
                        await this.deadlockDetector.RemoveWaitForAsync(transactionId, resourceLock.WriteLockHolder).ConfigureAwait(false);
                    }

                    // Track the lock for this transaction
                    var locks = this.transactionLocks.GetOrAdd(transactionId, id => new HashSet<string>());
                    lock (locks)
                    {
                        locks.Add(resourceId);
                    }
                }

                return acquired;
            }
            catch (OperationCanceledException)
            {
                // Remove from wait-for graph if we timeout
                if (resourceLock.WriteLockHolder != null)
                {
                    await this.deadlockDetector.RemoveWaitForAsync(transactionId, resourceLock.WriteLockHolder).ConfigureAwait(false);
                }

                return false;
            }
        }
    }

    /// <summary>
    /// Acquires a write lock on a resource for a transaction.
    /// </summary>
    /// <param name="transactionId">The ID of the transaction requesting the lock.</param>
    /// <param name="resourceId">The ID of the resource to lock.</param>
    /// <param name="timeout">The maximum time to wait for acquiring the lock.</param>
    /// <returns>A <see cref="Task"/> representing the asynchronous operation.</returns>
    public Task<bool> AcquireWriteLockAsync(string transactionId, string resourceId, TimeSpan timeout)
    {
        return this.AcquireWriteLockAsync(transactionId, resourceId, timeout, CancellationToken.None);
    }

    /// <summary>
    /// Acquires a write lock on a resource for a transaction.
    /// </summary>
    /// <param name="transactionId">The ID of the transaction requesting the lock.</param>
    /// <param name="resourceId">The ID of the resource to lock.</param>
    /// <param name="timeout">The maximum time to wait for acquiring the lock.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>A <see cref="Task"/> representing the asynchronous operation.</returns>
    public async Task<bool> AcquireWriteLockAsync(string transactionId, string resourceId, TimeSpan timeout, CancellationToken cancellationToken)
    {
        this.ThrowIfDisposed();

        if (string.IsNullOrEmpty(transactionId))
        {
            throw new ArgumentException("Transaction ID cannot be null or empty.", nameof(transactionId));
        }

        if (string.IsNullOrEmpty(resourceId))
        {
            throw new ArgumentException("Resource ID cannot be null or empty.", nameof(resourceId));
        }

        var resourceLock = this.resourceLocks.GetOrAdd(resourceId, id => new ResourceLock(id));

        using (var cts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken))
        {
            cts.CancelAfter(timeout);
            try
            {
                // Add to wait-for graph before trying to acquire
                var currentHolders = resourceLock.GetCurrentHolders();
                foreach (var holder in currentHolders.Where(h => h != transactionId))
                {
                    await this.deadlockDetector.AddWaitForAsync(transactionId, holder).ConfigureAwait(false);
                }

                var acquired = await resourceLock.AcquireWriteLockAsync(transactionId, cts.Token).ConfigureAwait(false);

                if (acquired)
                {
                    // Remove from wait-for graph after acquiring
                    foreach (var holder in currentHolders)
                    {
                        await this.deadlockDetector.RemoveWaitForAsync(transactionId, holder).ConfigureAwait(false);
                    }

                    // Track the lock for this transaction
                    var locks = this.transactionLocks.GetOrAdd(transactionId, id => new HashSet<string>());
                    lock (locks)
                    {
                        locks.Add(resourceId);
                    }
                }

                return acquired;
            }
            catch (OperationCanceledException)
            {
                // Remove from wait-for graph if we timeout
                var currentHolders = resourceLock.GetCurrentHolders();
                foreach (var holder in currentHolders)
                {
                    await this.deadlockDetector.RemoveWaitForAsync(transactionId, holder).ConfigureAwait(false);
                }

                return false;
            }
        }
    }

    /// <summary>
    /// Releases a lock on a resource for a transaction.
    /// </summary>
    /// <param name="transactionId">The ID of the transaction releasing the lock.</param>
    /// <param name="resourceId">The ID of the resource to unlock.</param>
    /// <returns>A <see cref="Task"/> representing the asynchronous operation.</returns>
    public async Task ReleaseLockAsync(string transactionId, string resourceId)
    {
        this.ThrowIfDisposed();

        if (string.IsNullOrEmpty(transactionId))
        {
            throw new ArgumentException("Transaction ID cannot be null or empty.", nameof(transactionId));
        }

        if (string.IsNullOrEmpty(resourceId))
        {
            throw new ArgumentException("Resource ID cannot be null or empty.", nameof(resourceId));
        }

        if (this.resourceLocks.TryGetValue(resourceId, out var resourceLock))
        {
            await resourceLock.ReleaseLockAsync(transactionId).ConfigureAwait(false);

            // Remove from transaction's lock list
            if (this.transactionLocks.TryGetValue(transactionId, out var locks))
            {
                lock (locks)
                {
                    locks.Remove(resourceId);
                }
            }
        }
    }

    /// <summary>
    /// Releases all locks held by a transaction.
    /// </summary>
    /// <param name="transactionId">The ID of the transaction whose locks should be released.</param>
    /// <returns>A <see cref="Task"/> representing the asynchronous operation.</returns>
    public async Task ReleaseAllLocksAsync(string transactionId)

    /* Unmerged change from project 'Kvs.Core(net8.0)'
    Before:
        {
            this.ThrowIfDisposed();

            if (string.IsNullOrEmpty(transactionId))
            {
                throw new ArgumentException("Transaction ID cannot be null or empty.", nameof(transactionId));
            }

            if (this.transactionLocks.TryRemove(transactionId, out var locks))
            {
                var releaseTasks = new List<Task>();

                lock (locks)
                {
                    foreach (var resourceId in locks)
                    {
                        if (this.resourceLocks.TryGetValue(resourceId, out var resourceLock))
                        {
                            releaseTasks.Add(resourceLock.ReleaseLockAsync(transactionId));
                        }
                    }
                }

                await Task.WhenAll(releaseTasks).ConfigureAwait(false);
            }

            // Remove transaction from deadlock detector
            await this.deadlockDetector.RemoveTransactionAsync(transactionId).ConfigureAwait(false);
        }

        /// <summary>
        /// Checks if a transaction holds a lock on a resource.
        /// </summary>
        public async Task<LockType> GetLockTypeAsync(string transactionId, string resourceId)
        {
    After:
        {
    */
    {
        this.ThrowIfDisposed();

        if (string.IsNullOrEmpty(transactionId))
        {
            throw new ArgumentException("Transaction ID cannot be null or empty.", nameof(transactionId));
        }

        if (this.transactionLocks.TryRemove(transactionId, out var locks))
        {
            var releaseTasks = new List<Task>();

            lock (locks)
            {
                foreach (var resourceId in locks)
                {
                    if (this.resourceLocks.TryGetValue(resourceId, out var resourceLock))
                    {
                        releaseTasks.Add(resourceLock.ReleaseLockAsync(transactionId));
                    }
                }
            }

            await Task.WhenAll(releaseTasks).ConfigureAwait(false);
        }

        // Remove transaction from deadlock detector
        await this.deadlockDetector.RemoveTransactionAsync(transactionId).ConfigureAwait(false);
    }

    /// <summary>
    /// Checks if a transaction holds a lock on a resource.
    /// </summary>
    /// <param name="transactionId">The ID of the transaction to check.</param>
    /// <param name="resourceId">The ID of the resource to check.</param>
    /// <returns>A <see cref="Task"/> representing the asynchronous operation.</returns>
    public async Task<LockType> GetLockTypeAsync(string transactionId, string resourceId)
    {
        this.ThrowIfDisposed();

        if (string.IsNullOrEmpty(transactionId))
        {
            throw new ArgumentException("Transaction ID cannot be null or empty.", nameof(transactionId));
        }

        if (string.IsNullOrEmpty(resourceId))
        {
            throw new ArgumentException("Resource ID cannot be null or empty.", nameof(resourceId));
        }

        if (this.resourceLocks.TryGetValue(resourceId, out var resourceLock))
        {
            return await resourceLock.GetLockTypeAsync(transactionId).ConfigureAwait(false);
        }

        return LockType.None;
    }

    /// <summary>
    /// Upgrades a read lock to a write lock.
    /// </summary>
    /// <param name="transactionId">The ID of the transaction upgrading the lock.</param>
    /// <param name="resourceId">The ID of the resource whose lock is being upgraded.</param>
    /// <param name="timeout">The maximum time to wait for upgrading the lock.</param>
    /// <returns>A <see cref="Task"/> representing the asynchronous operation.</returns>
    public Task<bool> UpgradeLockAsync(string transactionId, string resourceId, TimeSpan timeout)
    {
        return this.UpgradeLockAsync(transactionId, resourceId, timeout, CancellationToken.None);
    }

    /// <summary>
    /// Upgrades a read lock to a write lock.
    /// </summary>
    /// <param name="transactionId">The ID of the transaction upgrading the lock.</param>
    /// <param name="resourceId">The ID of the resource whose lock is being upgraded.</param>
    /// <param name="timeout">The maximum time to wait for upgrading the lock.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>A <see cref="Task"/> representing the asynchronous operation.</returns>
    public async Task<bool> UpgradeLockAsync(string transactionId, string resourceId, TimeSpan timeout, CancellationToken cancellationToken)
    {
        this.ThrowIfDisposed();

        if (string.IsNullOrEmpty(transactionId))
        {
            throw new ArgumentException("Transaction ID cannot be null or empty.", nameof(transactionId));
        }

        if (string.IsNullOrEmpty(resourceId))
        {
            throw new ArgumentException("Resource ID cannot be null or empty.", nameof(resourceId));
        }

        if (!this.resourceLocks.TryGetValue(resourceId, out var resourceLock))
        {
            return false;
        }

        using (var cts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken))
        {
            cts.CancelAfter(timeout);
            try
            {
                // Add to wait-for graph before trying to upgrade
                var currentHolders = resourceLock.GetCurrentHolders();
                foreach (var holder in currentHolders.Where(h => h != transactionId))
                {
                    await this.deadlockDetector.AddWaitForAsync(transactionId, holder).ConfigureAwait(false);
                }

                var upgraded = await resourceLock.UpgradeLockAsync(transactionId, cts.Token).ConfigureAwait(false);

                if (upgraded)
                {
                    // Remove from wait-for graph after upgrading
                    foreach (var holder in currentHolders)
                    {
                        await this.deadlockDetector.RemoveWaitForAsync(transactionId, holder).ConfigureAwait(false);
                    }
                }

                return upgraded;
            }
            catch (OperationCanceledException)
            {
                // Remove from wait-for graph if we timeout
                var currentHolders = resourceLock.GetCurrentHolders();
                foreach (var holder in currentHolders)
                {
                    await this.deadlockDetector.RemoveWaitForAsync(transactionId, holder).ConfigureAwait(false);
                }

                return false;
            }
        }
    }

    /// <summary>
    /// Gets the current lock status for a resource.
    /// </summary>
    /// <param name="resourceId">The ID of the resource to check.</param>
    /// <returns>A <see cref="Task"/> representing the asynchronous operation.</returns>
    public async Task<LockStatus> GetLockStatusAsync(string resourceId)
    {
        this.ThrowIfDisposed();

        if (string.IsNullOrEmpty(resourceId))
        {
            throw new ArgumentException("Resource ID cannot be null or empty.", nameof(resourceId));
        }

        if (this.resourceLocks.TryGetValue(resourceId, out var resourceLock))
        {
            return await resourceLock.GetStatusAsync().ConfigureAwait(false);
        }

        return new LockStatus { IsLocked = false, LockType = LockType.None };
    }

    /// <summary>
    /// Acquires a range lock on a collection for a transaction.
    /// </summary>
    /// <param name="transactionId">The transaction identifier.</param>
    /// <param name="collectionName">The collection name.</param>
    /// <param name="startKey">The start key of the range (inclusive).</param>
    /// <param name="endKey">The end key of the range (inclusive).</param>
    /// <param name="lockType">The type of lock to acquire.</param>
    /// <param name="timeout">The timeout for acquiring the lock.</param>
    /// <returns>A task that represents the asynchronous operation. The task result indicates whether the lock was acquired.</returns>
    public async Task<bool> AcquireRangeLockAsync(string transactionId, string collectionName, string startKey, string endKey, LockType lockType, TimeSpan timeout)
    {
        this.ThrowIfDisposed();

        if (string.IsNullOrEmpty(transactionId))
        {
            throw new ArgumentException("Transaction ID cannot be null or empty.", nameof(transactionId));
        }

        if (string.IsNullOrEmpty(collectionName))
        {
            throw new ArgumentException("Collection name cannot be null or empty.", nameof(collectionName));
        }

        // For now, implement range locks as a lock on the collection
        // In a full implementation, this would use interval trees or similar data structures
        var rangeKey = $"{collectionName}:range:{startKey}:{endKey}";

        if (lockType == LockType.Read)
        {
            return await this.AcquireReadLockAsync(transactionId, rangeKey, timeout).ConfigureAwait(false);
        }
        else if (lockType == LockType.Write)
        {
            return await this.AcquireWriteLockAsync(transactionId, rangeKey, timeout).ConfigureAwait(false);
        }

        return false;
    }

    /// <summary>
    /// Releases a range lock on a collection for a transaction.
    /// </summary>
    /// <param name="transactionId">The transaction identifier.</param>
    /// <param name="collectionName">The collection name.</param>
    /// <param name="startKey">The start key of the range.</param>
    /// <param name="endKey">The end key of the range.</param>
    /// <returns>A task that represents the asynchronous operation.</returns>
    public async Task ReleaseRangeLockAsync(string transactionId, string collectionName, string startKey, string endKey)
    {
        this.ThrowIfDisposed();

        if (string.IsNullOrEmpty(transactionId))
        {
            throw new ArgumentException("Transaction ID cannot be null or empty.", nameof(transactionId));
        }

        if (string.IsNullOrEmpty(collectionName))
        {
            throw new ArgumentException("Collection name cannot be null or empty.", nameof(collectionName));
        }

        var rangeKey = $"{collectionName}:range:{startKey}:{endKey}";
        await this.ReleaseLockAsync(transactionId, rangeKey).ConfigureAwait(false);
    }

    /// <summary>
    /// Disposes the lock manager.
    /// </summary>
    public void Dispose()
    {
        this.Dispose(true);
        GC.SuppressFinalize(this);
    }

    /// <summary>
    /// Disposes the lock manager.
    /// </summary>
    /// <param name="disposing">True if disposing managed resources; false otherwise.</param>
    protected virtual void Dispose(bool disposing)
    {
        if (!this.disposed)
        {
            if (disposing)
            {
                this.deadlockDetector.DeadlockDetected -= this.OnDeadlockDetected;
                this.lockSemaphore?.Dispose();

                foreach (var resourceLock in this.resourceLocks.Values)
                {
                    resourceLock.Dispose();
                }
            }

            this.disposed = true;
        }
    }

    /// <summary>
    /// Handles the deadlock detected event from the deadlock detector.
    /// </summary>
    /// <param name="sender">The source of the event.</param>
    /// <param name="e">The deadlock event arguments containing affected transactions.</param>
#if NET8_0_OR_GREATER
    private void OnDeadlockDetected(object? sender, DeadlockEventArgs e)
#else
    private void OnDeadlockDetected(object sender, DeadlockEventArgs e)
#endif
    {
        // Forward the deadlock event
        this.DeadlockDetected?.Invoke(this, e);
    }

    private void ThrowIfDisposed()
    {
        if (this.disposed)
        {
            throw new ObjectDisposedException(nameof(LockManager));
        }
    }

    /// <summary>
    /// Represents a lock on a specific resource.
    /// </summary>
    private sealed class ResourceLock : IDisposable
    {
        private readonly string resourceId;
        private readonly SemaphoreSlim lockSemaphore;
        private readonly HashSet<string> readLockHolders;
#if NET8_0_OR_GREATER
        private string? writeLockHolder;
#else
        private string writeLockHolder;
#endif
        private readonly Queue<LockRequest> waitQueue;
        private bool disposed;

        public ResourceLock(string resourceId)
        {
            this.resourceId = resourceId;
            this.lockSemaphore = new SemaphoreSlim(1, 1);
            this.readLockHolders = new HashSet<string>();
            this.waitQueue = new Queue<LockRequest>();
        }

#if NET8_0_OR_GREATER
        public string? WriteLockHolder => this.writeLockHolder;
#else
        public string WriteLockHolder => this.writeLockHolder;
#endif

        public async Task<bool> AcquireReadLockAsync(string transactionId, CancellationToken cancellationToken)
        {
            await this.lockSemaphore.WaitAsync(cancellationToken).ConfigureAwait(false);
            try
            {
                // Can acquire read lock if no write lock or if we hold the write lock
                if (this.writeLockHolder == null || this.writeLockHolder == transactionId)
                {
                    this.readLockHolders.Add(transactionId);
                    return true;
                }

                // Need to wait
                var tcs = new TaskCompletionSource<bool>();
                var request = new LockRequest(transactionId, LockType.Read, tcs);
                this.waitQueue.Enqueue(request);

                // Register cancellation
                cancellationToken.Register(() => tcs.TrySetCanceled());

                return await tcs.Task.ConfigureAwait(false);
            }
            finally
            {
                this.lockSemaphore.Release();
            }
        }

        public async Task<bool> AcquireWriteLockAsync(string transactionId, CancellationToken cancellationToken)
        {
            await this.lockSemaphore.WaitAsync(cancellationToken).ConfigureAwait(false);
            try
            {
                // Can acquire write lock if no locks or if we are the only lock holder
                if ((this.writeLockHolder == null && this.readLockHolders.Count == 0) ||
                    (this.writeLockHolder == transactionId && this.readLockHolders.Count <= 1 &&
                     (this.readLockHolders.Count == 0 || this.readLockHolders.Contains(transactionId))))
                {
                    this.writeLockHolder = transactionId;
                    return true;
                }

                // Need to wait
                var tcs = new TaskCompletionSource<bool>();
                var request = new LockRequest(transactionId, LockType.Write, tcs);
                this.waitQueue.Enqueue(request);

                // Register cancellation
                cancellationToken.Register(() => tcs.TrySetCanceled());

                return await tcs.Task.ConfigureAwait(false);
            }
            finally
            {
                this.lockSemaphore.Release();
            }
        }

        public async Task<bool> UpgradeLockAsync(string transactionId, CancellationToken cancellationToken)
        {
            await this.lockSemaphore.WaitAsync(cancellationToken).ConfigureAwait(false);
            try
            {
                // Must hold a read lock to upgrade
                if (!this.readLockHolders.Contains(transactionId))
                {
                    return false;
                }

                // Can upgrade if we're the only read lock holder
                if (this.readLockHolders.Count == 1 && this.writeLockHolder == null)
                {
                    this.readLockHolders.Remove(transactionId);
                    this.writeLockHolder = transactionId;
                    return true;
                }

                // Need to wait
                var tcs = new TaskCompletionSource<bool>();
                var request = new LockRequest(transactionId, LockType.Write, tcs, isUpgrade: true);
                this.waitQueue.Enqueue(request);

                // Register cancellation
                cancellationToken.Register(() => tcs.TrySetCanceled());

                return await tcs.Task.ConfigureAwait(false);
            }
            finally
            {
                this.lockSemaphore.Release();
            }
        }

        public async Task ReleaseLockAsync(string transactionId)
        {
            await this.lockSemaphore.WaitAsync().ConfigureAwait(false);
            try
            {
                var released = false;

                if (this.writeLockHolder == transactionId)
                {
                    this.writeLockHolder = null;
                    released = true;
                }

                if (this.readLockHolders.Remove(transactionId))
                {
                    released = true;
                }

                if (released)
                {
                    // Process wait queue
                    while (this.waitQueue.Count > 0)
                    {
                        var request = this.waitQueue.Peek();

                        if (request.LockType == LockType.Read && this.writeLockHolder == null)
                        {
                            this.waitQueue.Dequeue();
                            this.readLockHolders.Add(request.TransactionId);
                            request.CompletionSource.TrySetResult(true);
                        }
                        else if (request.LockType == LockType.Write &&
                                 this.writeLockHolder == null &&
                                 (this.readLockHolders.Count == 0 ||
                                  (request.IsUpgrade && this.readLockHolders.Count == 1 &&
                                   this.readLockHolders.Contains(request.TransactionId))))
                        {
                            this.waitQueue.Dequeue();
                            if (request.IsUpgrade)
                            {
                                this.readLockHolders.Remove(request.TransactionId);
                            }

                            this.writeLockHolder = request.TransactionId;
                            request.CompletionSource.TrySetResult(true);
                        }
                        else
                        {
                            break;
                        }
                    }
                }
            }
            finally
            {
                this.lockSemaphore.Release();
            }
        }

        public async Task<LockType> GetLockTypeAsync(string transactionId)
        {
            await this.lockSemaphore.WaitAsync().ConfigureAwait(false);
            try
            {
                if (this.writeLockHolder == transactionId)
                {
                    return LockType.Write;
                }

                if (this.readLockHolders.Contains(transactionId))
                {
                    return LockType.Read;
                }

                return LockType.None;
            }
            finally
            {
                this.lockSemaphore.Release();
            }
        }

        public async Task<LockStatus> GetStatusAsync()
        {
            await this.lockSemaphore.WaitAsync().ConfigureAwait(false);
            try
            {
                var status = new LockStatus
                {
                    IsLocked = this.writeLockHolder != null || this.readLockHolders.Count > 0,
                    LockType = this.GetLockTypeForStatus(),
                    ReadLockHolders = this.readLockHolders.ToArray(),
                    WriteLockHolder = this.writeLockHolder,
                    WaitingTransactions = this.waitQueue.Select(r => r.TransactionId).ToArray()
                };

                return status;
            }
            finally
            {
                this.lockSemaphore.Release();
            }
        }

        public string[] GetCurrentHolders()
        {
            var holders = new HashSet<string>();

            if (this.writeLockHolder != null)
            {
                holders.Add(this.writeLockHolder);
            }

            foreach (var reader in this.readLockHolders)
            {
                holders.Add(reader);
            }

            return holders.ToArray();
        }

        private LockType GetLockTypeForStatus()
        {
            if (this.writeLockHolder != null)
            {
                return LockType.Write;
            }

            if (this.readLockHolders.Count > 0)
            {
                return LockType.Read;
            }

            return LockType.None;
        }

        public void Dispose()
        {
            this.Dispose(true);
            GC.SuppressFinalize(this);
        }

        /// <summary>
        /// Disposes the resource lock.
        /// </summary>
        /// <param name="disposing">True if disposing managed resources; false otherwise.</param>
        private void Dispose(bool disposing)
        {
            if (!this.disposed)
            {
                if (disposing)
                {
                    // Cancel all waiting requests
                    while (this.waitQueue.Count > 0)
                    {
                        var request = this.waitQueue.Dequeue();
                        request.CompletionSource.TrySetCanceled();
                    }

                    this.lockSemaphore?.Dispose();
                }

                this.disposed = true;
            }
        }

        private sealed class LockRequest
        {
            public LockRequest(string transactionId, LockType lockType, TaskCompletionSource<bool> completionSource, bool isUpgrade = false)
            {
                this.TransactionId = transactionId;
                this.LockType = lockType;
                this.CompletionSource = completionSource;
                this.IsUpgrade = isUpgrade;
            }

            public string TransactionId { get; }

            public LockType LockType { get; }

            public TaskCompletionSource<bool> CompletionSource { get; }

            public bool IsUpgrade { get; }
        }
    }
}
