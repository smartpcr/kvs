using System;

namespace Kvs.Core.Storage;

/// <summary>
/// Represents the type of operation performed in a transaction.
/// </summary>
public enum OperationType : byte
{
    /// <summary>
    /// Insert operation - adds new data.
    /// </summary>
    Insert = 1,

    /// <summary>
    /// Update operation - modifies existing data.
    /// </summary>
    Update = 2,

    /// <summary>
    /// Delete operation - removes data.
    /// </summary>
    Delete = 3,

    /// <summary>
    /// Commit operation - finalizes a transaction.
    /// </summary>
    Commit = 4,

    /// <summary>
    /// Rollback operation - aborts a transaction.
    /// </summary>
    Rollback = 5,

    /// <summary>
    /// Checkpoint operation - creates a recovery checkpoint.
    /// </summary>
    Checkpoint = 6
}

/// <summary>
/// Represents an entry in the transaction log for write-ahead logging.
/// </summary>
public readonly struct TransactionLogEntry(
    long lsn,
    string transactionId,
    OperationType operationType,
    long pageId,
    ReadOnlyMemory<byte> beforeImage,
    ReadOnlyMemory<byte> afterImage,
    DateTime timestamp,
    uint checksum = 0)
{
    /// <summary>
    /// Gets the log sequence number (LSN) uniquely identifying this entry.
    /// </summary>
    public long Lsn { get; } = lsn;

    /// <summary>
    /// Gets the unique identifier of the transaction.
    /// </summary>
    public string TransactionId { get; } = transactionId;

    /// <summary>
    /// Gets the type of operation performed.
    /// </summary>
    public OperationType OperationType { get; } = operationType;

    /// <summary>
    /// Gets the ID of the page affected by this operation.
    /// </summary>
    public long PageId { get; } = pageId;

    /// <summary>
    /// Gets the before image of the data (for undo operations).
    /// </summary>
    public ReadOnlyMemory<byte> BeforeImage { get; } = beforeImage;

    /// <summary>
    /// Gets the after image of the data (for redo operations).
    /// </summary>
    public ReadOnlyMemory<byte> AfterImage { get; } = afterImage;

    /// <summary>
    /// Gets the timestamp when this entry was created.
    /// </summary>
    public DateTime Timestamp { get; } = timestamp;

    /// <summary>
    /// Gets the checksum value stored with the entry.
    /// </summary>
    public uint Checksum { get; } = checksum == 0 ? CalculateChecksum(lsn, transactionId, operationType, pageId, beforeImage, afterImage) : checksum;

    /// <summary>
    /// Gets the checksum calculated from the current field values.
    /// </summary>
    public uint ComputedChecksum { get; } = CalculateChecksum(lsn, transactionId, operationType, pageId, beforeImage, afterImage);

    /// <summary>
    /// Gets a value indicating whether this entry is valid based on checksum verification.
    /// </summary>
    public bool IsValid => this.Checksum == this.ComputedChecksum;

    private static uint CalculateChecksum(
        long lsn,
        string transactionId,
        OperationType operationType,
        long pageId,
        ReadOnlyMemory<byte> beforeImage,
        ReadOnlyMemory<byte> afterImage)
    {
        uint checksum = 0;

        checksum ^= (uint)(lsn ^ (lsn >> 32));
        checksum ^= (uint)(transactionId?.GetHashCode() ?? 0);
        checksum ^= (uint)operationType;
        checksum ^= (uint)(pageId ^ (pageId >> 32));

        if (!beforeImage.IsEmpty)
        {
            var beforeSpan = beforeImage.Span;
            for (int i = 0; i < beforeSpan.Length; i += 4)
            {
                var sliceLength = Math.Min(4, beforeSpan.Length - i);
                if (sliceLength == 4)
                {
#if NET472
                    var slice = beforeSpan.Slice(i, sliceLength).ToArray();
                    checksum ^= BitConverter.ToUInt32(slice, 0);
#else
                    checksum ^= BitConverter.ToUInt32(beforeSpan.Slice(i, sliceLength));
#endif
                }
            }
        }

        if (!afterImage.IsEmpty)
        {
            var afterSpan = afterImage.Span;
            for (int i = 0; i < afterSpan.Length; i += 4)
            {
                var sliceLength = Math.Min(4, afterSpan.Length - i);
                if (sliceLength == 4)
                {
#if NET472
                    var slice = afterSpan.Slice(i, sliceLength).ToArray();
                    checksum ^= BitConverter.ToUInt32(slice, 0);
#else
                    checksum ^= BitConverter.ToUInt32(afterSpan.Slice(i, sliceLength));
#endif
                }
            }
        }

        return checksum;
    }
}
