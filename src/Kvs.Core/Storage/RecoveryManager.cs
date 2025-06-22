using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

namespace Kvs.Core.Storage;

/// <summary>
/// Represents the phases of database recovery using the ARIES protocol.
/// </summary>
public enum RecoveryPhase
{
    /// <summary>
    /// Analysis phase - determines which transactions were active and which pages were dirty.
    /// </summary>
    Analysis,

    /// <summary>
    /// Redo phase - repeats all operations from committed transactions.
    /// </summary>
    Redo,

    /// <summary>
    /// Undo phase - undoes operations from uncommitted transactions.
    /// </summary>
    Undo
}

/// <summary>
/// Implements database recovery using the ARIES (Algorithm for Recovery and Isolation Exploiting Semantics) protocol.
/// </summary>
public class RecoveryManager(ITransactionLog transactionLog, IPageManager pageManager) : IRecoveryManager
{
    private readonly ITransactionLog transactionLog = transactionLog ?? throw new ArgumentNullException(nameof(transactionLog));
    private readonly IPageManager pageManager = pageManager ?? throw new ArgumentNullException(nameof(pageManager));
    private readonly Dictionary<string, List<TransactionLogEntry>> activeTransactions = [];
    private readonly HashSet<string> committedTransactions = [];

    /// <inheritdoc />
    public async Task<bool> RecoverAsync()
    {
        try
        {
            // Phase 1: Analysis - Find all transactions and their states
            var (lastCheckpointLsn, activeTransactions) = await this.AnalysisPhaseAsync();

            // Phase 2: Redo - Replay all committed operations
            await this.RedoPhaseAsync(lastCheckpointLsn);

            // Phase 3: Undo - Rollback uncommitted transactions
            await this.UndoPhaseAsync(activeTransactions);

            return true;
        }
        catch (Exception)
        {
            return false;
        }
    }

    /// <inheritdoc />
    public async Task<TransactionLogEntry[]> GetUncommittedTransactionsAsync()
    {
        var allEntries = await this.transactionLog.ReadEntriesAsync(0);
        var uncommittedEntries = new List<TransactionLogEntry>();

        var transactionStates = new Dictionary<string, bool>(); // true = committed, false = active

        foreach (var entry in allEntries)
        {
            switch (entry.OperationType)
            {
                case OperationType.Insert:
                case OperationType.Update:
                case OperationType.Delete:
                    transactionStates[entry.TransactionId] = false; // Mark as active
                    break;

                case OperationType.Commit:
                    transactionStates[entry.TransactionId] = true; // Mark as committed
                    break;

                case OperationType.Rollback:
                    transactionStates.Remove(entry.TransactionId); // Remove from tracking
                    break;
            }
        }

        // Find entries for uncommitted transactions
        foreach (var entry in allEntries)
        {
            if (!string.IsNullOrEmpty(entry.TransactionId) &&
                transactionStates.TryGetValue(entry.TransactionId, out var isCommitted) && !isCommitted)
            {
                uncommittedEntries.Add(entry);
            }
        }

        return [.. uncommittedEntries];
    }

    /// <inheritdoc />
    public async Task RollbackTransactionAsync(string transactionId)
    {
        if (string.IsNullOrEmpty(transactionId))
        {
            throw new ArgumentException("Transaction ID cannot be null or empty", nameof(transactionId));
        }

        // Get all entries for this transaction
        var allEntries = await this.transactionLog.ReadEntriesAsync(0);
        var transactionEntries = allEntries
            .Where(e => e.TransactionId == transactionId)
#if NET472
            .Where(e => e.OperationType == OperationType.Insert || e.OperationType == OperationType.Update || e.OperationType == OperationType.Delete)
#else
            .Where(e => e.OperationType is OperationType.Insert or OperationType.Update or OperationType.Delete)
#endif
            .OrderByDescending(e => e.Lsn) // Reverse order for undo
            .ToArray();

        // Apply undo operations
        foreach (var entry in transactionEntries)
        {
            await this.UndoOperationAsync(entry);
        }

        // Write rollback entry to WAL
        var rollbackEntry = new TransactionLogEntry(
            0, // LSN will be assigned by WAL
            transactionId,
            OperationType.Rollback,
            -1,
            ReadOnlyMemory<byte>.Empty,
            ReadOnlyMemory<byte>.Empty,
            DateTime.UtcNow);

        await this.transactionLog.WriteEntryAsync(rollbackEntry);
    }

    /// <inheritdoc />
    public async Task RedoTransactionAsync(string transactionId)
    {
        if (string.IsNullOrEmpty(transactionId))
        {
            throw new ArgumentException("Transaction ID cannot be null or empty", nameof(transactionId));
        }

        // Get all entries for this transaction
        var allEntries = await this.transactionLog.ReadEntriesAsync(0);
        var transactionEntries = allEntries
            .Where(e => e.TransactionId == transactionId)
#if NET472
            .Where(e => e.OperationType == OperationType.Insert || e.OperationType == OperationType.Update || e.OperationType == OperationType.Delete)
#else
            .Where(e => e.OperationType is OperationType.Insert or OperationType.Update or OperationType.Delete)
#endif
            .OrderBy(e => e.Lsn) // Forward order for redo
            .ToArray();

        // Apply redo operations
        foreach (var entry in transactionEntries)
        {
            await this.RedoOperationAsync(entry);
        }
    }

    /// <inheritdoc />
    public async Task<bool> IsRecoveryNeededAsync()
    {
        try
        {
            var uncommittedTransactions = await this.GetUncommittedTransactionsAsync();
            return uncommittedTransactions.Length > 0;
        }
        catch
        {
            return true; // Assume recovery is needed if we can't determine state
        }
    }

    private async Task<(long lastCheckpointLsn, Dictionary<string, List<TransactionLogEntry>> activeTransactions)> AnalysisPhaseAsync()
    {
        var allEntries = await this.transactionLog.ReadEntriesAsync(0);
        var activeTransactions = new Dictionary<string, List<TransactionLogEntry>>();
        var committedTransactions = new HashSet<string>();
        long lastCheckpointLsn = 0;

        foreach (var entry in allEntries)
        {
            switch (entry.OperationType)
            {
                case OperationType.Checkpoint:
                    lastCheckpointLsn = entry.Lsn;
                    break;

                case OperationType.Insert:
                case OperationType.Update:
                case OperationType.Delete:
                    if (!activeTransactions.ContainsKey(entry.TransactionId))
                    {
                        activeTransactions[entry.TransactionId] = [];
                    }

                    activeTransactions[entry.TransactionId].Add(entry);
                    break;

                case OperationType.Commit:
                    committedTransactions.Add(entry.TransactionId);
                    break;

                case OperationType.Rollback:
                    activeTransactions.Remove(entry.TransactionId);
                    break;
            }
        }

        // Remove committed transactions from active list
        foreach (var txId in committedTransactions)
        {
            activeTransactions.Remove(txId);
        }

        return (lastCheckpointLsn, activeTransactions);
    }

    private async Task RedoPhaseAsync(long fromLsn)
    {
        var entries = await this.transactionLog.ReadEntriesAsync(fromLsn);
        var committedTransactions = new HashSet<string>();

        // First pass: identify committed transactions
        foreach (var entry in entries)
        {
            if (entry.OperationType == OperationType.Commit)
            {
                committedTransactions.Add(entry.TransactionId);
            }
        }

        // Second pass: redo operations for committed transactions
        foreach (var entry in entries.OrderBy(e => e.Lsn))
        {
            if (committedTransactions.Contains(entry.TransactionId) &&
#if NET472
                (entry.OperationType == OperationType.Insert || entry.OperationType == OperationType.Update || entry.OperationType == OperationType.Delete))
#else
                entry.OperationType is OperationType.Insert or OperationType.Update or OperationType.Delete)
#endif
            {
                await this.RedoOperationAsync(entry);
            }
        }
    }

    private async Task UndoPhaseAsync(Dictionary<string, List<TransactionLogEntry>> activeTransactions)
    {
#if NET472
        foreach (var kvp in activeTransactions)
        {
            var transactionId = kvp.Key;
            var entries = kvp.Value;
#else
        foreach (var (transactionId, entries) in activeTransactions)
        {
#endif

            // Undo operations in reverse order
            foreach (var entry in entries.OrderByDescending(e => e.Lsn))
            {
                await this.UndoOperationAsync(entry);
            }

            // Write rollback entry
            var rollbackEntry = new TransactionLogEntry(
                0, // LSN will be assigned by WAL
                transactionId,
                OperationType.Rollback,
                -1,
                ReadOnlyMemory<byte>.Empty,
                ReadOnlyMemory<byte>.Empty,
                DateTime.UtcNow);

            await this.transactionLog.WriteEntryAsync(rollbackEntry);
        }
    }

    private async Task RedoOperationAsync(TransactionLogEntry entry)
    {
        try
        {
            if (entry.PageId >= 0 && !entry.AfterImage.IsEmpty)
            {
                // Apply the after image to the page
                var page = await this.pageManager.GetPageAsync(entry.PageId);
                page.WriteData(entry.AfterImage.Span);
                await this.pageManager.WritePageAsync(page);
            }
        }
        catch
        {
            // Log error but continue with recovery
        }
    }

    private async Task UndoOperationAsync(TransactionLogEntry entry)
    {
        try
        {
            if (entry.PageId >= 0 && !entry.BeforeImage.IsEmpty)
            {
                // Apply the before image to the page
                var page = await this.pageManager.GetPageAsync(entry.PageId);
                page.WriteData(entry.BeforeImage.Span);
                await this.pageManager.WritePageAsync(page);
            }
            else if (entry.PageId >= 0 && entry.OperationType == OperationType.Insert)
            {
                // For inserts, we need to remove the entry (free the page or mark as deleted)
                await this.pageManager.FreePageAsync(entry.PageId);
            }
        }
        catch
        {
            // Log error but continue with recovery
        }
    }

/* Unmerged change from project 'Kvs.Core(net8.0)'
Before:
}
After:
}
*/
}
