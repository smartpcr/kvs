#if !NET472
#nullable enable
#endif

using System;
using System.Data;
using System.Threading.Tasks;

namespace Kvs.Core.Database;

/// <summary>
/// Represents the state of a transaction.
/// </summary>
public enum TransactionState
{
    /// <summary>
    /// The transaction is active.
    /// </summary>
    Active,

    /// <summary>
    /// The transaction is preparing to commit.
    /// </summary>
    Preparing,

    /// <summary>
    /// The transaction is prepared.
    /// </summary>
    Prepared,

    /// <summary>
    /// The transaction is committing.
    /// </summary>
    Committing,

    /// <summary>
    /// The transaction is committed.
    /// </summary>
    Committed,

    /// <summary>
    /// The transaction is aborting.
    /// </summary>
    Aborting,

    /// <summary>
    /// The transaction is aborted.
    /// </summary>
    Aborted
}

/// <summary>
/// Represents a database transaction.
/// </summary>
public interface ITransaction : IDisposable
{
    /// <summary>
    /// Gets the transaction ID.
    /// </summary>
    string Id { get; }

    /// <summary>
    /// Gets the transaction state.
    /// </summary>
    TransactionState State { get; }

    /// <summary>
    /// Gets the isolation level.
    /// </summary>
    IsolationLevel IsolationLevel { get; }

    /// <summary>
    /// Reads a value from the database.
    /// </summary>
    /// <typeparam name="T">The type of the value.</typeparam>
    /// <param name="key">The key to read.</param>
    /// <returns>A task that represents the asynchronous operation. The task result contains the value, or null if not found.</returns>
#if NET472
    Task<T> ReadAsync<T>(string key);
#else
    Task<T?> ReadAsync<T>(string key);
#endif

    /// <summary>
    /// Writes a value to the database.
    /// </summary>
    /// <typeparam name="T">The type of the value.</typeparam>
    /// <param name="key">The key to write.</param>
    /// <param name="value">The value to write.</param>
    /// <returns>A task that represents the asynchronous operation.</returns>
    Task WriteAsync<T>(string key, T value);

    /// <summary>
    /// Deletes a value from the database.
    /// </summary>
    /// <param name="key">The key to delete.</param>
    /// <returns>A task that represents the asynchronous operation. The task result indicates whether the deletion was successful.</returns>
    Task<bool> DeleteAsync(string key);

    /// <summary>
    /// Commits the transaction.
    /// </summary>
    /// <returns>A task that represents the asynchronous operation.</returns>
    Task CommitAsync();

    /// <summary>
    /// Rolls back the transaction.
    /// </summary>
    /// <returns>A task that represents the asynchronous operation.</returns>
    Task RollbackAsync();
}
