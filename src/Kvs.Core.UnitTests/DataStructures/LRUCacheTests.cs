using System;
using System.Linq;
using FluentAssertions;
using Kvs.Core.DataStructures;
using Xunit;

namespace Kvs.Core.UnitTests.DataStructures;

public class LRUCacheTests
{
    [Fact]
    public void Constructor_WithValidCapacity_ShouldCreateEmptyCache()
    {
        // Arrange & Act
        var cache = new LRUCache<int, string>(5);

        // Assert
        cache.Capacity.Should().Be(5);
        cache.Count.Should().Be(0);
        cache.IsEmpty.Should().BeTrue();
        cache.IsFull.Should().BeFalse();
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    public void Constructor_WithInvalidCapacity_ShouldThrowArgumentException(int capacity)
    {
        // Arrange, Act & Assert
        Assert.Throws<ArgumentException>(() => new LRUCache<int, string>(capacity));
    }

    [Fact]
    public void Put_NewItem_ShouldAddToCache()
    {
        // Arrange
        var cache = new LRUCache<int, string>(3);

        // Act
        var result = cache.Put(1, "one");

        // Assert
        result.Should().BeTrue();
        cache.Count.Should().Be(1);
        cache.IsEmpty.Should().BeFalse();
    }

    [Fact]
    public void Put_ExistingItem_ShouldUpdateValue()
    {
        // Arrange
        var cache = new LRUCache<int, string>(3);
        cache.Put(1, "one");

        // Act
        var result = cache.Put(1, "ONE");

        // Assert
        result.Should().BeFalse();
        cache.Count.Should().Be(1);
        cache.Get(1).Should().Be("ONE");
    }

    [Fact]
    public void Put_NullKey_ShouldThrowArgumentNullException()
    {
        // Arrange
        var cache = new LRUCache<string, string>(3);

        // Act & Assert
        Assert.Throws<ArgumentNullException>(() => cache.Put(null!, "value"));
    }

    [Fact]
    public void Put_ExceedsCapacity_ShouldEvictLRU()
    {
        // Arrange
        var cache = new LRUCache<int, string>(2);
        cache.Put(1, "one");
        cache.Put(2, "two");

        // Act
        cache.Put(3, "three");

        // Assert
        cache.Count.Should().Be(2);
        cache.ContainsKey(1).Should().BeFalse(); // Should be evicted
        cache.ContainsKey(2).Should().BeTrue();
        cache.ContainsKey(3).Should().BeTrue();
    }

    [Fact]
    public void Get_ExistingItem_ShouldReturnValueAndMoveToFront()
    {
        // Arrange
        var cache = new LRUCache<int, string>(3);
        cache.Put(1, "one");
        cache.Put(2, "two");
        cache.Put(3, "three");

        // Act
        var result = cache.Get(1);

        // Assert
        result.Should().Be("one");
        cache.GetMostRecentlyUsedKey().Should().Be(1);
    }

    [Fact]
    public void Get_NonExistingItem_ShouldReturnDefault()
    {
        // Arrange
        var cache = new LRUCache<int, string>(3);
        cache.Put(1, "one");

        // Act
        var result = cache.Get(999);

        // Assert
        result.Should().BeNull();
    }

    [Fact]
    public void Get_NullKey_ShouldThrowArgumentNullException()
    {
        // Arrange
        var cache = new LRUCache<string, string>(3);

        // Act & Assert
        Assert.Throws<ArgumentNullException>(() => cache.Get(null!));
    }

    [Fact]
    public void TryGet_ExistingItem_ShouldReturnTrueAndValue()
    {
        // Arrange
        var cache = new LRUCache<int, string>(3);
        cache.Put(1, "one");

        // Act
        var result = cache.TryGet(1, out var value);

        // Assert
        result.Should().BeTrue();
        value.Should().Be("one");
    }

    [Fact]
    public void TryGet_NonExistingItem_ShouldReturnFalseAndDefault()
    {
        // Arrange
        var cache = new LRUCache<int, string>(3);

        // Act
        var result = cache.TryGet(999, out var value);

        // Assert
        result.Should().BeFalse();
        value.Should().BeNull();
    }

    [Fact]
    public void Remove_ExistingItem_ShouldReturnTrueAndRemove()
    {
        // Arrange
        var cache = new LRUCache<int, string>(3);
        cache.Put(1, "one");
        cache.Put(2, "two");

        // Act
        var result = cache.Remove(1);

        // Assert
        result.Should().BeTrue();
        cache.Count.Should().Be(1);
        cache.ContainsKey(1).Should().BeFalse();
        cache.ContainsKey(2).Should().BeTrue();
    }

    [Fact]
    public void Remove_NonExistingItem_ShouldReturnFalse()
    {
        // Arrange
        var cache = new LRUCache<int, string>(3);
        cache.Put(1, "one");

        // Act
        var result = cache.Remove(999);

        // Assert
        result.Should().BeFalse();
        cache.Count.Should().Be(1);
    }

    [Fact]
    public void ContainsKey_ExistingItem_ShouldReturnTrue()
    {
        // Arrange
        var cache = new LRUCache<int, string>(3);
        cache.Put(1, "one");

        // Act
        var result = cache.ContainsKey(1);

        // Assert
        result.Should().BeTrue();
    }

    [Fact]
    public void ContainsKey_NonExistingItem_ShouldReturnFalse()
    {
        // Arrange
        var cache = new LRUCache<int, string>(3);

        // Act
        var result = cache.ContainsKey(999);

        // Assert
        result.Should().BeFalse();
    }

    [Fact]
    public void Clear_WithItems_ShouldEmptyCache()
    {
        // Arrange
        var cache = new LRUCache<int, string>(3);
        cache.Put(1, "one");
        cache.Put(2, "two");

        // Act
        cache.Clear();

        // Assert
        cache.Count.Should().Be(0);
        cache.IsEmpty.Should().BeTrue();
        cache.ContainsKey(1).Should().BeFalse();
        cache.ContainsKey(2).Should().BeFalse();
    }

    [Fact]
    public void GetKeys_WithItems_ShouldReturnKeysInLRUOrder()
    {
        // Arrange
        var cache = new LRUCache<int, string>(3);
        cache.Put(1, "one");
        cache.Put(2, "two");
        cache.Put(3, "three");
        cache.Get(1); // Move 1 to front

        // Act
        var keys = cache.GetKeys().ToList();

        // Assert
        keys.Should().HaveCount(3);
        keys[0].Should().Be(1); // Most recently used
        keys[2].Should().Be(2); // Least recently used
    }

    [Fact]
    public void GetItems_WithItems_ShouldReturnItemsInLRUOrder()
    {
        // Arrange
        var cache = new LRUCache<int, string>(3);
        cache.Put(1, "one");
        cache.Put(2, "two");
        cache.Get(1); // Move 1 to front

        // Act
        var items = cache.GetItems().ToList();

        // Assert
        items.Should().HaveCount(2);
        items[0].Key.Should().Be(1); // Most recently used
        items[0].Value.Should().Be("one");
        items[1].Key.Should().Be(2); // Least recently used
        items[1].Value.Should().Be("two");
    }

    [Fact]
    public void GetMostRecentlyUsedKey_WithItems_ShouldReturnMRUKey()
    {
        // Arrange
        var cache = new LRUCache<int, string>(3);
        cache.Put(1, "one");
        cache.Put(2, "two");
        cache.Put(3, "three");

        // Act
        var result = cache.GetMostRecentlyUsedKey();

        // Assert
        result.Should().Be(3); // Last inserted item
    }

    [Fact]
    public void GetMostRecentlyUsedKey_EmptyCache_ShouldReturnDefault()
    {
        // Arrange
        var cache = new LRUCache<int, string>(3);

        // Act
        var result = cache.GetMostRecentlyUsedKey();

        // Assert
        result.Should().Be(0); // default(int)
    }

    [Fact]
    public void GetLeastRecentlyUsedKey_WithItems_ShouldReturnLRUKey()
    {
        // Arrange
        var cache = new LRUCache<int, string>(3);
        cache.Put(1, "one");
        cache.Put(2, "two");
        cache.Put(3, "three");

        // Act
        var result = cache.GetLeastRecentlyUsedKey();

        // Assert
        result.Should().Be(1); // First inserted item
    }

    [Fact]
    public void GetLeastRecentlyUsedKey_EmptyCache_ShouldReturnDefault()
    {
        // Arrange
        var cache = new LRUCache<int, string>(3);

        // Act
        var result = cache.GetLeastRecentlyUsedKey();

        // Assert
        result.Should().Be(0); // default(int)
    }

    [Fact]
    public void IsFull_AtCapacity_ShouldReturnTrue()
    {
        // Arrange
        var cache = new LRUCache<int, string>(2);
        cache.Put(1, "one");
        cache.Put(2, "two");

        // Act & Assert
        cache.IsFull.Should().BeTrue();
    }

    [Fact]
    public void IsFull_BelowCapacity_ShouldReturnFalse()
    {
        // Arrange
        var cache = new LRUCache<int, string>(3);
        cache.Put(1, "one");

        // Act & Assert
        cache.IsFull.Should().BeFalse();
    }

    [Fact]
    public void GetStatistics_WithItems_ShouldReturnCorrectStatistics()
    {
        // Arrange
        var cache = new LRUCache<int, string>(5);
        cache.Put(1, "one");
        cache.Put(2, "two");

        // Act
        var stats = cache.GetStatistics();

        // Assert
        stats["Capacity"].Should().Be(5);
        stats["Count"].Should().Be(2);
        stats["IsEmpty"].Should().Be(false);
        stats["IsFull"].Should().Be(false);
        stats["UtilizationPercentage"].Should().Be(40.0);
    }

    [Fact]
    public void LRUCache_EvictionOrder_ShouldBeCorrect()
    {
        // Arrange
        var cache = new LRUCache<int, string>(3);
        cache.Put(1, "one");
        cache.Put(2, "two");
        cache.Put(3, "three");

        // Access items in specific order to establish LRU ordering
        cache.Get(1); // 1 becomes MRU
        cache.Get(2); // 2 becomes MRU

        // Act - Add new item that should evict LRU (3)
        cache.Put(4, "four");

        // Assert
        cache.Count.Should().Be(3);
        cache.ContainsKey(1).Should().BeTrue();
        cache.ContainsKey(2).Should().BeTrue();
        cache.ContainsKey(3).Should().BeFalse(); // Should be evicted
        cache.ContainsKey(4).Should().BeTrue();
    }

    [Fact]
    public void LRUCache_AccessPattern_ShouldUpdateLRUOrder()
    {
        // Arrange
        var cache = new LRUCache<int, string>(3);
        cache.Put(1, "one");
        cache.Put(2, "two");
        cache.Put(3, "three");

        // Act - Access items to change LRU order
        cache.Get(1); // 1 becomes MRU
        cache.Put(2, "TWO"); // 2 becomes MRU (update operation)

        // Assert - Check the ordering
        var keys = cache.GetKeys().ToList();
        keys[0].Should().Be(2); // Most recently used (updated)
        keys[1].Should().Be(1); // Second most recently used (accessed)
        keys[2].Should().Be(3); // Least recently used
    }

    [Fact]
    public void Dispose_ShouldClearCache()
    {
        // Arrange
        var cache = new LRUCache<int, string>(3);
        cache.Put(1, "one");
        cache.Put(2, "two");

        // Act
        cache.Dispose();

        // Assert
        cache.Count.Should().Be(0);
        cache.IsEmpty.Should().BeTrue();
    }

    [Fact]
    public void Dispose_MultipleCalls_ShouldNotThrow()
    {
        // Arrange
        var cache = new LRUCache<int, string>(3);

        // Act & Assert - Multiple dispose calls should not throw
        try
        {
            cache.Dispose();
#pragma warning disable S3966 // Objects should not be disposed more than once
            cache.Dispose(); // Should not throw - testing multiple dispose is safe
#pragma warning restore S3966 // Objects should not be disposed more than once
        }
        catch (Exception ex)
        {
            Assert.Fail($"Unexpected exception: {ex.Message}");
        }
    }

    [Fact]
    public void Operations_AfterDispose_ShouldThrowObjectDisposedException()
    {
        // Arrange
        var cache = new LRUCache<int, string>(3);
        cache.Dispose();

        // Act & Assert
        Assert.Throws<ObjectDisposedException>(() => cache.Put(1, "one"));
        Assert.Throws<ObjectDisposedException>(() => cache.Get(1));
        Assert.Throws<ObjectDisposedException>(() => cache.Remove(1));
        Assert.Throws<ObjectDisposedException>(() => cache.ContainsKey(1));
        Assert.Throws<ObjectDisposedException>(() => cache.Clear());
    }
}
