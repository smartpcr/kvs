#nullable enable
using System;
using System.Collections.Generic;
using System.Linq;

namespace Kvs.Core.DataStructures;

/// <summary>
/// Represents a node in a B-Tree structure.
/// </summary>
/// <typeparam name="TKey">The type of the key.</typeparam>
/// <typeparam name="TValue">The type of the value.</typeparam>
public class Node<TKey, TValue>
    where TKey : IComparable<TKey>
{
    private readonly int degree;
    private readonly List<TKey> keys;
    private readonly List<TValue> values;
    private readonly List<Node<TKey, TValue>> children;

    /// <summary>
    /// Initializes a new instance of the <see cref="Node{TKey, TValue}"/> class.
    /// </summary>
    /// <param name="degree">The degree of the B-Tree node.</param>
    /// <param name="isLeaf">A value indicating whether this node is a leaf node.</param>
    public Node(int degree, bool isLeaf = false)
    {
        this.degree = degree;
        this.IsLeaf = isLeaf;
        this.keys = new List<TKey>(degree - 1);
        this.values = new List<TValue>(degree - 1);
        this.children = new List<Node<TKey, TValue>>(degree);
        this.PageId = -1; // Will be assigned when persisted
    }

    /// <summary>
    /// Gets a value indicating whether this node is a leaf node.
    /// </summary>
    public bool IsLeaf { get; private set; }

    /// <summary>
    /// Gets the number of keys in this node.
    /// </summary>
    public int KeyCount => this.keys.Count;

    /// <summary>
    /// Gets a value indicating whether this node is full.
    /// </summary>
    public bool IsFull => this.keys.Count >= this.degree - 1;

    /// <summary>
    /// Gets a value indicating whether this node has the minimum number of keys.
    /// </summary>
    public bool IsMinimal => this.keys.Count < (this.degree - 1) / 2;

    /// <summary>
    /// Gets or sets the page ID for this node when persisted to disk.
    /// </summary>
    public long PageId { get; set; }

    /// <summary>
    /// Gets or sets a value indicating whether this node has been modified since last persistence.
    /// </summary>
    public bool IsDirty { get; set; }

    /// <summary>
    /// Gets the keys in this node as a read-only collection.
    /// </summary>
    public IReadOnlyList<TKey> Keys => this.keys.AsReadOnly();

    /// <summary>
    /// Gets the values in this node as a read-only collection.
    /// </summary>
    public IReadOnlyList<TValue> Values => this.values.AsReadOnly();

    /// <summary>
    /// Gets the child nodes as a read-only collection.
    /// </summary>
    public IReadOnlyList<Node<TKey, TValue>> Children => this.children.AsReadOnly();

    /// <summary>
    /// Searches for a key in this node and returns its index if found.
    /// </summary>
    /// <param name="key">The key to search for.</param>
    /// <returns>The index of the key if found; otherwise, the bitwise complement of the index where it should be inserted.</returns>
    public int SearchKey(TKey key)
    {
        return this.keys.BinarySearch(key);
    }

    /// <summary>
    /// Gets the value associated with the specified key.
    /// </summary>
    /// <param name="key">The key to search for.</param>
    /// <returns>The value if found; otherwise, the default value.</returns>
    public TValue? GetValue(TKey key)
    {
        var index = this.SearchKey(key);
        return index >= 0 ? this.values[index] : default;
    }

    /// <summary>
    /// Inserts a key-value pair into this node at the specified index.
    /// </summary>
    /// <param name="index">The index at which to insert the key-value pair.</param>
    /// <param name="key">The key to insert.</param>
    /// <param name="value">The value to insert.</param>
    public void InsertAt(int index, TKey key, TValue value)
    {
        this.keys.Insert(index, key);
        this.values.Insert(index, value);
        this.IsDirty = true;
    }

    /// <summary>
    /// Inserts a child node at the specified index.
    /// </summary>
    /// <param name="index">The index at which to insert the child.</param>
    /// <param name="child">The child node to insert.</param>
    public void InsertChildAt(int index, Node<TKey, TValue> child)
    {
        this.children.Insert(index, child);
        this.IsDirty = true;
    }

    /// <summary>
    /// Removes the key-value pair at the specified index.
    /// </summary>
    /// <param name="index">The index of the key-value pair to remove.</param>
    /// <returns>The removed key-value pair.</returns>
    public (TKey key, TValue value) RemoveAt(int index)
    {
        var key = this.keys[index];
        var value = this.values[index];
        this.keys.RemoveAt(index);
        this.values.RemoveAt(index);
        this.IsDirty = true;
        return (key, value);
    }

    /// <summary>
    /// Removes the child node at the specified index.
    /// </summary>
    /// <param name="index">The index of the child to remove.</param>
    /// <returns>The removed child node.</returns>
    public Node<TKey, TValue> RemoveChildAt(int index)
    {
        var child = this.children[index];
        this.children.RemoveAt(index);
        this.IsDirty = true;
        return child;
    }

    /// <summary>
    /// Updates the value at the specified index.
    /// </summary>
    /// <param name="index">The index of the value to update.</param>
    /// <param name="value">The new value.</param>
    public void UpdateValueAt(int index, TValue value)
    {
        this.values[index] = value;
        this.IsDirty = true;
    }

    /// <summary>
    /// Splits this node into two nodes, moving half of the keys to a new node.
    /// </summary>
    /// <returns>A tuple containing the median key-value pair and the new right node.</returns>
    public (TKey medianKey, TValue medianValue, Node<TKey, TValue> rightNode) Split()
    {
        var medianIndex = this.keys.Count / 2;
        var medianKey = this.keys[medianIndex];
        var medianValue = this.values[medianIndex];

        var rightNode = new Node<TKey, TValue>(this.degree, this.IsLeaf);

        // Move keys and values from median+1 to end to the right node
        for (int i = medianIndex + 1; i < this.keys.Count; i++)
        {
            rightNode.keys.Add(this.keys[i]);
            rightNode.values.Add(this.values[i]);
        }

        // Move children if not a leaf
        if (!this.IsLeaf)
        {
            for (int i = medianIndex + 1; i < this.children.Count; i++)
            {
                rightNode.children.Add(this.children[i]);
            }
        }

        // Remove moved keys, values, and children from current node
        var removeCount = this.keys.Count - medianIndex;
        this.keys.RemoveRange(medianIndex, removeCount);
        this.values.RemoveRange(medianIndex, removeCount);

        if (!this.IsLeaf)
        {
            var childRemoveCount = this.children.Count - medianIndex - 1;
            this.children.RemoveRange(medianIndex + 1, childRemoveCount);
        }

        this.IsDirty = true;
        rightNode.IsDirty = true;

        return (medianKey, medianValue, rightNode);
    }

    /// <summary>
    /// Merges this node with the specified right node using the given separator key-value pair.
    /// </summary>
    /// <param name="separatorKey">The separator key from the parent.</param>
    /// <param name="separatorValue">The separator value from the parent.</param>
    /// <param name="rightNode">The right node to merge with.</param>
    public void Merge(TKey separatorKey, TValue separatorValue, Node<TKey, TValue> rightNode)
    {
        // Add separator key-value pair
        this.keys.Add(separatorKey);
        this.values.Add(separatorValue);

        // Add all keys and values from right node
        this.keys.AddRange(rightNode.keys);
        this.values.AddRange(rightNode.values);

        // Add all children from right node if not a leaf
        if (!this.IsLeaf)
        {
            this.children.AddRange(rightNode.children);
        }

        this.IsDirty = true;
    }

    /// <summary>
    /// Borrows a key-value pair from the left sibling through the parent.
    /// </summary>
    /// <param name="parentKey">The parent key that separates this node from the left sibling.</param>
    /// <param name="parentValue">The parent value corresponding to the parent key.</param>
    /// <param name="leftSibling">The left sibling node.</param>
    /// <returns>The key-value pair that should replace the parent key-value pair.</returns>
    public (TKey newParentKey, TValue newParentValue) BorrowFromLeft(TKey parentKey, TValue parentValue, Node<TKey, TValue> leftSibling)
    {
        // Insert parent key-value at the beginning
        this.keys.Insert(0, parentKey);
        this.values.Insert(0, parentValue);

        // Move rightmost child from left sibling if not a leaf
        if (!this.IsLeaf && leftSibling.children.Count > 0)
        {
            var child = leftSibling.children[leftSibling.children.Count - 1];
            leftSibling.children.RemoveAt(leftSibling.children.Count - 1);
            this.children.Insert(0, child);
            leftSibling.IsDirty = true;
        }

        // Remove and return the rightmost key-value from left sibling
        var lastIndex = leftSibling.keys.Count - 1;
        var newParentKey = leftSibling.keys[lastIndex];
        var newParentValue = leftSibling.values[lastIndex];
        leftSibling.keys.RemoveAt(lastIndex);
        leftSibling.values.RemoveAt(lastIndex);

        this.IsDirty = true;
        leftSibling.IsDirty = true;

        return (newParentKey, newParentValue);
    }

    /// <summary>
    /// Borrows a key-value pair from the right sibling through the parent.
    /// </summary>
    /// <param name="parentKey">The parent key that separates this node from the right sibling.</param>
    /// <param name="parentValue">The parent value corresponding to the parent key.</param>
    /// <param name="rightSibling">The right sibling node.</param>
    /// <returns>The key-value pair that should replace the parent key-value pair.</returns>
    public (TKey newParentKey, TValue newParentValue) BorrowFromRight(TKey parentKey, TValue parentValue, Node<TKey, TValue> rightSibling)
    {
        // Add parent key-value at the end
        this.keys.Add(parentKey);
        this.values.Add(parentValue);

        // Move leftmost child from right sibling if not a leaf
        if (!this.IsLeaf && rightSibling.children.Count > 0)
        {
            var child = rightSibling.children[0];
            rightSibling.children.RemoveAt(0);
            this.children.Add(child);
            rightSibling.IsDirty = true;
        }

        // Remove and return the leftmost key-value from right sibling
        var newParentKey = rightSibling.keys[0];
        var newParentValue = rightSibling.values[0];
        rightSibling.keys.RemoveAt(0);
        rightSibling.values.RemoveAt(0);

        this.IsDirty = true;
        rightSibling.IsDirty = true;

        return (newParentKey, newParentValue);
    }

    /// <summary>
    /// Converts this node to a leaf node.
    /// </summary>
    public void ConvertToLeaf()
    {
        this.IsLeaf = true;
        this.children.Clear();
        this.IsDirty = true;
    }

    /// <summary>
    /// Gets the predecessor key-value pair (rightmost in left subtree).
    /// </summary>
    /// <param name="index">The index of the key whose predecessor to find.</param>
    /// <returns>The predecessor key-value pair.</returns>
    public (TKey key, TValue value) GetPredecessor(int index)
    {
        if (index >= this.children.Count)
        {
            throw new ArgumentOutOfRangeException(nameof(index), "Child index is out of range");
        }

        var current = this.children[index];
        while (!current.IsLeaf)
        {
            current = current.children[current.children.Count - 1];
        }

        var lastIndex = current.keys.Count - 1;
        return (current.keys[lastIndex], current.values[lastIndex]);
    }

    /// <summary>
    /// Gets the rightmost key-value pair in this subtree.
    /// </summary>
    /// <returns>The rightmost key-value pair.</returns>
    public (TKey key, TValue value) GetRightmostKeyValue()
    {
        var current = this;
        while (!current.IsLeaf)
        {
            if (current.children.Count == 0)
            {
                throw new InvalidOperationException("Internal node has no children");
            }

            current = current.children[current.children.Count - 1];
        }

        if (current.keys.Count == 0)
        {
            throw new InvalidOperationException("Cannot get rightmost key from empty node");
        }

        var lastIndex = current.keys.Count - 1;
        return (current.keys[lastIndex], current.values[lastIndex]);
    }

    /// <summary>
    /// Gets the successor key-value pair (leftmost in right subtree).
    /// </summary>
    /// <param name="index">The index of the key whose successor to find.</param>
    /// <returns>The successor key-value pair.</returns>
    public (TKey key, TValue value) GetSuccessor(int index)
    {
        var current = this.children[index + 1];
        while (!current.IsLeaf)
        {
            current = current.children[0];
        }

        return (current.keys[0], current.values[0]);
    }
}