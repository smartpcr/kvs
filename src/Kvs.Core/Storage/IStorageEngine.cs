using System;
using System.Threading.Tasks;

namespace Kvs.Core.Storage;

/// <summary>
/// Defines the contract for a storage engine that provides low-level file operations.
/// </summary>
public interface IStorageEngine : IDisposable
{
    /// <summary>
    /// Reads data from the storage at the specified position.
    /// </summary>
    /// <param name="position">The position in the storage to start reading from.</param>
    /// <param name="length">The number of bytes to read.</param>
    /// <returns>A task that represents the asynchronous operation. The task result contains the read data.</returns>
    Task<ReadOnlyMemory<byte>> ReadAsync(long position, int length);

    /// <summary>
    /// Writes data to the storage at the end of the file (append-only).
    /// </summary>
    /// <param name="data">The data to write.</param>
    /// <returns>A task that represents the asynchronous operation. The task result contains the position where the data was written.</returns>
    Task<long> WriteAsync(ReadOnlyMemory<byte> data);

    /// <summary>
    /// Writes data to the storage at the specified position.
    /// </summary>
    /// <param name="position">The position in the storage to start writing at.</param>
    /// <param name="data">The data to write.</param>
    /// <returns>A task that represents the asynchronous write operation.</returns>
    Task WriteAsync(long position, ReadOnlyMemory<byte> data);

    /// <summary>
    /// Flushes any buffered data to the underlying storage.
    /// </summary>
    /// <returns>A task that represents the asynchronous flush operation.</returns>
    Task FlushAsync();

    /// <summary>
    /// Forces a synchronization of the file system cache to disk.
    /// </summary>
    /// <returns>A task that represents the asynchronous operation. The task result indicates whether the operation succeeded.</returns>
    Task<bool> FsyncAsync();

    /// <summary>
    /// Gets the current size of the storage file.
    /// </summary>
    /// <returns>A task that represents the asynchronous operation. The task result contains the size in bytes.</returns>
    Task<long> GetSizeAsync();

    /// <summary>
    /// Truncates the storage file to the specified size.
    /// </summary>
    /// <param name="size">The new size of the file in bytes.</param>
    /// <returns>A task that represents the asynchronous truncate operation.</returns>
    Task TruncateAsync(long size);

    /// <summary>
    /// Checks whether the storage engine is open and operational.
    /// </summary>
    /// <returns>A task that represents the asynchronous operation. The task result indicates whether the storage is open.</returns>
    Task<bool> IsOpenAsync();
}

/// <summary>
/// Defines the contract for a write-ahead log (WAL) that records all transactions.
/// </summary>
public interface ITransactionLog : IDisposable
{
    /// <summary>
    /// Writes a transaction log entry to the log.
    /// </summary>
    /// <param name="entry">The transaction log entry to write.</param>
    /// <returns>A task that represents the asynchronous operation. The task result contains the assigned log sequence number (LSN).</returns>
    Task<long> WriteEntryAsync(TransactionLogEntry entry);

    /// <summary>
    /// Reads transaction log entries starting from the specified LSN.
    /// </summary>
    /// <param name="fromLsn">The LSN to start reading from (inclusive).</param>
    /// <returns>A task that represents the asynchronous operation. The task result contains an array of transaction log entries.</returns>
    Task<TransactionLogEntry[]> ReadEntriesAsync(long fromLsn);

    /// <summary>
    /// Flushes all buffered log entries to persistent storage.
    /// </summary>
    /// <returns>A task that represents the asynchronous operation. The task result indicates whether the flush succeeded.</returns>
    Task<bool> FlushAsync();

    /// <summary>
    /// Creates a checkpoint at the specified LSN.
    /// </summary>
    /// <param name="lsn">The LSN to checkpoint at.</param>
    /// <returns>A task that represents the asynchronous checkpoint operation.</returns>
    Task CheckpointAsync(long lsn);

    /// <summary>
    /// Gets the last (highest) LSN in the log.
    /// </summary>
    /// <returns>A task that represents the asynchronous operation. The task result contains the last LSN.</returns>
    Task<long> GetLastLsnAsync();

    /// <summary>
    /// Gets the first (lowest) LSN in the log.
    /// </summary>
    /// <returns>A task that represents the asynchronous operation. The task result contains the first LSN.</returns>
    Task<long> GetFirstLsnAsync();
}

/// <summary>
/// Defines the contract for managing database recovery using the ARIES protocol.
/// </summary>
public interface IRecoveryManager
{
    /// <summary>
    /// Performs database recovery using the ARIES protocol (Analysis, Redo, Undo phases).
    /// </summary>
    /// <returns>A task that represents the asynchronous operation. The task result indicates whether recovery succeeded.</returns>
    Task<bool> RecoverAsync();

    /// <summary>
    /// Gets all uncommitted transactions from the transaction log.
    /// </summary>
    /// <returns>A task that represents the asynchronous operation. The task result contains an array of uncommitted transaction log entries.</returns>
    Task<TransactionLogEntry[]> GetUncommittedTransactionsAsync();

    /// <summary>
    /// Rolls back a specific transaction by undoing all its operations.
    /// </summary>
    /// <param name="transactionId">The ID of the transaction to rollback.</param>
    /// <returns>A task that represents the asynchronous rollback operation.</returns>
    Task RollbackTransactionAsync(string transactionId);

    /// <summary>
    /// Redoes a specific transaction by reapplying all its operations.
    /// </summary>
    /// <param name="transactionId">The ID of the transaction to redo.</param>
    /// <returns>A task that represents the asynchronous redo operation.</returns>
    Task RedoTransactionAsync(string transactionId);

    /// <summary>
    /// Checks whether database recovery is needed.
    /// </summary>
    /// <returns>A task that represents the asynchronous operation. The task result indicates whether recovery is needed.</returns>
    Task<bool> IsRecoveryNeededAsync();
}
