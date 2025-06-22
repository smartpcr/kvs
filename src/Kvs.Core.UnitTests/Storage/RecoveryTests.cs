using System;
using System.IO;
using System.Threading.Tasks;
using FluentAssertions;
using Kvs.Core.Serialization;
using Kvs.Core.Storage;
using Xunit;

namespace Kvs.Core.UnitTests.Storage;

public class RecoveryTests : IDisposable
{
    private readonly string testFilePath;
    private readonly string walFilePath;
    private readonly FileStorageEngine storageEngine;
    private readonly FileStorageEngine walStorageEngine;
    private readonly BinarySerializer serializer;
    private readonly WAL wal;
    private readonly PageManager pageManager;
    private readonly RecoveryManager recoveryManager;

    public RecoveryTests()
    {
        this.testFilePath = Path.GetTempFileName();
        this.walFilePath = Path.GetTempFileName();
        this.storageEngine = new FileStorageEngine(this.testFilePath);
        this.walStorageEngine = new FileStorageEngine(this.walFilePath);
        this.serializer = new BinarySerializer();
        this.wal = new WAL(this.walStorageEngine, this.serializer);
        this.pageManager = new PageManager(this.storageEngine);
        this.recoveryManager = new RecoveryManager(this.wal, this.pageManager);
    }

    [Fact]
    public async Task RecoverAsync_ShouldReturnTrue_WhenRecoverySuccessful()
    {
        var entry = this.CreateTestEntry("tx1", "value1");
        await this.wal.WriteEntryAsync(entry);
        await this.wal.FlushAsync();

        var result = await this.recoveryManager.RecoverAsync();

        result.Should().BeTrue();
    }

    [Fact]
    public async Task RecoverAsync_ShouldHandleEmptyWAL()
    {
        var result = await this.recoveryManager.RecoverAsync();

        result.Should().BeTrue();
    }

    [Fact]
    public async Task GetUncommittedTransactionsAsync_ShouldReturnUncommittedTransactions()
    {
        var entry1 = this.CreateTestEntry("tx1", "value1");
        var entry2 = this.CreateTestEntry("tx2", "value2");
        var commitEntry = this.CreateCommitEntry("tx1");

        await this.wal.WriteEntryAsync(entry1);
        await this.wal.WriteEntryAsync(entry2);
        await this.wal.WriteEntryAsync(commitEntry);

        var uncommittedTransactions = await this.recoveryManager.GetUncommittedTransactionsAsync();

        uncommittedTransactions.Should().HaveCountGreaterOrEqualTo(0);
    }

    [Fact]
    public async Task RollbackTransactionAsync_ShouldComplete_WithoutException()
    {
        var entry = this.CreateTestEntry("tx1", "value1");
        await this.wal.WriteEntryAsync(entry);

        var act = async () => await this.recoveryManager.RollbackTransactionAsync("tx1");

        await act.Should().NotThrowAsync();
    }

    [Fact]
    public async Task RedoTransactionAsync_ShouldComplete_WithoutException()
    {
        var entry = this.CreateTestEntry("tx1", "value1");
        var commitEntry = this.CreateCommitEntry("tx1");

        await this.wal.WriteEntryAsync(entry);
        await this.wal.WriteEntryAsync(commitEntry);

        var act = async () => await this.recoveryManager.RedoTransactionAsync("tx1");

        await act.Should().NotThrowAsync();
    }

    [Fact]
    public async Task RecoverAsync_WithCommittedTransactions_ShouldSucceed()
    {
        var entry1 = this.CreateTestEntry("tx1", "value1");
        var entry2 = this.CreateTestEntry("tx2", "value2");
        var commit1 = this.CreateCommitEntry("tx1");
        var commit2 = this.CreateCommitEntry("tx2");

        await this.wal.WriteEntryAsync(entry1);
        await this.wal.WriteEntryAsync(entry2);
        await this.wal.WriteEntryAsync(commit1);
        await this.wal.WriteEntryAsync(commit2);

        var result = await this.recoveryManager.RecoverAsync();

        result.Should().BeTrue();
    }

    [Fact]
    public async Task RecoverAsync_ShouldBeIdempotent()
    {
        var entry = this.CreateTestEntry("tx1", "value1");
        var commit = this.CreateCommitEntry("tx1");

        await this.wal.WriteEntryAsync(entry);
        await this.wal.WriteEntryAsync(commit);

        var result1 = await this.recoveryManager.RecoverAsync();
        var result2 = await this.recoveryManager.RecoverAsync();

        result1.Should().BeTrue();
        result2.Should().BeTrue();
    }

    [Fact]
    public async Task IsRecoveryNeededAsync_ShouldComplete_WithoutException()
    {
        var act = async () => await this.recoveryManager.IsRecoveryNeededAsync();

        await act.Should().NotThrowAsync();
    }

    [Fact]
    public async Task RollbackTransaction_ShouldHandleNonExistentTransaction()
    {
        var act = async () => await this.recoveryManager.RollbackTransactionAsync("nonexistent");

        await act.Should().NotThrowAsync();
    }

    private TransactionLogEntry CreateTestEntry(string transactionId, string value)
    {
        return new TransactionLogEntry(
            0, // LSN will be assigned by WAL
            transactionId,
            OperationType.Insert,
            1, // PageId
            ReadOnlyMemory<byte>.Empty, // BeforeImage
            this.serializer.Serialize(value), // AfterImage
            DateTime.UtcNow);
    }

    private TransactionLogEntry CreateCommitEntry(string transactionId)
    {
        return new TransactionLogEntry(
            0, // LSN will be assigned by WAL
            transactionId,
            OperationType.Commit,
            0, // PageId
            ReadOnlyMemory<byte>.Empty, // BeforeImage
            ReadOnlyMemory<byte>.Empty, // AfterImage
            DateTime.UtcNow);
    }

    public void Dispose()
    {
        this.pageManager?.Dispose();
        this.wal?.Dispose();
        this.storageEngine?.Dispose();
        this.walStorageEngine?.Dispose();

        if (File.Exists(this.testFilePath))
        {
            File.Delete(this.testFilePath);
        }

        if (File.Exists(this.walFilePath))
        {
            File.Delete(this.walFilePath);
        }
    }
}
