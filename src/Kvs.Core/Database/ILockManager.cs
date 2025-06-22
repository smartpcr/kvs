using System;
using System.Threading.Tasks;

namespace Kvs.Core.Database;

/// <summary>
/// Defines the interface for managing locks in database transactions.
/// </summary>
public interface ILockManager : IDisposable
{
    /// <summary>
    /// Acquires a read lock on a resource for a transaction.
    /// </summary>
    /// <param name="transactionId">The transaction identifier.</param>
    /// <param name="resourceId">The resource identifier.</param>
    /// <param name="timeout">The timeout for acquiring the lock.</param>
    /// <returns>A task that represents the asynchronous operation. The task result indicates whether the lock was acquired.</returns>
    Task<bool> AcquireReadLockAsync(string transactionId, string resourceId, TimeSpan timeout);

    /// <summary>
    /// Acquires a write lock on a resource for a transaction.
    /// </summary>
    /// <param name="transactionId">The transaction identifier.</param>
    /// <param name="resourceId">The resource identifier.</param>
    /// <param name="timeout">The timeout for acquiring the lock.</param>
    /// <returns>A task that represents the asynchronous operation. The task result indicates whether the lock was acquired.</returns>
    Task<bool> AcquireWriteLockAsync(string transactionId, string resourceId, TimeSpan timeout);

    /// <summary>
    /// Releases a lock on a resource for a transaction.
    /// </summary>
    /// <param name="transactionId">The transaction identifier.</param>
    /// <param name="resourceId">The resource identifier.</param>
    /// <returns>A task that represents the asynchronous operation.</returns>
    Task ReleaseLockAsync(string transactionId, string resourceId);

    /// <summary>
    /// Releases all locks held by a transaction.
    /// </summary>
    /// <param name="transactionId">The transaction identifier.</param>
    /// <returns>A task that represents the asynchronous operation.</returns>
    Task ReleaseAllLocksAsync(string transactionId);

    /// <summary>
    /// Checks if a transaction holds a lock on a resource.
    /// </summary>
    /// <param name="transactionId">The transaction identifier.</param>
    /// <param name="resourceId">The resource identifier.</param>
    /// <returns>A task that represents the asynchronous operation. The task result contains the lock type held.</returns>
    Task<LockType> GetLockTypeAsync(string transactionId, string resourceId);

    /// <summary>
    /// Upgrades a read lock to a write lock.
    /// </summary>
    /// <param name="transactionId">The transaction identifier.</param>
    /// <param name="resourceId">The resource identifier.</param>
    /// <param name="timeout">The timeout for upgrading the lock.</param>
    /// <returns>A task that represents the asynchronous operation. The task result indicates whether the upgrade was successful.</returns>
    Task<bool> UpgradeLockAsync(string transactionId, string resourceId, TimeSpan timeout);

    /// <summary>
    /// Gets the current lock status for a resource.
    /// </summary>
    /// <param name="resourceId">The resource identifier.</param>
    /// <returns>A task that represents the asynchronous operation. The task result contains the lock status.</returns>
    Task<LockStatus> GetLockStatusAsync(string resourceId);

    /// <summary>
    /// Occurs when a deadlock is detected.
    /// </summary>
    event EventHandler<DeadlockEventArgs> DeadlockDetected;
}

/// <summary>
/// Represents the type of lock.
/// </summary>
public enum LockType
{
    /// <summary>
    /// No lock is held.
    /// </summary>
    None,

    /// <summary>
    /// A read lock is held.
    /// </summary>
    Read,

    /// <summary>
    /// A write lock is held.
    /// </summary>
    Write
}

/// <summary>
/// Represents the status of a lock on a resource.
/// </summary>
public class LockStatus
{
    /// <summary>
    /// Gets or sets a value indicating whether the resource is locked.
    /// </summary>
    public bool IsLocked { get; set; }

    /// <summary>
    /// Gets or sets the type of lock.
    /// </summary>
    public LockType LockType { get; set; }

    /// <summary>
    /// Gets or sets the transaction IDs holding read locks.
    /// </summary>
    public string[] ReadLockHolders { get; set; } = Array.Empty<string>();

    /// <summary>
    /// Gets or sets the transaction ID holding a write lock.
    /// </summary>
#if NET8_0_OR_GREATER
    public string? WriteLockHolder { get; set; }
#else
    public string WriteLockHolder { get; set; }
#endif

    /// <summary>
    /// Gets or sets the transaction IDs waiting for locks.
    /// </summary>
    public string[] WaitingTransactions { get; set; } = Array.Empty<string>();
}
