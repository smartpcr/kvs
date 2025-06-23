using System;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using FluentAssertions;
using Kvs.Core.Serialization;
using Kvs.Core.Storage;
using Kvs.Core.TestUtilities;
using Xunit;

namespace Kvs.Core.UnitTests.Storage;

public class WALTests : IDisposable
{
    private readonly string testFilePath;
    private FileStorageEngine storageEngine;
    private readonly BinarySerializer serializer;
    private WAL wal;

    public WALTests()
    {
        this.testFilePath = Path.GetTempFileName();
        this.storageEngine = new FileStorageEngine(this.testFilePath);
        this.serializer = new BinarySerializer();
        this.wal = new WAL(this.storageEngine, this.serializer);
    }

    [Fact]
    public async Task WriteEntryAsync_ShouldReturnLSN()
    {
        var entry = this.CreateTestEntry("tx1", "value1");

        var lsn = await this.wal.WriteEntryAsync(entry);

        lsn.Should().BeGreaterThan(0);
    }

    [Fact]
    public async Task WriteMultipleEntries_ShouldReturnIncrementingLSNs()
    {
        var entry1 = this.CreateTestEntry("tx1", "value1");
        var entry2 = this.CreateTestEntry("tx2", "value2");

        var lsn1 = await this.wal.WriteEntryAsync(entry1);
        var lsn2 = await this.wal.WriteEntryAsync(entry2);

        lsn2.Should().BeGreaterThan(lsn1);
    }

    [Fact]
    public async Task ReadEntriesAsync_ShouldReturnWrittenEntries()
    {
        var entry1 = this.CreateTestEntry("tx1", "value1");
        var entry2 = this.CreateTestEntry("tx2", "value2");

        var lsn1 = await this.wal.WriteEntryAsync(entry1);
        await this.wal.WriteEntryAsync(entry2);

        var entries = await this.wal.ReadEntriesAsync(lsn1);

        entries.Should().HaveCount(2);
    }

    [Fact]
    public async Task ReadEntriesAsync_WithSpecificLSN_ShouldReturnEntriesFromThatPoint()
    {
        var entry1 = this.CreateTestEntry("tx1", "value1");
        var entry2 = this.CreateTestEntry("tx2", "value2");
        var entry3 = this.CreateTestEntry("tx3", "value3");

        await this.wal.WriteEntryAsync(entry1);
        var lsn2 = await this.wal.WriteEntryAsync(entry2);
        await this.wal.WriteEntryAsync(entry3);

        var entries = await this.wal.ReadEntriesAsync(lsn2);

        entries.Should().HaveCount(2);
    }

    [Fact]
    public async Task FlushAsync_ShouldReturnTrue_WhenSuccessful()
    {
        var entry = this.CreateTestEntry("tx1", "value1");
        await this.wal.WriteEntryAsync(entry);

        var result = await this.wal.FlushAsync();

        result.Should().BeTrue();
    }

    [Fact]
    public async Task GetLastLsnAsync_ShouldReturnCorrectLSN()
    {
        var entry1 = this.CreateTestEntry("tx1", "value1");
        var entry2 = this.CreateTestEntry("tx2", "value2");

        await this.wal.WriteEntryAsync(entry1);
        var lastLsn = await this.wal.WriteEntryAsync(entry2);

        var retrievedLastLsn = await this.wal.GetLastLsnAsync();

        retrievedLastLsn.Should().Be(lastLsn);
    }

    [Fact]
    public async Task GetLastLsnAsync_ShouldReturnZero_WhenEmpty()
    {
        var lastLsn = await this.wal.GetLastLsnAsync();

        lastLsn.Should().Be(0);
    }

    [Fact]
    public async Task WriteEntry_ShouldPersistData_AfterFlush()
    {
        var entry = this.CreateTestEntry("tx1", "value1");
        var lsn = await this.wal.WriteEntryAsync(entry);
        await this.wal.FlushAsync();

        var entries = await this.wal.ReadEntriesAsync(lsn);

        entries.Should().HaveCount(1);
        var retrievedEntry = entries[0];
        retrievedEntry.TransactionId.Should().Be(entry.TransactionId);
        retrievedEntry.OperationType.Should().Be(entry.OperationType);
    }

    [Fact]
    public async Task WriteEntry_WithNullTransaction_ShouldThrow()
    {
        var entry = new TransactionLogEntry(
            0,
            null!,
            OperationType.Insert,
            1,
            ReadOnlyMemory<byte>.Empty,
            this.serializer.Serialize("value1"),
            DateTime.UtcNow);

        var act = async () => await this.wal.WriteEntryAsync(entry);

        await act.Should().ThrowAsync<ArgumentException>();
    }

    [Fact]
    public async Task ReadEntries_WithInvalidLSN_ShouldReturnEmpty()
    {
        var invalidLsn = 9999L;

        var entries = await this.wal.ReadEntriesAsync(invalidLsn);

        entries.Should().BeEmpty();
    }

    [Fact]
    public async Task WAL_ShouldMaintainOrder_ForConcurrentWrites()
    {
        var tasks = new Task<long>[10];
        for (int i = 0; i < 10; i++)
        {
            var index = i;
            tasks[i] = this.wal.WriteEntryAsync(this.CreateTestEntry($"tx{index}", $"value{index}"));
        }

        var lsns = await Task.WhenAll(tasks);

        lsns.Should().BeInAscendingOrder();
        lsns.Should().OnlyHaveUniqueItems();
    }

    [Fact]
    public async Task WriteEntry_ShouldUpdateLastLSN_Immediately()
    {
        var entry = this.CreateTestEntry("tx1", "value1");

        var lsn = await this.wal.WriteEntryAsync(entry);
        var lastLsn = await this.wal.GetLastLsnAsync();

        lastLsn.Should().Be(lsn);
    }

    [Fact]
    public async Task ReadEntriesAsync_ShouldSkipCorruptedEntries()
    {
        var entry = this.CreateTestEntry("tx1", "value1");
        var lsn = await this.wal.WriteEntryAsync(entry);
        await this.wal.FlushAsync();

        // Dispose WAL and storage engine to release file lock before corrupting
        this.wal.Dispose();
        this.storageEngine.Dispose();

        // Corrupt the checksum in the log file
        using (var stream = new FileStream(
                   this.testFilePath,
                   FileMode.Open,
                   FileAccess.ReadWrite,
                   FileShare.None))
        {
            stream.Seek(-1, SeekOrigin.End);
            var current = stream.ReadByte();
            stream.Seek(-1, SeekOrigin.End);
            stream.WriteByte((byte)(current ^ 0xFF));
        }

        // Recreate WAL to read corrupted data
        this.storageEngine = new FileStorageEngine(this.testFilePath);
        this.wal = new WAL(this.storageEngine, this.serializer);

        var entries = await this.wal.ReadEntriesAsync(lsn);

        entries.Should().BeEmpty();
    }

    [Fact]
    public async Task CheckpointLsn_ShouldPersist_AfterReopen()
    {
        var entry = this.CreateTestEntry("tx1", "value1");
        var lsn = await this.wal.WriteEntryAsync(entry);

        await this.wal.CheckpointAsync(lsn);

        this.wal.Dispose();
        this.storageEngine.Dispose();

        var newStorage = new FileStorageEngine(this.testFilePath);
        var newWal = new WAL(newStorage, this.serializer);

        var entries = await newWal.ReadEntriesAsync(0);
        var lastCheckpoint = entries
            .Where(e => e.OperationType == OperationType.Checkpoint)
            .Select(e => e.Lsn)
            .DefaultIfEmpty(0)
            .Max();

        lastCheckpoint.Should().Be(lsn);

        newWal.Dispose();
        newStorage.Dispose();
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
        this.wal?.Dispose();
        this.storageEngine?.Dispose();
        FileHelper.DeleteFileWithRetry(this.testFilePath);
    }
}
