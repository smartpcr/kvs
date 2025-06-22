using System;
using System.Linq;
using FluentAssertions;
using Kvs.Core.DataStructures;
using Xunit;

namespace Kvs.Core.UnitTests.DataStructures;

public class SkipListTests
{
    [Fact]
    public void Constructor_ShouldCreateEmptySkipList()
    {
        // Arrange & Act
        var skipList = new SkipList<int, string>();

        // Assert
        skipList.Count.Should().Be(0);
        skipList.IsEmpty.Should().BeTrue();
    }

    [Fact]
    public void Insert_NewKey_ShouldAddToSkipList()
    {
        // Arrange
        var skipList = new SkipList<int, string>();

        // Act
        var result = skipList.Insert(1, "one");

        // Assert
        result.Should().BeTrue();
        skipList.Count.Should().Be(1);
        skipList.IsEmpty.Should().BeFalse();
    }

    [Fact]
    public void Insert_ExistingKey_ShouldUpdateValue()
    {
        // Arrange
        var skipList = new SkipList<int, string>();
        skipList.Insert(1, "one");

        // Act
        var result = skipList.Insert(1, "ONE");

        // Assert
        result.Should().BeFalse();
        skipList.Count.Should().Be(1);
        skipList.Search(1).Should().Be("ONE");
    }

    [Fact]
    public void Insert_NullKey_ShouldThrowArgumentNullException()
    {
        // Arrange
        var skipList = new SkipList<string, string>();

        // Act & Assert
        Assert.Throws<ArgumentNullException>(() => skipList.Insert(null!, "value"));
    }

    [Fact]
    public void Insert_MultipleItems_ShouldMaintainOrder()
    {
        // Arrange
        var skipList = new SkipList<int, string>();
        var items = new[] { (3, "three"), (1, "one"), (4, "four"), (2, "two") };

        // Act
        foreach (var (key, value) in items)
        {
            skipList.Insert(key, value);
        }

        // Assert
        var orderedItems = skipList.ToList();
        orderedItems.Should().HaveCount(4);
        orderedItems.Select(kvp => kvp.Key).Should().BeInAscendingOrder();
    }

    [Fact]
    public void Search_ExistingKey_ShouldReturnValue()
    {
        // Arrange
        var skipList = new SkipList<int, string>();
        skipList.Insert(1, "one");
        skipList.Insert(2, "two");
        skipList.Insert(3, "three");

        // Act
        var result = skipList.Search(2);

        // Assert
        result.Should().Be("two");
    }

    [Fact]
    public void Search_NonExistingKey_ShouldReturnDefault()
    {
        // Arrange
        var skipList = new SkipList<int, string>();
        skipList.Insert(1, "one");

        // Act
        var result = skipList.Search(999);

        // Assert
        result.Should().BeNull();
    }

    [Fact]
    public void Search_NullKey_ShouldThrowArgumentNullException()
    {
        // Arrange
        var skipList = new SkipList<string, string>();

        // Act & Assert
        Assert.Throws<ArgumentNullException>(() => skipList.Search(null!));
    }

    [Fact]
    public void TryGetValue_ExistingKey_ShouldReturnTrueAndValue()
    {
        // Arrange
        var skipList = new SkipList<int, string>();
        skipList.Insert(1, "one");

        // Act
        var result = skipList.TryGetValue(1, out var value);

        // Assert
        result.Should().BeTrue();
        value.Should().Be("one");
    }

    [Fact]
    public void TryGetValue_NonExistingKey_ShouldReturnFalse()
    {
        // Arrange
        var skipList = new SkipList<int, string>();

        // Act
        var result = skipList.TryGetValue(999, out var value);

        // Assert
        result.Should().BeFalse();
        value.Should().BeNull();
    }

    [Fact]
    public void Delete_ExistingKey_ShouldReturnTrueAndRemove()
    {
        // Arrange
        var skipList = new SkipList<int, string>();
        skipList.Insert(1, "one");
        skipList.Insert(2, "two");

        // Act
        var result = skipList.Delete(1);

        // Assert
        result.Should().BeTrue();
        skipList.Count.Should().Be(1);
        skipList.ContainsKey(1).Should().BeFalse();
        skipList.ContainsKey(2).Should().BeTrue();
    }

    [Fact]
    public void Delete_NonExistingKey_ShouldReturnFalse()
    {
        // Arrange
        var skipList = new SkipList<int, string>();
        skipList.Insert(1, "one");

        // Act
        var result = skipList.Delete(999);

        // Assert
        result.Should().BeFalse();
        skipList.Count.Should().Be(1);
    }

    [Fact]
    public void Delete_NullKey_ShouldThrowArgumentNullException()
    {
        // Arrange
        var skipList = new SkipList<string, string>();

        // Act & Assert
        Assert.Throws<ArgumentNullException>(() => skipList.Delete(null!));
    }

    [Fact]
    public void ContainsKey_ExistingKey_ShouldReturnTrue()
    {
        // Arrange
        var skipList = new SkipList<int, string>();
        skipList.Insert(1, "one");

        // Act
        var result = skipList.ContainsKey(1);

        // Assert
        result.Should().BeTrue();
    }

    [Fact]
    public void ContainsKey_NonExistingKey_ShouldReturnFalse()
    {
        // Arrange
        var skipList = new SkipList<int, string>();

        // Act
        var result = skipList.ContainsKey(999);

        // Assert
        result.Should().BeFalse();
    }

    [Fact]
    public void Range_ValidRange_ShouldReturnItemsInRange()
    {
        // Arrange
        var skipList = new SkipList<int, string>();
        for (int i = 1; i <= 10; i++)
        {
            skipList.Insert(i, $"value{i}");
        }

        // Act
        var result = skipList.Range(3, 7).ToList();

        // Assert
        result.Should().HaveCount(5);
        result.Select(kvp => kvp.Key).Should().BeInAscendingOrder();
        result[0].Key.Should().Be(3);
        result[result.Count - 1].Key.Should().Be(7);
    }

    [Fact]
    public void Range_EmptyRange_ShouldReturnEmpty()
    {
        // Arrange
        var skipList = new SkipList<int, string>();
        skipList.Insert(1, "one");
        skipList.Insert(5, "five");

        // Act
        var result = skipList.Range(2, 4).ToList();

        // Assert
        result.Should().BeEmpty();
    }

    [Fact]
    public void Range_InvalidRange_ShouldThrowArgumentException()
    {
        // Arrange
        var skipList = new SkipList<int, string>();

        // Act & Assert
        Assert.Throws<ArgumentException>(() => skipList.Range(10, 5).ToList());
    }

    [Fact]
    public void Range_NullKeys_ShouldThrowArgumentNullException()
    {
        // Arrange
        var skipList = new SkipList<string, string>();

        // Act & Assert
        Assert.Throws<ArgumentNullException>(() => skipList.Range(null!, "end").ToList());
        Assert.Throws<ArgumentNullException>(() => skipList.Range("start", null!).ToList());
    }

    [Fact]
    public void GetMinKey_WithItems_ShouldReturnSmallestKey()
    {
        // Arrange
        var skipList = new SkipList<int, string>();
        skipList.Insert(5, "five");
        skipList.Insert(2, "two");
        skipList.Insert(8, "eight");

        // Act
        var result = skipList.GetMinKey();

        // Assert
        result.Should().Be(2);
    }

    [Fact]
    public void GetMinKey_EmptyList_ShouldReturnDefault()
    {
        // Arrange
        var skipList = new SkipList<int, string>();

        // Act
        var result = skipList.GetMinKey();

        // Assert
        result.Should().Be(0);
    }

    [Fact]
    public void GetMaxKey_WithItems_ShouldReturnLargestKey()
    {
        // Arrange
        var skipList = new SkipList<int, string>();
        skipList.Insert(5, "five");
        skipList.Insert(2, "two");
        skipList.Insert(8, "eight");

        // Act
        var result = skipList.GetMaxKey();

        // Assert
        result.Should().Be(8);
    }

    [Fact]
    public void GetMaxKey_EmptyList_ShouldReturnDefault()
    {
        // Arrange
        var skipList = new SkipList<int, string>();

        // Act
        var result = skipList.GetMaxKey();

        // Assert
        result.Should().Be(0);
    }

    [Fact]
    public void Clear_WithItems_ShouldEmptyList()
    {
        // Arrange
        var skipList = new SkipList<int, string>();
        skipList.Insert(1, "one");
        skipList.Insert(2, "two");

        // Act
        skipList.Clear();

        // Assert
        skipList.Count.Should().Be(0);
        skipList.IsEmpty.Should().BeTrue();
        skipList.ContainsKey(1).Should().BeFalse();
        skipList.ContainsKey(2).Should().BeFalse();
    }

    [Fact]
    public void GetEnumerator_ShouldReturnItemsInOrder()
    {
        // Arrange
        var skipList = new SkipList<int, string>();
        var items = new[] { (3, "three"), (1, "one"), (4, "four"), (2, "two") };
        foreach (var (key, value) in items)
        {
            skipList.Insert(key, value);
        }

        // Act
        var result = skipList.ToList();

        // Assert
        result.Should().HaveCount(4);
        result.Select(kvp => kvp.Key).Should().BeInAscendingOrder();
        result[0].Should().Be(new System.Collections.Generic.KeyValuePair<int, string>(1, "one"));
        result[1].Should().Be(new System.Collections.Generic.KeyValuePair<int, string>(2, "two"));
        result[2].Should().Be(new System.Collections.Generic.KeyValuePair<int, string>(3, "three"));
        result[3].Should().Be(new System.Collections.Generic.KeyValuePair<int, string>(4, "four"));
    }

    [Fact]
    public void SkipList_LargeDataSet_ShouldMaintainCorrectness()
    {
        // Arrange
        var skipList = new SkipList<int, string>();
        var itemCount = 1000;

        // Act - Insert items
        for (int i = 1; i <= itemCount; i++)
        {
            skipList.Insert(i, $"value{i}");
        }

        // Assert - All items should be present
        skipList.Count.Should().Be(itemCount);

        // Verify random access
        for (int i = 1; i <= itemCount; i++)
        {
            skipList.Search(i).Should().Be($"value{i}");
            skipList.ContainsKey(i).Should().BeTrue();
        }

        // Act - Delete half the items
        for (int i = 1; i <= itemCount / 2; i++)
        {
            skipList.Delete(i).Should().BeTrue();
        }

        // Assert - Remaining items should still be correct
        skipList.Count.Should().Be(itemCount / 2);

        for (int i = 1; i <= itemCount / 2; i++)
        {
            skipList.ContainsKey(i).Should().BeFalse();
        }

        for (int i = (itemCount / 2) + 1; i <= itemCount; i++)
        {
            skipList.Search(i).Should().Be($"value{i}");
        }
    }

    [Fact]
    public void SkipList_RandomOperations_ShouldMaintainConsistency()
    {
        // Arrange
        var skipList = new SkipList<int, string>();
        var random = new Random(42);
        var expectedItems = new System.Collections.Generic.Dictionary<int, string>();

        // Act - Perform random operations
        for (int i = 0; i < 500; i++)
        {
            var operation = random.Next(3); // 0: insert, 1: delete, 2: search
            var key = random.Next(1, 100);

            switch (operation)
            {
                case 0: // Insert
                    var value = $"value{key}";
                    var inserted = skipList.Insert(key, value);
                    expectedItems[key] = value; // Insert or update

                    break;

                case 1: // Delete
                    var deleted = skipList.Delete(key);
                    if (deleted)
                    {
                        expectedItems.Remove(key);
                    }

                    break;

                case 2: // Search
                    var found = skipList.Search(key);
                    var expectedFound = expectedItems.TryGetValue(key, out var expectedValue) ? expectedValue : null;
                    found.Should().Be(expectedFound);
                    break;
            }
        }

        // Assert - Final state should match expected
        skipList.Count.Should().Be(expectedItems.Count);

        foreach (var (key, expectedValue) in expectedItems)
        {
            skipList.Search(key).Should().Be(expectedValue);
            skipList.ContainsKey(key).Should().BeTrue();
        }

        var allItems = skipList.ToList();
        allItems.Should().HaveCount(expectedItems.Count);
        allItems.Select(kvp => kvp.Key).Should().BeInAscendingOrder();
    }

    [Fact]
    public async Task SkipList_ConcurrentReads_ShouldBeThreadSafe()
    {
        // Arrange
        var skipList = new SkipList<int, string>();
        for (int i = 0; i < 100; i++)
        {
            skipList.Insert(i, $"value{i}");
        }

        // Act - Perform concurrent reads
        var tasks = new System.Threading.Tasks.Task[10];
        for (int t = 0; t < 10; t++)
        {
            tasks[t] = System.Threading.Tasks.Task.Run(() =>
            {
                for (int i = 0; i < 100; i++)
                {
                    var value = skipList.Search(i);
                    value.Should().Be($"value{i}");
                }
            });
        }

        // Assert
        await System.Threading.Tasks.Task.WhenAll(tasks);
        skipList.Count.Should().Be(100);
    }

    [Fact]
    public void Dispose_ShouldCompleteWithoutError()
    {
        // Arrange
        var skipList = new SkipList<int, string>();
        skipList.Insert(1, "one");

        // Act & Assert - Should not throw
        skipList.Dispose();
        Assert.True(true); // Test passed if no exception
    }

    [Fact]
    public void Operations_AfterDispose_ShouldThrowObjectDisposedException()
    {
        // Arrange
        var skipList = new SkipList<int, string>();
        skipList.Dispose();

        // Act & Assert
        Assert.Throws<ObjectDisposedException>(() => skipList.Insert(1, "one"));
        Assert.Throws<ObjectDisposedException>(() => skipList.Search(1));
        Assert.Throws<ObjectDisposedException>(() => skipList.TryGetValue(1, out _));
        Assert.Throws<ObjectDisposedException>(() => skipList.Delete(1));
        Assert.Throws<ObjectDisposedException>(() => skipList.ContainsKey(1));
        Assert.Throws<ObjectDisposedException>(() => skipList.Range(1, 2).ToList());
        Assert.Throws<ObjectDisposedException>(() => skipList.GetMinKey());
        Assert.Throws<ObjectDisposedException>(() => skipList.GetMaxKey());
        Assert.Throws<ObjectDisposedException>(() => skipList.Clear());
        Assert.Throws<ObjectDisposedException>(() =>
        {
            foreach (var kvp in skipList)
            {
                _ = kvp;
            }
        });
    }
}