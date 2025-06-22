using System;
using System.IO;
using System.Threading.Tasks;
using FluentAssertions;
using Kvs.Core.Serialization;
using Kvs.Core.Storage;
using Xunit;

namespace Kvs.Core.UnitTests.Storage;

public class CheckpointTests : IDisposable
{
    private readonly string testFilePath;
    private readonly string walFilePath;
    private readonly FileStorageEngine storageEngine;
    private readonly FileStorageEngine walStorageEngine;
    private readonly BinarySerializer serializer;
    private readonly WAL wal;
    private readonly PageManager pageManager;
    private readonly CheckpointManager checkpointManager;

    public CheckpointTests()
    {
        this.testFilePath = Path.GetTempFileName();
        this.walFilePath = Path.GetTempFileName();
        this.storageEngine = new FileStorageEngine(this.testFilePath);
        this.walStorageEngine = new FileStorageEngine(this.walFilePath);
        this.serializer = new BinarySerializer();
        this.wal = new WAL(this.walStorageEngine, this.serializer);
        this.pageManager = new PageManager(this.storageEngine);
        this.checkpointManager = new CheckpointManager(this.wal, this.pageManager);
    }

    [Fact]
    public async Task CreateCheckpointAsync_ShouldReturnTrue()
    {
        var entry = this.CreateTestEntry("tx1", "value1");
        await this.wal.WriteEntryAsync(entry);

        var result = await this.checkpointManager.CreateCheckpointAsync();

        result.Should().BeTrue();
    }

    [Fact]
    public async Task CreateCheckpoint_ShouldComplete_WithoutException()
    {
        var entry = this.CreateTestEntry("tx1", "value1");
        await this.wal.WriteEntryAsync(entry);

        var act = async () => await this.checkpointManager.CreateCheckpointAsync();

        await act.Should().NotThrowAsync();
    }

    [Fact]
    public async Task GetLastCheckpointLsnAsync_ShouldReturnZero_Initially()
    {
        var lastCheckpointLsn = await this.checkpointManager.GetLastCheckpointLsnAsync();

        lastCheckpointLsn.Should().Be(0);
    }

    [Fact]
    public async Task GetLastCheckpointLsnAsync_ShouldReturnValidLSN_AfterCheckpoint()
    {
        var entry = this.CreateTestEntry("tx1", "value1");
        await this.wal.WriteEntryAsync(entry);

        await this.checkpointManager.CreateCheckpointAsync();
        var lastCheckpointLsn = await this.checkpointManager.GetLastCheckpointLsnAsync();

        lastCheckpointLsn.Should().BeGreaterThan(0);
    }

    [Fact]
    public async Task CreateMultipleCheckpoints_ShouldUpdateLastCheckpointLSN()
    {
        var entry1 = this.CreateTestEntry("tx1", "value1");
        var entry2 = this.CreateTestEntry("tx2", "value2");

        await this.wal.WriteEntryAsync(entry1);
        await this.checkpointManager.CreateCheckpointAsync();
        var checkpoint1Lsn = await this.checkpointManager.GetLastCheckpointLsnAsync();

        await this.wal.WriteEntryAsync(entry2);
        await this.checkpointManager.CreateCheckpointAsync();
        var checkpoint2Lsn = await this.checkpointManager.GetLastCheckpointLsnAsync();

        checkpoint2Lsn.Should().BeGreaterOrEqualTo(checkpoint1Lsn);
    }

    [Fact]
    public async Task IsCheckpointNeededAsync_ShouldComplete_WithoutException()
    {
        var act = async () => await this.checkpointManager.IsCheckpointNeededAsync();

        await act.Should().NotThrowAsync();
    }

    [Fact]
    public async Task CreateCheckpoint_WithMultipleEntries_ShouldSucceed()
    {
        for (int i = 0; i < 10; i++)
        {
            var entry = this.CreateTestEntry($"tx{i}", $"value{i}");
            await this.wal.WriteEntryAsync(entry);
        }

        var result = await this.checkpointManager.CreateCheckpointAsync();

        result.Should().BeTrue();
    }

    [Fact]
    public void CheckpointManager_ShouldImplementIDisposable()
    {
        this.checkpointManager.Should().BeAssignableTo<IDisposable>();
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

    public void Dispose()
    {
        this.checkpointManager?.Dispose();
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
