namespace Kvs.Core.Database;

/// <summary>
/// Represents the type of operation in a transaction.
/// </summary>
internal enum TransactionOperationType
{
    /// <summary>
    /// Read operation.
    /// </summary>
    Read,

    /// <summary>
    /// Update operation (insert or update).
    /// </summary>
    Update,

    /// <summary>
    /// Delete operation.
    /// </summary>
    Delete
}

