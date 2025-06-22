namespace Kvs.Core.Database;

/// <summary>
/// Represents the type of transaction log entry.
/// </summary>
public enum TransactionLogEntryType
{
    /// <summary>
    /// Transaction begin.
    /// </summary>
    Begin,

    /// <summary>
    /// Transaction prepare.
    /// </summary>
    Prepare,

    /// <summary>
    /// Transaction commit.
    /// </summary>
    Commit,

    /// <summary>
    /// Transaction rollback.
    /// </summary>
    Rollback,

    /// <summary>
    /// Read operation.
    /// </summary>
    Read,

    /// <summary>
    /// Write operation.
    /// </summary>
    Write,

    /// <summary>
    /// Insert operation.
    /// </summary>
    Insert,

    /// <summary>
    /// Update operation.
    /// </summary>
    Update,

    /// <summary>
    /// Delete operation.
    /// </summary>
    Delete,

    /// <summary>
    /// Clear operation.
    /// </summary>
    Clear,

    /// <summary>
    /// Checkpoint operation.
    /// </summary>
    Checkpoint
}
