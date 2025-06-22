using System;
using System.Linq;
using FluentAssertions;
using Kvs.Core.DataStructures;
using Xunit;

namespace Kvs.Core.UnitTests.DataStructures;

public class BTreeTests
{
    [Fact]
    public void Constructor_WithValidDegree_ShouldCreateEmptyTree()
    {
        // Arrange & Act
        var btree = new BTree<int, string>(5);

        // Assert
        btree.Count.Should().Be(0);
        btree.IsEmpty.Should().BeTrue();
    }

    [Theory]
    [InlineData(1)]
    [InlineData(2)]
    public void Constructor_WithInvalidDegree_ShouldThrowArgumentException(int degree)
    {
        // Arrange, Act & Assert
        Assert.Throws<ArgumentException>(() => new BTree<int, string>(degree));
    }

    [Fact]
    public void Insert_SingleItem_ShouldIncreaseCount()
    {
        // Arrange
        var btree = new BTree<int, string>();

        // Act
        var result = btree.Insert(1, "one");

        // Assert
        result.Should().BeTrue();
        btree.Count.Should().Be(1);
        btree.IsEmpty.Should().BeFalse();
    }

    [Fact]
    public void Insert_DuplicateKey_ShouldUpdateValue()
    {
        // Arrange
        var btree = new BTree<int, string>();
        btree.Insert(1, "one");

        // Act
        var result = btree.Insert(1, "ONE");

        // Assert
        result.Should().BeFalse();
        btree.Count.Should().Be(1);
        btree.Search(1).Should().Be("ONE");
    }

    [Fact]
    public void Insert_NullKey_ShouldThrowArgumentNullException()
    {
        // Arrange
        var btree = new BTree<string, string>();

        // Act & Assert
        Assert.Throws<ArgumentNullException>(() => btree.Insert(null!, "value"));
    }

    [Fact]
    public void Search_ExistingKey_ShouldReturnValue()
    {
        // Arrange
        var btree = new BTree<int, string>();
        btree.Insert(1, "one");
        btree.Insert(2, "two");
        btree.Insert(3, "three");

        // Act
        var result = btree.Search(2);

        // Assert
        result.Should().Be("two");
    }

    [Fact]
    public void Search_NonExistingKey_ShouldReturnDefault()
    {
        // Arrange
        var btree = new BTree<int, string>();
        btree.Insert(1, "one");

        // Act
        var result = btree.Search(999);

        // Assert
        result.Should().BeNull();
    }

    [Fact]
    public void Search_EmptyTree_ShouldReturnDefault()
    {
        // Arrange
        var btree = new BTree<int, string>();

        // Act
        var result = btree.Search(1);

        // Assert
        result.Should().BeNull();
    }

    [Fact]
    public void Search_NullKey_ShouldThrowArgumentNullException()
    {
        // Arrange
        var btree = new BTree<string, string>();

        // Act & Assert
        Assert.Throws<ArgumentNullException>(() => btree.Search(null!));
    }

    [Fact]
    public void Delete_ExistingKey_ShouldReturnTrueAndDecreaseCount()
    {
        // Arrange
        var btree = new BTree<int, string>();
        btree.Insert(1, "one");
        btree.Insert(2, "two");

        // Act
        var result = btree.Delete(1);

        // Assert
        result.Should().BeTrue();
        btree.Count.Should().Be(1);
        btree.Search(1).Should().BeNull();
        btree.Search(2).Should().Be("two");
    }

    [Fact]
    public void Delete_NonExistingKey_ShouldReturnFalse()
    {
        // Arrange
        var btree = new BTree<int, string>();
        btree.Insert(1, "one");

        // Act
        var result = btree.Delete(999);

        // Assert
        result.Should().BeFalse();
        btree.Count.Should().Be(1);
    }

    [Fact]
    public void Delete_EmptyTree_ShouldReturnFalse()
    {
        // Arrange
        var btree = new BTree<int, string>();

        // Act
        var result = btree.Delete(1);

        // Assert
        result.Should().BeFalse();
        btree.Count.Should().Be(0);
    }

    [Fact]
    public void Delete_NullKey_ShouldThrowArgumentNullException()
    {
        // Arrange
        var btree = new BTree<string, string>();

        // Act & Assert
        Assert.Throws<ArgumentNullException>(() => btree.Delete(null!));
    }

    [Fact]
    public void ContainsKey_ExistingKey_ShouldReturnTrue()
    {
        // Arrange
        var btree = new BTree<int, string>();
        btree.Insert(1, "one");

        // Act
        var result = btree.ContainsKey(1);

        // Assert
        result.Should().BeTrue();
    }

    [Fact]
    public void ContainsKey_NonExistingKey_ShouldReturnFalse()
    {
        // Arrange
        var btree = new BTree<int, string>();
        btree.Insert(1, "one");

        // Act
        var result = btree.ContainsKey(999);

        // Assert
        result.Should().BeFalse();
    }

    [Fact]
    public void GetAll_WithMultipleItems_ShouldReturnItemsInSortedOrder()
    {
        // Arrange
        var btree = new BTree<int, string>();
        var items = new[] { (3, "three"), (1, "one"), (4, "four"), (2, "two") };
        foreach (var (key, value) in items)
        {
            btree.Insert(key, value);
        }

        // Act
        var result = btree.GetAll().ToList();

        // Assert
        result.Should().HaveCount(4);
        result.Select(kvp => kvp.Key).Should().BeInAscendingOrder();
        result.Should().ContainInOrder(
            new System.Collections.Generic.KeyValuePair<int, string>(1, "one"),
            new System.Collections.Generic.KeyValuePair<int, string>(2, "two"),
            new System.Collections.Generic.KeyValuePair<int, string>(3, "three"),
            new System.Collections.Generic.KeyValuePair<int, string>(4, "four"));
    }

    [Fact]
    public void GetAll_EmptyTree_ShouldReturnEmptySequence()
    {
        // Arrange
        var btree = new BTree<int, string>();

        // Act
        var result = btree.GetAll().ToList();

        // Assert
        result.Should().BeEmpty();
    }

    [Fact]
    public void Range_ValidRange_ShouldReturnItemsInRange()
    {
        // Arrange
        var btree = new BTree<int, string>();
        for (int i = 1; i <= 10; i++)
        {
            btree.Insert(i, $"value{i}");
        }

        // Act
        var result = btree.Range(3, 7).ToList();

        // Assert
        result.Should().HaveCount(5);
        result.Select(kvp => kvp.Key).Should().BeInAscendingOrder();
        result[0].Key.Should().Be(3);
        result[result.Count - 1].Key.Should().Be(7);
    }

    [Fact]
    public void Range_SingleItemRange_ShouldReturnSingleItem()
    {
        // Arrange
        var btree = new BTree<int, string>();
        btree.Insert(5, "five");
        btree.Insert(3, "three");
        btree.Insert(7, "seven");

        // Act
        var result = btree.Range(5, 5).ToList();

        // Assert
        result.Should().HaveCount(1);
        result[0].Key.Should().Be(5);
        result[0].Value.Should().Be("five");
    }

    [Fact]
    public void Range_InvalidRange_ShouldThrowArgumentException()
    {
        // Arrange
        var btree = new BTree<int, string>();

        // Act & Assert
        Assert.Throws<ArgumentException>(() => btree.Range(10, 5).ToList());
    }

    [Fact]
    public void Range_NullKeys_ShouldThrowArgumentNullException()
    {
        // Arrange
        var btree = new BTree<string, string>();

        // Act & Assert
        Assert.Throws<ArgumentNullException>(() => btree.Range(null!, "end").ToList());
        Assert.Throws<ArgumentNullException>(() => btree.Range("start", null!).ToList());
    }

    [Fact]
    public void GetMinKey_WithItems_ShouldReturnSmallestKey()
    {
        // Arrange
        var btree = new BTree<int, string>();
        btree.Insert(5, "five");
        btree.Insert(2, "two");
        btree.Insert(8, "eight");
        btree.Insert(1, "one");

        // Act
        var result = btree.GetMinKey();

        // Assert
        result.Should().Be(1);
    }

    [Fact]
    public void GetMinKey_EmptyTree_ShouldReturnDefault()
    {
        // Arrange
        var btree = new BTree<int, string>();

        // Act
        var result = btree.GetMinKey();

        // Assert
        result.Should().Be(0); // default(int)
    }

    [Fact]
    public void GetMaxKey_WithItems_ShouldReturnLargestKey()
    {
        // Arrange
        var btree = new BTree<int, string>();
        btree.Insert(5, "five");
        btree.Insert(2, "two");
        btree.Insert(8, "eight");
        btree.Insert(10, "ten");

        // Act
        var result = btree.GetMaxKey();

        // Assert
        result.Should().Be(10);
    }

    [Fact]
    public void GetMaxKey_EmptyTree_ShouldReturnDefault()
    {
        // Arrange
        var btree = new BTree<int, string>();

        // Act
        var result = btree.GetMaxKey();

        // Assert
        result.Should().Be(0); // default(int)
    }

    [Fact]
    public void Clear_WithItems_ShouldEmptyTree()
    {
        // Arrange
        var btree = new BTree<int, string>();
        btree.Insert(1, "one");
        btree.Insert(2, "two");
        btree.Insert(3, "three");

        // Act
        btree.Clear();

        // Assert
        btree.Count.Should().Be(0);
        btree.IsEmpty.Should().BeTrue();
        btree.Search(1).Should().BeNull();
        btree.Search(2).Should().BeNull();
        btree.Search(3).Should().BeNull();
    }

    [Fact]
    public void BTree_LargeDataSet_ShouldMaintainCorrectness()
    {
        // Arrange
        var btree = new BTree<int, string>(5); // Small degree to force splitting
        var itemCount = 1000;

        // Act - Insert items
        for (int i = 1; i <= itemCount; i++)
        {
            btree.Insert(i, $"value{i}");
        }

        // Assert - All items should be present and sorted
        btree.Count.Should().Be(itemCount);

        var allItems = btree.GetAll().ToList();
        allItems.Should().HaveCount(itemCount);
        allItems.Select(kvp => kvp.Key).Should().BeInAscendingOrder();

        // Verify random access
        for (int i = 1; i <= itemCount; i++)
        {
            btree.Search(i).Should().Be($"value{i}");
            btree.ContainsKey(i).Should().BeTrue();
        }

        // Act - Delete half the items
        for (int i = 1; i <= itemCount / 2; i++)
        {
            btree.Delete(i).Should().BeTrue();
        }

        // Assert - Remaining items should still be correct
        btree.Count.Should().Be(itemCount / 2);

        for (int i = 1; i <= itemCount / 2; i++)
        {
            btree.ContainsKey(i).Should().BeFalse();
        }

        for (int i = (itemCount / 2) + 1; i <= itemCount; i++)
        {
            btree.Search(i).Should().Be($"value{i}");
        }
    }

    [Fact]
    public void BTree_RandomOperations_ShouldMaintainConsistency()
    {
        // Arrange
        var btree = new BTree<int, string>(4); // Small degree
        var random = new Random(42); // Fixed seed for reproducibility
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
                    btree.Insert(key, value);
                    expectedItems[key] = value;
                    break;

                case 1: // Delete
                    var deleted = btree.Delete(key);
                    var expectedDeleted = expectedItems.Remove(key);
                    deleted.Should().Be(expectedDeleted);
                    break;

                case 2: // Search
                    var found = btree.Search(key);
                    var expectedFound = expectedItems.TryGetValue(key, out var expectedValue) ? expectedValue : null;
                    found.Should().Be(expectedFound);
                    break;
            }
        }

        // Assert - Final state should match expected
        btree.Count.Should().Be(expectedItems.Count);

        foreach (var (key, expectedValue) in expectedItems)
        {
            btree.Search(key).Should().Be(expectedValue);
            btree.ContainsKey(key).Should().BeTrue();
        }

        var allItems = btree.GetAll().ToList();
        allItems.Should().HaveCount(expectedItems.Count);
        allItems.Select(kvp => kvp.Key).Should().BeInAscendingOrder();
    }
}
