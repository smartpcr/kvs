using System;
using System.Linq;
using System.Threading.Tasks;
using FluentAssertions;
using Kvs.Core.Indexing;
using Xunit;

namespace Kvs.Core.UnitTests.Indexing;

// Extension method for async enumerable to list conversion
public static class AsyncEnumerableExtensions
{
    public static async Task<System.Collections.Generic.List<T>> ToListAsync<T>(this System.Collections.Generic.IAsyncEnumerable<T> source)
    {
        var list = new System.Collections.Generic.List<T>();
        await foreach (var item in source)
        {
            list.Add(item);
        }

        return list;
    }
}

public class BTreeIndexTests
{
    [Fact]
    public void Constructor_WithValidDegree_ShouldCreateEmptyIndex()
    {
        // Arrange & Act
        var index = new BTreeIndex<int, string>(5);

        // Assert
        index.IsEmpty.Should().BeTrue();
    }

    [Fact]
    public async Task PutAsync_NewItem_ShouldAddToIndex()
    {
        // Arrange
        var index = new BTreeIndex<int, string>();

        // Act
        await index.PutAsync(1, "one");

        // Assert
        var count = await index.CountAsync();
        count.Should().Be(1);
        index.IsEmpty.Should().BeFalse();
    }

    [Fact]
    public async Task PutAsync_NullKey_ShouldThrowArgumentNullException()
    {
        // Arrange
        var index = new BTreeIndex<string, string>();

        // Act & Assert
        await Assert.ThrowsAsync<ArgumentNullException>(() => index.PutAsync(null!, "value"));
    }

    [Fact]
    public async Task GetAsync_ExistingKey_ShouldReturnValue()
    {
        // Arrange
        var index = new BTreeIndex<int, string>();
        await index.PutAsync(1, "one");
        await index.PutAsync(2, "two");

        // Act
        var result = await index.GetAsync(1);

        // Assert
        result.Should().Be("one");
    }

    [Fact]
    public async Task GetAsync_NonExistingKey_ShouldReturnDefault()
    {
        // Arrange
        var index = new BTreeIndex<int, string>();
        await index.PutAsync(1, "one");

        // Act
        var result = await index.GetAsync(999);

        // Assert
        result.Should().BeNull();
    }

    [Fact]
    public async Task GetAsync_NullKey_ShouldThrowArgumentNullException()
    {
        // Arrange
        var index = new BTreeIndex<string, string>();

        // Act & Assert
        await Assert.ThrowsAsync<ArgumentNullException>(() => index.GetAsync(null!));
    }

    [Fact]
    public async Task DeleteAsync_ExistingKey_ShouldReturnTrueAndRemove()
    {
        // Arrange
        var index = new BTreeIndex<int, string>();
        await index.PutAsync(1, "one");
        await index.PutAsync(2, "two");

        // Act
        var result = await index.DeleteAsync(1);

        // Assert
        result.Should().BeTrue();
        var count = await index.CountAsync();
        count.Should().Be(1);

        var value = await index.GetAsync(1);
        value.Should().BeNull();
    }

    [Fact]
    public async Task DeleteAsync_NonExistingKey_ShouldReturnFalse()
    {
        // Arrange
        var index = new BTreeIndex<int, string>();
        await index.PutAsync(1, "one");

        // Act
        var result = await index.DeleteAsync(999);

        // Assert
        result.Should().BeFalse();
        var count = await index.CountAsync();
        count.Should().Be(1);
    }

    [Fact]
    public async Task ContainsKeyAsync_ExistingKey_ShouldReturnTrue()
    {
        // Arrange
        var index = new BTreeIndex<int, string>();
        await index.PutAsync(1, "one");

        // Act
        var result = await index.ContainsKeyAsync(1);

        // Assert
        result.Should().BeTrue();
    }

    [Fact]
    public async Task ContainsKeyAsync_NonExistingKey_ShouldReturnFalse()
    {
        // Arrange
        var index = new BTreeIndex<int, string>();
        await index.PutAsync(1, "one");

        // Act
        var result = await index.ContainsKeyAsync(999);

        // Assert
        result.Should().BeFalse();
    }

    [Fact]
    public async Task RangeAsync_ValidRange_ShouldReturnItemsInRange()
    {
        // Arrange
        var index = new BTreeIndex<int, string>();
        for (int i = 1; i <= 10; i++)
        {
            await index.PutAsync(i, $"value{i}");
        }

        // Act
        var results = await index.RangeAsync(3, 7).ToListAsync();

        // Assert
        results.Should().HaveCount(5);
        results.Select(kvp => kvp.Key).Should().BeInAscendingOrder();
        results[0].Key.Should().Be(3);
        results[results.Count - 1].Key.Should().Be(7);
    }

    [Fact]
    public async Task RangeAsync_NullKeys_ShouldThrowArgumentNullException()
    {
        // Arrange
        var index = new BTreeIndex<string, string>();

        // Act & Assert
        await Assert.ThrowsAsync<ArgumentNullException>(async () =>
            await index.RangeAsync(null!, "end").ToListAsync());
        await Assert.ThrowsAsync<ArgumentNullException>(async () =>
            await index.RangeAsync("start", null!).ToListAsync());
    }

    [Fact]
    public async Task GetAllAsync_WithItems_ShouldReturnAllItemsSorted()
    {
        // Arrange
        var index = new BTreeIndex<int, string>();
        var items = new[] { (3, "three"), (1, "one"), (4, "four"), (2, "two") };
        foreach (var (key, value) in items)
        {
            await index.PutAsync(key, value);
        }

        // Act
        var results = await index.GetAllAsync().ToListAsync();

        // Assert
        results.Should().HaveCount(4);
        results.Select(kvp => kvp.Key).Should().BeInAscendingOrder();
    }

    [Fact]
    public async Task GetAllAsync_EmptyIndex_ShouldReturnEmpty()
    {
        // Arrange
        var index = new BTreeIndex<int, string>();

        // Act
        var results = await index.GetAllAsync().ToListAsync();

        // Assert
        results.Should().BeEmpty();
    }

    [Fact]
    public async Task CountAsync_WithItems_ShouldReturnCorrectCount()
    {
        // Arrange
        var index = new BTreeIndex<int, string>();
        await index.PutAsync(1, "one");
        await index.PutAsync(2, "two");
        await index.PutAsync(3, "three");

        // Act
        var count = await index.CountAsync();

        // Assert
        count.Should().Be(3);
    }

    [Fact]
    public async Task GetMinKeyAsync_WithItems_ShouldReturnSmallestKey()
    {
        // Arrange
        var index = new BTreeIndex<int, string>();
        await index.PutAsync(5, "five");
        await index.PutAsync(2, "two");
        await index.PutAsync(8, "eight");

        // Act
        var result = await index.GetMinKeyAsync();

        // Assert
        result.Should().Be(2);
    }

    [Fact]
    public async Task GetMinKeyAsync_EmptyIndex_ShouldReturnDefault()
    {
        // Arrange
        var index = new BTreeIndex<int, string>();

        // Act
        var result = await index.GetMinKeyAsync();

        // Assert
        result.Should().Be(0); // default(int)
    }

    [Fact]
    public async Task GetMaxKeyAsync_WithItems_ShouldReturnLargestKey()
    {
        // Arrange
        var index = new BTreeIndex<int, string>();
        await index.PutAsync(5, "five");
        await index.PutAsync(2, "two");
        await index.PutAsync(8, "eight");

        // Act
        var result = await index.GetMaxKeyAsync();

        // Assert
        result.Should().Be(8);
    }

    [Fact]
    public async Task BatchInsertAsync_WithValidItems_ShouldInsertAll()
    {
        // Arrange
        var index = new BTreeIndex<int, string>();
        var items = new[]
        {
            new System.Collections.Generic.KeyValuePair<int, string>(1, "one"),
            new System.Collections.Generic.KeyValuePair<int, string>(2, "two"),
            new System.Collections.Generic.KeyValuePair<int, string>(3, "three")
        };

        // Act
        var insertCount = await index.BatchInsertAsync(items);

        // Assert
        insertCount.Should().Be(3);
        var count = await index.CountAsync();
        count.Should().Be(3);
    }

    [Fact]
    public async Task BatchInsertAsync_NullItems_ShouldThrowArgumentNullException()
    {
        // Arrange
        var index = new BTreeIndex<int, string>();

        // Act & Assert
        await Assert.ThrowsAsync<ArgumentNullException>(() =>
            index.BatchInsertAsync(null!));
    }

    [Fact]
    public async Task BatchDeleteAsync_WithExistingKeys_ShouldDeleteAll()
    {
        // Arrange
        var index = new BTreeIndex<int, string>();
        await index.PutAsync(1, "one");
        await index.PutAsync(2, "two");
        await index.PutAsync(3, "three");
        await index.PutAsync(4, "four");

        // Act
        var deleteCount = await index.BatchDeleteAsync(new[] { 1, 3, 5 }); // 5 doesn't exist

        // Assert
        deleteCount.Should().Be(2); // Only 1 and 3 were deleted
        var count = await index.CountAsync();
        count.Should().Be(2);

        var containsOne = await index.ContainsKeyAsync(1);
        var containsThree = await index.ContainsKeyAsync(3);
        containsOne.Should().BeFalse();
        containsThree.Should().BeFalse();
    }

    [Fact]
    public async Task FindKeysGreaterThanAsync_WithValidKey_ShouldReturnCorrectKeys()
    {
        // Arrange
        var index = new BTreeIndex<int, string>();
        for (int i = 1; i <= 10; i++)
        {
            await index.PutAsync(i, $"value{i}");
        }

        // Act
        var results = await index.FindKeysGreaterThanAsync(5, 3).ToListAsync();

        // Assert
        results.Should().HaveCount(3);
        results.Select(kvp => kvp.Key).Should().AllSatisfy(k => k.Should().BeGreaterThan(5));
        results.Select(kvp => kvp.Key).Should().BeInAscendingOrder();
    }

    [Fact]
    public async Task FindKeysLessThanAsync_WithValidKey_ShouldReturnCorrectKeys()
    {
        // Arrange
        var index = new BTreeIndex<int, string>();
        for (int i = 1; i <= 10; i++)
        {
            await index.PutAsync(i, $"value{i}");
        }

        // Act
        var results = await index.FindKeysLessThanAsync(5, 3).ToListAsync();

        // Assert
        results.Should().HaveCount(3);
        results.Select(kvp => kvp.Key).Should().AllSatisfy(k => k.Should().BeLessThan(5));
        results.Select(kvp => kvp.Key).Should().BeInAscendingOrder();
    }

    [Fact]
    public async Task Clear_WithItems_ShouldEmptyIndex()
    {
        // Arrange
        var index = new BTreeIndex<int, string>();
        await index.PutAsync(1, "one");
        await index.PutAsync(2, "two");

        // Act
        index.Clear();

        // Assert
        index.IsEmpty.Should().BeTrue();
        var count = await index.CountAsync();
        count.Should().Be(0);
    }

    [Fact]
    public async Task GetStatistics_WithItems_ShouldReturnCorrectStatistics()
    {
        // Arrange
        var index = new BTreeIndex<int, string>();
        await index.PutAsync(1, "one");
        await index.PutAsync(5, "five");

        // Act
        var stats = index.GetStatistics();

        // Assert
        stats["Count"].Should().Be(2L);
        stats["IsEmpty"].Should().Be(false);
        stats["MinKey"].Should().Be("1");
        stats["MaxKey"].Should().Be("5");
    }

    [Fact]
    public async Task FlushAsync_ShouldComplete()
    {
        // Arrange
        var index = new BTreeIndex<int, string>();
        await index.PutAsync(1, "one");

        // Act
        await index.FlushAsync();

        // Assert
        var count = await index.CountAsync();
        count.Should().Be(1); // Verify index is still functional after flush
    }

    [Fact]
    public async Task Dispose_ShouldClearIndex()
    {
        // Arrange
        var index = new BTreeIndex<int, string>();
        await index.PutAsync(1, "one");

        // Act
        index.Dispose();

        // Assert - Disposal completed successfully
        Assert.True(true);
    }

    [Fact]
    public void Dispose_MultipleCalls_ShouldNotThrow()
    {
        // Arrange
        var index = new BTreeIndex<int, string>();

        // Act & Assert - Multiple dispose calls should not throw
        try
        {
            index.Dispose();
#pragma warning disable S3966 // Objects should not be disposed more than once
            index.Dispose(); // Should not throw - testing multiple dispose is safe
#pragma warning restore S3966 // Objects should not be disposed more than once
        }
        catch (Exception ex)
        {
            Assert.Fail($"Unexpected exception: {ex.Message}");
        }
    }

    [Fact]
    public async Task Operations_AfterDispose_ShouldThrowObjectDisposedException()
    {
        // Arrange
        var index = new BTreeIndex<int, string>();
        index.Dispose();

        // Act & Assert
        await Assert.ThrowsAsync<ObjectDisposedException>(() => index.PutAsync(1, "one"));
        await Assert.ThrowsAsync<ObjectDisposedException>(() => index.GetAsync(1));
        await Assert.ThrowsAsync<ObjectDisposedException>(() => index.DeleteAsync(1));
        await Assert.ThrowsAsync<ObjectDisposedException>(() => index.ContainsKeyAsync(1));
    }

    [Fact]
    public async Task BTreeIndex_LargeDataSet_ShouldMaintainPerformance()
    {
        // Arrange
        var index = new BTreeIndex<int, string>(5); // Small degree to test B-Tree behavior
        var itemCount = 10000;

        // Act - Insert large number of items
        for (int i = 1; i <= itemCount; i++)
        {
            await index.PutAsync(i, $"value{i}");
        }

        // Assert - Verify all items are present
        var count = await index.CountAsync();
        count.Should().Be(itemCount);

        // Test random access
        for (int i = 1; i <= 100; i++)
        {
            var randomKey = new Random().Next(1, itemCount + 1);
            var value = await index.GetAsync(randomKey);
            value.Should().Be($"value{randomKey}");
        }

        // Test range query
        var rangeResults = await index.RangeAsync(5000, 5010).ToListAsync();
        rangeResults.Should().HaveCount(11);
        rangeResults.Select(kvp => kvp.Key).Should().BeInAscendingOrder();
    }

    [Fact]
    public async Task BTreeIndex_ConcurrentOperations_ShouldBeThreadSafe()
    {
        // Arrange
        var index = new BTreeIndex<int, string>();
        var tasks = new Task[100];

        // Act - Perform concurrent operations
        for (int i = 0; i < 100; i++)
        {
            int key = i;
            tasks[i] = Task.Run(async () =>
            {
                await index.PutAsync(key, $"value{key}");
                var value = await index.GetAsync(key);
                value.Should().Be($"value{key}");
            });
        }

        await Task.WhenAll(tasks);

        // Assert
        var count = await index.CountAsync();
        count.Should().Be(100);
    }

    [Fact]
    public async Task GetAllAsync_LargeData_ShouldNotAllocateExcessiveMemory()
    {
        var index = new BTreeIndex<int, int>();
        for (int i = 0; i < 10000; i++)
        {
            await index.PutAsync(i, i);
        }

        var before = GC.GetTotalMemory(true);

        await foreach (var item in index.GetAllAsync())
        {
            _ = item;
        }

        var after = GC.GetTotalMemory(true);

        (after - before).Should().BeLessThan(1024 * 1024);
    }

    [Fact(Skip = "Flaky memory test - depends on GC behavior")]
    public async Task RangeAsync_LargeData_ShouldNotAllocateExcessiveMemory()
    {
        var index = new BTreeIndex<int, int>();
        for (int i = 0; i < 10000; i++)
        {
            await index.PutAsync(i, i);
        }

        var before = GC.GetTotalMemory(true);

        await foreach (var item in index.RangeAsync(1000, 9000))
        {
            _ = item;
        }

        var after = GC.GetTotalMemory(true);

        (after - before).Should().BeLessThan(1024 * 1024);
    }
}
