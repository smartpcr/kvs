using System;
using FluentAssertions;
using Kvs.Core.Storage;
using Xunit;

namespace Kvs.Core.UnitTests.Storage;

public class PageTests
{
    private const int DefaultPageSize = 4096;

    [Fact]
    public void Constructor_ShouldCreatePage_WithCorrectPageId()
    {
        var pageId = 42L;

        var page = new Page(pageId, PageType.Data);

        page.PageId.Should().Be(pageId);
    }

    [Fact]
    public void Constructor_ShouldCreatePage_WithDefaultPageSize()
    {
        var pageId = 1L;

        var page = new Page(pageId, PageType.Data);

        page.PageSize.Should().Be(DefaultPageSize);
    }

    [Fact]
    public void Constructor_ShouldCreatePage_WithCustomPageSize()
    {
        var pageId = 1L;
        var customSize = 8192;

        var page = new Page(pageId, PageType.Data, customSize);

        page.PageSize.Should().Be(customSize);
    }

    [Fact]
    public void Constructor_ShouldThrow_WhenPageSizeIsZero()
    {
        var pageId = 1L;
        var invalidSize = 0;

        var act = () => new Page(pageId, PageType.Data, invalidSize);

        act.Should().Throw<ArgumentException>();
    }

    [Fact]
    public void Constructor_ShouldThrow_WhenPageSizeIsNegative()
    {
        var pageId = 1L;
        var invalidSize = -1;

        var act = () => new Page(pageId, PageType.Data, invalidSize);

        act.Should().Throw<ArgumentException>();
    }

    [Fact]
    public void PageType_ShouldReturnCorrectType()
    {
        var page = new Page(1L, PageType.LeafNode);

        page.PageType.Should().Be(PageType.LeafNode);
    }

    [Fact]
    public void DataSize_ShouldBeZero_Initially()
    {
        var page = new Page(1L, PageType.Data);

        page.DataSize.Should().Be(0);
    }

    [Fact]
    public void AvailableSpace_ShouldBeMaximum_Initially()
    {
        var page = new Page(1L, PageType.Data);

        page.AvailableSpace.Should().Be(Page.MaxDataSize);
    }

    [Fact]
    public void Data_ShouldBeEmpty_Initially()
    {
        var page = new Page(1L, PageType.Data);

        page.Data.Length.Should().Be(0);
    }

    [Fact]
    public void WriteData_ShouldModifyPageData()
    {
        var page = new Page(1L, PageType.Data);
        var testData = new byte[] { 1, 2, 3, 4, 5 };

        page.WriteData(testData);

        page.Data.ToArray().Should().BeEquivalentTo(testData);
        page.DataSize.Should().Be(testData.Length);
    }

    [Fact]
    public void Buffer_ShouldProvideAccessToEntirePage()
    {
        var page = new Page(1L, PageType.Data);

        page.Buffer.Length.Should().Be(Page.DefaultPageSize);
    }

    [Fact]
    public void PageSize_ShouldRemainConstant_AfterModification()
    {
        var page = new Page(1L, PageType.Data, 2048);
        var originalSize = page.PageSize;

        page.WriteData(new byte[] { 1, 2, 3, 4, 5 });

        page.PageSize.Should().Be(originalSize);
    }

    [Fact]
    public void SetNextPageId_ShouldUpdateNextPageId()
    {
        var page = new Page(1L, PageType.Data);

        page.SetNextPageId(42L);

        page.NextPageId.Should().Be(42L);
    }

    [Fact]
    public void SetPrevPageId_ShouldUpdatePrevPageId()
    {
        var page = new Page(1L, PageType.Data);

        page.SetPrevPageId(42L);

        page.PrevPageId.Should().Be(42L);
    }

    [Fact]
    public void Clear_ShouldResetDataSize()
    {
        var page = new Page(1L, PageType.Data);
        page.WriteData(new byte[] { 1, 2, 3, 4, 5 });

        page.Clear();

        page.DataSize.Should().Be(0);
    }
}
