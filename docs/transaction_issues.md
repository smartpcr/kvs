# Transaction Implementation Issues

This document lists potential concurrency and correctness issues identified in `Transaction.cs`.

## 1. Lock-Order Inversion
- Locks from the lock manager (read/write) are acquired before the internal `transactionLock`.
- If another code path holds `transactionLock` first and then tries to acquire a database lock, a deadlock can occur.

### Suggested Fix for Lock-Order Inversion
- Establish a strict lock hierarchy and always acquire the internal `transactionLock` *before* any external lock from the lock manager.
- In `ReadAsync`, `WriteAsync`, and `DeleteAsync`, move `await transactionLock.WaitAsync()` to occur *prior* to calls to `AcquireReadLockAsync` or `AcquireWriteLockAsync`.
- Release locks in reverse order (external lock then `transactionLock`) to maintain consistency.
- Audit all code paths (including commit, rollback, and timeout callback) to respect this same ordering.

## 2. Incomplete Cancellation/Abort Handling
- Not all async operations (e.g., WAL writes, lock waits) observe the abort cancellation token.
- The `CancellationTokenSource` and the timeout `Timer` are not disposed on transaction end, causing resource leaks.

## 3. No Rollback on Partial Commit Failures
- If an exception occurs during `ApplyOperationAsync`, already-applied operations are not rolled back.
- Locks may not be released correctly, and the transaction state can become inconsistent.

## 4. Read Cache Staleness
- Read cache is not cleared or updated after `WriteAsync` or `DeleteAsync`.
- Subsequent reads under RepeatableRead/Serializable may return stale data instead of the most recent transaction writes.

## 5. Write-Lock Upgrade Issues
- On failed `UpgradeLockAsync`, the old read lock may still be held, starving other writers or leaving the transaction without required write access.

## 6. Races on `operations` and `readCache`
- Although `ConcurrentDictionary` is used, access to these structures happens both inside and outside the `transactionLock`, leading to potential races under concurrent writes.

## 7. Incomplete Dispose Implementation
- `Dispose()` rolls back only if in certain states, but does not release held locks or stop the timeout timer in all terminal states.
- Resources like `abortCancellationSource` and `timeoutTimer` are not always cleaned up.

## 8. Timeout-Timer Races
- The timer callback can abort the transaction in the middle of a commit, leading to mixed or partial commit states.
- There is no guard preventing an abort from firing during critical commit phases.

## 9. Deadlock Detector Thread-Safety and Correctness
- The internal `HashSet<string>` used for each adjacency list in `waitForGraph` is not thread-safe. Concurrent `AddWaitForAsync`/`RemoveWaitForAsync` calls and the detection pass can corrupt its state or throw exceptions.
- The DFS implementation uses a single shared `path` list and returns early on finding a cycle, which can lead to incomplete or duplicate cycle reporting.
- The periodic detection via `Timer` can overlap previous runs if detection takes longer than the interval, potentially causing concurrent emissions of the same deadlock.

### Suggested Fixes
- Replace `HashSet<string>` with a thread-safe structure (e.g., `ConcurrentDictionary<string,bool>`) or wrap each adjacency list with its own lock.
- In `FindCycles`, use a fresh, local `path` list on each DFS branch and collect all cycles without returning early.
- In `DetectDeadlocks`, take an immutable snapshot of `waitForGraph` under `graphLock` and then release the lock before running cycle detection to avoid long lock holding and overlapping runs.
