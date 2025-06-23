#if !NET472
#nullable enable
#endif

using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace Kvs.Core.Database;

/// <summary>
/// Event arguments for deadlock detection.
/// </summary>
public class DeadlockEventArgs : EventArgs
{
    /// <summary>
    /// Gets the victim transaction that should be rolled back.
    /// </summary>
    public string Victim { get; }

    /// <summary>
    /// Gets the transactions involved in the deadlock.
    /// </summary>
    public IReadOnlyList<string> InvolvedTransactions { get; }

    /// <summary>
    /// Initializes a new instance of the <see cref="DeadlockEventArgs"/> class.
    /// </summary>
    /// <param name="victim">The victim transaction.</param>
    /// <param name="involvedTransactions">The transactions involved in the deadlock.</param>
    public DeadlockEventArgs(string victim, IReadOnlyList<string> involvedTransactions)
    {
        this.Victim = victim;
        this.InvolvedTransactions = involvedTransactions;
    }
}

/// <summary>
/// Detects deadlocks in transaction dependencies.
/// </summary>
public sealed class DeadlockDetector : IDisposable
{
    private readonly ConcurrentDictionary<string, HashSet<string>> waitForGraph;
    private readonly ConcurrentDictionary<string, DateTime> transactionStartTimes;
    private readonly TimeSpan detectionInterval;
    private readonly SemaphoreSlim graphLock;
    private readonly Timer detectionTimer;
    private bool disposed;

    /// <summary>
    /// Occurs when a deadlock is detected.
    /// </summary>
#if NET8_0_OR_GREATER
    public event EventHandler<DeadlockEventArgs>? DeadlockDetected;
#else
    public event EventHandler<DeadlockEventArgs> DeadlockDetected;
#endif

    /// <summary>
    /// Initializes a new instance of the <see cref="DeadlockDetector"/> class.
    /// </summary>
    /// <param name="detectionInterval">The interval between deadlock detection runs.</param>
    public DeadlockDetector(TimeSpan detectionInterval)
    {
        this.waitForGraph = new ConcurrentDictionary<string, HashSet<string>>();
        this.transactionStartTimes = new ConcurrentDictionary<string, DateTime>();
        this.detectionInterval = detectionInterval;
        this.graphLock = new SemaphoreSlim(1, 1);
        this.detectionTimer = new Timer(this.DetectDeadlocks, null, detectionInterval, detectionInterval);
    }

    /// <summary>
    /// Adds a wait-for dependency between transactions.
    /// </summary>
    /// <param name="waitingTransaction">The transaction that is waiting.</param>
    /// <param name="holdingTransaction">The transaction that is holding the resource.</param>
    /// <returns>A task that represents the asynchronous operation.</returns>
    public async Task AddWaitForAsync(string waitingTransaction, string holdingTransaction)
    {
        if (waitingTransaction == holdingTransaction)
        {
            return;
        }

        await this.graphLock.WaitAsync().ConfigureAwait(false);
        try
        {
            var dependencies = this.waitForGraph.GetOrAdd(waitingTransaction, _ => new HashSet<string>());
            dependencies.Add(holdingTransaction);

            this.transactionStartTimes.TryAdd(waitingTransaction, DateTime.UtcNow);
            this.transactionStartTimes.TryAdd(holdingTransaction, DateTime.UtcNow);

            // Check for deadlock immediately
            var cycles = this.FindCycles();
            foreach (var cycle in cycles)
            {
                var victim = this.SelectVictim(cycle);
                this.OnDeadlockDetected(new DeadlockEventArgs(victim, cycle));
            }
        }
        finally
        {
            this.graphLock.Release();
        }
    }

    /// <summary>
    /// Removes a specific wait-for dependency.
    /// </summary>
    /// <param name="waitingTransaction">The waiting transaction.</param>
    /// <param name="holdingTransaction">The holding transaction.</param>
    /// <returns>A task that represents the asynchronous operation.</returns>
    public async Task RemoveWaitForAsync(string waitingTransaction, string holdingTransaction)
    {
        if (string.IsNullOrEmpty(waitingTransaction) || string.IsNullOrEmpty(holdingTransaction))
        {
            return;
        }

        await this.graphLock.WaitAsync().ConfigureAwait(false);
        try
        {
            if (this.waitForGraph.TryGetValue(waitingTransaction, out var dependencies))
            {
                dependencies.Remove(holdingTransaction);
                if (dependencies.Count == 0)
                {
                    this.waitForGraph.TryRemove(waitingTransaction, out _);
                }
            }
        }
        finally
        {
            this.graphLock.Release();
        }
    }

    /// <summary>
    /// Removes all wait-for dependencies for a transaction.
    /// </summary>
    /// <param name="transactionId">The transaction ID.</param>
    /// <returns>A task that represents the asynchronous operation.</returns>
    public async Task RemoveTransactionAsync(string transactionId)
    {
        await this.graphLock.WaitAsync().ConfigureAwait(false);
        try
        {
            this.waitForGraph.TryRemove(transactionId, out _);
            this.transactionStartTimes.TryRemove(transactionId, out _);

            // Remove transaction from all dependency lists
            foreach (var kvp in this.waitForGraph)
            {
                kvp.Value.Remove(transactionId);
            }
        }
        finally
        {
            this.graphLock.Release();
        }
    }

    /// <summary>
    /// Disposes the deadlock detector.
    /// </summary>
    public void Dispose()
    {
        if (this.disposed)
        {
            return;
        }

        this.disposed = true;
        this.detectionTimer?.Dispose();
        this.graphLock?.Dispose();
    }

#if NET8_0_OR_GREATER
    private void DetectDeadlocks(object? state)
#else
    private void DetectDeadlocks(object state)
#endif
    {
        if (this.disposed)
        {
            return;
        }

        _ = Task.Run(async () =>
        {
            await this.graphLock.WaitAsync().ConfigureAwait(false);
            try
            {
                var cycles = this.FindCycles();
                foreach (var cycle in cycles)
                {
                    var victim = this.SelectVictim(cycle);
                    this.OnDeadlockDetected(new DeadlockEventArgs(victim, cycle));
                }
            }
            finally
            {
                this.graphLock.Release();
            }
        });
    }

    private List<List<string>> FindCycles()
    {
        var cycles = new List<List<string>>();
        var visited = new HashSet<string>();
        var recursionStack = new HashSet<string>();

        foreach (var node in this.waitForGraph.Keys)
        {
            if (!visited.Contains(node))
            {
                var path = new List<string>();
                this.DFS(node, visited, recursionStack, path, cycles);
            }
        }

        return cycles;
    }

    private bool DFS(string node, HashSet<string> visited, HashSet<string> recursionStack, List<string> path, List<List<string>> cycles)
    {
        visited.Add(node);
        recursionStack.Add(node);
        path.Add(node);

        if (this.waitForGraph.TryGetValue(node, out var neighbors))
        {
            foreach (var neighbor in neighbors)
            {
                if (!visited.Contains(neighbor))
                {
                    if (this.DFS(neighbor, visited, recursionStack, path, cycles))
                    {
                        return true;
                    }
                }
                else if (recursionStack.Contains(neighbor))
                {
                    // Found a cycle
                    var cycleStart = path.IndexOf(neighbor);
                    if (cycleStart >= 0)
                    {
                        var cycle = path.Skip(cycleStart).ToList();
                        cycles.Add(cycle);
                    }
                }
            }
        }

        path.RemoveAt(path.Count - 1);
        recursionStack.Remove(node);
        return false;
    }

    private string SelectVictim(List<string> cycle)
    {
        // Select the youngest transaction as the victim
        string victim = cycle[0];
#if NET472
        var victimStartTime = this.transactionStartTimes.TryGetValue(victim, out var vTime) ? vTime : DateTime.MinValue;
#else
        var victimStartTime = this.transactionStartTimes.GetValueOrDefault(victim, DateTime.MinValue);
#endif

        foreach (var transaction in cycle.Skip(1))
        {
#if NET472
            var startTime = this.transactionStartTimes.TryGetValue(transaction, out var sTime) ? sTime : DateTime.MinValue;
#else
            var startTime = this.transactionStartTimes.GetValueOrDefault(transaction, DateTime.MinValue);
#endif
            if (startTime > victimStartTime)
            {
                victim = transaction;
                victimStartTime = startTime;
            }
        }

        return victim;
    }

    private void OnDeadlockDetected(DeadlockEventArgs e)
    {
        this.DeadlockDetected?.Invoke(this, e);
    }
}
