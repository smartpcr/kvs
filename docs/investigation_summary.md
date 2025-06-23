# Investigation Summary: Flaky and Disabled Tests

Date: 2025-06-23 (Updated)

## Overview
This document summarizes the investigation of flaky and disabled tests in the KVS project, identifying root causes and implemented fixes.

## Tests Investigated

### 1. DeadlockTests.NoDeadlock_WithProperLockOrdering_ShouldSucceed
**Status**: ✅ FIXED

**Issue**: 
- Test was failing with false deadlock detection
- Two transactions acquiring locks in the same order were being flagged as deadlocked

**Root Cause**:
- The lock manager was granting read locks even when there were pending write/upgrade requests
- This caused a scenario where:
  1. Txn1 gets read lock on doc1
  2. Txn1 requests upgrade to write lock (pending)
  3. Txn2 gets read lock on doc1 (should have been blocked)
  4. Txn2 requests upgrade to write lock
  5. Both transactions now waiting for each other → false deadlock

**Fix**:
- Modified `ResourceLock.AcquireReadLockAsync()` to check for pending write/upgrade requests
- Read locks are now blocked if there are any pending write requests in the queue
- Added proper wait-for graph edges for transactions waiting on pending writes

**Code Changes**:
```csharp
// Check if there are any pending write/upgrade requests
var hasPendingWrites = this.waitQueue.Any(req => req.LockType == LockType.Write || req.IsUpgrade);

if (!hasPendingWrites)
{
    this.readLockHolders.Add(transactionId);
    // ...
}
```

### 2. IsolationTests.Serializable_ShouldPreventNonRepeatableReads
**Status**: ✅ FIXED (New issue discovered and resolved)

**Issue**:
- Test was failing with DeadlockException
- Transaction 2 was being marked as deadlock victim when Transaction 1 tried to re-read a document

**Root Cause**:
- Lock manager wasn't checking if a transaction already held a read lock before blocking on pending writes
- Scenario:
  1. Txn1 reads doc1 (gets read lock)
  2. Txn2 tries to write doc1 (blocks, waits for Txn1's read lock)
  3. Txn1 tries to read doc1 again - blocked by pending write from Txn2
  4. Deadlock detected: Txn1 waiting for Txn2's pending write, Txn2 waiting for Txn1's read lock

**Fix**:
- Added check in `ResourceLock.AcquireReadLockAsync()` to immediately grant read lock if transaction already holds it
- Prevents false deadlock when a transaction re-reads a resource it already has locked

**Code Changes**:
```csharp
// If we already hold the read lock, return immediately
if (this.readLockHolders.Contains(transactionId))
{
    this.lockSemaphore.Release();
    semaphoreReleased = true;
    return true;
}
```

### 3. VersionManager Timeout Tests (3 tests)
**Status**: ⚠️ DESIGN LIMITATION - Tests remain skipped

**Tests**:
- SnapshotIsolation_ShouldNotSeeUncommittedChanges
- ConcurrentUpdates_LastCommitWins  
- DeletedDocument_ShouldNotBeVisibleAfterDeletion

**Issue**:
- All three tests timeout after 30 seconds
- Transactions get aborted due to timeout

**Root Cause**:
- Tests expect true MVCC behavior where reads never block on writes
- Current implementation uses a hybrid approach:
  - Version management for document history
  - Locking for concurrency control
- When a transaction writes to a document, it acquires a write lock
- Other transactions trying to read the same document will block, even with RepeatableRead/ReadCommitted isolation levels

**Why Not Fixed**:
- Fixing this requires fundamental redesign of the transaction system
- Would need to implement true MVCC without write locks for non-Serializable isolation levels
- Current design decision: Use simpler locking approach with version tracking

### 4. TwoPhaseCommit Timeout Test
**Status**: ℹ️ TEST INFRASTRUCTURE - Test remains skipped

**Issue**:
- Test expects timeout functionality in TestTransactionCoordinator
- TestTransactionCoordinator doesn't implement timeout handling

**Root Cause**:
- This is a test infrastructure limitation
- The test coordinator is a mock implementation for testing
- Timeout functionality was not implemented in the test mock

**Why Not Fixed**:
- Not a core functionality issue
- Would require implementing timeout in test infrastructure
- Core TransactionCoordinator implementation may already support timeouts

### 5. BTreeIndex Memory Allocation Test
**Status**: ℹ️ INHERENTLY FLAKY - Test remains skipped

**Test**: RangeAsync_LargeData_ShouldNotAllocateExcessiveMemory

**Issue**:
- Test measures memory allocation during range iteration
- Expects less than 1MB allocation for iterating 8000 items
- Fails intermittently

**Root Cause**:
- GC behavior is non-deterministic
- JIT compilation may allocate memory during test
- Other system activities can affect memory measurements
- `GC.GetTotalMemory()` is not precise enough for this type of testing

**Why Not Fixed**:
- Memory allocation tests are inherently unreliable in unit tests
- Should be tested with proper benchmarking tools (BenchmarkDotNet)
- Not appropriate for CI/CD pipeline

## Summary

### Fixed Issues
1. **Deadlock Detection**: Fixed false positive in deadlock detection by properly handling pending write requests in lock manager
2. **Read Lock Re-acquisition**: Fixed deadlock when transaction tries to re-read a document it already has locked

### Design Limitations
1. **MVCC Implementation**: Current hybrid locking/versioning approach causes reads to block on writes, which is not true MVCC behavior
2. **Isolation Levels**: RepeatableRead and ReadCommitted still use locking, preventing non-blocking reads

### Test Infrastructure
1. **Two-Phase Commit**: Test mock doesn't implement all coordinator features
2. **Memory Tests**: Unit tests are not suitable for memory allocation testing

## Recommendations

1. **MVCC Redesign** (Future Enhancement):
   - Implement true MVCC where reads never block writes
   - Use timestamps/versions for isolation without locks (except Serializable)
   - Allow concurrent readers even with active writers

2. **Memory Testing**:
   - Remove memory allocation unit tests
   - Implement proper benchmarks using BenchmarkDotNet
   - Run benchmarks separately from unit tests

3. **Test Infrastructure**:
   - Either implement missing features in test mocks or remove tests that rely on them
   - Focus on testing actual implementation rather than mock behavior