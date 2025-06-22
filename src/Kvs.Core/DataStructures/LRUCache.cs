#nullable enable
using System;
using System.Collections.Generic;

namespace Kvs.Core.DataStructures;

/// <summary>
/// Implements a Least Recently Used (LRU) cache with a fixed capacity.
/// </summary>
/// <typeparam name="TKey">The type of the key.</typeparam>
/// <typeparam name="TValue">The type of the value.</typeparam>
public class LRUCache<TKey, TValue> : IDisposable
    where TKey : notnull
{
    private readonly int capacity;
    private readonly Dictionary<TKey, Node> cache;
    private readonly Node head;
    private readonly Node tail;
    private readonly object lockObject;
    private bool disposed;

    /// <summary>
    /// Initializes a new instance of the <see cref="LRUCache{TKey, TValue}"/> class.
    /// </summary>
    /// <param name="capacity">The maximum number of items the cache can hold.</param>
    public LRUCache(int capacity)
    {
        if (capacity <= 0)
        {
            throw new ArgumentException("Capacity must be greater than zero", nameof(capacity));
        }

        this.capacity = capacity;
        this.cache = new Dictionary<TKey, Node>(capacity);
        this.lockObject = new object();

        // Create dummy head and tail nodes for easier list manipulation
        this.head = new Node(default!, default!);
        this.tail = new Node(default!, default!);
        this.head.Next = this.tail;
        this.tail.Previous = this.head;
    }

    /// <summary>
    /// Gets the maximum capacity of the cache.
    /// </summary>
    public int Capacity => this.capacity;

    /// <summary>
    /// Gets the current number of items in the cache.
    /// </summary>
    public int Count
    {
        get
        {
            lock (this.lockObject)
            {
                return this.cache.Count;
            }
        }
    }

    /// <summary>
    /// Gets a value indicating whether the cache is empty.
    /// </summary>
    public bool IsEmpty
    {
        get
        {
            lock (this.lockObject)
            {
                return this.cache.Count == 0;
            }
        }
    }

    /// <summary>
    /// Gets a value indicating whether the cache is at full capacity.
    /// </summary>
    public bool IsFull
    {
        get
        {
            lock (this.lockObject)
            {
                return this.cache.Count >= this.capacity;
            }
        }
    }

    /// <summary>
    /// Gets the value associated with the specified key.
    /// </summary>
    /// <param name="key">The key to search for.</param>
    /// <returns>The value if found; otherwise, the default value.</returns>
    public TValue? Get(TKey key)
    {
        this.ThrowIfDisposed();

        if (key == null)
        {
            throw new ArgumentNullException(nameof(key));
        }

        lock (this.lockObject)
        {
            if (this.cache.TryGetValue(key, out var node))
            {
                // Move to front (most recently used)
                this.MoveToFront(node);
                return node.Value;
            }

            return default;
        }
    }

    /// <summary>
    /// Attempts to get the value associated with the specified key.
    /// </summary>
    /// <param name="key">The key to search for.</param>
    /// <param name="value">When this method returns, contains the value associated with the key if found; otherwise, the default value.</param>
    /// <returns>True if the key was found; otherwise, false.</returns>
    public bool TryGet(TKey key, out TValue? value)
    {
        this.ThrowIfDisposed();

        if (key == null)
        {
            throw new ArgumentNullException(nameof(key));
        }

        lock (this.lockObject)
        {
            if (this.cache.TryGetValue(key, out var node))
            {
                // Move to front (most recently used)
                this.MoveToFront(node);
                value = node.Value;
                return true;
            }

            value = default;
            return false;
        }
    }

    /// <summary>
    /// Adds or updates a key-value pair in the cache.
    /// </summary>
    /// <param name="key">The key to add or update.</param>
    /// <param name="value">The value to associate with the key.</param>
    /// <returns>True if a new key was added; false if an existing key was updated.</returns>
    public bool Put(TKey key, TValue value)
    {
        this.ThrowIfDisposed();

        if (key == null)
        {
            throw new ArgumentNullException(nameof(key));
        }

        lock (this.lockObject)
        {
            if (this.cache.TryGetValue(key, out var existingNode))
            {
                // Update existing node
                existingNode.Value = value;
                this.MoveToFront(existingNode);
                return false;
            }

            // Add new node
            var newNode = new Node(key, value);
            this.cache[key] = newNode;
            this.AddToFront(newNode);

            // Remove least recently used item if at capacity
            if (this.cache.Count > this.capacity)
            {
                var lru = this.tail.Previous!;
                this.RemoveNode(lru);
                this.cache.Remove(lru.Key);
            }

            return true;
        }
    }

    /// <summary>
    /// Removes the specified key from the cache.
    /// </summary>
    /// <param name="key">The key to remove.</param>
    /// <returns>True if the key was found and removed; otherwise, false.</returns>
    public bool Remove(TKey key)
    {
        this.ThrowIfDisposed();

        if (key == null)
        {
            throw new ArgumentNullException(nameof(key));
        }

        lock (this.lockObject)
        {
            if (this.cache.TryGetValue(key, out var node))
            {
                this.RemoveNode(node);
                this.cache.Remove(key);
                return true;
            }

            return false;
        }
    }

    /// <summary>
    /// Determines whether the cache contains the specified key.
    /// </summary>
    /// <param name="key">The key to search for.</param>
    /// <returns>True if the key exists; otherwise, false.</returns>
    public bool ContainsKey(TKey key)
    {
        this.ThrowIfDisposed();

        if (key == null)
        {
            throw new ArgumentNullException(nameof(key));
        }

        lock (this.lockObject)
        {
            return this.cache.ContainsKey(key);
        }
    }

    /// <summary>
    /// Clears all items from the cache.
    /// </summary>
    public void Clear()
    {
        this.ThrowIfDisposed();

        lock (this.lockObject)
        {
            this.cache.Clear();
            this.head.Next = this.tail;
            this.tail.Previous = this.head;
        }
    }

    /// <summary>
    /// Gets all keys in the cache in order from most recently used to least recently used.
    /// </summary>
    /// <returns>An enumerable of keys ordered by recency of use.</returns>
    public IEnumerable<TKey> GetKeys()
    {
        this.ThrowIfDisposed();

        lock (this.lockObject)
        {
            var keys = new List<TKey>();
            var current = this.head.Next;
            while (current != this.tail)
            {
                keys.Add(current!.Key);
                current = current.Next;
            }

            return keys;
        }
    }

    /// <summary>
    /// Gets all key-value pairs in the cache in order from most recently used to least recently used.
    /// </summary>
    /// <returns>An enumerable of key-value pairs ordered by recency of use.</returns>
    public IEnumerable<KeyValuePair<TKey, TValue>> GetItems()
    {
        this.ThrowIfDisposed();

        lock (this.lockObject)
        {
            var items = new List<KeyValuePair<TKey, TValue>>();
            var current = this.head.Next;
            while (current != this.tail)
            {
                items.Add(new KeyValuePair<TKey, TValue>(current!.Key, current.Value));
                current = current.Next;
            }

            return items;
        }
    }

    /// <summary>
    /// Gets the least recently used key without affecting its position.
    /// </summary>
    /// <returns>The least recently used key if the cache is not empty; otherwise, the default value.</returns>
    public TKey? GetLeastRecentlyUsedKey()
    {
        this.ThrowIfDisposed();

        lock (this.lockObject)
        {
            if (this.cache.Count == 0)
            {
                return default;
            }

            return this.tail.Previous!.Key;
        }
    }

    /// <summary>
    /// Gets the most recently used key without affecting its position.
    /// </summary>
    /// <returns>The most recently used key if the cache is not empty; otherwise, the default value.</returns>
    public TKey? GetMostRecentlyUsedKey()
    {
        this.ThrowIfDisposed();

        lock (this.lockObject)
        {
            if (this.cache.Count == 0)
            {
                return default;
            }

            return this.head.Next!.Key;
        }
    }

    /// <summary>
    /// Gets cache statistics.
    /// </summary>
    /// <returns>A dictionary containing cache statistics.</returns>
    public Dictionary<string, object> GetStatistics()
    {
        this.ThrowIfDisposed();

        lock (this.lockObject)
        {
            return new Dictionary<string, object>
            {
                ["Capacity"] = this.capacity,
                ["Count"] = this.cache.Count,
                ["IsEmpty"] = this.cache.Count == 0,
                ["IsFull"] = this.cache.Count >= this.capacity,
                ["UtilizationPercentage"] = this.cache.Count * 100.0 / this.capacity,
                ["MostRecentlyUsedKey"] = this.GetMostRecentlyUsedKey()?.ToString() ?? "null",
                ["LeastRecentlyUsedKey"] = this.GetLeastRecentlyUsedKey()?.ToString() ?? "null"
            };
        }
    }

    private void AddToFront(Node node)
    {
        node.Previous = this.head;
        node.Next = this.head.Next;
        this.head.Next!.Previous = node;
        this.head.Next = node;
    }

    private void RemoveNode(Node node)
    {
        node.Previous!.Next = node.Next;
        node.Next!.Previous = node.Previous;
    }

    private void MoveToFront(Node node)
    {
        this.RemoveNode(node);
        this.AddToFront(node);
    }

    private void ThrowIfDisposed()
    {
        if (this.disposed)
        {
            throw new ObjectDisposedException(nameof(LRUCache<TKey, TValue>));
        }
    }

    /// <inheritdoc />
    public void Dispose()
    {
        if (this.disposed)
        {
            return;
        }

        lock (this.lockObject)
        {
            this.Clear();
            this.disposed = true;
        }
    }

    /// <summary>
    /// Represents a node in the doubly-linked list used by the LRU cache.
    /// </summary>
    private sealed class Node
    {
        public Node(TKey key, TValue value)
        {
            this.Key = key;
            this.Value = value;
        }

        public TKey Key { get; }

        public TValue Value { get; set; }

        public Node? Next { get; set; }

        public Node? Previous { get; set; }
    }
}