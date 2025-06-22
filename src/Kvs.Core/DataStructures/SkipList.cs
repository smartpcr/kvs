using System;
using System.Collections;
using System.Collections.Generic;
using System.Threading;

namespace Kvs.Core.DataStructures;

/// <summary>
/// Represents a node in the skip list.
/// </summary>
/// <typeparam name="TKey">The type of the key.</typeparam>
/// <typeparam name="TValue">The type of the value.</typeparam>
internal class SkipListNode<TKey, TValue>
    where TKey : IComparable<TKey>
{
    /// <summary>
    /// Initializes a new instance of the <see cref="SkipListNode{TKey, TValue}"/> class.
    /// </summary>
    /// <param name="key">The key for this node.</param>
    /// <param name="value">The value for this node.</param>
    /// <param name="level">The level of this node.</param>
    public SkipListNode(TKey key, TValue value, int level)
    {
        this.Key = key;
        this.Value = value;
        this.Forward = new SkipListNode<TKey, TValue>[level + 1];
    }

    /// <summary>
    /// Gets the key of this node.
    /// </summary>
    public TKey Key { get; }

    /// <summary>
    /// Gets or sets the value of this node.
    /// </summary>
    public TValue Value { get; set; }

    /// <summary>
    /// Gets the forward pointers for this node.
    /// </summary>
    public SkipListNode<TKey, TValue>[] Forward { get; }
}

/// <summary>
/// Implements a skip list data structure for efficient searching and range queries.
/// </summary>
/// <typeparam name="TKey">The type of keys in the skip list.</typeparam>
/// <typeparam name="TValue">The type of values in the skip list.</typeparam>
public class SkipList<TKey, TValue> : IEnumerable<KeyValuePair<TKey, TValue>>, IDisposable
    where TKey : IComparable<TKey>
{
    private const int MaxLevel = 32;
    private const double Probability = 0.5;

    private readonly SkipListNode<TKey, TValue> head;
    private readonly Random random;
    private readonly ReaderWriterLockSlim rwLock;
    private int level;
    private int count;

    /// <summary>
    /// Initializes a new instance of the <see cref="SkipList{TKey, TValue}"/> class.
    /// </summary>
    public SkipList()
    {
        this.head = new SkipListNode<TKey, TValue>(default!, default!, MaxLevel);
        this.random = new Random();
        this.rwLock = new ReaderWriterLockSlim();
        this.level = 0;
        this.count = 0;
    }

    /// <summary>
    /// Gets the number of elements in the skip list.
    /// </summary>
    public int Count
    {
        get
        {
            this.rwLock.EnterReadLock();
            try
            {
                return this.count;
            }
            finally
            {
                this.rwLock.ExitReadLock();
            }
        }
    }

    /// <summary>
    /// Gets a value indicating whether the skip list is empty.
    /// </summary>
    public bool IsEmpty => this.Count == 0;

    /// <summary>
    /// Inserts a key-value pair into the skip list.
    /// </summary>
    /// <param name="key">The key to insert.</param>
    /// <param name="value">The value to insert.</param>
    /// <returns>True if the key was inserted; false if it already existed and was updated.</returns>
    public bool Insert(TKey key, TValue value)
    {
        if (key == null)
        {
            throw new ArgumentNullException(nameof(key));
        }

        this.rwLock.EnterWriteLock();
        try
        {
            var update = new SkipListNode<TKey, TValue>[MaxLevel + 1];
            var current = this.head;

            // Find the position to insert
            for (int i = this.level; i >= 0; i--)
            {
                while (current.Forward[i] != null && current.Forward[i].Key.CompareTo(key) < 0)
                {
                    current = current.Forward[i];
                }

                update[i] = current;
            }

            current = current.Forward[0];

            // Key already exists, update value
            if (current != null && current.Key.CompareTo(key) == 0)
            {
                current.Value = value;
                return false;
            }

            // Generate random level for new node
            int newLevel = this.RandomLevel();
            if (newLevel > this.level)
            {
                for (int i = this.level + 1; i <= newLevel; i++)
                {
                    update[i] = this.head;
                }

                this.level = newLevel;
            }

            // Create new node
            var newNode = new SkipListNode<TKey, TValue>(key, value, newLevel);

            // Update forward pointers
            for (int i = 0; i <= newLevel; i++)
            {
                newNode.Forward[i] = update[i].Forward[i];
                update[i].Forward[i] = newNode;
            }

            this.count++;
            return true;
        }
        finally
        {
            this.rwLock.ExitWriteLock();
        }
    }

    /// <summary>
    /// Searches for a value by key.
    /// </summary>
    /// <param name="key">The key to search for.</param>
    /// <returns>The value if found; default value otherwise.</returns>
#if NET472
    public TValue Search(TKey key)
#else
    public TValue? Search(TKey key)
#endif
    {
        if (key == null)
        {
            throw new ArgumentNullException(nameof(key));
        }

        this.rwLock.EnterReadLock();
        try
        {
            var current = this.head;

            for (int i = this.level; i >= 0; i--)
            {
                while (current.Forward[i] != null && current.Forward[i].Key.CompareTo(key) < 0)
                {
                    current = current.Forward[i];
                }
            }

            current = current.Forward[0];

            if (current != null && current.Key.CompareTo(key) == 0)
            {
                return current.Value;
            }

            return default;
        }
        finally
        {
            this.rwLock.ExitReadLock();
        }
    }

    /// <summary>
    /// Tries to get a value by key.
    /// </summary>
    /// <param name="key">The key to search for.</param>
    /// <param name="value">The value if found.</param>
    /// <returns>True if the key was found; false otherwise.</returns>
    public bool TryGetValue(TKey key, out TValue value)
    {
        if (key == null)
        {
            throw new ArgumentNullException(nameof(key));
        }

        this.rwLock.EnterReadLock();
        try
        {
            var current = this.head;

            for (int i = this.level; i >= 0; i--)
            {
                while (current.Forward[i] != null && current.Forward[i].Key.CompareTo(key) < 0)
                {
                    current = current.Forward[i];
                }
            }

            current = current.Forward[0];

            if (current != null && current.Key.CompareTo(key) == 0)
            {
                value = current.Value;
                return true;
            }

            value = default!;
            return false;
        }
        finally
        {
            this.rwLock.ExitReadLock();
        }
    }

    /// <summary>
    /// Deletes a key from the skip list.
    /// </summary>
    /// <param name="key">The key to delete.</param>
    /// <returns>True if the key was deleted; false if it was not found.</returns>
    public bool Delete(TKey key)
    {
        if (key == null)
        {
            throw new ArgumentNullException(nameof(key));
        }

        this.rwLock.EnterWriteLock();
        try
        {
            var update = new SkipListNode<TKey, TValue>[MaxLevel + 1];
            var current = this.head;

            // Find the node to delete
            for (int i = this.level; i >= 0; i--)
            {
                while (current.Forward[i] != null && current.Forward[i].Key.CompareTo(key) < 0)
                {
                    current = current.Forward[i];
                }

                update[i] = current;
            }

            current = current.Forward[0];

            // Node not found
            if (current == null || current.Key.CompareTo(key) != 0)
            {
                return false;
            }

            // Update forward pointers
            for (int i = 0; i <= this.level; i++)
            {
                if (update[i].Forward[i] != current)
                {
                    break;
                }

                update[i].Forward[i] = current.Forward[i];
            }

            // Update level if necessary
            while (this.level > 0 && this.head.Forward[this.level] == null)
            {
                this.level--;
            }

            this.count--;
            return true;
        }
        finally
        {
            this.rwLock.ExitWriteLock();
        }
    }

    /// <summary>
    /// Checks if the skip list contains a key.
    /// </summary>
    /// <param name="key">The key to check.</param>
    /// <returns>True if the key exists; false otherwise.</returns>
    public bool ContainsKey(TKey key)
    {
        if (key == null)
        {
            throw new ArgumentNullException(nameof(key));
        }

        this.rwLock.EnterReadLock();
        try
        {
            var current = this.head;

            for (int i = this.level; i >= 0; i--)
            {
                while (current.Forward[i] != null && current.Forward[i].Key.CompareTo(key) < 0)
                {
                    current = current.Forward[i];
                }
            }

            current = current.Forward[0];
            return current != null && current.Key.CompareTo(key) == 0;
        }
        finally
        {
            this.rwLock.ExitReadLock();
        }
    }

    /// <summary>
    /// Returns all key-value pairs within the specified range.
    /// </summary>
    /// <param name="startKey">The start key (inclusive).</param>
    /// <param name="endKey">The end key (inclusive).</param>
    /// <returns>An enumerable of key-value pairs in the range.</returns>
    public IEnumerable<KeyValuePair<TKey, TValue>> Range(TKey startKey, TKey endKey)
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

        return this.RangeCore(startKey, endKey);
    }

    private IEnumerable<KeyValuePair<TKey, TValue>> RangeCore(TKey startKey, TKey endKey)
    {
        this.rwLock.EnterReadLock();
        try
        {
            var current = this.head;

            // Find the starting position
            for (int i = this.level; i >= 0; i--)
            {
                while (current.Forward[i] != null && current.Forward[i].Key.CompareTo(startKey) < 0)
                {
                    current = current.Forward[i];
                }
            }

            current = current.Forward[0];

            // Yield all nodes in range
            while (current != null && current.Key.CompareTo(endKey) <= 0)
            {
                yield return new KeyValuePair<TKey, TValue>(current.Key, current.Value);
                current = current.Forward[0];
            }
        }
        finally
        {
            this.rwLock.ExitReadLock();
        }
    }

    /// <summary>
    /// Gets the minimum key in the skip list.
    /// </summary>
    /// <returns>The minimum key, or default if empty.</returns>
#if NET472
    public TKey GetMinKey()
#else
    public TKey? GetMinKey()
#endif
    {
        this.rwLock.EnterReadLock();
        try
        {
            var first = this.head.Forward[0];
            return first != null ? first.Key : default;
        }
        finally
        {
            this.rwLock.ExitReadLock();
        }
    }

    /// <summary>
    /// Gets the maximum key in the skip list.
    /// </summary>
    /// <returns>The maximum key, or default if empty.</returns>
#if NET472
    public TKey GetMaxKey()
#else
    public TKey? GetMaxKey()
#endif
    {
        this.rwLock.EnterReadLock();
        try
        {
            if (this.head.Forward[0] == null)
            {
                return default;
            }

            var current = this.head;
            for (int i = this.level; i >= 0; i--)
            {
                while (current.Forward[i] != null)
                {
                    current = current.Forward[i];
                }
            }

            return current.Key;
        }
        finally
        {
            this.rwLock.ExitReadLock();
        }
    }

    /// <summary>
    /// Clears all elements from the skip list.
    /// </summary>
    public void Clear()
    {
        this.rwLock.EnterWriteLock();
        try
        {
            for (int i = 0; i <= MaxLevel; i++)
            {
#if NET472
                this.head.Forward[i] = null;
#else
                this.head.Forward[i] = null!;
#endif
            }

            this.level = 0;
            this.count = 0;
        }
        finally
        {
            this.rwLock.ExitWriteLock();
        }
    }

    /// <summary>
    /// Returns an enumerator that iterates through the skip list.
    /// </summary>
    /// <returns>An enumerator for the skip list.</returns>
    public IEnumerator<KeyValuePair<TKey, TValue>> GetEnumerator()
    {
        this.rwLock.EnterReadLock();
        try
        {
            var current = this.head.Forward[0];
            while (current != null)
            {
                yield return new KeyValuePair<TKey, TValue>(current.Key, current.Value);
                current = current.Forward[0];
            }
        }
        finally
        {
            this.rwLock.ExitReadLock();
        }
    }

    /// <summary>
    /// Returns an enumerator that iterates through the skip list.
    /// </summary>
    /// <returns>An enumerator for the skip list.</returns>
    IEnumerator IEnumerable.GetEnumerator()
    {
        return this.GetEnumerator();
    }

    /// <summary>
    /// Disposes the skip list and releases resources.
    /// </summary>
    public void Dispose()
    {
        this.Dispose(true);
        GC.SuppressFinalize(this);
    }

    /// <summary>
    /// Releases the unmanaged resources used by the SkipList and optionally releases the managed resources.
    /// </summary>
    /// <param name="disposing">True to release both managed and unmanaged resources; false to release only unmanaged resources.</param>
    protected virtual void Dispose(bool disposing)
    {
        if (disposing)
        {
            this.rwLock?.Dispose();
        }
    }

    private int RandomLevel()
    {
        int level = 0;
        while (this.random.NextDouble() < Probability && level < MaxLevel)
        {
            level++;
        }

        return level;
    }
}
