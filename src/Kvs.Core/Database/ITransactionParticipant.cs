using System.Threading.Tasks;

namespace Kvs.Core.Database;

/// <summary>
/// Represents a participant in a two-phase commit protocol.
/// </summary>
public interface ITransactionParticipant
{
    /// <summary>
    /// Gets the unique identifier for this participant.
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
