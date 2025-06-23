#if !NET472
#nullable enable
#endif

using System;
using System.Collections.Concurrent;
using System.Data;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using Kvs.Core.Storage;

namespace Kvs.Core.Database;

/// <summary>
/// Represents a NoSQL database instance.
/// </summary>
public class Database : IDatabase
{
    private readonly string path;
    private readonly ConcurrentDictionary<string, object> collections;
    private readonly ConcurrentDictionary<string, Transaction> activeTransactions;
    private readonly SemaphoreSlim transactionLock;
#if NET8_0_OR_GREATER
    private IStorageEngine? storageEngine;
    private IPageManager? pageManager;
    private WAL? wal;
    private DatabaseWAL? databaseWal;
    private RecoveryManager? recoveryManager;
    private CheckpointManager? checkpointManager;
    private DeadlockDetector? deadlockDetector;
    private LockManager? lockManager;
    private TransactionCoordinator? transactionCoordinator;
#else
    private IStorageEngine storageEngine;
    private IPageManager pageManager;
    private WAL wal;
    private DatabaseWAL databaseWal;
    private RecoveryManager recoveryManager;
    private CheckpointManager checkpointManager;
    private DeadlockDetector deadlockDetector;
    private LockManager lockManager;
    private TransactionCoordinator transactionCoordinator;
#endif
    private bool isOpen;
    private bool disposed;
    private long nextTransactionId;

    /// <summary>
    /// Gets the database path.
    /// </summary>
    public string Path => this.path;

    /// <summary>
    /// Gets a value indicating whether the database is open.
    /// </summary>
    public bool IsOpen => this.isOpen;

    /// <summary>
    /// Initializes a new instance of the <see cref="Database"/> class.
    /// </summary>
    /// <param name="path">The path to the database file.</param>
    public Database(string path)
    {
        this.path = path ?? throw new ArgumentNullException(nameof(path));
        this.collections = new ConcurrentDictionary<string, object>();
        this.activeTransactions = new ConcurrentDictionary<string, Transaction>();
        this.transactionLock = new SemaphoreSlim(1, 1);
        this.nextTransactionId = 1;
    }

    /// <summary>
    /// Opens the database.
    /// </summary>
    /// <returns>A task that represents the asynchronous operation.</returns>
    public async Task OpenAsync()
    {
        if (this.isOpen)
        {
            return;
        }

        try
        {
            var directory = System.IO.Path.GetDirectoryName(this.path);
            if (!string.IsNullOrEmpty(directory) && !Directory.Exists(directory))
            {
                Directory.CreateDirectory(directory);
            }

            this.storageEngine = new FileStorageEngine(this.path);

            this.pageManager = new PageManager(this.storageEngine);

            var walPath = System.IO.Path.ChangeExtension(this.path, ".wal");
            var walStorageEngine = new FileStorageEngine(walPath);
            var serializer = new Serialization.BinarySerializer();
            this.wal = new WAL(walStorageEngine, serializer);
            this.databaseWal = new DatabaseWAL(this.wal);

            this.recoveryManager = new RecoveryManager(this.wal, this.pageManager);
            this.checkpointManager = new CheckpointManager(this.wal, this.pageManager);

            this.deadlockDetector = new DeadlockDetector(TimeSpan.FromMilliseconds(100));
            this.deadlockDetector.DeadlockDetected += this.OnDeadlockDetected;

            this.lockManager = new LockManager(this.deadlockDetector);
            this.lockManager.DeadlockDetected += this.OnLockManagerDeadlockDetected;

            this.transactionCoordinator = new TransactionCoordinator(this.databaseWal);

            if (await this.recoveryManager.IsRecoveryNeededAsync().ConfigureAwait(false))
            {
                await this.recoveryManager.RecoverAsync().ConfigureAwait(false);
            }

            this.isOpen = true;
        }
        catch
        {
            await this.CloseInternalAsync().ConfigureAwait(false);
            throw;
        }
    }

    /// <summary>
    /// Closes the database.
    /// </summary>
    /// <returns>A task that represents the asynchronous operation.</returns>
    public async Task CloseAsync()
    {
        if (!this.isOpen)
        {
            return;
        }

        await this.transactionLock.WaitAsync().ConfigureAwait(false);
        try
        {
            foreach (var transaction in this.activeTransactions.Values)
            {
                if (transaction.State == TransactionState.Active ||
                    transaction.State == TransactionState.Preparing ||
                    transaction.State == TransactionState.Prepared)
                {
                    await transaction.RollbackAsync().ConfigureAwait(false);
                }
            }

            this.activeTransactions.Clear();

            await this.CloseInternalAsync().ConfigureAwait(false);
            this.isOpen = false;
        }
        finally
        {
            this.transactionLock.Release();
        }
    }

    /// <summary>
    /// Gets a collection from the database.
    /// </summary>
    /// <typeparam name="T">The type of documents in the collection.</typeparam>
    /// <param name="name">The name of the collection.</param>
    /// <returns>The collection instance.</returns>
    public ICollection<T> GetCollection<T>(string name)
        where T : class
    {
        this.ThrowIfDisposed();
        this.ThrowIfNotOpen();

        if (string.IsNullOrEmpty(name))
        {
            throw new ArgumentException("Collection name cannot be null or empty.", nameof(name));
        }

        return (ICollection<T>)this.collections.GetOrAdd(name, key =>
        {
            return new Collection<T>(key, this);
        });
    }

    /// <summary>
    /// Begins a new transaction.
    /// </summary>
    /// <returns>A task that represents the asynchronous operation. The task result contains the transaction.</returns>
    public Task<ITransaction> BeginTransactionAsync()
    {
        return this.BeginTransactionAsync(IsolationLevel.Serializable);
    }

    /// <summary>
    /// Begins a new transaction with the specified isolation level.
    /// </summary>
    /// <param name="isolationLevel">The isolation level for the transaction.</param>
    /// <returns>A task that represents the asynchronous operation. The task result contains the transaction.</returns>
    public async Task<ITransaction> BeginTransactionAsync(IsolationLevel isolationLevel)
    {
        this.ThrowIfDisposed();
        this.ThrowIfNotOpen();

        var transactionId = this.GenerateTransactionId();
        var transaction = new Transaction(transactionId, this, isolationLevel);

        if (!this.activeTransactions.TryAdd(transactionId, transaction))
        {
            throw new InvalidOperationException($"Transaction {transactionId} already exists.");
        }

        var entry = new TransactionLogEntry
        {
            TransactionId = transactionId,
            Type = TransactionLogEntryType.Begin,
            Timestamp = DateTime.UtcNow
        };

        await this.databaseWal!.WriteEntryAsync(entry).ConfigureAwait(false);

        return transaction;
    }

    /// <summary>
    /// Creates a checkpoint to compact the write-ahead log.
    /// </summary>
    /// <returns>A task that represents the asynchronous operation. The task result indicates whether the checkpoint was successful.</returns>
    public async Task<bool> CheckpointAsync()
    {
        this.ThrowIfDisposed();
        this.ThrowIfNotOpen();

        return await this.checkpointManager!.CreateCheckpointAsync().ConfigureAwait(false);
    }

    /// <summary>
    /// Performs recovery from the write-ahead log.
    /// </summary>
    /// <returns>A task that represents the asynchronous operation. The task result indicates whether recovery was successful.</returns>
    public async Task<bool> RecoverAsync()
    {
        this.ThrowIfDisposed();
        this.ThrowIfNotOpen();

        return await this.recoveryManager!.RecoverAsync().ConfigureAwait(false);
    }

    /// <summary>
    /// Disposes the database.
    /// </summary>
    public void Dispose()
    {
        this.Dispose(true);
        GC.SuppressFinalize(this);
    }

    /// <summary>
    /// Gets the write-ahead log.
    /// </summary>
    /// <returns>The write-ahead log instance.</returns>
    internal WAL GetWAL()
    {
        return this.wal ?? throw new InvalidOperationException("WAL is not initialized.");
    }

    /// <summary>
    /// Gets the database write-ahead log wrapper.
    /// </summary>
    /// <returns>The database write-ahead log instance.</returns>
    internal DatabaseWAL GetDatabaseWAL()
    {
        return this.databaseWal ?? throw new InvalidOperationException("DatabaseWAL is not initialized.");
    }

    /// <summary>
    /// Gets the page manager.
    /// </summary>
    /// <returns>The page manager instance.</returns>
    internal IPageManager GetPageManager()
    {
        return this.pageManager ?? throw new InvalidOperationException("PageManager is not initialized.");
    }

    /// <summary>
    /// Gets the lock manager.
    /// </summary>
    /// <returns>The lock manager instance.</returns>
    internal ILockManager GetLockManager()
    {
        return this.lockManager ?? throw new InvalidOperationException("LockManager is not initialized.");
    }

    /// <summary>
    /// Gets the transaction coordinator.
    /// </summary>
    /// <returns>The transaction coordinator instance.</returns>
    internal ITransactionCoordinator GetTransactionCoordinator()
    {
        return this.transactionCoordinator ?? throw new InvalidOperationException("TransactionCoordinator is not initialized.");
    }

    /// <summary>
    /// Removes a transaction from the active transactions.
    /// </summary>
    /// <param name="transactionId">The transaction ID.</param>
    internal void RemoveTransaction(string transactionId)
    {
        this.activeTransactions.TryRemove(transactionId, out _);
    }

#if NET8_0_OR_GREATER
    /// <summary>
    /// Tries to get a transaction by ID.
    /// </summary>
    /// <param name="transactionId">The transaction ID.</param>
    /// <param name="transaction">The transaction if found.</param>
    /// <returns>True if the transaction was found; otherwise, false.</returns>
    internal bool TryGetTransaction(string transactionId, out Transaction? transaction)
#else
    /// <summary>
    /// Tries to get a transaction by ID.
    /// </summary>
    /// <param name="transactionId">The transaction ID.</param>
    /// <param name="transaction">The transaction if found.</param>
    /// <returns>True if the transaction was found; otherwise, false.</returns>
    internal bool TryGetTransaction(string transactionId, out Transaction transaction)
#endif
    {
        return this.activeTransactions.TryGetValue(transactionId, out transaction);
    }

    /// <summary>
    /// Disposes the database.
    /// </summary>
    /// <param name="disposing">true to release both managed and unmanaged resources; false to release only unmanaged resources.</param>
    protected virtual void Dispose(bool disposing)
    {
        if (!this.disposed)
        {
            if (disposing)
            {
                if (this.isOpen)
                {
                    this.CloseAsync().GetAwaiter().GetResult();
                }

                this.transactionLock?.Dispose();
            }

            this.disposed = true;
        }
    }

    private async Task CloseInternalAsync()
    {
        if (this.checkpointManager != null)
        {
            await this.checkpointManager.CreateCheckpointAsync().ConfigureAwait(false);
            this.checkpointManager.Dispose();
            this.checkpointManager = null;
        }

        this.recoveryManager = null;

        if (this.deadlockDetector != null)
        {
            this.deadlockDetector.DeadlockDetected -= this.OnDeadlockDetected;
            this.deadlockDetector.Dispose();
            this.deadlockDetector = null;
        }

        if (this.lockManager != null)
        {
            this.lockManager.DeadlockDetected -= this.OnLockManagerDeadlockDetected;
            this.lockManager.Dispose();
            this.lockManager = null;
        }

        if (this.transactionCoordinator != null)
        {
            this.transactionCoordinator.Dispose();
            this.transactionCoordinator = null;
        }

        if (this.wal != null)
        {
            await this.wal.FlushAsync().ConfigureAwait(false);
            this.wal.Dispose();
            this.wal = null;
        }

        if (this.pageManager != null)
        {
            await this.pageManager.FlushAsync().ConfigureAwait(false);
            this.pageManager.Dispose();
            this.pageManager = null;
        }

        this.storageEngine?.Dispose();
        this.storageEngine = null;

        this.collections.Clear();
    }

    private string GenerateTransactionId()
    {
        var id = Interlocked.Increment(ref this.nextTransactionId);
        return $"TXN_{id:D10}_{DateTime.UtcNow.Ticks}";
    }

#if NET8_0_OR_GREATER
    private async void OnDeadlockDetected(object? sender, DeadlockEventArgs e)
#else
    private async void OnDeadlockDetected(object sender, DeadlockEventArgs e)
#endif
    {
        // Abort the victim transaction
        if (this.TryGetTransaction(e.Victim, out var victimTransaction))
        {
            // Mark as aborted before rollback to prevent further operations
            victimTransaction!.State = TransactionState.Aborted;
            
            // The transaction will detect it's been aborted and throw DeadlockException
            // We don't need to call RollbackAsync here as it will be called when the
            // transaction throws the exception
        }
    }

#if NET8_0_OR_GREATER
    private async void OnLockManagerDeadlockDetected(object? sender, DeadlockEventArgs e)
#else
    private async void OnLockManagerDeadlockDetected(object sender, DeadlockEventArgs e)
#endif
    {
        // The lock manager detected a deadlock - abort the victim transaction
        if (this.activeTransactions.TryGetValue(e.Victim, out var victimTransaction))
        {
            // Mark as aborted before rollback to prevent further operations
            victimTransaction.State = TransactionState.Aborted;
            
            // The transaction will detect it's been aborted and throw DeadlockException
            // We don't need to call RollbackAsync here as it will be called when the
            // transaction throws the exception
        }
    }

    private void ThrowIfDisposed()
    {
        if (this.disposed)
        {
            throw new ObjectDisposedException(nameof(Database));
        }
    }

    private void ThrowIfNotOpen()
    {
        if (!this.isOpen)
        {
            throw new InvalidOperationException("Database is not open.");
        }
    }
}
