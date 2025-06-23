# Session History: Deadlock Detection and Locking Fixes
Date: 2025-06-23
Focus: Fixing test failures related to deadlock detection and transaction locking

## Summary
Fixed critical issues in the transaction and locking system that were causing test timeouts and deadlocks. The main issue was a semaphore deadlock in the ResourceLock class within LockManager.cs.

## Issues Found and Fixed

### 1. Race Condition in Wait-For Graph Cleanup
**Problem**: When a cancellation occurred, the callback registered with `cancellationToken.Register` ran asynchronously but tried to access `this.writeLockHolder` which could change.

**Fix**: Captured the current write lock holder in a local variable before using it in cancellation callbacks.

### 2. Async Void in Cancellation Callbacks  
**Problem**: Cancellation callbacks used `async` lambdas without proper error handling, causing unobserved exceptions.

**Fix**: Changed to synchronous callbacks with `Task.Run` for async operations and added try-catch blocks.

### 3. Missing Cancellation Token in Transaction
**Problem**: `Transaction.DeleteAsync` wasn't passing the linked cancellation token to `AcquireWriteLockAsync`.

**Fix**: Added `linkedCts.Token` parameter to the lock acquisition call.

### 4. Null Check in DeadlockDetector
**Problem**: `RemoveWaitForAsync` didn't check if `waitingTransaction` was null.

**Fix**: Added null check for both `waitingTransaction` and `holdingTransaction`.

### 5. Async Void in Timeout Handler
**Problem**: `OnTimeout` method used `async void` which could cause unhandled exceptions during rollback.

**Fix**: Changed to synchronous method with `Task.Run` for async rollback operations.

### 6. Semaphore Deadlock in ResourceLock (Critical)
**Problem**: In `AcquireReadLockAsync` and `AcquireWriteLockAsync`, when a lock could be immediately acquired, the method would return while still holding the semaphore. This caused subsequent lock operations to hang indefinitely.

**Fix**: Modified both methods to always release the semaphore after updating lock state, whether the lock was acquired immediately or after waiting.

## Code Changes

### LockManager.cs - ResourceLock class
- Added `this.lockSemaphore.Release()` after immediate lock acquisitions
- Added `semaphoreReleased` flag to prevent double-release in exception handlers
- Fixed similar issues in `UpgradeLockAsync`

### Transaction.cs
- Fixed `OnTimeout` to avoid async void
- Added proper cancellation token passing in `DeleteAsync`

### DeadlockDetector.cs
- Added null checks in `RemoveWaitForAsync`

## Test Results
- Created and successfully ran a minimal deadlock test
- Some existing deadlock tests are failing due to test design issues (using ReadCommitted instead of Serializable isolation level)
- Total test count: 696 tests
- Build status: Clean (0 warnings, 0 errors)

## Next Steps
- Review failing deadlock tests to ensure they use appropriate isolation levels
- Consider adjusting deadlock detection timeout (currently 100ms)
- Monitor for any remaining timing-sensitive test failures

## Files Modified
- `/mnt/e/work/github/crp/kvs/src/Kvs.Core/Database/LockManager.cs`
- `/mnt/e/work/github/crp/kvs/src/Kvs.Core/Database/Transaction.cs`
- `/mnt/e/work/github/crp/kvs/src/Kvs.Core/Database/DeadlockDetector.cs`
- Created: `/mnt/e/work/github/crp/kvs/src/Kvs.Core.UnitTests/Database/DeadlockTest.cs`
- Removed: `/mnt/e/work/github/crp/kvs/src/Kvs.Core.UnitTests/Database/DeadlockDebugTest.cs`