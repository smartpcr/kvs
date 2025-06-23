# NoSQL Database Implementation Plan for C#

## Overview
Build a lightweight NoSQL key-value store database in C# with document storage capabilities, targeting under 1000 lines of core code.

## Architecture

### Core Components

1. **Storage Engine**
   - File-based persistence using binary format
   - Memory-mapped files for performance
   - Write-ahead logging (WAL) for durability and recovery
   - Distributed storage with synchronous replication
   - ACID transaction guarantees with fsync on commit
   - Checkpointing for WAL compaction and fast recovery

2. **Data Structures**
   - B-Tree for indexed access
   - Hash table for in-memory caching
   - Skip list for range queries

3. **Query Engine**
   - JSON-based query syntax
   - Support for basic CRUD operations
   - Simple indexing system

4. **Concurrency**
   - Reader-writer locks
   - MVCC (Multi-Version Concurrency Control) basics
   - Thread-safe operations

5. **Cluster Management**
   - Leader election and consensus (Raft algorithm)
   - Node discovery and health monitoring
   - Automatic failover and recovery
   - Data replication and synchronization

6. **High Availability**
   - Multi-master replication
   - Conflict resolution strategies
   - Quorum-based operations
   - Split-brain prevention

## Implementation Phases

### Phase 1: Core Storage (Week 1) - ✅ COMPLETED
- [x] Create `IStorageEngine` interface
- [x] Implement `FileStorageEngine` with async read/write
- [x] Create `Page` class for disk storage units (4KB pages)
- [x] Implement `PageManager` for page allocation/deallocation/caching
- [x] Add binary serialization/deserialization with multi-framework support
- [x] Implement Write-Ahead Log (WAL) with forced sync and LSN management
- [x] Add transaction log entries with checksums and integrity validation
- [x] Create checkpoint mechanism for WAL compaction with automatic triggering
- [x] Implement ARIES-style crash recovery (Analysis/Redo/Undo phases)
- [x] Add comprehensive XML documentation for all public members
- [x] Implement multi-framework support (.NET Framework 4.7.2, .NET 8.0, .NET 9.0)

**Phase 1 Tests:**
- [ ] `StorageEngineTests` - Basic read/write operations
  - **Setup**: Initialize a temporary `FileStorageEngine` instance.
  - **Actions**: Write a block of data, flush, then read it back.
  - **Expected**: Retrieved bytes match what was written.
- [ ] `PageTests` - Page structure and operations
  - **Setup**: Create a `Page` object in memory.
  - **Actions**: Insert records and serialize/deserialize the page.
  - **Expected**: Deserialized data equals the original records.
- [ ] `PageManagerTests` - Page allocation/deallocation
  - **Setup**: Instantiate `PageManager` against a temp file.
  - **Actions**: Allocate a page, release it, then reallocate.
  - **Expected**: Allocation table reflects the expected free/used pages.
- [ ] `SerializationTests` - Binary serialization/deserialization
  - **Setup**: Prepare a sample object graph.
  - **Actions**: Serialize to binary, then deserialize.
  - **Expected**: The deserialized object matches the original.
- [ ] `WALTests` - Write-ahead log operations and persistence
  - **Setup**: Open a WAL file with forced sync enabled.
  - **Actions**: Append log entries and reopen the file.
  - **Expected**: All entries are present and in order.
- [ ] `CheckpointTests` - WAL compaction and checkpoint creation
  - **Setup**: Perform several writes to generate WAL segments.
  - **Actions**: Trigger a checkpoint operation.
  - **Expected**: WAL size is reduced and a checkpoint record exists.
- [ ] `RecoveryTests` - Crash recovery and WAL replay
  - **Setup**: Write data without checkpointing and simulate a crash.
  - **Actions**: Restart the engine and run recovery.
  - **Expected**: Committed transactions are restored; incomplete ones rolled back.

### Phase 2: Data Structures (Week 2) - ✅ COMPLETED
- [x] Implement `BTree<TKey, TValue>` class with full CRUD operations
- [x] Create `Node` class for B-Tree nodes with splitting/merging
- [x] Add `IIndex` interface with async operations
- [x] Implement `BTreeIndex` for primary key indexing
- [x] Add in-memory `LRUCache<TKey, TValue>` with eviction policies
- [x] Implement `SkipList<TKey, TValue>` with probabilistic balancing
- [x] Implement `HashIndex<TKey, TValue>` for O(1) operations

**Phase 2 Tests:**
- [ ] `BTreeTests` - B-Tree insertion, deletion, search operations
  - **Setup**: Create an empty `BTree` instance.
  - **Actions**: Insert keys, query them, then delete and verify absence.
  - **Expected**: Searches return correct results before and after deletion.
- [ ] `NodeTests` - B-Tree node splitting and merging
  - **Setup**: Instantiate a node near its capacity.
  - **Actions**: Insert enough keys to trigger a split and then a merge.
  - **Expected**: Tree structure remains balanced after operations.
- [ ] `IndexTests` - Index interface operations
  - **Setup**: Initialize an implementation of `IIndex`.
  - **Actions**: Put and get several key/value pairs.
  - **Expected**: Retrieved values match inserted data.
- [ ] `BTreeIndexTests` - Primary key indexing functionality
  - **Setup**: Create a `BTreeIndex` bound to a collection.
  - **Actions**: Index documents and lookup by key.
  - **Expected**: Lookups return the expected document identifiers.
- [ ] `LRUCacheTests` - Cache eviction and memory management
  - **Setup**: Instantiate `LRUCache` with a small capacity.
  - **Actions**: Add more entries than the capacity and access some entries repeatedly.
  - **Expected**: Least recently used items are evicted first.

### Phase 3: Database Core (Week 3) - ✅ COMPLETED
- [x] Create `Database` class as main entry point
- [x] Implement `Collection` class for document storage
- [x] Add `Document` class with JSON support
- [x] Create `Transaction` class with full ACID guarantees
- [x] Implement two-phase commit for distributed transactions
- [x] Add isolation levels (Read Uncommitted, Read Committed, Repeatable Read, Serializable)
- [x] Implement deadlock detection and resolution (semaphore-based timeout)
- [x] Add transaction timeout and abort mechanisms
- [x] Implement MVCC (Multi-Version Concurrency Control) with version chains
- [x] Create `VersionManager` for document version management
- [x] Add `LockManager` with two-phase locking protocol
- [x] Implement automatic version cleanup for old versions

**Phase 3 Tests:**
- [ ] `DatabaseTests` - Database lifecycle and collection management
  - **Setup**: Create a new database instance and open a collection.
  - **Actions**: Insert a document, close and reopen the database.
  - **Expected**: Data persists and collections load correctly.
- [ ] `CollectionTests` - Document storage and retrieval
  - **Setup**: Obtain a reference to a test collection.
  - **Actions**: Insert documents and query by id.
  - **Expected**: Retrieved documents match the inserted content.
- [ ] `DocumentTests` - JSON document handling and validation
  - **Setup**: Define a JSON document with required fields.
  - **Actions**: Save and then reload the document.
  - **Expected**: Deserialized document equals the original JSON.
- [ ] `TransactionTests` - ACID properties and transaction lifecycle
  - **Setup**: Begin a new transaction.
  - **Actions**: Perform multiple writes then commit.
  - **Expected**: All changes become visible atomically after commit.
- [ ] `TwoPhaseCommitTests` - Distributed transaction coordination
  - **Setup**: Start transactions on multiple nodes.
  - **Actions**: Run prepare/commit sequence across the cluster.
  - **Expected**: Either all nodes commit or all rollback on failure.
- [ ] `IsolationTests` - Transaction isolation level verification
  - **Setup**: Begin concurrent transactions with different isolation levels.
  - **Actions**: Read and write overlapping keys.
  - **Expected**: Observed behavior matches the specified isolation semantics.
- [ ] `DeadlockTests` - Deadlock detection and resolution
  - **Setup**: Launch two transactions that lock resources in opposite order.
  - **Actions**: Wait for deadlock detection logic to trigger.
  - **Expected**: System aborts one transaction to break the deadlock.
- [ ] `TransactionTimeoutTests` - Timeout and abort mechanisms
  - **Setup**: Begin a transaction with a short timeout.
  - **Actions**: Hold a lock beyond the timeout period.
  - **Expected**: Transaction is automatically aborted.

### Phase 4: Query Engine (Week 4)
- [ ] Define `Query` class with JSON syntax
- [ ] Implement `QueryParser` 
- [ ] Create `QueryExecutor` 
- [ ] Add support for filters, sorting, pagination
- [ ] Implement basic aggregations

**Phase 4 Tests:**
- [ ] `QueryTests` - JSON query syntax validation
  - **Setup**: Define a query string with various operators.
  - **Actions**: Parse the string and validate the output structure.
  - **Expected**: Parser returns a valid query object with no errors.
- [ ] `QueryParserTests` - Query parsing and validation
  - **Setup**: Instantiate the `QueryParser`.
  - **Actions**: Parse valid and invalid queries.
  - **Expected**: Valid queries succeed, invalid ones throw meaningful errors.
- [ ] `QueryExecutorTests` - Query execution and optimization
  - **Setup**: Load a collection with sample documents.
  - **Actions**: Execute queries with filters and sorts.
  - **Expected**: Results match the query criteria and come back in the expected order.
- [ ] `FilterTests` - Filter operations and logic
  - **Setup**: Insert documents containing various field values.
  - **Actions**: Apply filters on different fields.
  - **Expected**: Only matching documents are returned.
- [ ] `SortingTests` - Sorting algorithms and performance
  - **Setup**: Store a set of unsorted records.
  - **Actions**: Query with ascending and descending sort options.
  - **Expected**: Results are sorted properly without excessive latency.
- [ ] `PaginationTests` - Pagination logic and efficiency
  - **Setup**: Populate more records than the default page size.
  - **Actions**: Request successive pages.
  - **Expected**: Each page contains the correct subset of records.
- [ ] `AggregationTests` - Basic aggregation operations
  - **Setup**: Store numeric documents for aggregation.
  - **Actions**: Run sum and average queries.
  - **Expected**: Aggregated values equal those calculated manually.

### Phase 5: Concurrency & Performance (Week 5)
- [ ] Add `ReaderWriterLockSlim` for thread safety
- [ ] Implement connection pooling
- [ ] Add async/await support throughout
- [ ] Create comprehensive benchmark suite
- [ ] Optimize hot paths
- [ ] Implement performance monitoring
- [ ] Add stress testing framework
- [ ] Create parallel/concurrency test suite
- [ ] Add chaos engineering tests
- [ ] Performance profiling and optimization

**Phase 5 Tests:**
- [ ] `ConcurrencyTests` - Thread safety and locking mechanisms
  - **Setup**: Run multiple threads against a shared database instance.
  - **Actions**: Perform concurrent reads and writes.
  - **Expected**: No data races or unhandled exceptions occur.
- [ ] `ConnectionPoolTests` - Connection pooling and resource management
  - **Setup**: Initialize a connection pool with limited size.
  - **Actions**: Borrow and return connections under load.
  - **Expected**: Pool reuses connections without leaks.
- [ ] `AsyncTests` - Async/await pattern validation
  - **Setup**: Use asynchronous API calls.
  - **Actions**: Await multiple operations concurrently.
  - **Expected**: Tasks complete without deadlocks or thread starvation.
- [ ] `BenchmarkTests` - Performance baseline measurements
  - **Setup**: Configure BenchmarkDotNet harness.
  - **Actions**: Measure read and write throughput.
  - **Expected**: Results meet documented performance goals.
- [ ] `HotPathTests` - Critical path optimization verification
  - **Setup**: Instrument hot methods with timers.
  - **Actions**: Execute typical workloads.
  - **Expected**: Latencies remain within targeted thresholds.
- [ ] `MonitoringTests` - Performance monitoring accuracy
  - **Setup**: Enable built-in metrics collection.
  - **Actions**: Perform a mix of operations.
  - **Expected**: Reported metrics reflect actual operation counts.
- [ ] `StressTests` - High-load and endurance testing
  - **Setup**: Prepare large data sets and continuous workload generator.
  - **Actions**: Run system under sustained load for extended time.
  - **Expected**: No crashes or resource exhaustion.
- [ ] `ParallelTests` - Parallel execution correctness
  - **Setup**: Spawn many tasks that operate on the database simultaneously.
  - **Actions**: Mix read and write operations across tasks.
  - **Expected**: All operations succeed and final data state is consistent.
- [ ] `ChaosTests` - Failure injection and recovery
  - **Setup**: Operate a multi-node cluster.
  - **Actions**: Inject faults such as process termination and disk errors.
  - **Expected**: Cluster remains available and recovers automatically.
- [ ] `ProfilingTests` - Performance profiling validation
  - **Setup**: Attach profilers during typical workloads.
  - **Actions**: Collect CPU and memory profiles.
  - **Expected**: Profiling output identifies hotspots without affecting stability.

### Phase 6: Clustering & Replication (Week 6)
- [ ] Implement `IClusterManager` interface
- [ ] Create `RaftConsensus` implementation
- [ ] Add `NodeRegistry` for cluster membership
- [ ] Implement `ReplicationManager` for synchronous data sync
- [ ] Create `HealthMonitor` for node monitoring
- [ ] Add `FailoverManager` for automatic failover
- [ ] Implement quorum-based writes for consistency
- [ ] Add distributed WAL replication
- [ ] Create cross-node transaction coordination

**Phase 6 Tests:**
- [ ] `ClusterManagerTests` - Cluster membership and management
  - **Setup**: Start a cluster with multiple nodes.
  - **Actions**: Add and remove nodes via the cluster manager.
  - **Expected**: Membership list reflects the changes on all nodes.
- [ ] `RaftConsensusTests` - Raft algorithm implementation
  - **Setup**: Launch several nodes running the Raft implementation.
  - **Actions**: Force elections and replicate log entries.
  - **Expected**: Consensus is reached with a single leader and identical logs.
- [ ] `NodeRegistryTests` - Node discovery and registration
  - **Setup**: Create a fresh node registry.
  - **Actions**: Register nodes and query their status.
  - **Expected**: Nodes are discoverable and status information is accurate.
- [ ] `ReplicationManagerTests` - Data synchronization and consistency
  - **Setup**: Stand up a leader and a follower node.
  - **Actions**: Write data to the leader and allow replication.
  - **Expected**: Follower contains the same data as the leader.
- [ ] `HealthMonitorTests` - Node health monitoring and detection
  - **Setup**: Enable health checks on all nodes.
  - **Actions**: Simulate a node failure.
  - **Expected**: Health monitor reports the failure within the configured interval.
- [ ] `FailoverManagerTests` - Automatic failover mechanisms
  - **Setup**: Configure a cluster with failover enabled.
  - **Actions**: Kill the leader node.
  - **Expected**: Another node is promoted to leader automatically.
- [ ] `QuorumTests` - Quorum-based operation validation
  - **Setup**: Create a three-node cluster.
  - **Actions**: Attempt writes with and without quorum.
  - **Expected**: Writes succeed only when a quorum of nodes acknowledges.
- [ ] `DistributedWALTests` - Distributed WAL replication
  - **Setup**: Enable WAL replication to all followers.
  - **Actions**: Append log entries on the leader.
  - **Expected**: Each follower stores the same WAL entries.
- [ ] `CrossNodeTransactionTests` - Multi-node transaction coordination
  - **Setup**: Begin a distributed transaction touching multiple nodes.
  - **Actions**: Commit the transaction through the coordinator.
  - **Expected**: All nodes either commit or rollback in unison.

### Phase 7: High Availability (Week 7)
- [ ] Implement leader election protocols
- [ ] Add conflict resolution for concurrent writes
- [ ] Create quorum-based read/write operations
- [ ] Implement split-brain detection and prevention
- [ ] Add network partition tolerance
- [ ] Create data consistency validation

**Phase 7 Tests:**
- [ ] `LeaderElectionTests` - Leader election algorithm verification
  - **Setup**: Start a cluster with a stable leader.
  - **Actions**: Stop the leader and wait for election.
  - **Expected**: A new leader is elected within the timeout period.
- [ ] `ConflictResolutionTests` - Concurrent write conflict handling
  - **Setup**: Enable replication across nodes.
  - **Actions**: Issue conflicting writes to the same key from different nodes.
  - **Expected**: System resolves conflicts according to the configured strategy.
- [ ] `QuorumOperationTests` - Quorum-based read/write operations
  - **Setup**: Configure a three-node cluster.
  - **Actions**: Execute reads and writes with varying node availability.
  - **Expected**: Operations succeed only when the quorum requirement is met.
- [ ] `SplitBrainTests` - Split-brain detection and prevention
  - **Setup**: Partition a cluster into two equal halves.
  - **Actions**: Attempt writes on both partitions.
  - **Expected**: Only the partition with quorum accepts writes; the other rejects them.
- [ ] `NetworkPartitionTests` - Network partition tolerance
  - **Setup**: Run a five-node cluster under load.
  - **Actions**: Introduce a network partition isolating two nodes.
  - **Expected**: Majority partition continues processing; isolated nodes enter read-only mode.
- [ ] `ConsistencyValidationTests` - Data consistency verification
  - **Setup**: After partition healing, allow nodes to synchronize.
  - **Actions**: Compare data across all replicas.
  - **Expected**: Every node contains identical data sets.
- [ ] `HighAvailabilityIntegrationTests` - End-to-end HA scenarios
  - **Setup**: Deploy a production-like cluster with clients running.
  - **Actions**: Simulate leader failures, network issues and node restarts.
  - **Expected**: Service remains available and no acknowledged writes are lost.

## Project Structure

### Current Implementation (Phase 1 Complete)
```
src/
├── Kvs.Core/                          ✅ IMPLEMENTED
│   ├── Storage/                        ✅ COMPLETE
│   │   ├── IStorageEngine.cs          ✅ Interface for storage operations
│   │   ├── FileStorageEngine.cs       ✅ File-based storage implementation
│   │   ├── Page.cs                    ✅ Fixed-size pages with headers
│   │   ├── PageManager.cs             ✅ Page allocation and caching
│   │   ├── WAL.cs                     ✅ Write-ahead log implementation
│   │   ├── TransactionLogEntry.cs     ✅ Transaction log entry structure
│   │   ├── CheckpointManager.cs       ✅ WAL checkpointing
│   │   └── RecoveryManager.cs         ✅ ARIES recovery implementation
│   ├── Serialization/                 ✅ COMPLETE
│   │   ├── ISerializer.cs             ✅ Serialization interfaces
│   │   └── BinarySerializer.cs        ✅ Binary serialization implementation
│   ├── DataStructures/                ✅ COMPLETE
│   │   ├── BTree.cs                   ✅ B-Tree implementation with full CRUD
│   │   ├── Node.cs                    ✅ B-Tree node with split/merge operations
│   │   ├── LRUCache.cs                ✅ LRU cache with eviction policies
│   │   ├── SkipList.cs                ✅ Probabilistic data structure with O(log n) operations
│   │   └── HashIndex.cs               ✅ Hash-based indexing with O(1) average case
│   ├── Indexing/                      ✅ COMPLETE  
│   │   ├── IIndex.cs                  ✅ Index interface with async operations
│   │   └── BTreeIndex.cs              ✅ B-Tree based indexing implementation
│   ├── Database/                      ✅ COMPLETE
│   │   ├── Database.cs                ✅ Main database entry point with collection management
│   │   ├── Collection.cs              ✅ Document storage with CRUD operations
│   │   ├── Document.cs                ✅ Document model with versioning support
│   │   ├── Transaction.cs             ✅ Full ACID transaction implementation
│   │   ├── VersionManager.cs          ✅ MVCC implementation with version chains
│   │   ├── LockManager.cs             ✅ Two-phase locking with deadlock detection
│   │   ├── ITransactionContext.cs     ✅ Transaction context interface
│   │   └── TransactionTimeoutException.cs ✅ Custom exception for timeouts
│   ├── Query/                         ⏳ PLANNED (Phase 4)
│   │   ├── Query.cs                   ❌ Not implemented
│   │   ├── QueryParser.cs             ❌ Not implemented
│   │   ├── QueryExecutor.cs           ❌ Not implemented
│   │   └── QueryResult.cs             ❌ Not implemented
│   ├── Cluster/                       ⏳ PLANNED (Phase 6)
│   │   └── [Future implementation]
│   ├── Replication/                   ⏳ PLANNED (Phase 6)
│   │   └── [Future implementation]
│   └── Consensus/                     ⏳ PLANNED (Phase 7)
│       └── [Future implementation]
└── Kvs.Core.UnitTests/                ✅ IMPLEMENTED
    ├── Storage/                        ✅ COMPLETE (81/81 tests passing)
    │   ├── StorageEngineTests.cs      ✅ File operations and async I/O
    │   ├── PageTests.cs               ✅ Page structure and operations
    │   ├── PageManagerTests.cs        ✅ Page allocation and caching
    │   ├── WALTests.cs                ✅ Write-ahead logging
    │   ├── CheckpointTests.cs         ✅ Checkpoint management
    │   └── RecoveryTests.cs           ✅ ARIES recovery testing
    ├── Serialization/                  ✅ COMPLETE
    │   └── SerializationTests.cs       ✅ Binary serialization testing
    ├── DataStructures/                 ✅ COMPLETE (98/98 tests passing)
    │   ├── BTreeTests.cs              ✅ B-Tree operations and edge cases
    │   ├── NodeTests.cs               ✅ Node split/merge operations  
    │   ├── LRUCacheTests.cs           ✅ Cache eviction and concurrency
    │   ├── SkipListTests.cs           ✅ Probabilistic data structure testing
    │   └── HashIndexTests.cs          ✅ Hash index operations and concurrency
    ├── Indexing/                       ✅ COMPLETE (27/27 tests passing)
    │   └── BTreeIndexTests.cs          ✅ Async index operations
    └── Database/                       ✅ COMPLETE (81/81 tests passing)
        ├── DatabaseTests.cs            ✅ Database lifecycle and operations
        ├── CollectionTests.cs          ✅ Document CRUD operations
        ├── TransactionTests.cs         ✅ ACID properties and isolation levels
        ├── VersionManagerTests.cs      ✅ MVCC version chain management
        ├── LockManagerTests.cs         ✅ Two-phase locking and deadlock detection
        └── DeadlockTests.cs            ✅ Deadlock scenario testing

Future Structure (Phases 2-7):
├── Kvs.Client/                        ⏳ PLANNED (Phase 5)
├── Kvs.Server/                        ⏳ PLANNED (Phase 5)
├── benchmarks/                        ⏳ PLANNED (Phase 5)
└── [Additional test categories]       ⏳ PLANNED (Phases 2-7)
```

### Implementation Status Legend:
- ✅ **IMPLEMENTED**: Complete and tested
- ⏳ **PLANNED**: Designed but not yet implemented
- ❌ **NOT STARTED**: Not yet begun

## Key Interfaces

```csharp
public interface IStorageEngine : IDisposable
{
    Task<ReadOnlyMemory<byte>> ReadAsync(long position, int length);
    Task<long> WriteAsync(ReadOnlyMemory<byte> data);
    Task FlushAsync();
    Task<bool> FsyncAsync(); // Force sync to disk
    Task<long> GetSizeAsync();
    Task TruncateAsync(long size);
    Task<bool> IsOpenAsync(); // Check if storage is operational
}

public interface ITransactionLog : IDisposable
{
    Task<long> WriteEntryAsync(TransactionLogEntry entry);
    Task<TransactionLogEntry[]> ReadEntriesAsync(long fromLsn);
    Task<bool> FlushAsync();
    Task CheckpointAsync(long lsn);
    Task<long> GetLastLsnAsync();
    Task<long> GetFirstLsnAsync(); // Added for recovery scenarios
}

public interface IRecoveryManager
{
    Task<bool> RecoverAsync();
    Task<TransactionLogEntry[]> GetUncommittedTransactionsAsync();
    Task RollbackTransactionAsync(string transactionId);
    Task RedoTransactionAsync(string transactionId);
    Task<bool> IsRecoveryNeededAsync(); // Added to check if recovery is needed
}

public interface IPageManager : IDisposable
{
    Task<Page> AllocatePageAsync(PageType pageType);
    Task<Page> GetPageAsync(long pageId);
    Task WritePageAsync(Page page);
    Task FreePageAsync(long pageId);
    Task<long> GetPageCountAsync();
    Task FlushAsync();
    Task<bool> PageExistsAsync(long pageId);
}

public interface ICheckpointManager : IDisposable
{
    Task<bool> CreateCheckpointAsync();
    Task<long> GetLastCheckpointLsnAsync();
    Task<bool> IsCheckpointNeededAsync();
    event EventHandler<CheckpointCompletedEventArgs> CheckpointCompleted;
}

public interface ISerializer
{
    ReadOnlyMemory<byte> Serialize<T>(T value);
    T Deserialize<T>(ReadOnlyMemory<byte> data);
    Type GetSerializedType(ReadOnlyMemory<byte> data);
}

public interface IAsyncSerializer
{
    Task<ReadOnlyMemory<byte>> SerializeAsync<T>(T value);
    Task<T> DeserializeAsync<T>(ReadOnlyMemory<byte> data);
}

public interface IIndex<TKey, TValue> : IDisposable
    where TKey : IComparable<TKey>
{
    Task<TValue?> GetAsync(TKey key);
    Task PutAsync(TKey key, TValue value);
    Task<bool> DeleteAsync(TKey key);
    IAsyncEnumerable<KeyValuePair<TKey, TValue>> RangeAsync(TKey startKey, TKey endKey);
    Task<long> CountAsync();
}

public interface IDatabase
{
    ICollection<T> GetCollection<T>(string name);
    Task<ITransaction> BeginTransactionAsync();
    Task<ITransaction> BeginTransactionAsync(IsolationLevel isolationLevel);
    Task<bool> CheckpointAsync();
    Task<bool> RecoverAsync();
}

public interface ITransaction : IDisposable
{
    string TransactionId { get; }
    Task CommitAsync();
    Task RollbackAsync();
    Task<T> GetAsync<T>(string key);
    Task PutAsync<T>(string key, T value);
    Task DeleteAsync(string key);
    bool IsReadOnly { get; }
    IsolationLevel IsolationLevel { get; }
}

public interface IClusterManager
{
    Task<bool> JoinClusterAsync(NodeInfo nodeInfo);
    Task<bool> LeaveClusterAsync(string nodeId);
    Task<IEnumerable<NodeInfo>> GetActiveNodesAsync();
    Task<NodeInfo?> GetLeaderAsync();
    event EventHandler<NodeFailedEventArgs> NodeFailed;
    event EventHandler<LeaderChangedEventArgs> LeaderChanged;
}

public interface IReplicationManager
{
    Task<bool> ReplicateAsync(ReplicationEntry entry);
    Task<bool> SyncWithLeaderAsync();
    Task<ReplicationStatus> GetReplicationStatusAsync();
    Task<bool> ReplicateTransactionAsync(string transactionId, IEnumerable<TransactionLogEntry> entries);
    Task<bool> WaitForQuorumAsync(string transactionId, TimeSpan timeout);
}

public interface IRaftConsensus
{
    Task<bool> RequestVoteAsync(VoteRequest request);
    Task<bool> AppendEntriesAsync(AppendEntriesRequest request);
    Task<bool> BecomeLeaderAsync();
    RaftState CurrentState { get; }
}
```

## Technical Decisions

1. **Storage Format**: Binary format with fixed-size pages (4KB default)
2. **Indexing**: B-Tree with order 64 for balanced read/write performance
3. **Concurrency**: Serializable isolation (like LibraDB) with ReaderWriterLockSlim
4. **Query Language**: JSON-based similar to MongoDB
5. **Networking**: gRPC for client-server communication
6. **Serialization**: MessagePack for efficiency, JSON for compatibility
7. **Testing**: BenchmarkDotNet for performance testing, custom framework for stress/chaos tests
8. **Monitoring**: Built-in metrics collection for performance analysis
9. **Consensus Algorithm**: Raft consensus for leader election and log replication
10. **Replication**: Synchronous quorum-based replication for write safety
11. **Clustering**: Minimum 3 nodes for quorum, configurable replication factor
12. **Failover**: Automatic failover with sub-second detection and recovery
13. **Network Partitions**: Majority partition continues operations, minority partitions become read-only
14. **Write Durability**: All writes must be acknowledged by quorum before returning success
15. **Transaction Guarantees**: Two-phase commit with WAL persistence on all nodes
16. **Recovery**: Automatic crash recovery with WAL replay and transaction rollback

## Performance Goals

### Single Node Performance
- 100K+ reads/second on single thread
- 50K+ writes/second with durability
- Sub-millisecond latency for key lookups
- Support for databases up to 1TB

### Cluster Performance
- Linear read scalability across nodes
- Consistent write performance with replication
- Sub-second failover time
- 99.9% availability with 3-node cluster
- Maximum 1MB/s replication bandwidth per node
- Support for up to 100 nodes in cluster

## Example Usage

### Single Node
```csharp
// Create database
var db = new Database("mydb.kvs");

// Get collection
var users = db.GetCollection<User>("users");

// Insert document
await users.InsertAsync(new User { Id = 1, Name = "John" });

// Query
var query = new Query
{
    Filter = new { age = new { gte = 18 } },
    Sort = new { name = 1 },
    Limit = 10
};

var results = await users.FindAsync(query);
```

### Cluster Mode
```csharp
// Configure cluster
var clusterConfig = new ClusterConfig
{
    Nodes = new[]
    {
        new NodeInfo("node1", "192.168.1.10", 5000),
        new NodeInfo("node2", "192.168.1.11", 5000),
        new NodeInfo("node3", "192.168.1.12", 5000)
    },
    ReplicationFactor = 2,
    ElectionTimeout = TimeSpan.FromSeconds(5)
};

// Create cluster-aware client
var client = new ClusterAwareClient(clusterConfig);

// Operations automatically route to appropriate nodes
var users = client.GetCollection<User>("users");
await users.InsertAsync(new User { Id = 1, Name = "John" });

// Reads can go to any healthy node
var user = await users.GetAsync(1);

// Client handles failover transparently
```

## Testing Strategy

### Unit Tests
- Test each component in isolation
- Mock dependencies using interfaces
- Cover edge cases and error conditions
- Achieve >90% code coverage

### Integration Tests
- Full end-to-end workflows
- Multi-component interactions
- Transaction ACID properties
- Data persistence verification

### Performance Tests
```csharp
[Fact]
public async Task Should_Handle_100K_Sequential_Reads_Within_10_Seconds()
{
    // Test sequential read performance
}

[Fact] 
public async Task Should_Handle_50K_Sequential_Writes_Within_20_Seconds()
{
    // Test sequential write performance
}

[Fact]
public async Task Should_Maintain_Sub_Millisecond_Key_Lookup()
{
    // Test individual operation latency
}
```

### Stress Tests
```csharp
[Fact]
public async Task Should_Handle_10_Million_Operations_Without_Memory_Leak()
{
    // Long-running test with memory monitoring
}

[Fact]
public async Task Should_Recover_From_Disk_Full_Scenarios()
{
    // Test behavior when disk space is exhausted
}

[Fact]
public async Task Should_Handle_Large_Documents_Up_To_16MB()
{
    // Test with maximum document sizes
}
```

### Parallel/Concurrency Tests
```csharp
[Fact]
public async Task Should_Handle_1000_Concurrent_Readers()
{
    // Multiple threads reading simultaneously
    var tasks = Enumerable.Range(0, 1000)
        .Select(_ => Task.Run(async () => await ReadOperations()))
        .ToArray();
    
    await Task.WhenAll(tasks);
}

[Fact]
public async Task Should_Serialize_Concurrent_Writers()
{
    // Test write serialization with high concurrency
    var tasks = Enumerable.Range(0, 100)
        .Select(i => Task.Run(async () => await WriteOperation(i)))
        .ToArray();
        
    await Task.WhenAll(tasks);
    // Verify all writes were applied correctly
}

[Fact]
public async Task Should_Handle_Mixed_Read_Write_Workload()
{
    // 80% reads, 20% writes concurrently
    var readers = GenerateReaderTasks(800);
    var writers = GenerateWriterTasks(200);
    
    await Task.WhenAll(readers.Concat(writers));
}
```

### Chaos Engineering Tests
```csharp
[Fact]
public async Task Should_Recover_From_Sudden_Process_Termination()
{
    // Test WAL recovery after crash
    var db = await CreateDatabase();
    await WriteTestData(db);
    
    // Simulate sudden crash
    await KillProcess(db);
    
    // Restart and verify recovery
    var recoveredDb = await RestartDatabase();
    await VerifyDataIntegrity(recoveredDb);
}

[Fact]
public async Task Should_Handle_Disk_IO_Errors_Gracefully()
{
    // Simulate disk failures during write operations
    var db = await CreateDatabase();
    
    // Simulate disk full during transaction
    await SimulateDiskFull();
    
    var result = await db.WriteAsync("key", "value");
    Assert.False(result.Success);
    Assert.Equal(ErrorCode.DiskFull, result.ErrorCode);
}

[Fact]
public async Task Should_Maintain_Consistency_During_Network_Partitions()
{
    // Test distributed scenarios with guaranteed consistency
    var cluster = await CreateCluster(5);
    
    // Partition network (3 vs 2 nodes)
    await CreateNetworkPartition(cluster, majority: new[]{0,1,2}, minority: new[]{3,4});
    
    // Majority should accept writes
    var majorityWrite = await cluster.Majority.WriteAsync("key1", "value1");
    Assert.True(majorityWrite.Success);
    
    // Minority should reject writes
    var minorityWrite = await cluster.Minority.WriteAsync("key2", "value2");
    Assert.False(minorityWrite.Success);
    Assert.Equal(ErrorCode.NoQuorum, minorityWrite.ErrorCode);
}

[Fact]
public async Task Should_Guarantee_No_Data_Loss_During_Concurrent_Writes()
{
    // Test write reliability under stress
    var cluster = await CreateCluster(3);
    var tasks = new List<Task<WriteResult>>();
    
    // Start 1000 concurrent writes
    for (int i = 0; i < 1000; i++)
    {
        var key = $"key_{i}";
        var value = $"value_{i}";
        tasks.Add(cluster.WriteAsync(key, value));
    }
    
    var results = await Task.WhenAll(tasks);
    
    // All successful writes must be recoverable
    var successfulWrites = results.Where(r => r.Success).ToList();
    
    // Simulate crash and recovery
    await SimulateCrashAndRecover(cluster);
    
    // Verify all successful writes are still present
    foreach (var write in successfulWrites)
    {
        var value = await cluster.ReadAsync(write.Key);
        Assert.Equal(write.Value, value);
    }
}

[Fact]
public async Task Should_Rollback_Failed_Distributed_Transactions()
{
    // Test two-phase commit rollback on failure
    var cluster = await CreateCluster(3);
    
    using var transaction = await cluster.BeginTransactionAsync();
    
    await transaction.WriteAsync("key1", "value1");
    await transaction.WriteAsync("key2", "value2");
    
    // Simulate network failure to one node during commit
    await SimulateNodeFailure(cluster.Nodes[2]);
    
    try
    {
        await transaction.CommitAsync();
        Assert.True(false, "Commit should have failed");
    }
    catch (InsufficientQuorumException)
    {
        // Transaction should auto-rollback
    }
    
    // Verify no partial writes exist
    Assert.Null(await cluster.ReadAsync("key1"));
    Assert.Null(await cluster.ReadAsync("key2"));
}
```

[Fact]
public async Task Should_Handle_Leader_Node_Failure()
{
    // Simulate leader failure and test automatic failover
    var cluster = CreateThreeNodeCluster();
    var leader = await cluster.GetLeaderAsync();
    
    // Kill leader node
    await cluster.KillNodeAsync(leader.Id);
    
    // Verify new leader elected within timeout
    var newLeader = await WaitForNewLeader(TimeSpan.FromSeconds(10));
    Assert.NotNull(newLeader);
    Assert.NotEqual(leader.Id, newLeader.Id);
    
    // Verify cluster continues to serve requests
    await VerifyClusterOperational(cluster);
}

[Fact]
public async Task Should_Handle_Split_Brain_Scenarios()
{
    // Test network partition creating two separate clusters
    var cluster = CreateFiveNodeCluster();
    
    // Create network partition (3 nodes vs 2 nodes)
    await CreateNetworkPartition(cluster, new[] {0, 1, 2}, new[] {3, 4});
    
    // Majority partition should continue operations
    var majorityPartition = GetPartition(cluster, new[] {0, 1, 2});
    await VerifyWriteOperations(majorityPartition, shouldSucceed: true);
    
    // Minority partition should reject writes
    var minorityPartition = GetPartition(cluster, new[] {3, 4});
    await VerifyWriteOperations(minorityPartition, shouldSucceed: false);
}

[Fact]
public async Task Should_Maintain_Data_Consistency_During_Concurrent_Failovers()
{
    // Multiple node failures in rapid succession
    var cluster = CreateFiveNodeCluster();
    
    // Simulate rapid failure of 2 nodes
    var tasks = new[]
    {
        cluster.KillNodeAsync("node1"),
        Task.Delay(500).ContinueWith(_ => cluster.KillNodeAsync("node2"))
    };
    
    await Task.WhenAll(tasks);
    
    // Verify cluster maintains consistency
    await VerifyDataConsistency(cluster);
}
```

### Benchmark Suite
```csharp
[MemoryDiagnoser]
[SimpleJob(RuntimeMoniker.Net80)]
[SimpleJob(RuntimeMoniker.Net90)]
public class DatabaseBenchmarks
{
    [Benchmark]
    public async Task BenchmarkSequentialReads() { }
    
    [Benchmark]
    public async Task BenchmarkSequentialWrites() { }
    
    [Benchmark]
    public async Task BenchmarkRandomReads() { }
    
    [Benchmark]
    public async Task BenchmarkConcurrentReads() { }
    
    [Params(1, 10, 100, 1000)]
    public int ConcurrencyLevel { get; set; }
    
    [Params(1024, 4096, 16384, 65536)]
    public int DocumentSize { get; set; }
}
```

### Load Testing
- Simulate realistic workloads
- Test with varying data sizes
- Monitor resource usage (CPU, memory, disk)
- Test with different concurrency levels

### Corruption Recovery Tests
- Simulate partial writes
- Test WAL replay functionality
- Verify data integrity after crashes
- Test with corrupted index files

## Documentation

- API documentation with XML comments
- Architecture decision records (ADRs)
- Performance tuning guide
- Client library examples

## High Availability Features

### Automatic Failover
- Health monitoring with configurable intervals (default: 1 second)
- Leader election timeout: 5-10 seconds
- Automatic promotion of follower to leader
- Client-side connection failover with retry logic
- Zero-downtime rolling updates

### Data Replication
- Configurable replication factor (default: 2)
- Synchronous replication with quorum acknowledgment
- Strong consistency with immediate replication
- Cross-datacenter replication support
- Incremental backups and point-in-time recovery
- Write-ahead log replication to all replicas
- Automatic rollback on replication failure

### Network Partition Tolerance
- CAP theorem compliance (CP system during partitions)
- Majority quorum requirements for writes
- Read-only mode for minority partitions
- Automatic cluster healing when partitions resolve
- Split-brain detection and prevention

## Deliverables

1. Core library (Kvs.Core) with clustering support
2. Cluster-aware client library (Kvs.Client) 
3. Server executable (Kvs.Server) with cluster membership
4. Comprehensive test suite including distributed scenarios
5. Benchmarking suite with cluster performance tests
6. Documentation including deployment and operational guides
7. Configuration templates for multi-node deployments
8. Monitoring and alerting integration
9. Backup and recovery tools
