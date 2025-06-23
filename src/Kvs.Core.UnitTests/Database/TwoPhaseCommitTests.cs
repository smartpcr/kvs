using System;
using System.Collections.Generic;
using System.IO;
using System.Threading.Tasks;
using FluentAssertions;
using Kvs.Core.Database;
using Kvs.Core.Storage;
using Xunit;

namespace Kvs.Core.UnitTests.DatabaseTests;

/// <summary>
/// Tests for two-phase commit protocol implementation.
/// </summary>
public class TwoPhaseCommitTests : IDisposable
{
    private readonly string testDbPath;
    private readonly Core.Database.Database database;
    private readonly List<string> tempFiles;

    public TwoPhaseCommitTests()
    {
        this.testDbPath = Path.Combine(Path.GetTempPath(), $"kvs_test_2pc_{Guid.NewGuid()}.db");
        this.database = new Core.Database.Database(this.testDbPath);
        this.tempFiles = new List<string> { this.testDbPath };
    }

    [Fact(Timeout = 5000)]
    public async Task TwoPhaseCommit_PrepareAndCommit_ShouldSucceed()
    {
        // Arrange
        await this.database.OpenAsync();
        var collection = this.database.GetCollection<Document>("test");
        var coordinator = new TestTransactionCoordinator();
        var participants = new[]
        {
            new TestTransactionParticipant("participant1"),
            new TestTransactionParticipant("participant2")
        };

        // Act
        await coordinator.BeginTransactionAsync("txn1", participants);
        var prepareResult = await coordinator.PrepareAsync("txn1");
        await coordinator.CommitAsync("txn1");

        // Assert
        Assert.True(prepareResult);
        Assert.All(participants, p => Assert.True(p.IsPrepared));
        Assert.All(participants, p => Assert.True(p.IsCommitted));
    }

    [Fact(Timeout = 5000)]
    public async Task TwoPhaseCommit_PrepareFailure_ShouldAbort()
    {
        // Arrange
        await this.database.OpenAsync();
        var coordinator = new TestTransactionCoordinator();
        var participants = new[]
        {
            new TestTransactionParticipant("participant1"),
            new TestTransactionParticipant("participant2") { ShouldFailPrepare = true }
        };

        // Act
        await coordinator.BeginTransactionAsync("txn2", participants);
        var prepareResult = await coordinator.PrepareAsync("txn2");

        // Assert
        Assert.False(prepareResult);
        Assert.True(participants[0].IsPrepared);
        Assert.False(participants[1].IsPrepared);
        Assert.All(participants, p => Assert.True(p.IsAborted));
    }

    [Fact(Timeout = 5000)]
    public async Task TwoPhaseCommit_CommitFailure_ShouldRetry()
    {
        // Arrange
        await this.database.OpenAsync();
        var coordinator = new TestTransactionCoordinator();
        var participants = new[]
        {
            new TestTransactionParticipant("participant1"),
            new TestTransactionParticipant("participant2") { ShouldFailCommitOnce = true }
        };

        // Act
        await coordinator.BeginTransactionAsync("txn3", participants);
        await coordinator.PrepareAsync("txn3");
        await coordinator.CommitAsync("txn3");

        // Assert
        Assert.All(participants, p => Assert.True(p.IsCommitted));
        Assert.Equal(2, participants[1].CommitAttempts);
    }

    [Fact(Timeout = 5000)]
    public async Task TwoPhaseCommit_ConcurrentTransactions_ShouldIsolate()
    {
        // Arrange
        await this.database.OpenAsync();
        var collection = this.database.GetCollection<Document>("test");
        var coordinator = new TestTransactionCoordinator();

        // Act - Start two concurrent transactions
        var participants1 = new[]
        {
            new TestTransactionParticipant("p1-1"),
            new TestTransactionParticipant("p1-2")
        };
        var participants2 = new[]
        {
            new TestTransactionParticipant("p2-1"),
            new TestTransactionParticipant("p2-2")
        };

        await coordinator.BeginTransactionAsync("txn4", participants1);
        await coordinator.BeginTransactionAsync("txn5", participants2);

        // Prepare and commit both
        var prepare1 = coordinator.PrepareAsync("txn4");
        var prepare2 = coordinator.PrepareAsync("txn5");

        await Task.WhenAll(prepare1, prepare2);

        var commit1 = coordinator.CommitAsync("txn4");
        var commit2 = coordinator.CommitAsync("txn5");

        await Task.WhenAll(commit1, commit2);

        // Assert
        Assert.All(participants1, p => Assert.True(p.IsCommitted));
        Assert.All(participants2, p => Assert.True(p.IsCommitted));
    }

    [Fact(Timeout = 5000, Skip = "Timeout functionality not fully implemented in TestTransactionCoordinator")]
    public async Task TwoPhaseCommit_Timeout_ShouldAbort()
    {
        // Arrange
        await this.database.OpenAsync();
        var coordinator = new TestTransactionCoordinator { TimeoutMs = 100 };
        var participants = new[]
        {
            new TestTransactionParticipant("participant1"),
            new TestTransactionParticipant("participant2") { PrepareDelayMs = 200 }
        };

        // Act & Assert
        await coordinator.BeginTransactionAsync("txn6", participants);
        await Assert.ThrowsAsync<TimeoutException>(async () =>
            await coordinator.PrepareAsync("txn6"));
    }

    public void Dispose()
    {
        this.database?.Dispose();
        foreach (var file in this.tempFiles)
        {
            try
            {
                if (File.Exists(file))
                {
                    File.Delete(file);
                }

                var walFile = Path.ChangeExtension(file, ".wal");
                if (File.Exists(walFile))
                {
                    File.Delete(walFile);
                }
            }
            catch
            {
                // Ignore cleanup errors
            }
        }
    }

    private class TestTransactionCoordinator : TransactionCoordinator
    {
        public int TimeoutMs { get; set; } = 5000;

        public TestTransactionCoordinator()
            : base(CreateTestWAL())
        {
        }

        private static DatabaseWAL CreateTestWAL()
        {
            var tempPath = Path.Combine(Path.GetTempPath(), $"test_wal_{Guid.NewGuid()}.wal");
            var storageEngine = new FileStorageEngine(tempPath);
            var serializer = new Kvs.Core.Serialization.BinarySerializer();
            var wal = new WAL(storageEngine, serializer);
            return new DatabaseWAL(wal);
        }
    }

    private class TestTransactionParticipant : ITransactionParticipant
    {
        public string ParticipantId { get; }

        public bool IsPrepared { get; private set; }

        public bool IsCommitted { get; private set; }

        public bool IsAborted { get; private set; }

        public bool ShouldFailPrepare { get; set; }

        public bool ShouldFailCommitOnce { get; set; }

        public int CommitAttempts { get; private set; }

        public int PrepareDelayMs { get; set; }

        public TestTransactionParticipant(string id)
        {
            this.ParticipantId = id;
        }

        public async Task<bool> PrepareAsync(string transactionId)
        {
            if (this.PrepareDelayMs > 0)
            {
                await Task.Delay(this.PrepareDelayMs);
            }

            if (this.ShouldFailPrepare)
            {
                return false;
            }

            this.IsPrepared = true;
            return true;
        }

        public async Task CommitAsync(string transactionId)
        {
            this.CommitAttempts++;

            if (this.ShouldFailCommitOnce && this.CommitAttempts == 1)
            {
                throw new InvalidOperationException("Simulated commit failure");
            }

            this.IsCommitted = true;
            await Task.CompletedTask;
        }

        public async Task AbortAsync(string transactionId)
        {
            this.IsAborted = true;
            await Task.CompletedTask;
        }

        public Task<ParticipantStatus> GetStatusAsync(string transactionId)
        {
            ParticipantState state;
            if (this.IsCommitted)
            {
                state = ParticipantState.Committed;
            }
            else if (this.IsAborted)
            {
                state = ParticipantState.Aborted;
            }
            else if (this.IsPrepared)
            {
                state = ParticipantState.Prepared;
            }
            else
            {
                state = ParticipantState.Active;
            }

            var status = new ParticipantStatus
            {
                ParticipantId = this.ParticipantId,
                State = state,
                LastUpdate = DateTime.UtcNow
            };
            return Task.FromResult(status);
        }
    }
}
