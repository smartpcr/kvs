using System;
using System.IO;
using System.Threading.Tasks;
using FluentAssertions;
using Kvs.Core.Storage;
using Xunit;

namespace Kvs.Core.UnitTests.Storage;

public class PageManagerTests : IDisposable
{
    private readonly string testFilePath;
    private readonly FileStorageEngine storageEngine;
    private readonly PageManager pageManager;

    public PageManagerTests()
    {
        this.testFilePath = Path.GetTempFileName();
        this.storageEngine = new FileStorageEngine(this.testFilePath);
        this.pageManager = new PageManager(this.storageEngine);
    }

    [Fact]
    public async Task AllocatePageAsync_ShouldReturnUniquePageIds()
    {
        var page1 = await this.pageManager.AllocatePageAsync(PageType.Data);
        var page2 = await this.pageManager.AllocatePageAsync(PageType.Data);

        page1.PageId.Should().NotBe(page2.PageId);
        page1.PageId.Should().BeGreaterOrEqualTo(0);
        page2.PageId.Should().BeGreaterOrEqualTo(0);
    }

    [Fact]
    public async Task GetPageAsync_ShouldReturnAllocatedPage()
    {
        var allocatedPage = await this.pageManager.AllocatePageAsync(PageType.Data);

        var retrievedPage = await this.pageManager.GetPageAsync(allocatedPage.PageId);

        retrievedPage.PageId.Should().Be(allocatedPage.PageId);
    }

    [Fact]
    public async Task GetPageAsync_ShouldThrow_WhenPageDoesNotExist()
    {
        var nonExistentPageId = 9999L;

        var act = async () => await this.pageManager.GetPageAsync(nonExistentPageId);

        await act.Should().ThrowAsync<InvalidOperationException>();
    }

    [Fact]
    public async Task WritePageAsync_ShouldPersistPageData()
    {
        var page = await this.pageManager.AllocatePageAsync(PageType.Data);
        var testData = new byte[] { 1, 2, 3, 4, 5 };
        page.WriteData(testData);

        await this.pageManager.WritePageAsync(page);

        var retrievedPage = await this.pageManager.GetPageAsync(page.PageId);
        retrievedPage.Data.ToArray().Should().BeEquivalentTo(testData);
    }

    [Fact]
    public async Task WritePageAsync_ShouldWriteToCorrectPosition()
    {
        var page = await this.pageManager.AllocatePageAsync(PageType.Data);
        var testData = new byte[] { 10, 20, 30 };
        page.WriteData(testData);

        await this.pageManager.WritePageAsync(page);

        var expectedPosition = page.PageId * Page.DefaultPageSize;
        var raw = await this.storageEngine.ReadAsync(expectedPosition, Page.DefaultPageSize);

        raw.ToArray().Should().BeEquivalentTo(page.BufferMemory.ToArray());
    }

    [Fact]
    public async Task WritePageAsync_ShouldComplete_WithoutException()
    {
        var page = await this.pageManager.AllocatePageAsync(PageType.Data);
        page.WriteData(new byte[] { 1, 2, 3 });

        var act = async () => await this.pageManager.WritePageAsync(page);

        await act.Should().NotThrowAsync();
    }

    [Fact]
    public async Task FreePageAsync_ShouldMakePageIdReusable()
    {
        var page = await this.pageManager.AllocatePageAsync(PageType.Data);
        var originalPageId = page.PageId;

        await this.pageManager.FreePageAsync(originalPageId);

        var pageExists = await this.pageManager.PageExistsAsync(originalPageId);
        pageExists.Should().BeFalse();
    }

    [Fact]
    public async Task FreePageAsync_ShouldPersistFreePageAtCorrectPosition()
    {
        var page = await this.pageManager.AllocatePageAsync(PageType.Data);
        var pageId = page.PageId;

        await this.pageManager.FreePageAsync(pageId);

        var position = pageId * Page.DefaultPageSize;
        var data = await this.storageEngine.ReadAsync(position, Page.DefaultPageSize);
        var readPage = new Page(data.Span);

        readPage.PageType.Should().Be(PageType.Free);
    }

    [Fact]
    public async Task GetPageCountAsync_ShouldReturnCorrectCount()
    {
        var initialCount = await this.pageManager.GetPageCountAsync();

        // For a new empty file, initial count should be 0
        initialCount.Should().Be(0);

        await this.pageManager.AllocatePageAsync(PageType.Data);
        await this.pageManager.AllocatePageAsync(PageType.Data);

        var finalCount = await this.pageManager.GetPageCountAsync();
        finalCount.Should().Be(2);
    }

    [Fact]
    public async Task FlushAsync_ShouldComplete_WithoutException()
    {
        var page1 = await this.pageManager.AllocatePageAsync(PageType.Data);
        var page2 = await this.pageManager.AllocatePageAsync(PageType.Data);

        page1.WriteData(new byte[] { 0xAA });
        page2.WriteData(new byte[] { 0xBB });

        var act = async () => await this.pageManager.FlushAsync();

        await act.Should().NotThrowAsync();
    }

    [Fact]
    public async Task FlushAsync_ShouldWriteCachedPagesToCorrectPositions()
    {
        var page1 = await this.pageManager.AllocatePageAsync(PageType.Data);
        var page2 = await this.pageManager.AllocatePageAsync(PageType.Data);

        page1.WriteData(new byte[] { 0x11, 0x22 });
        page2.WriteData(new byte[] { 0x33, 0x44 });

        await this.pageManager.FlushAsync();

        var data1 = await this.storageEngine.ReadAsync(page1.PageId * Page.DefaultPageSize, Page.DefaultPageSize);
        var data2 = await this.storageEngine.ReadAsync(page2.PageId * Page.DefaultPageSize, Page.DefaultPageSize);

        data1.ToArray().Should().BeEquivalentTo(page1.BufferMemory.ToArray());
        data2.ToArray().Should().BeEquivalentTo(page2.BufferMemory.ToArray());
    }

    [Fact]
    public async Task PageExists_ShouldReturnTrue_ForAllocatedPage()
    {
        var page = await this.pageManager.AllocatePageAsync(PageType.Data);
        await this.pageManager.WritePageAsync(page);

        var exists = await this.pageManager.PageExistsAsync(page.PageId);

        exists.Should().BeTrue();
    }

    [Fact]
    public async Task PageExists_ShouldReturnFalse_ForNonExistentPage()
    {
        var nonExistentPageId = 9999L;

        var exists = await this.pageManager.PageExistsAsync(nonExistentPageId);

        exists.Should().BeFalse();
    }

    [Fact]
    public async Task AllocateAndFree_ShouldWorkCorrectly()
    {
        var page = await this.pageManager.AllocatePageAsync(PageType.Data);
        var pageId = page.PageId;

        await this.pageManager.FreePageAsync(pageId);
        var exists = await this.pageManager.PageExistsAsync(pageId);

        exists.Should().BeFalse();
    }

    public void Dispose()
    {
        this.pageManager?.Dispose();
        this.storageEngine?.Dispose();
        if (File.Exists(this.testFilePath))
        {
            File.Delete(this.testFilePath);
        }
    }
}
