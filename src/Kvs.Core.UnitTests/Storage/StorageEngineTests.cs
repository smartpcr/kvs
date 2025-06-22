using System;
using System.IO;
using System.Threading.Tasks;
using FluentAssertions;
using Kvs.Core.Storage;
using Xunit;

namespace Kvs.Core.UnitTests.Storage;

public class StorageEngineTests : IDisposable
{
    private readonly string testFilePath;
    private readonly FileStorageEngine storageEngine;

    public StorageEngineTests()
    {
        this.testFilePath = Path.GetTempFileName();
        this.storageEngine = new FileStorageEngine(this.testFilePath);
    }

    [Fact]
    public async Task WriteAsync_ShouldReturnPosition_WhenDataIsWritten()
    {
        var data = new byte[] { 1, 2, 3, 4, 5 };

        var position = await this.storageEngine.WriteAsync(data);

        position.Should().BeGreaterOrEqualTo(0);
    }

    [Fact]
    public async Task ReadAsync_ShouldReturnWrittenData_WhenValidPositionAndLength()
    {
        var originalData = new byte[] { 1, 2, 3, 4, 5 };
        var position = await this.storageEngine.WriteAsync(originalData);

        var readData = await this.storageEngine.ReadAsync(position, originalData.Length);

        readData.ToArray().Should().BeEquivalentTo(originalData);
    }

    [Fact]
    public async Task ReadAsync_ShouldThrow_WhenInvalidPosition()
    {
        var invalidPosition = -1L;

        var act = async () => await this.storageEngine.ReadAsync(invalidPosition, 10);

        await act.Should().ThrowAsync<ArgumentOutOfRangeException>();
    }

    [Fact]
    public async Task GetSizeAsync_ShouldReturnCorrectSize_AfterWrites()
    {
        var data1 = new byte[] { 1, 2, 3 };
        var data2 = new byte[] { 4, 5, 6, 7 };

        await this.storageEngine.WriteAsync(data1);
        await this.storageEngine.WriteAsync(data2);

        var size = await this.storageEngine.GetSizeAsync();

        size.Should().BeGreaterOrEqualTo(data1.Length + data2.Length);
    }

    [Fact]
    public async Task FlushAsync_ShouldComplete_WithoutException()
    {
        var data = new byte[] { 1, 2, 3, 4, 5 };
        await this.storageEngine.WriteAsync(data);

        var act = async () => await this.storageEngine.FlushAsync();

        await act.Should().NotThrowAsync();
    }

    [Fact]
    public async Task FsyncAsync_ShouldReturnTrue_WhenSuccessful()
    {
        var data = new byte[] { 1, 2, 3, 4, 5 };
        await this.storageEngine.WriteAsync(data);

        var result = await this.storageEngine.FsyncAsync();

        result.Should().BeTrue();
    }

    [Fact]
    public async Task TruncateAsync_ShouldReduceFileSize_WhenSizeIsSmaller()
    {
        var data = new byte[] { 1, 2, 3, 4, 5, 6, 7, 8, 9, 10 };
        await this.storageEngine.WriteAsync(data);
        await this.storageEngine.FlushAsync();

        var originalSize = await this.storageEngine.GetSizeAsync();
        var newSize = originalSize / 2;

        await this.storageEngine.TruncateAsync(newSize);

        var actualSize = await this.storageEngine.GetSizeAsync();
        actualSize.Should().BeLessOrEqualTo(newSize);
    }

    [Fact]
    public async Task MultipleWrites_ShouldMaintainDataIntegrity()
    {
        var data1 = new byte[] { 1, 2, 3 };
        var data2 = new byte[] { 4, 5, 6, 7 };
        var data3 = new byte[] { 8, 9 };

        var pos1 = await this.storageEngine.WriteAsync(data1);
        var pos2 = await this.storageEngine.WriteAsync(data2);
        var pos3 = await this.storageEngine.WriteAsync(data3);

        var read1 = await this.storageEngine.ReadAsync(pos1, data1.Length);
        var read2 = await this.storageEngine.ReadAsync(pos2, data2.Length);
        var read3 = await this.storageEngine.ReadAsync(pos3, data3.Length);

        read1.ToArray().Should().BeEquivalentTo(data1);
        read2.ToArray().Should().BeEquivalentTo(data2);
        read3.ToArray().Should().BeEquivalentTo(data3);
    }

    public void Dispose()
    {
        this.storageEngine?.Dispose();
        if (File.Exists(this.testFilePath))
        {
            File.Delete(this.testFilePath);
        }
    }
}
