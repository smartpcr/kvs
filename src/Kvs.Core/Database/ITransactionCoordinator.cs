using System;
using System.Threading.Tasks;

namespace Kvs.Core.Database;

/// <summary>
/// Defines the interface for coordinating distributed transactions using two-phase commit.
/// </summary>
public interface ITransactionCoordinator : IDisposable
{
    /// <summary>
    /// Begins a distributed transaction.
    /// </summary>
    /// <param name="transactionId">The unique identifier for the transaction.</param>
    /// <param name="participants">The participants in the transaction.</param>
    /// <returns>A task that represents the asynchronous operation.</returns>
    Task BeginTransactionAsync(string transactionId, ITransactionParticipant[] participants);

    /// <summary>
    /// Prepares the transaction for commit (Phase 1 of 2PC).
    /// </summary>
    /// <param name="transactionId">The transaction identifier.</param>
    /// <returns>A task that represents the asynchronous operation. The task result indicates whether all participants voted to commit.</returns>
    Task<bool> PrepareAsync(string transactionId);

    /// <summary>
    /// Commits the transaction (Phase 2 of 2PC).
    /// </summary>
    /// <param name="transactionId">The transaction identifier.</param>
    /// <returns>A task that represents the asynchronous operation.</returns>
    Task CommitAsync(string transactionId);

    /// <summary>
    /// Aborts the transaction.
    /// </summary>
    /// <param name="transactionId">The transaction identifier.</param>
    /// <returns>A task that represents the asynchronous operation.</returns>
    Task AbortAsync(string transactionId);

    /// <summary>
    /// Gets the status of a transaction.
    /// </summary>
    /// <param name="transactionId">The transaction identifier.</param>
    /// <returns>A task that represents the asynchronous operation. The task result contains the transaction status.</returns>
    Task<TransactionCoordinatorStatus> GetStatusAsync(string transactionId);

    /// <summary>
    /// Recovers incomplete transactions after a failure.
    /// </summary>
    /// <returns>A task that represents the asynchronous operation.</returns>
    Task RecoverAsync();
}

/// <summary>
/// Defines the interface for participants in a distributed transaction.
/// </summary>
public interface ITransactionParticipant
{
    /// <summary>
    /// Gets the unique identifier for the participant.
    /// </summary>
    string ParticipantId { get; }

    /// <summary>
    /// Prepares the participant for commit.
    /// </summary>
    /// <param name="transactionId">The transaction identifier.</param>
    /// <returns>A task that represents the asynchronous operation. The task result indicates whether the participant votes to commit.</returns>
    Task<bool> PrepareAsync(string transactionId);

    /// <summary>
    /// Commits the transaction on the participant.
    /// </summary>
    /// <param name="transactionId">The transaction identifier.</param>
    /// <returns>A task that represents the asynchronous operation.</returns>
    Task CommitAsync(string transactionId);

    /// <summary>
    /// Aborts the transaction on the participant.
    /// </summary>
    /// <param name="transactionId">The transaction identifier.</param>
    /// <returns>A task that represents the asynchronous operation.</returns>
    Task AbortAsync(string transactionId);

    /// <summary>
    /// Gets the status of the transaction on the participant.
    /// </summary>
    /// <param name="transactionId">The transaction identifier.</param>
    /// <returns>A task that represents the asynchronous operation. The task result contains the participant's status.</returns>
    Task<ParticipantStatus> GetStatusAsync(string transactionId);
}

/// <summary>
/// Represents the status of a distributed transaction coordinator.
/// </summary>
public class TransactionCoordinatorStatus
{
    /// <summary>
    /// Gets or sets the transaction identifier.
    /// </summary>
    public string TransactionId { get; set; } = string.Empty;

    /// <summary>
    /// Gets or sets the current phase of the transaction.
    /// </summary>
    public TransactionPhase Phase { get; set; }

    /// <summary>
    /// Gets or sets the transaction state.
    /// </summary>
    public TransactionCoordinatorState State { get; set; }

    /// <summary>
    /// Gets or sets the participant statuses.
    /// </summary>
    public ParticipantStatus[] ParticipantStatuses { get; set; } = Array.Empty<ParticipantStatus>();

    /// <summary>
    /// Gets or sets the start time of the transaction.
    /// </summary>
    public DateTime StartTime { get; set; }

    /// <summary>
    /// Gets or sets the end time of the transaction.
    /// </summary>
#if NET8_0_OR_GREATER
    public DateTime? EndTime { get; set; }
#else
    public DateTime? EndTime { get; set; }
#endif
}

/// <summary>
/// Represents the status of a participant in a distributed transaction.
/// </summary>
public class ParticipantStatus
{
    /// <summary>
    /// Gets or sets the participant identifier.
    /// </summary>
    public string ParticipantId { get; set; } = string.Empty;

    /// <summary>
    /// Gets or sets the participant's vote in the prepare phase.
    /// </summary>
#if NET8_0_OR_GREATER
    public bool? Vote { get; set; }
#else
    public bool? Vote { get; set; }
#endif

    /// <summary>
    /// Gets or sets the participant's state.
    /// </summary>
    public ParticipantState State { get; set; }

    /// <summary>
    /// Gets or sets the last update time.
    /// </summary>
    public DateTime LastUpdate { get; set; }
}

/// <summary>
/// Represents the phase of a distributed transaction.
/// </summary>
public enum TransactionPhase
{
    /// <summary>
    /// Transaction has not started.
    /// </summary>
    None,

    /// <summary>
    /// Transaction is active.
    /// </summary>
    Active,

    /// <summary>
    /// Transaction is in the prepare phase.
    /// </summary>
    Preparing,

    /// <summary>
    /// Transaction is in the commit phase.
    /// </summary>
    Committing,

    /// <summary>
    /// Transaction is aborting.
    /// </summary>
    Aborting,

    /// <summary>
    /// Transaction is complete.
    /// </summary>
    Complete
}

/// <summary>
/// Represents the state of a distributed transaction coordinator.
/// </summary>
public enum TransactionCoordinatorState
{
    /// <summary>
    /// Transaction is active.
    /// </summary>
    Active,

    /// <summary>
    /// Waiting for participant votes.
    /// </summary>
    WaitingForVotes,

    /// <summary>
    /// All participants voted to commit.
    /// </summary>
    Prepared,

    /// <summary>
    /// Transaction is committed.
    /// </summary>
    Committed,

    /// <summary>
    /// Transaction is aborted.
    /// </summary>
    Aborted,

    /// <summary>
    /// Transaction is in an uncertain state.
    /// </summary>
    Uncertain
}

/// <summary>
/// Represents the state of a participant in a distributed transaction.
/// </summary>
public enum ParticipantState
{
    /// <summary>
    /// Participant is active.
    /// </summary>
    Active,

    /// <summary>
    /// Participant is preparing.
    /// </summary>
    Preparing,

    /// <summary>
    /// Participant is prepared.
    /// </summary>
    Prepared,

    /// <summary>
    /// Participant has committed.
    /// </summary>
    Committed,

    /// <summary>
    /// Participant has aborted.
    /// </summary>
    Aborted,

    /// <summary>
    /// Participant is unreachable.
    /// </summary>
    Unreachable
}
