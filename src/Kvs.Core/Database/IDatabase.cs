#if !NET472
#nullable enable
#endif

using System;
using System.Data;
using System.Threading.Tasks;

namespace Kvs.Core.Database;

/// <summary>
/// Represents a NoSQL database.
/// </summary>
public interface IDatabase : IDisposable
{
    /// <summary>
    /// Gets the database path.
    /// </summary>
    string Path { get; }

    /// <summary>
    /// Gets a value indicating whether the database is open.
    /// </summary>
    bool IsOpen { get; }

    /// <summary>
    /// Opens the database.
    /// </summary>
    /// <returns>A task that represents the asynchronous operation.</returns>
    Task OpenAsync();

    /// <summary>
    /// Closes the database.
    /// </summary>
    /// <returns>A task that represents the asynchronous operation.</returns>
    Task CloseAsync();

    /// <summary>
    /// Gets a collection from the database.
    /// </summary>
    /// <typeparam name="T">The type of documents in the collection.</typeparam>
    /// <param name="name">The name of the collection.</param>
    /// <returns>The collection instance.</returns>
    ICollection<T> GetCollection<T>(string name)
        where T : class;

    /// <summary>
    /// Begins a new transaction.
    /// </summary>
    /// <returns>A task that represents the asynchronous operation. The task result contains the transaction.</returns>
    Task<ITransaction> BeginTransactionAsync();

    /// <summary>
    /// Begins a new transaction with the specified isolation level.
    /// </summary>
    /// <param name="isolationLevel">The isolation level for the transaction.</param>
    /// <returns>A task that represents the asynchronous operation. The task result contains the transaction.</returns>
    Task<ITransaction> BeginTransactionAsync(IsolationLevel isolationLevel);

    /// <summary>
    /// Creates a checkpoint to compact the write-ahead log.
    /// </summary>
    /// <returns>A task that represents the asynchronous operation. The task result indicates whether the checkpoint was successful.</returns>
    Task<bool> CheckpointAsync();

    /// <summary>
    /// Performs recovery from the write-ahead log.
    /// </summary>
    /// <returns>A task that represents the asynchronous operation. The task result indicates whether recovery was successful.</returns>
    Task<bool> RecoverAsync();
}
