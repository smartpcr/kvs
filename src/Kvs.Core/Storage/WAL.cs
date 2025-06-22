using System;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using Kvs.Core.Serialization;

namespace Kvs.Core.Storage;

/// <summary>
/// Implements a Write-Ahead Log (WAL) for transaction durability and recovery.
/// </summary>
public class WAL(IStorageEngine storageEngine, ISerializer serializer) : ITransactionLog
{
    private readonly IStorageEngine storageEngine = storageEngine ?? throw new ArgumentNullException(nameof(storageEngine));
    private readonly ISerializer serializer = serializer ?? throw new ArgumentNullException(nameof(serializer));
    private readonly SemaphoreSlim writeSemaphore = new SemaphoreSlim(1, 1);
    private readonly object lsnLock = new();
    private long nextLsn = 1;
    private long lastCheckpointLsn = 0;
    private bool disposed;

    /// <inheritdoc />
    public async Task<long> WriteEntryAsync(TransactionLogEntry entry)
    {
        this.ThrowIfDisposed();

        if (string.IsNullOrEmpty(entry.TransactionId))
        {
            throw new ArgumentException("Transaction ID cannot be null or empty", nameof(entry));
        }

        await this.writeSemaphore.WaitAsync();
        try
        {
            long lsn;
            lock (this.lsnLock)
            {
                lsn = this.nextLsn++;
            }

            // Create entry with assigned LSN
            var walEntry = new TransactionLogEntry(
                lsn,
                entry.TransactionId,
                entry.OperationType,
                entry.PageId,
                entry.BeforeImage,
                entry.AfterImage,
                DateTime.UtcNow);

            // Serialize the entry
            var serializedEntry = this.serializer.Serialize(walEntry);
            var entrySize = BitConverter.GetBytes(serializedEntry.Length);

            // Write entry size followed by entry data
            var totalData = new byte[entrySize.Length + serializedEntry.Length];
            entrySize.CopyTo(totalData, 0);
            serializedEntry.Span.CopyTo(totalData.AsSpan(entrySize.Length));

            await this.storageEngine.WriteAsync(totalData);

            // Force sync for durability
            await this.storageEngine.FsyncAsync();

            return lsn;
        }
        finally
        {
            this.writeSemaphore.Release();
        }
    }

    /// <inheritdoc />
    public async Task<TransactionLogEntry[]> ReadEntriesAsync(long fromLsn)
    {
        this.ThrowIfDisposed();

        var entries = new List<TransactionLogEntry>();
        var position = 0L;
        var fileSize = await this.storageEngine.GetSizeAsync();

        // If file is empty, return empty array
        if (fileSize == 0)
        {
            return [];
        }

        while (position < fileSize)
        {
            // Read entry size
            var sizeData = await this.storageEngine.ReadAsync(position, sizeof(int));
            if (sizeData.Length < sizeof(int))
            {
                break;
            }

            // Use ToArray() for compatibility across all platforms
            var entrySize = BitConverter.ToInt32(sizeData.ToArray(), 0);
            if (entrySize is <= 0 or > (1024 * 1024)) // Max 1MB per entry
            {
                break;
            }

            position += sizeof(int);

            // Read entry data
            var entryData = await this.storageEngine.ReadAsync(position, entrySize);
            if (entryData.Length < entrySize)
            {
                break;
            }

            try
            {
                var entry = this.serializer.Deserialize<TransactionLogEntry>(entryData);

                // Validate entry integrity
                if (!entry.IsValid)
                {
                    // Log corruption detected, skip this entry
                    position += entrySize;
                    continue;
                }

                if (entry.Lsn >= fromLsn)
                {
                    entries.Add(entry);
                }
            }
            catch
            {
                // Skip corrupted entries
                position += entrySize;
                continue;
            }

            position += entrySize;
        }

        return [.. entries];
    }

    /// <inheritdoc />
    public async Task<bool> FlushAsync()
    {
        this.ThrowIfDisposed();

        await this.storageEngine.FlushAsync();
        return await this.storageEngine.FsyncAsync();
    }

    /// <inheritdoc />
    public async Task CheckpointAsync(long lsn)
    {
        this.ThrowIfDisposed();

        await this.writeSemaphore.WaitAsync();
        try
        {
            this.lastCheckpointLsn = lsn;

            // Write checkpoint entry
            var checkpointEntry = new TransactionLogEntry(
                lsn,
                "CHECKPOINT",
                OperationType.Checkpoint,
                -1,
                ReadOnlyMemory<byte>.Empty,
                BitConverter.GetBytes(lsn),
                DateTime.UtcNow);

            await this.WriteEntryInternal(checkpointEntry);
        }
        finally
        {
            this.writeSemaphore.Release();
        }
    }

    /// <inheritdoc />
    public Task<long> GetLastLsnAsync()
    {
        this.ThrowIfDisposed();

        lock (this.lsnLock)
        {
            return Task.FromResult(this.nextLsn - 1);
        }
    }

    /// <inheritdoc />
    public async Task<long> GetFirstLsnAsync()
    {
        this.ThrowIfDisposed();

        var entries = await this.ReadEntriesAsync(0);
        return entries.Length > 0 ? entries[0].Lsn : 0;
    }

    /// <summary>
    /// Gets the log sequence number of the last checkpoint.
    /// </summary>
    /// <returns>The LSN of the last checkpoint.</returns>
    public long GetLastCheckpointLsn()
    {
        return this.lastCheckpointLsn;
    }

    /// <summary>
    /// Truncates the WAL, removing all entries before the specified LSN.
    /// </summary>
    /// <param name="beforeLsn">The LSN before which to remove entries.</param>
    /// <returns>A task that represents the asynchronous truncate operation.</returns>
    public async Task TruncateAsync(long beforeLsn)
    {
        this.ThrowIfDisposed();

        // For simplicity, we'll rebuild the WAL with entries >= beforeLsn
        var entries = await this.ReadEntriesAsync(beforeLsn);

        // Truncate the file
        await this.storageEngine.TruncateAsync(0);

        // Rewrite remaining entries
        foreach (var entry in entries)
        {
            await this.WriteEntryInternal(entry);
        }
    }

    private async Task WriteEntryInternal(TransactionLogEntry entry)
    {
        var serializedEntry = this.serializer.Serialize(entry);
        var entrySize = BitConverter.GetBytes(serializedEntry.Length);

        var totalData = new byte[entrySize.Length + serializedEntry.Length];
        entrySize.CopyTo(totalData, 0);
        serializedEntry.Span.CopyTo(totalData.AsSpan(entrySize.Length));

        await this.storageEngine.WriteAsync(totalData);
    }

    private void ThrowIfDisposed()
    {
        if (this.disposed)
        {
            throw new ObjectDisposedException(nameof(WAL));
        }
    }

    /// <inheritdoc />
    public void Dispose()
    {
        if (this.disposed)
        {
            return;
        }

        this.disposed = true;

        this.writeSemaphore?.Dispose();
    }
}
