#if !NET472
#nullable enable
#endif

using System;

namespace Kvs.Core.Database;

/// <summary>
/// Represents a database-level transaction log entry.
/// </summary>
public class TransactionLogEntry
{
    /// <summary>
    /// Gets or sets the transaction ID.
    /// </summary>
    public string TransactionId { get; set; } = string.Empty;

    /// <summary>
    /// Gets or sets the type of the log entry.
    /// </summary>
    public TransactionLogEntryType Type { get; set; }

    /// <summary>
    /// Gets or sets the collection name.
    /// </summary>
#if NET8_0_OR_GREATER
    public string? CollectionName { get; set; }
#else
    public string CollectionName { get; set; }
#endif

    /// <summary>
    /// Gets or sets the key.
    /// </summary>
#if NET8_0_OR_GREATER
    public string? Key { get; set; }
#else
    public string Key { get; set; }
#endif

    /// <summary>
    /// Gets or sets the data.
    /// </summary>
    public ReadOnlyMemory<byte> Data { get; set; }

    /// <summary>
    /// Gets or sets the old data (for updates).
    /// </summary>
    public ReadOnlyMemory<byte> OldData { get; set; }

    /// <summary>
    /// Gets or sets the timestamp.
    /// </summary>
    public DateTime Timestamp { get; set; }
}
