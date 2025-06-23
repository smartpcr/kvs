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
    private readonly ConcurrentDictionary<string, ConcurrentDictionary<string, byte>> waitForGraph;
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
        this.waitForGraph = new ConcurrentDictionary<string, ConcurrentDictionary<string, byte>>();
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
            var dependencies = this.waitForGraph.GetOrAdd(waitingTransaction, _ => new ConcurrentDictionary<string, byte>());
            dependencies[holdingTransaction] = 0;

            this.transactionStartTimes.TryAdd(waitingTransaction, DateTime.UtcNow);
            this.transactionStartTimes.TryAdd(holdingTransaction, DateTime.UtcNow);

            // Check for deadlock immediately
            var cycles = this.FindCyclesSnapshot(this.GetGraphSnapshot());
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
        if (string.IsNullOrEmpty(holdingTransaction))
        {
            return;
        }

        await this.graphLock.WaitAsync().ConfigureAwait(false);
        try
        {
            if (this.waitForGraph.TryGetValue(waitingTransaction, out var dependencies))
            {
                dependencies.TryRemove(holdingTransaction, out _);
                if (dependencies.IsEmpty)
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
                kvp.Value.TryRemove(transactionId, out _);
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
            // snapshot graph under lock, then release early
            Dictionary<string, List<string>> snapshot;
            await this.graphLock.WaitAsync().ConfigureAwait(false);
            try
            {
                snapshot = this.GetGraphSnapshot();
            }
            finally
            {
                this.graphLock.Release();
            }

            var cycles = this.FindCyclesSnapshot(snapshot);
            foreach (var cycle in cycles)
            {
                var victim = this.SelectVictim(cycle);
                this.OnDeadlockDetected(new DeadlockEventArgs(victim, cycle));
            }
        });
    }

    // Create a snapshot of the current wait-for graph for cycle detection
    private Dictionary<string, List<string>> GetGraphSnapshot()
    {
        return this.waitForGraph.ToDictionary(
            kvp => kvp.Key,
            kvp => kvp.Value.Keys.ToList());
    }

    // Perform cycle detection on a provided snapshot
    private List<List<string>> FindCyclesSnapshot(Dictionary<string, List<string>> graph)
    {
        var cycles = new List<List<string>>();
        var globalVisited = new HashSet<string>();

        foreach (var node in graph.Keys)
        {
            if (!globalVisited.Contains(node))
            {
                var recursionStack = new HashSet<string>();
                var path = new List<string>();
                this.DFS(node, globalVisited, recursionStack, path, graph, cycles);
            }
        }

        return cycles;
    }

    // DFS helper for snapshot-based cycle detection
    private void DFS(
        string node,
        HashSet<string> globalVisited,
        HashSet<string> recursionStack,
        List<string> path,
        Dictionary<string, List<string>> graph,
        List<List<string>> cycles)
    {
        globalVisited.Add(node);
        recursionStack.Add(node);
        path.Add(node);

        if (graph.TryGetValue(node, out var neighbors))
        {
            foreach (var neighbor in neighbors)
            {
                if (!globalVisited.Contains(neighbor))
                {
                    this.DFS(neighbor, globalVisited, recursionStack, path, graph, cycles);
                }
                else if (recursionStack.Contains(neighbor))
                {
                    // Found a cycle
                    var start = path.IndexOf(neighbor);
                    if (start >= 0)
                    {
                        var cycle = path.Skip(start).ToList();

                        // Only add if we haven't seen this cycle before
                        if (!cycles.Exists(c => c.SequenceEqual(cycle)))
                        {
                            cycles.Add(cycle);
                        }
                    }
                }
            }
        }

        recursionStack.Remove(node);
        path.RemoveAt(path.Count - 1);
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
