using System;
using FluentAssertions;
using Kvs.Core.DataStructures;
using Xunit;

namespace Kvs.Core.UnitTests.DataStructures;

public class NodeTests
{
    [Fact]
    public void Constructor_WithValidParameters_ShouldCreateNode()
    {
        // Arrange & Act
        var node = new Node<int, string>(5, true);

        // Assert
        node.IsLeaf.Should().BeTrue();
        node.KeyCount.Should().Be(0);
        node.IsFull.Should().BeFalse();
        node.IsMinimal.Should().BeTrue();
        node.PageId.Should().Be(-1);
        node.IsDirty.Should().BeFalse();
    }

    [Fact]
    public void InsertAt_ValidIndex_ShouldInsertKeyValue()
    {
        // Arrange
        var node = new Node<int, string>(5, true);

        // Act
        node.InsertAt(0, 1, "one");

        // Assert
        node.KeyCount.Should().Be(1);
        node.Keys[0].Should().Be(1);
        node.Values[0].Should().Be("one");
        node.IsDirty.Should().BeTrue();
    }

    [Fact]
    public void SearchKey_ExistingKey_ShouldReturnCorrectIndex()
    {
        // Arrange
        var node = new Node<int, string>(5, true);
        node.InsertAt(0, 1, "one");
        node.InsertAt(1, 3, "three");
        node.InsertAt(2, 5, "five");

        // Act
        var result = node.SearchKey(3);

        // Assert
        result.Should().Be(1);
    }

    [Fact]
    public void SearchKey_NonExistingKey_ShouldReturnNegativeIndex()
    {
        // Arrange
        var node = new Node<int, string>(5, true);
        node.InsertAt(0, 1, "one");
        node.InsertAt(1, 5, "five");

        // Act
        var result = node.SearchKey(3);

        // Assert
        result.Should().BeLessThan(0);
        var insertIndex = ~result;
        insertIndex.Should().Be(1); // Should be inserted at index 1
    }

    [Fact]
    public void GetValue_ExistingKey_ShouldReturnValue()
    {
        // Arrange
        var node = new Node<int, string>(5, true);
        node.InsertAt(0, 1, "one");
        node.InsertAt(1, 2, "two");

        // Act
        var result = node.GetValue(2);

        // Assert
        result.Should().Be("two");
    }

    [Fact]
    public void GetValue_NonExistingKey_ShouldReturnDefault()
    {
        // Arrange
        var node = new Node<int, string>(5, true);
        node.InsertAt(0, 1, "one");

        // Act
        var result = node.GetValue(999);

        // Assert
        result.Should().BeNull();
    }

    [Fact]
    public void RemoveAt_ValidIndex_ShouldRemoveKeyValue()
    {
        // Arrange
        var node = new Node<int, string>(5, true);
        node.InsertAt(0, 1, "one");
        node.InsertAt(1, 2, "two");
        node.InsertAt(2, 3, "three");

        // Act
        var (key, value) = node.RemoveAt(1);

        // Assert
        key.Should().Be(2);
        value.Should().Be("two");
        node.KeyCount.Should().Be(2);
        node.Keys[0].Should().Be(1);
        node.Keys[1].Should().Be(3);
        node.IsDirty.Should().BeTrue();
    }

    [Fact]
    public void UpdateValueAt_ValidIndex_ShouldUpdateValue()
    {
        // Arrange
        var node = new Node<int, string>(5, true);
        node.InsertAt(0, 1, "one");

        // Act
        node.UpdateValueAt(0, "ONE");

        // Assert
        node.Values[0].Should().Be("ONE");
        node.IsDirty.Should().BeTrue();
    }

    [Fact]
    public void Split_FullNode_ShouldSplitCorrectly()
    {
        // Arrange
        var node = new Node<int, string>(5, true);

        // Fill node to capacity (degree - 1 = 4 keys)
        node.InsertAt(0, 1, "one");
        node.InsertAt(1, 2, "two");
        node.InsertAt(2, 3, "three");
        node.InsertAt(3, 4, "four");

        // Act
        var (medianKey, medianValue, rightNode) = node.Split();

        // Assert
        medianKey.Should().Be(3);
        medianValue.Should().Be("three");

        // Left node (original) should have keys before median
        node.KeyCount.Should().Be(2);
        node.Keys[0].Should().Be(1);
        node.Keys[1].Should().Be(2);
        node.Values[0].Should().Be("one");
        node.Values[1].Should().Be("two");

        // Right node should have keys after median
        rightNode.KeyCount.Should().Be(1);
        rightNode.Keys[0].Should().Be(4);
        rightNode.Values[0].Should().Be("four");

        // Both nodes should be marked as dirty
        node.IsDirty.Should().BeTrue();
        rightNode.IsDirty.Should().BeTrue();
    }

    [Fact]
    public void Merge_TwoNodes_ShouldMergeCorrectly()
    {
        // Arrange
        var leftNode = new Node<int, string>(5, true);
        leftNode.InsertAt(0, 1, "one");

        var rightNode = new Node<int, string>(5, true);
        rightNode.InsertAt(0, 3, "three");
        rightNode.InsertAt(1, 4, "four");

        // Act
        leftNode.Merge(2, "two", rightNode);

        // Assert
        leftNode.KeyCount.Should().Be(4);
        leftNode.Keys[0].Should().Be(1);
        leftNode.Keys[1].Should().Be(2); // separator
        leftNode.Keys[2].Should().Be(3);
        leftNode.Keys[3].Should().Be(4);

        leftNode.Values[0].Should().Be("one");
        leftNode.Values[1].Should().Be("two"); // separator
        leftNode.Values[2].Should().Be("three");
        leftNode.Values[3].Should().Be("four");

        leftNode.IsDirty.Should().BeTrue();
    }

    [Fact]
    public void BorrowFromLeft_WithLeftSibling_ShouldBorrowCorrectly()
    {
        // Arrange
        var currentNode = new Node<int, string>(5, true);
        currentNode.InsertAt(0, 5, "five");

        var leftSibling = new Node<int, string>(5, true);
        leftSibling.InsertAt(0, 1, "one");
        leftSibling.InsertAt(1, 2, "two");

        // Act
        var (newParentKey, newParentValue) = currentNode.BorrowFromLeft(3, "three", leftSibling);

        // Assert
        newParentKey.Should().Be(2);
        newParentValue.Should().Be("two");

        // Current node should have parent key at beginning and original key
        currentNode.KeyCount.Should().Be(2);
        currentNode.Keys[0].Should().Be(3); // parent key
        currentNode.Keys[1].Should().Be(5); // original key

        // Left sibling should have one less key
        leftSibling.KeyCount.Should().Be(1);
        leftSibling.Keys[0].Should().Be(1);

        // Both nodes should be dirty
        currentNode.IsDirty.Should().BeTrue();
        leftSibling.IsDirty.Should().BeTrue();
    }

    [Fact]
    public void BorrowFromRight_WithRightSibling_ShouldBorrowCorrectly()
    {
        // Arrange
        var currentNode = new Node<int, string>(5, true);
        currentNode.InsertAt(0, 1, "one");

        var rightSibling = new Node<int, string>(5, true);
        rightSibling.InsertAt(0, 4, "four");
        rightSibling.InsertAt(1, 5, "five");

        // Act
        var (newParentKey, newParentValue) = currentNode.BorrowFromRight(3, "three", rightSibling);

        // Assert
        newParentKey.Should().Be(4);
        newParentValue.Should().Be("four");

        // Current node should have original key and parent key at end
        currentNode.KeyCount.Should().Be(2);
        currentNode.Keys[0].Should().Be(1); // original key
        currentNode.Keys[1].Should().Be(3); // parent key

        // Right sibling should have one less key
        rightSibling.KeyCount.Should().Be(1);
        rightSibling.Keys[0].Should().Be(5);

        // Both nodes should be dirty
        currentNode.IsDirty.Should().BeTrue();
        rightSibling.IsDirty.Should().BeTrue();
    }

    [Fact]
    public void ConvertToLeaf_InternalNode_ShouldConvertToLeaf()
    {
        // Arrange
        var node = new Node<int, string>(5, false); // Internal node
        var child = new Node<int, string>(5, true);
        node.InsertChildAt(0, child);

        // Act
        node.ConvertToLeaf();

        // Assert
        node.IsLeaf.Should().BeTrue();
        node.Children.Should().BeEmpty();
        node.IsDirty.Should().BeTrue();
    }

    [Fact]
    public void IsFull_WithMaxKeys_ShouldReturnTrue()
    {
        // Arrange
        var node = new Node<int, string>(5, true); // degree 5, max keys = 4

        // Act - Fill to capacity
        for (int i = 0; i < 4; i++)
        {
            node.InsertAt(i, i + 1, $"value{i + 1}");
        }

        // Assert
        node.IsFull.Should().BeTrue();
    }

    [Fact]
    public void IsMinimal_WithTooFewKeys_ShouldReturnTrue()
    {
        // Arrange
        var node = new Node<int, string>(5, true); // degree 5, minimal keys for non-root = 2

        // Act - Add only 1 key (less than minimal)
        node.InsertAt(0, 1, "one");

        // Assert
        node.IsMinimal.Should().BeTrue();
    }

    [Fact]
    public void IsMinimal_WithEnoughKeys_ShouldReturnFalse()
    {
        // Arrange
        var node = new Node<int, string>(5, true); // degree 5, minimal keys for non-root = 2

        // Act - Add 2 keys (meets minimal requirement)
        node.InsertAt(0, 1, "one");
        node.InsertAt(1, 2, "two");

        // Assert
        node.IsMinimal.Should().BeFalse();
    }

    [Fact]
    public void InsertChildAt_ValidIndex_ShouldInsertChild()
    {
        // Arrange
        var parentNode = new Node<int, string>(5, false);
        var childNode = new Node<int, string>(5, true);

        // Act
        parentNode.InsertChildAt(0, childNode);

        // Assert
        parentNode.Children.Should().HaveCount(1);
        parentNode.Children[0].Should().BeSameAs(childNode);
        parentNode.IsDirty.Should().BeTrue();
    }

    [Fact]
    public void RemoveChildAt_ValidIndex_ShouldRemoveChild()
    {
        // Arrange
        var parentNode = new Node<int, string>(5, false);
        var child1 = new Node<int, string>(5, true);
        var child2 = new Node<int, string>(5, true);
        parentNode.InsertChildAt(0, child1);
        parentNode.InsertChildAt(1, child2);

        // Act
        var removedChild = parentNode.RemoveChildAt(0);

        // Assert
        removedChild.Should().BeSameAs(child1);
        parentNode.Children.Should().HaveCount(1);
        parentNode.Children[0].Should().BeSameAs(child2);
        parentNode.IsDirty.Should().BeTrue();
    }
}
