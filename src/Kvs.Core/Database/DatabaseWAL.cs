#if !NET472
#nullable enable
#endif

using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Kvs.Core.Storage;

namespace Kvs.Core.Database;

/// <summary>
/// Wrapper for WAL that works with database transaction log entries.
/// </summary>
public class DatabaseWAL
{
    private readonly WAL wal;

    /// <summary>
    /// Initializes a new instance of the <see cref="DatabaseWAL"/> class.
    /// </summary>
    /// <param name="wal">The underlying WAL instance.</param>
    public DatabaseWAL(WAL wal)
    {
        this.wal = wal ?? throw new ArgumentNullException(nameof(wal));
    }

    /// <summary>
    /// Writes a database transaction log entry to the WAL.
    /// </summary>
    /// <param name="entry">The entry to write.</param>
    /// <returns>A task that represents the asynchronous operation. The task result contains the LSN.</returns>
    public async Task<long> WriteEntryAsync(TransactionLogEntry entry)
    {
        var storageEntry = this.ConvertToStorageEntry(entry);
        return await this.wal.WriteEntryAsync(storageEntry).ConfigureAwait(false);
    }

    /// <summary>
    /// Reads database transaction log entries from the WAL.
    /// </summary>
    /// <param name="fromLsn">The LSN to start reading from.</param>
    /// <returns>A task that represents the asynchronous operation. The task result contains the entries.</returns>
    public async Task<TransactionLogEntry[]> ReadEntriesAsync(long fromLsn)
    {
        var storageEntries = await this.wal.ReadEntriesAsync(fromLsn).ConfigureAwait(false);
        return storageEntries.Select(this.ConvertFromStorageEntry).ToArray();
    }

    /// <summary>
    /// Flushes the WAL.
    /// </summary>
    /// <returns>A task that represents the asynchronous operation.</returns>
    public Task<bool> FlushAsync()
    {
        return this.wal.FlushAsync();
    }

    /// <summary>
    /// Gets the last (highest) LSN in the log.
    /// </summary>
    /// <returns>A task that represents the asynchronous operation. The task result contains the last LSN.</returns>
    public Task<long> GetLastLsnAsync()
    {
        return this.wal.GetLastLsnAsync();
    }

    private Storage.TransactionLogEntry ConvertToStorageEntry(TransactionLogEntry entry)
    {
        var operationType = entry.Type switch
        {
            TransactionLogEntryType.Begin => OperationType.Insert,
            TransactionLogEntryType.Prepare => OperationType.Update,
            TransactionLogEntryType.Commit => OperationType.Commit,
            TransactionLogEntryType.Rollback => OperationType.Rollback,
            TransactionLogEntryType.Read => OperationType.Insert,
            TransactionLogEntryType.Write => OperationType.Update,
            TransactionLogEntryType.Insert => OperationType.Insert,
            TransactionLogEntryType.Update => OperationType.Update,
            TransactionLogEntryType.Delete => OperationType.Delete,
            TransactionLogEntryType.Clear => OperationType.Delete,
            TransactionLogEntryType.Checkpoint => OperationType.Checkpoint,
            _ => throw new ArgumentException($"Unknown transaction log entry type: {entry.Type}")
        };

#if NET8_0_OR_GREATER
        var metadata = new Dictionary<string, object?>
#else
        var metadata = new Dictionary<string, object>
#endif
        {
            ["Type"] = entry.Type.ToString(),
            ["CollectionName"] = entry.CollectionName,
            ["Key"] = entry.Key
        };

        var serializer = new Serialization.BinarySerializer();
        var metadataBytes = serializer.Serialize(metadata);

        return new Storage.TransactionLogEntry(
            lsn: 0, // Will be assigned by WAL
            transactionId: entry.TransactionId,
            operationType: operationType,
            pageId: 0,
            beforeImage: entry.OldData.IsEmpty ? metadataBytes : entry.OldData,
            afterImage: entry.Data.IsEmpty ? metadataBytes : entry.Data,
            timestamp: entry.Timestamp,
            checksum: 0); // Will be calculated by WAL
    }

    private TransactionLogEntry ConvertFromStorageEntry(Storage.TransactionLogEntry entry)
    {
        var serializer = new Serialization.BinarySerializer();
#if NET8_0_OR_GREATER
        Dictionary<string, object?>? metadata = null;
#else
        Dictionary<string, object> metadata = null;
#endif

        if (!entry.BeforeImage.IsEmpty)
        {
            try
            {
#if NET8_0_OR_GREATER
                metadata = serializer.Deserialize<Dictionary<string, object?>>(entry.BeforeImage);
#else
                metadata = serializer.Deserialize<Dictionary<string, object>>(entry.BeforeImage);
#endif
            }
            catch
            {
                // Ignore deserialization errors - data might be actual record data, not metadata
            }
        }

        if (metadata == null && !entry.AfterImage.IsEmpty)
        {
            try
            {
#if NET8_0_OR_GREATER
                metadata = serializer.Deserialize<Dictionary<string, object?>>(entry.AfterImage);
#else
                metadata = serializer.Deserialize<Dictionary<string, object>>(entry.AfterImage);
#endif
            }
            catch
            {
                // Ignore deserialization errors - data might be actual record data, not metadata
            }
        }

        var type = TransactionLogEntryType.Write;
        if (metadata != null && metadata.TryGetValue("Type", out var typeObj) && typeObj is string typeStr)
        {
            Enum.TryParse<TransactionLogEntryType>(typeStr, out type);
        }

        return new TransactionLogEntry
        {
            TransactionId = entry.TransactionId,
            Type = type,
#if NET472
            CollectionName = metadata != null && metadata.TryGetValue("CollectionName", out var collName) ? collName as string : null,
            Key = metadata != null && metadata.TryGetValue("Key", out var key) ? key as string : null,
#else
            CollectionName = metadata?.TryGetValue("CollectionName", out var collName) == true ? collName as string : null,
            Key = metadata?.TryGetValue("Key", out var key) == true ? key as string : null,
#endif
            Data = entry.AfterImage,
            OldData = entry.BeforeImage,
            Timestamp = entry.Timestamp
        };
    }
}
