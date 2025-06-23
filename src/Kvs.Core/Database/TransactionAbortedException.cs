using System;

namespace Kvs.Core.Database;

/// <summary>
/// Exception thrown when an operation is attempted on an aborted transaction.
/// </summary>
public class TransactionAbortedException : InvalidOperationException
{
    /// <summary>
    /// Gets the ID of the aborted transaction.
    /// </summary>
    public string TransactionId { get; }

    /// <summary>
    /// Initializes a new instance of the <see cref="TransactionAbortedException"/> class.
    /// </summary>
    /// <param name="transactionId">The ID of the aborted transaction.</param>
    public TransactionAbortedException(string transactionId)
        : base($"Transaction {transactionId} has been aborted.")
    {
        this.TransactionId = transactionId;
    }

    /// <summary>
    /// Initializes a new instance of the <see cref="TransactionAbortedException"/> class.
    /// </summary>
    /// <param name="message">The error message.</param>
    /// <param name="transactionId">The ID of the aborted transaction.</param>
    public TransactionAbortedException(string message, string transactionId)
        : base(message)
    {
        this.TransactionId = transactionId;
    }

    /// <summary>
    /// Initializes a new instance of the <see cref="TransactionAbortedException"/> class.
    /// </summary>
    /// <param name="message">The error message.</param>
    /// <param name="innerException">The inner exception.</param>
    /// <param name="transactionId">The ID of the aborted transaction.</param>
    public TransactionAbortedException(string message, Exception innerException, string transactionId)
        : base(message, innerException)
    {
        this.TransactionId = transactionId;
    }
}
