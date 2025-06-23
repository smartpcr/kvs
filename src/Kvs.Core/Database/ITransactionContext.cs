#if !NET472
#nullable enable
#endif

using System;
using System.Data;
using System.Threading.Tasks;

namespace Kvs.Core.Database;

/// <summary>
/// Provides transaction context for collections to support isolation levels.
/// </summary>
internal interface ITransactionContext
{
    /// <summary>
    /// Gets the current transaction ID if in a transaction context.
    /// </summary>
#if NET8_0_OR_GREATER
    string? CurrentTransactionId { get; }
#else
    string CurrentTransactionId { get; }
#endif

    /// <summary>
    /// Gets the transaction data for the current transaction.
    /// </summary>
    /// <param name="transactionId">The transaction ID.</param>
    /// <returns>The transaction data, or null if not found.</returns>
#if NET8_0_OR_GREATER
    TransactionData? GetTransactionData(string transactionId);
#else
    TransactionData GetTransactionData(string transactionId);
#endif

    /// <summary>
    /// Gets the isolation level for a transaction.
    /// </summary>
    /// <param name="transactionId">The transaction ID.</param>
    /// <returns>The isolation level.</returns>
    IsolationLevel GetIsolationLevel(string transactionId);

    /// <summary>
    /// Gets the start time of a transaction.
    /// </summary>
    /// <param name="transactionId">The transaction ID.</param>
    /// <returns>The start time.</returns>
    DateTime GetTransactionStartTime(string transactionId);

    /// <summary>
    /// Checks if a document version is visible to a transaction.
    /// </summary>
    /// <param name="document">The document to check.</param>
    /// <param name="transactionId">The transaction ID.</param>
    /// <returns>True if the document is visible; otherwise, false.</returns>
    bool IsDocumentVisible(Document document, string transactionId);

    /// <summary>
    /// Gets the version manager.
    /// </summary>
    VersionManager VersionManager { get; }
}

