#nullable enable
using System;
using System.Collections.Generic;
using System.Threading.Tasks;

namespace Kvs.Core.DataStructures;

/// <summary>
/// Represents a B-Tree data structure for efficient storage and retrieval of key-value pairs.
/// </summary>
/// <typeparam name="TKey">The type of the key.</typeparam>
/// <typeparam name="TValue">The type of the value.</typeparam>
public class BTree<TKey, TValue>
    where TKey : IComparable<TKey>
{
    private readonly int degree;
    private Node<TKey, TValue>? root;

    /// <summary>
    /// Initializes a new instance of the <see cref="BTree{TKey, TValue}"/> class.
    /// </summary>
    /// <param name="degree">The degree of the B-Tree (minimum number of children for internal nodes).</param>
    public BTree(int degree = 64)
    {
        if (degree < 3)
        {
            throw new ArgumentException("B-Tree degree must be at least 3", nameof(degree));
        }

        this.degree = degree;
        this.root = null;
        this.Count = 0;
    }

    /// <summary>
    /// Gets the number of key-value pairs in the B-Tree.
    /// </summary>
    public long Count { get; private set; }

    /// <summary>
    /// Gets a value indicating whether the B-Tree is empty.
    /// </summary>
    public bool IsEmpty => this.root == null;

    /// <summary>
    /// Searches for a key in the B-Tree and returns its associated value.
    /// </summary>
    /// <param name="key">The key to search for.</param>
    /// <returns>The value associated with the key if found; otherwise, the default value.</returns>
    public TValue? Search(TKey key)
    {
        if (key == null)
        {
            throw new ArgumentNullException(nameof(key));
        }

        return this.SearchInternal(this.root, key);
    }

    /// <summary>
    /// Inserts a key-value pair into the B-Tree.
    /// </summary>
    /// <param name="key">The key to insert.</param>
    /// <param name="value">The value to associate with the key.</param>
    /// <returns>True if the key was inserted; false if the key already existed and was updated.</returns>
    public bool Insert(TKey key, TValue value)
    {
        if (key == null)
        {
            throw new ArgumentNullException(nameof(key));
        }

        if (this.root == null)
        {
            this.root = new Node<TKey, TValue>(this.degree, true);
            this.root.InsertAt(0, key, value);
            this.Count++;
            return true;
        }

        // Check if key already exists
        if (this.SearchInternal(this.root, key) != null)
        {
            // Update existing key
            this.UpdateInternal(this.root, key, value);
            return false;
        }

        // Check if root is full
        if (this.root.IsFull)
        {
            var newRoot = new Node<TKey, TValue>(this.degree, false);
            newRoot.InsertChildAt(0, this.root);
            this.SplitChild(newRoot, 0);
            this.root = newRoot;
        }

        this.InsertNonFull(this.root, key, value);
        this.Count++;
        return true;
    }

    /// <summary>
    /// Deletes a key from the B-Tree.
    /// </summary>
    /// <param name="key">The key to delete.</param>
    /// <returns>True if the key was found and deleted; otherwise, false.</returns>
    public bool Delete(TKey key)
    {
        if (key == null)
        {
            throw new ArgumentNullException(nameof(key));
        }

        if (this.root == null)
        {
            return false;
        }

        var deleted = this.DeleteInternal(this.root, key);

        // If root becomes empty, make first child new root
        if (this.root.KeyCount == 0)
        {
            if (!this.root.IsLeaf)
            {
                this.root = this.root.Children[0];
            }
            else
            {
                this.root = null;
            }
        }

        if (deleted)
        {
            this.Count--;
        }

        return deleted;
    }

    /// <summary>
    /// Determines whether the B-Tree contains the specified key.
    /// </summary>
    /// <param name="key">The key to search for.</param>
    /// <returns>True if the key exists; otherwise, false.</returns>
    public bool ContainsKey(TKey key)
    {
        if (key == null)
        {
            throw new ArgumentNullException(nameof(key));
        }

        return this.SearchInternal(this.root, key) != null;
    }

    /// <summary>
    /// Returns all key-value pairs in the B-Tree in sorted order.
    /// </summary>
    /// <returns>An enumerable of key-value pairs in ascending order.</returns>
    public IEnumerable<KeyValuePair<TKey, TValue>> GetAll()
    {
        if (this.root == null)
        {
            yield break;
        }

        foreach (var kvp in this.InOrderTraversal(this.root))
        {
            yield return kvp;
        }
    }

    /// <summary>
    /// Returns key-value pairs within the specified range.
    /// </summary>
    /// <param name="startKey">The start key of the range (inclusive).</param>
    /// <param name="endKey">The end key of the range (inclusive).</param>
    /// <returns>An enumerable of key-value pairs within the range.</returns>
    public IEnumerable<KeyValuePair<TKey, TValue>> Range(TKey startKey, TKey endKey)
    {
        this.ValidateRangeParameters(startKey, endKey);
        return this.RangeCore(startKey, endKey);
    }

    private void ValidateRangeParameters(TKey startKey, TKey endKey)
    {
        if (startKey == null)
        {
            throw new ArgumentNullException(nameof(startKey));
        }

        if (endKey == null)
        {
            throw new ArgumentNullException(nameof(endKey));
        }

        if (startKey.CompareTo(endKey) > 0)
        {
            throw new ArgumentException("Start key must be less than or equal to end key");
        }
    }

    private IEnumerable<KeyValuePair<TKey, TValue>> RangeCore(TKey startKey, TKey endKey)
    {
        if (this.root == null)
        {
            yield break;
        }

        foreach (var kvp in this.RangeSearch(this.root, startKey, endKey))
        {
            yield return kvp;
        }
    }

    /// <summary>
    /// Gets the minimum key in the B-Tree.
    /// </summary>
    /// <returns>The minimum key if the tree is not empty; otherwise, the default value.</returns>
    public TKey? GetMinKey()
    {
        if (this.root == null)
        {
            return default;
        }

        var current = this.root;
        while (!current.IsLeaf)
        {
            current = current.Children[0];
        }

        return current.Keys.Count > 0 ? current.Keys[0] : default;
    }

    /// <summary>
    /// Gets the maximum key in the B-Tree.
    /// </summary>
    /// <returns>The maximum key if the tree is not empty; otherwise, the default value.</returns>
    public TKey? GetMaxKey()
    {
        if (this.root == null)
        {
            return default;
        }

        var current = this.root;
        while (!current.IsLeaf)
        {
            current = current.Children[current.Children.Count - 1];
        }

        return current.Keys.Count > 0 ? current.Keys[current.Keys.Count - 1] : default;
    }

    /// <summary>
    /// Clears all key-value pairs from the B-Tree.
    /// </summary>
    public void Clear()
    {
        this.root = null;
        this.Count = 0;
    }

    private TValue? SearchInternal(Node<TKey, TValue>? node, TKey key)
    {
        if (node == null)
        {
            return default;
        }

        var index = node.SearchKey(key);
        if (index >= 0)
        {
            return node.Values[index];
        }

        if (node.IsLeaf)
        {
            return default;
        }

        var childIndex = ~index;
        return this.SearchInternal(node.Children[childIndex], key);
    }

    private void UpdateInternal(Node<TKey, TValue> node, TKey key, TValue value)
    {
        var index = node.SearchKey(key);
        if (index >= 0)
        {
            node.UpdateValueAt(index, value);
            return;
        }

        if (!node.IsLeaf)
        {
            var childIndex = ~index;
            this.UpdateInternal(node.Children[childIndex], key, value);
        }
    }

    private void InsertNonFull(Node<TKey, TValue> node, TKey key, TValue value)
    {
        var index = node.SearchKey(key);
        if (index >= 0)
        {
            // Key already exists, update value
            node.UpdateValueAt(index, value);
            return;
        }

        var insertIndex = ~index;

        if (node.IsLeaf)
        {
            node.InsertAt(insertIndex, key, value);
        }
        else
        {
            var child = node.Children[insertIndex];
            if (child.IsFull)
            {
                this.SplitChild(node, insertIndex);
                if (key.CompareTo(node.Keys[insertIndex]) > 0)
                {
                    insertIndex++;
                }
            }

            this.InsertNonFull(node.Children[insertIndex], key, value);
        }
    }

    private void SplitChild(Node<TKey, TValue> parent, int childIndex)
    {
        var fullChild = parent.Children[childIndex];
        var (medianKey, medianValue, rightNode) = fullChild.Split();

        parent.InsertAt(childIndex, medianKey, medianValue);
        parent.InsertChildAt(childIndex + 1, rightNode);
    }

    private bool DeleteInternal(Node<TKey, TValue> node, TKey key)
    {
        var index = node.SearchKey(key);

        if (index >= 0)
        {
            // Key found in current node
            if (node.IsLeaf)
            {
                node.RemoveAt(index);
                return true;
            }
            else
            {
                return this.DeleteFromInternalNode(node, index);
            }
        }
        else
        {
            // Key not in current node
            if (node.IsLeaf)
            {
                return false; // Key not found
            }

            var childIndex = ~index;
            var child = node.Children[childIndex];

            // Ensure child has enough keys
            if (child.IsMinimal)
            {
                this.EnsureChildHasEnoughKeys(node, childIndex);

                // Child index might have changed after rebalancing
                index = node.SearchKey(key);
                if (index >= 0)
                {
                    return this.DeleteFromInternalNode(node, index);
                }

                childIndex = ~index;
            }

            return this.DeleteInternal(node.Children[childIndex], key);
        }
    }

    private bool DeleteFromInternalNode(Node<TKey, TValue> node, int keyIndex)
    {
        var leftChild = node.Children[keyIndex];
        var rightChild = node.Children[keyIndex + 1];

        if (!leftChild.IsMinimal && leftChild.KeyCount > 0)
        {
            // Get predecessor from left subtree (rightmost key in left child)
            var (predKey, predValue) = leftChild.GetRightmostKeyValue();
            node.UpdateValueAt(keyIndex, predValue);
            var oldKey = node.Keys[keyIndex];
            node.RemoveAt(keyIndex);
            node.InsertAt(keyIndex, predKey, predValue);
            return this.DeleteInternal(leftChild, predKey);
        }
        else if (!rightChild.IsMinimal && rightChild.KeyCount > 0)
        {
            // Get successor from right subtree
            var (succKey, succValue) = rightChild.GetSuccessor(0);
            node.UpdateValueAt(keyIndex, succValue);
            var oldKey = node.Keys[keyIndex];
            node.RemoveAt(keyIndex);
            node.InsertAt(keyIndex, succKey, succValue);
            return this.DeleteInternal(rightChild, succKey);
        }
        else
        {
            // Both children are minimal, merge them
            var keyToDelete = node.Keys[keyIndex];
            var valueToDelete = node.Values[keyIndex];
            node.RemoveAt(keyIndex);
            node.RemoveChildAt(keyIndex + 1);

            leftChild.Merge(keyToDelete, valueToDelete, rightChild);
            return this.DeleteInternal(leftChild, keyToDelete);
        }
    }

    private void EnsureChildHasEnoughKeys(Node<TKey, TValue> parent, int childIndex)
    {
        var child = parent.Children[childIndex];

        if (!child.IsMinimal)
        {
            return;
        }

        // Try to borrow from left sibling
        if (childIndex > 0)
        {
            var leftSibling = parent.Children[childIndex - 1];
            if (!leftSibling.IsMinimal)
            {
                var (newParentKey, newParentValue) = child.BorrowFromLeft(
                    parent.Keys[childIndex - 1],
                    parent.Values[childIndex - 1],
                    leftSibling);
                parent.UpdateValueAt(childIndex - 1, newParentValue);
                parent.RemoveAt(childIndex - 1);
                parent.InsertAt(childIndex - 1, newParentKey, newParentValue);
                return;
            }
        }

        // Try to borrow from right sibling
        if (childIndex < parent.Children.Count - 1)
        {
            var rightSibling = parent.Children[childIndex + 1];
            if (!rightSibling.IsMinimal)
            {
                var (newParentKey, newParentValue) = child.BorrowFromRight(
                    parent.Keys[childIndex],
                    parent.Values[childIndex],
                    rightSibling);
                parent.UpdateValueAt(childIndex, newParentValue);
                parent.RemoveAt(childIndex);
                parent.InsertAt(childIndex, newParentKey, newParentValue);
                return;
            }
        }

        // Merge with a sibling
        if (childIndex > 0)
        {
            // Merge with left sibling
            var leftSibling = parent.Children[childIndex - 1];
            var separatorKey = parent.Keys[childIndex - 1];
            var separatorValue = parent.Values[childIndex - 1];
            parent.RemoveAt(childIndex - 1);
            parent.RemoveChildAt(childIndex);

            leftSibling.Merge(separatorKey, separatorValue, child);
        }
        else
        {
            // Merge with right sibling
            var rightSibling = parent.Children[childIndex + 1];
            var separatorKey = parent.Keys[childIndex];
            var separatorValue = parent.Values[childIndex];
            parent.RemoveAt(childIndex);
            parent.RemoveChildAt(childIndex + 1);

            child.Merge(separatorKey, separatorValue, rightSibling);
        }
    }

    private IEnumerable<KeyValuePair<TKey, TValue>> InOrderTraversal(Node<TKey, TValue> node)
    {
        if (node.IsLeaf)
        {
            for (int i = 0; i < node.KeyCount; i++)
            {
                yield return new KeyValuePair<TKey, TValue>(node.Keys[i], node.Values[i]);
            }
        }
        else
        {
            for (int i = 0; i < node.KeyCount; i++)
            {
                foreach (var kvp in this.InOrderTraversal(node.Children[i]))
                {
                    yield return kvp;
                }

                yield return new KeyValuePair<TKey, TValue>(node.Keys[i], node.Values[i]);
            }

            foreach (var kvp in this.InOrderTraversal(node.Children[node.KeyCount]))
            {
                yield return kvp;
            }
        }
    }

    private IEnumerable<KeyValuePair<TKey, TValue>> RangeSearch(Node<TKey, TValue> node, TKey startKey, TKey endKey)
    {
        if (node.IsLeaf)
        {
            for (int i = 0; i < node.KeyCount; i++)
            {
                var key = node.Keys[i];
                if (key.CompareTo(startKey) >= 0 && key.CompareTo(endKey) <= 0)
                {
                    yield return new KeyValuePair<TKey, TValue>(key, node.Values[i]);
                }
                else if (key.CompareTo(endKey) > 0)
                {
                    break;
                }
            }
        }
        else
        {
            for (int i = 0; i < node.KeyCount; i++)
            {
                var key = node.Keys[i];

                // Search left child if it might contain values in range
                if (key.CompareTo(startKey) >= 0)
                {
                    foreach (var kvp in this.RangeSearch(node.Children[i], startKey, endKey))
                    {
                        yield return kvp;
                    }
                }

                // Include current key if in range
                if (key.CompareTo(startKey) >= 0 && key.CompareTo(endKey) <= 0)
                {
                    yield return new KeyValuePair<TKey, TValue>(key, node.Values[i]);
                }

                // Stop if we've passed the end key
                if (key.CompareTo(endKey) > 0)
                {
                    yield break;
                }
            }

            // Search rightmost child if it might contain values in range
            var lastKey = node.Keys[node.KeyCount - 1];
            if (lastKey.CompareTo(endKey) <= 0)
            {
                foreach (var kvp in this.RangeSearch(node.Children[node.KeyCount], startKey, endKey))
                {
                    yield return kvp;
                }
            }
        }
    }
}