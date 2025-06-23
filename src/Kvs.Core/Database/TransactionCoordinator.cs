using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace Kvs.Core.Database;

/// <summary>
/// Coordinates distributed transactions using two-phase commit protocol.
/// </summary>
public class TransactionCoordinator : ITransactionCoordinator
{
    private readonly ConcurrentDictionary<string, CoordinatedTransaction> transactions;
    private readonly DatabaseWAL transactionLog;
    private readonly SemaphoreSlim coordinatorLock;
    private readonly Timer recoveryTimer;
    private bool disposed;

    /// <summary>
    /// Initializes a new instance of the <see cref="TransactionCoordinator"/> class.
    /// </summary>
    /// <param name="transactionLog">The transaction log for durability.</param>
    public TransactionCoordinator(DatabaseWAL transactionLog)
    {
        this.transactionLog = transactionLog ?? throw new ArgumentNullException(nameof(transactionLog));
        this.transactions = new ConcurrentDictionary<string, CoordinatedTransaction>();
        this.coordinatorLock = new SemaphoreSlim(1, 1);

        // Start recovery timer to handle incomplete transactions
        this.recoveryTimer = new Timer(
            async _ => await this.RecoverAsync().ConfigureAwait(false),
            null,
            TimeSpan.FromMinutes(1),
            TimeSpan.FromMinutes(5));
    }

    /// <summary>
    /// Begins a distributed transaction.
    /// </summary>
    /// <param name="transactionId">The unique identifier for the transaction.</param>
    /// <param name="participants">The array of participants in the distributed transaction.</param>
    /// <returns>A <see cref="Task"/> representing the asynchronous operation.</returns>
    public async Task BeginTransactionAsync(string transactionId, ITransactionParticipant[] participants)
    {
        this.ThrowIfDisposed();

        if (string.IsNullOrEmpty(transactionId))
        {
            throw new ArgumentException("Transaction ID cannot be null or empty.", nameof(transactionId));
        }

        if (participants == null || participants.Length == 0)
        {
            throw new ArgumentException("At least one participant is required.", nameof(participants));
        }

        var transaction = new CoordinatedTransaction
        {
            TransactionId = transactionId,
            Participants = participants.ToList(),
            Phase = TransactionPhase.Active,
            State = TransactionCoordinatorState.Active,
            StartTime = DateTime.UtcNow
        };

        if (!this.transactions.TryAdd(transactionId, transaction))
        {
            throw new InvalidOperationException($"Transaction {transactionId} already exists.");
        }

        // Log transaction start
        var entry = new TransactionLogEntry
        {
            TransactionId = transactionId,
            Type = TransactionLogEntryType.Begin,
            Data = new ReadOnlyMemory<byte>(System.Text.Encoding.UTF8.GetBytes(string.Join(",", participants.Select(p => p.ParticipantId)))),
            Timestamp = DateTime.UtcNow
        };

        await this.transactionLog.WriteEntryAsync(entry).ConfigureAwait(false);
    }

    /// <summary>
    /// Prepares the transaction for commit (Phase 1 of 2PC).
    /// </summary>
    /// <param name="transactionId">The ID of the transaction to prepare.</param>
    /// <returns>A <see cref="Task"/> representing the asynchronous operation.</returns>
    public async Task<bool> PrepareAsync(string transactionId)
    {
        this.ThrowIfDisposed();

        if (!this.transactions.TryGetValue(transactionId, out var transaction))
        {
            throw new InvalidOperationException($"Transaction {transactionId} not found.");
        }

        await this.coordinatorLock.WaitAsync().ConfigureAwait(false);
        try
        {
            if (transaction.State != TransactionCoordinatorState.Active)
            {
                throw new InvalidOperationException($"Transaction {transactionId} is not in Active state.");
            }

            transaction.Phase = TransactionPhase.Preparing;
            transaction.State = TransactionCoordinatorState.WaitingForVotes;

            // Log prepare phase start
            var prepareEntry = new TransactionLogEntry
            {
                TransactionId = transactionId,
                Type = TransactionLogEntryType.Prepare,
                Timestamp = DateTime.UtcNow
            };

            await this.transactionLog.WriteEntryAsync(prepareEntry).ConfigureAwait(false);

            // Send prepare to all participants
            var prepareTasks = new List<Task<bool>>();
            foreach (var participant in transaction.Participants)
            {
                prepareTasks.Add(this.PrepareParticipantAsync(transaction, participant));
            }

            var votes = await Task.WhenAll(prepareTasks).ConfigureAwait(false);

            // Check if all voted to commit
            var votesList = new List<bool>(votes);
            var allVotedCommit = votesList.TrueForAll(v => v);

            if (allVotedCommit)
            {
                transaction.State = TransactionCoordinatorState.Prepared;

                // Log prepared state
                var preparedEntry = new TransactionLogEntry
                {
                    TransactionId = transactionId,
                    Type = TransactionLogEntryType.Prepare,
                    Timestamp = DateTime.UtcNow
                };

                await this.transactionLog.WriteEntryAsync(preparedEntry).ConfigureAwait(false);
            }
            else
            {
                // At least one participant voted to abort
                transaction.State = TransactionCoordinatorState.Aborted;

                // Must abort the transaction
                await this.AbortInternalAsync(transaction).ConfigureAwait(false);
            }

            return allVotedCommit;
        }
        finally
        {
            this.coordinatorLock.Release();
        }
    }

    /// <summary>
    /// Commits the transaction (Phase 2 of 2PC).
    /// </summary>
    /// <param name="transactionId">The ID of the transaction to commit.</param>
    /// <returns>A <see cref="Task"/> representing the asynchronous operation.</returns>
    public async Task CommitAsync(string transactionId)
    {
        this.ThrowIfDisposed();

        if (!this.transactions.TryGetValue(transactionId, out var transaction))
        {
            throw new InvalidOperationException($"Transaction {transactionId} not found.");
        }

        await this.coordinatorLock.WaitAsync().ConfigureAwait(false);
        try
        {
            if (transaction.State != TransactionCoordinatorState.Prepared)
            {
                throw new InvalidOperationException($"Transaction {transactionId} is not in Prepared state.");
            }

            transaction.Phase = TransactionPhase.Committing;

            // Log commit decision
            var commitEntry = new TransactionLogEntry
            {
                TransactionId = transactionId,
                Type = TransactionLogEntryType.Commit,
                Timestamp = DateTime.UtcNow
            };

            await this.transactionLog.WriteEntryAsync(commitEntry).ConfigureAwait(false);

            // Send commit to all participants
            var commitTasks = new List<Task>();
            foreach (var participant in transaction.Participants)
            {
                commitTasks.Add(this.CommitParticipantAsync(transaction, participant));
            }

            await Task.WhenAll(commitTasks).ConfigureAwait(false);

            transaction.State = TransactionCoordinatorState.Committed;
            transaction.Phase = TransactionPhase.Complete;
            transaction.EndTime = DateTime.UtcNow;

            // Log completion
            var completedEntry = new TransactionLogEntry
            {
                TransactionId = transactionId,
                Type = TransactionLogEntryType.Commit,
                Timestamp = DateTime.UtcNow
            };

            await this.transactionLog.WriteEntryAsync(completedEntry).ConfigureAwait(false);

            // Remove from active transactions
            this.transactions.TryRemove(transactionId, out _);
        }
        finally
        {
            this.coordinatorLock.Release();
        }
    }

    /// <summary>
    /// Aborts the transaction.
    /// </summary>
    /// <param name="transactionId">The ID of the transaction to abort.</param>
    /// <returns>A <see cref="Task"/> representing the asynchronous operation.</returns>
    public async Task AbortAsync(string transactionId)
    {
        this.ThrowIfDisposed();

        if (!this.transactions.TryGetValue(transactionId, out var transaction))
        {
            throw new InvalidOperationException($"Transaction {transactionId} not found.");
        }

        await this.coordinatorLock.WaitAsync().ConfigureAwait(false);
        try
        {
            await this.AbortInternalAsync(transaction).ConfigureAwait(false);
        }
        finally
        {
            this.coordinatorLock.Release();
        }
    }

    /// <summary>
    /// Gets the status of a transaction.
    /// </summary>
    /// <param name="transactionId">The ID of the transaction to check.</param>
    /// <returns>A <see cref="Task"/> representing the asynchronous operation.</returns>
    public async Task<TransactionCoordinatorStatus> GetStatusAsync(string transactionId)
    {
        this.ThrowIfDisposed();

        if (!this.transactions.TryGetValue(transactionId, out var transaction))
        {
            return new TransactionCoordinatorStatus
            {
                TransactionId = transactionId,
                State = TransactionCoordinatorState.Aborted,
                Phase = TransactionPhase.None
            };
        }

        var participantStatuses = new List<ParticipantStatus>();
        foreach (var participant in transaction.Participants)
        {
            var status = await participant.GetStatusAsync(transactionId).ConfigureAwait(false);
            participantStatuses.Add(status);
        }

        return new TransactionCoordinatorStatus
        {
            TransactionId = transactionId,
            Phase = transaction.Phase,
            State = transaction.State,
            ParticipantStatuses = participantStatuses.ToArray(),
            StartTime = transaction.StartTime,
            EndTime = transaction.EndTime
        };
    }

    /// <summary>
    /// Recovers incomplete transactions after a failure.
    /// </summary>
    /// <returns>A <see cref="Task"/> representing the asynchronous operation.</returns>
    public async Task RecoverAsync()
    {
        this.ThrowIfDisposed();

        await this.coordinatorLock.WaitAsync().ConfigureAwait(false);
        try
        {
            // Get the last checkpoint
            var lastLsn = await this.transactionLog.GetLastLsnAsync().ConfigureAwait(false);
            if (lastLsn == 0)
            {
                return;
            }

            // Read all entries from the beginning
            var entries = await this.transactionLog.ReadEntriesAsync(0).ConfigureAwait(false);

            // Group by transaction ID
            var transactionEntries = entries.GroupBy(e => e.TransactionId);

            foreach (var group in transactionEntries)
            {
                var transactionId = group.Key;
                var lastEntry = group.OrderByDescending(e => e.Timestamp).First();

                // Check if transaction is incomplete
                if (lastEntry.Type != TransactionLogEntryType.Commit && lastEntry.Type != TransactionLogEntryType.Rollback)
                {
                    // Transaction is incomplete - need to recover
                    if (lastEntry.Type == TransactionLogEntryType.Commit)
                    {
                        // Commit decision was made but not completed
                        await this.RecoverCommitAsync(transactionId).ConfigureAwait(false);
                    }
                    else if (lastEntry.Type == TransactionLogEntryType.Prepare)
                    {
                        // Transaction was prepared but no decision made - abort
                        await this.RecoverAbortAsync(transactionId).ConfigureAwait(false);
                    }
                    else
                    {
                        // Transaction was not prepared - abort
                        await this.RecoverAbortAsync(transactionId).ConfigureAwait(false);
                    }
                }
            }
        }
        finally
        {
            this.coordinatorLock.Release();
        }
    }

    /// <summary>
    /// Disposes the transaction coordinator.
    /// </summary>
    public void Dispose()
    {
        this.Dispose(true);
        GC.SuppressFinalize(this);
    }

    /// <summary>
    /// Disposes the transaction coordinator.
    /// </summary>
    /// <param name="disposing">True if disposing managed resources; false otherwise.</param>
    protected virtual void Dispose(bool disposing)
    {
        if (!this.disposed)
        {
            if (disposing)
            {
                this.recoveryTimer?.Dispose();
                this.coordinatorLock?.Dispose();
            }

            this.disposed = true;
        }
    }

    /// <summary>
    /// Prepares a single participant for the transaction.
    /// </summary>
    /// <param name="transaction">The coordinated transaction.</param>
    /// <param name="participant">The participant to prepare.</param>
    /// <returns>True if the participant votes to commit; false otherwise.</returns>
    private async Task<bool> PrepareParticipantAsync(CoordinatedTransaction transaction, ITransactionParticipant participant)
    {
        try
        {
            var vote = await participant.PrepareAsync(transaction.TransactionId).ConfigureAwait(false);

            transaction.ParticipantVotes[participant.ParticipantId] = vote;

            return vote;
        }
        catch (Exception)
        {
            // If we can't reach a participant, vote to abort
            transaction.ParticipantVotes[participant.ParticipantId] = false;
            return false;
        }
    }

    /// <summary>
    /// Commits the transaction for a single participant.
    /// </summary>
    /// <param name="transaction">The coordinated transaction.</param>
    /// <param name="participant">The participant to commit.</param>
    /// <returns>A task representing the asynchronous operation.</returns>
    private async Task CommitParticipantAsync(CoordinatedTransaction transaction, ITransactionParticipant participant)
    {
        const int maxRetries = 3;
        int retryCount = 0;

        while (retryCount < maxRetries)
        {
            try
            {
                await participant.CommitAsync(transaction.TransactionId).ConfigureAwait(false);
                return; // Success
            }
            catch (Exception)
            {
                retryCount++;
                if (retryCount >= maxRetries)
                {
                    // Log error but continue - participant will need to recover
                    // In a real system, we would retry with exponential backoff
                    break;
                }

                // Wait before retry
                await Task.Delay(100 * retryCount).ConfigureAwait(false);
            }
        }
    }

    /// <summary>
    /// Aborts the transaction for a single participant.
    /// </summary>
    /// <param name="transaction">The coordinated transaction.</param>
    /// <param name="participant">The participant to abort.</param>
    /// <returns>A task representing the asynchronous operation.</returns>
    private async Task AbortParticipantAsync(CoordinatedTransaction transaction, ITransactionParticipant participant)
    {
        try
        {
            await participant.AbortAsync(transaction.TransactionId).ConfigureAwait(false);
        }
        catch (Exception)
        {
            // Log error but continue - participant will need to recover
        }
    }

    /// <summary>
    /// Internal method to abort a transaction and notify all participants.
    /// </summary>
    /// <param name="transaction">The transaction to abort.</param>
    /// <returns>A task representing the asynchronous operation.</returns>
    private async Task AbortInternalAsync(CoordinatedTransaction transaction)
    {
        transaction.Phase = TransactionPhase.Aborting;
        transaction.State = TransactionCoordinatorState.Aborted;

        // Log abort decision
        var abortEntry = new TransactionLogEntry
        {
            TransactionId = transaction.TransactionId,
            Type = TransactionLogEntryType.Rollback,
            Timestamp = DateTime.UtcNow
        };

        await this.transactionLog.WriteEntryAsync(abortEntry).ConfigureAwait(false);

        // Send abort to all participants
        var abortTasks = new List<Task>();
        foreach (var participant in transaction.Participants)
        {
            abortTasks.Add(this.AbortParticipantAsync(transaction, participant));
        }

        await Task.WhenAll(abortTasks).ConfigureAwait(false);

        transaction.Phase = TransactionPhase.Complete;
        transaction.EndTime = DateTime.UtcNow;

        // Log completion
        var completedEntry = new TransactionLogEntry
        {
            TransactionId = transaction.TransactionId,
            Type = TransactionLogEntryType.Commit,
            Timestamp = DateTime.UtcNow
        };

        await this.transactionLog.WriteEntryAsync(completedEntry).ConfigureAwait(false);

        // Remove from active transactions
        this.transactions.TryRemove(transaction.TransactionId, out _);
    }

    /// <summary>
    /// Recovers a transaction that had a commit decision but was not completed.
    /// </summary>
    /// <param name="transactionId">The ID of the transaction to recover.</param>
    /// <returns>A task representing the asynchronous operation.</returns>
    private async Task RecoverCommitAsync(string transactionId)
    {
        // In recovery, we don't have the participant list
        // In a real system, this would be persisted in the log
        // For now, just log completion
        var completedEntry = new TransactionLogEntry
        {
            TransactionId = transactionId,
            Type = TransactionLogEntryType.Commit,
            Timestamp = DateTime.UtcNow
        };

        await this.transactionLog.WriteEntryAsync(completedEntry).ConfigureAwait(false);
    }

    /// <summary>
    /// Recovers a transaction by aborting it.
    /// </summary>
    /// <param name="transactionId">The ID of the transaction to abort.</param>
    /// <returns>A task representing the asynchronous operation.</returns>
    private async Task RecoverAbortAsync(string transactionId)
    {
        // Log abort decision
        var abortEntry = new TransactionLogEntry
        {
            TransactionId = transactionId,
            Type = TransactionLogEntryType.Rollback,
            Timestamp = DateTime.UtcNow
        };

        await this.transactionLog.WriteEntryAsync(abortEntry).ConfigureAwait(false);

        // Log completion
        var completedEntry = new TransactionLogEntry
        {
            TransactionId = transactionId,
            Type = TransactionLogEntryType.Commit,
            Timestamp = DateTime.UtcNow
        };

        await this.transactionLog.WriteEntryAsync(completedEntry).ConfigureAwait(false);
    }

    private void ThrowIfDisposed()
    {
        if (this.disposed)
        {
            throw new ObjectDisposedException(nameof(TransactionCoordinator));
        }
    }

    /// <summary>
    /// Represents a coordinated transaction.
    /// </summary>
    private sealed class CoordinatedTransaction
    {
        public string TransactionId { get; set; } = string.Empty;

        public List<ITransactionParticipant> Participants { get; set; } = new List<ITransactionParticipant>();

        public TransactionPhase Phase { get; set; }

        public TransactionCoordinatorState State { get; set; }

        public DateTime StartTime { get; set; }

#if NET8_0_OR_GREATER
        public DateTime? EndTime { get; set; }
#else
        public DateTime? EndTime { get; set; }
#endif

        public Dictionary<string, bool> ParticipantVotes { get; } = new Dictionary<string, bool>();
    }
}
