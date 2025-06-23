using System;

namespace Kvs.Core.Database;

/// <summary>
/// Exception thrown when a deadlock is detected.
/// </summary>
public class DeadlockException : Exception
{
    /// <summary>
    /// Gets the ID of the transaction that was chosen as the deadlock victim.
    /// </summary>
    public string VictimTransactionId { get; }

    /// <summary>
    /// Gets the transactions involved in the deadlock.
    /// </summary>
    public string[] InvolvedTransactions { get; }

    /// <summary>
    /// Initializes a new instance of the <see cref="DeadlockException"/> class.
    /// </summary>
    /// <param name="victimTransactionId">The ID of the victim transaction.</param>
    /// <param name="involvedTransactions">The transactions involved in the deadlock.</param>
    public DeadlockException(string victimTransactionId, string[] involvedTransactions)
        : base($"Transaction {victimTransactionId} was chosen as deadlock victim.")
    {
        this.VictimTransactionId = victimTransactionId;
        this.InvolvedTransactions = involvedTransactions;
    }

    /// <summary>
    /// Initializes a new instance of the <see cref="DeadlockException"/> class.
    /// </summary>
    /// <param name="message">The error message.</param>
    /// <param name="victimTransactionId">The ID of the victim transaction.</param>
    /// <param name="involvedTransactions">The transactions involved in the deadlock.</param>
    public DeadlockException(string message, string victimTransactionId, string[] involvedTransactions)
        : base(message)
    {
        this.VictimTransactionId = victimTransactionId;
        this.InvolvedTransactions = involvedTransactions;
    }

    /// <summary>
    /// Initializes a new instance of the <see cref="DeadlockException"/> class.
    /// </summary>
    /// <param name="message">The error message.</param>
    /// <param name="innerException">The inner exception.</param>
    /// <param name="victimTransactionId">The ID of the victim transaction.</param>
    /// <param name="involvedTransactions">The transactions involved in the deadlock.</param>
    public DeadlockException(string message, Exception innerException, string victimTransactionId, string[] involvedTransactions)
        : base(message, innerException)
    {
        this.VictimTransactionId = victimTransactionId;
        this.InvolvedTransactions = involvedTransactions;
    }
}
