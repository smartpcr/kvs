namespace Kvs.Core.Database;

/// <summary>
/// Specifies the transaction isolation level.
/// </summary>
public enum IsolationLevel
{
    /// <summary>
    /// Read Uncommitted - Allows dirty reads, non-repeatable reads, and phantom reads.
    /// </summary>
    ReadUncommitted = 0,

    /// <summary>
    /// Read Committed - Prevents dirty reads but allows non-repeatable reads and phantom reads.
    /// </summary>
    ReadCommitted = 1,

    /// <summary>
    /// Repeatable Read - Prevents dirty reads and non-repeatable reads but allows phantom reads.
    /// </summary>
    RepeatableRead = 2,

    /// <summary>
    /// Serializable - Prevents dirty reads, non-repeatable reads, and phantom reads.
    /// </summary>
    Serializable = 3,

    /// <summary>
    /// Snapshot - Uses multi-version concurrency control (MVCC) to provide consistent snapshots.
    /// </summary>
    Snapshot = 4
}
