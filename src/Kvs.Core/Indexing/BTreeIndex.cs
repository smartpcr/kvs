#nullable enable
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Kvs.Core.DataStructures;

namespace Kvs.Core.Indexing;

/// <summary>
/// Implements an index using a B-Tree data structure for fast key-value lookups and range queries.
/// </summary>
/// <typeparam name="TKey">The type of the key.</typeparam>
/// <typeparam name="TValue">The type of the value.</typeparam>
public class BTreeIndex<TKey, TValue> : IIndex<TKey, TValue>
    where TKey : IComparable<TKey>
{
    private readonly BTree<TKey, TValue> btree;
    private readonly object lockObject;
    private bool disposed;

    /// <summary>
    /// Initializes a new instance of the <see cref="BTreeIndex{TKey, TValue}"/> class.
    /// </summary>
    /// <param name="degree">The degree of the underlying B-Tree.</param>
    public BTreeIndex(int degree = 64)
    {
        this.btree = new BTree<TKey, TValue>(degree);
        this.lockObject = new object();
        this.disposed = false;
    }

    /// <inheritdoc />
    public Task<TValue?> GetAsync(TKey key)
    {
        this.ThrowIfDisposed();

        if (key == null)
        {
            throw new ArgumentNullException(nameof(key));
        }

        lock (this.lockObject)
        {
            var result = this.btree.Search(key);
            return Task.FromResult(result);
        }
    }

    /// <inheritdoc />
    public Task PutAsync(TKey key, TValue value)
    {
        this.ThrowIfDisposed();

        if (key == null)
        {
            throw new ArgumentNullException(nameof(key));
        }

        lock (this.lockObject)
        {
            this.btree.Insert(key, value);
            return Task.CompletedTask;
        }
    }

    /// <inheritdoc />
    public Task<bool> DeleteAsync(TKey key)
    {
        this.ThrowIfDisposed();

        if (key == null)
        {
            throw new ArgumentNullException(nameof(key));
        }

        lock (this.lockObject)
        {
            var result = this.btree.Delete(key);
            return Task.FromResult(result);
        }
    }

    /// <inheritdoc />
    public Task<bool> ContainsKeyAsync(TKey key)
    {
        this.ThrowIfDisposed();

        if (key == null)
        {
            throw new ArgumentNullException(nameof(key));
        }

        lock (this.lockObject)
        {
            var result = this.btree.ContainsKey(key);
            return Task.FromResult(result);
        }
    }

    /// <inheritdoc />
    public IAsyncEnumerable<KeyValuePair<TKey, TValue>> RangeAsync(TKey startKey, TKey endKey)
    {
        this.ValidateRangeParameters(startKey, endKey);
        return this.RangeAsyncCore(startKey, endKey);
    }

    private void ValidateRangeParameters(TKey startKey, TKey endKey)
    {
        this.ThrowIfDisposed();

        if (startKey == null)
        {
            throw new ArgumentNullException(nameof(startKey));
        }

        if (endKey == null)
        {
            throw new ArgumentNullException(nameof(endKey));
        }
    }

    private async IAsyncEnumerable<KeyValuePair<TKey, TValue>> RangeAsyncCore(TKey startKey, TKey endKey)
    {
        IEnumerable<KeyValuePair<TKey, TValue>> results;
        lock (this.lockObject)
        {
            // Create a snapshot to avoid holding the lock during enumeration
            results = new List<KeyValuePair<TKey, TValue>>(this.btree.Range(startKey, endKey));
        }

        foreach (var kvp in results)
        {
            yield return kvp;
            await Task.Yield(); // Allow other tasks to run
        }
    }

    /// <inheritdoc />
    public async IAsyncEnumerable<KeyValuePair<TKey, TValue>> GetAllAsync()
    {
        this.ThrowIfDisposed();

        IEnumerable<KeyValuePair<TKey, TValue>> results;
        lock (this.lockObject)
        {
            // Create a snapshot to avoid holding the lock during enumeration
            results = new List<KeyValuePair<TKey, TValue>>(this.btree.GetAll());
        }

        foreach (var kvp in results)
        {
            yield return kvp;
            await Task.Yield(); // Allow other tasks to run
        }
    }

    /// <inheritdoc />
    public Task<long> CountAsync()
    {
        this.ThrowIfDisposed();

        lock (this.lockObject)
        {
            return Task.FromResult(this.btree.Count);
        }
    }

    /// <inheritdoc />
    public Task FlushAsync()
    {
        this.ThrowIfDisposed();

        // For in-memory B-Tree, flush is a no-op
        // In a persistent implementation, this would flush dirty nodes to storage
        return Task.CompletedTask;
    }

    /// <inheritdoc />
    public Task<TKey?> GetMinKeyAsync()
    {
        this.ThrowIfDisposed();

        lock (this.lockObject)
        {
            var result = this.btree.GetMinKey();
            return Task.FromResult(result);
        }
    }

    /// <inheritdoc />
    public Task<TKey?> GetMaxKeyAsync()
    {
        this.ThrowIfDisposed();

        lock (this.lockObject)
        {
            var result = this.btree.GetMaxKey();
            return Task.FromResult(result);
        }
    }

    /// <summary>
    /// Clears all entries from the index.
    /// </summary>
    public void Clear()
    {
        this.ThrowIfDisposed();

        lock (this.lockObject)
        {
            this.btree.Clear();
        }
    }

    /// <summary>
    /// Gets a value indicating whether the index is empty.
    /// </summary>
    public bool IsEmpty
    {
        get
        {
            this.ThrowIfDisposed();

            lock (this.lockObject)
            {
                return this.btree.IsEmpty;
            }
        }
    }

    /// <summary>
    /// Gets statistics about the B-Tree index.
    /// </summary>
    /// <returns>A dictionary containing index statistics.</returns>
    public Dictionary<string, object> GetStatistics()
    {
        this.ThrowIfDisposed();

        lock (this.lockObject)
        {
            return new Dictionary<string, object>
            {
                ["Count"] = this.btree.Count,
                ["IsEmpty"] = this.btree.IsEmpty,
                ["MinKey"] = this.btree.GetMinKey()?.ToString() ?? "null",
                ["MaxKey"] = this.btree.GetMaxKey()?.ToString() ?? "null"
            };
        }
    }

    /// <summary>
    /// Performs a batch insert operation for multiple key-value pairs.
    /// </summary>
    /// <param name="items">The key-value pairs to insert.</param>
    /// <returns>A task that represents the asynchronous operation. The task result contains the number of items inserted.</returns>
    public Task<int> BatchInsertAsync(IEnumerable<KeyValuePair<TKey, TValue>> items)
    {
        this.ThrowIfDisposed();

        if (items == null)
        {
            throw new ArgumentNullException(nameof(items));
        }

        lock (this.lockObject)
        {
            var insertCount = 0;
            foreach (var kvp in items)
            {
                if (kvp.Key != null)
                {
                    this.btree.Insert(kvp.Key, kvp.Value);
                    insertCount++;
                }
            }

            return Task.FromResult(insertCount);
        }
    }

    /// <summary>
    /// Performs a batch delete operation for multiple keys.
    /// </summary>
    /// <param name="keys">The keys to delete.</param>
    /// <returns>A task that represents the asynchronous operation. The task result contains the number of keys deleted.</returns>
    public Task<int> BatchDeleteAsync(IEnumerable<TKey> keys)
    {
        this.ThrowIfDisposed();

        if (keys == null)
        {
            throw new ArgumentNullException(nameof(keys));
        }

        lock (this.lockObject)
        {
            var deleteCount = 0;
            foreach (var key in keys)
            {
                if (key != null && this.btree.Delete(key))
                {
                    deleteCount++;
                }
            }

            return Task.FromResult(deleteCount);
        }
    }

    /// <summary>
    /// Finds keys that are greater than the specified key.
    /// </summary>
    /// <param name="key">The key to compare against.</param>
    /// <param name="limit">The maximum number of results to return.</param>
    /// <returns>An async enumerable of key-value pairs where keys are greater than the specified key.</returns>
    public IAsyncEnumerable<KeyValuePair<TKey, TValue>> FindKeysGreaterThanAsync(TKey key, int limit = int.MaxValue)
    {
        this.ValidateFindParameters(key);
        return this.FindKeysGreaterThanAsyncCore(key, limit);
    }

    private void ValidateFindParameters(TKey key)
    {
        this.ThrowIfDisposed();

        if (key == null)
        {
            throw new ArgumentNullException(nameof(key));
        }
    }

    private async IAsyncEnumerable<KeyValuePair<TKey, TValue>> FindKeysGreaterThanAsyncCore(TKey key, int limit)
    {
        var maxKey = await this.GetMaxKeyAsync();
        if (maxKey == null)
        {
            yield break;
        }

        var count = 0;
        await foreach (var kvp in this.RangeAsync(key, maxKey))
        {
            if (kvp.Key.CompareTo(key) > 0)
            {
                yield return kvp;
                count++;
                if (count >= limit)
                {
                    break;
                }
            }
        }
    }

    /// <summary>
    /// Finds keys that are less than the specified key.
    /// </summary>
    /// <param name="key">The key to compare against.</param>
    /// <param name="limit">The maximum number of results to return.</param>
    /// <returns>An async enumerable of key-value pairs where keys are less than the specified key.</returns>
    public IAsyncEnumerable<KeyValuePair<TKey, TValue>> FindKeysLessThanAsync(TKey key, int limit = int.MaxValue)
    {
        this.ValidateFindParameters(key);
        return this.FindKeysLessThanAsyncCore(key, limit);
    }

    private async IAsyncEnumerable<KeyValuePair<TKey, TValue>> FindKeysLessThanAsyncCore(TKey key, int limit)
    {
        var minKey = await this.GetMinKeyAsync();
        if (minKey == null)
        {
            yield break;
        }

        var count = 0;
        await foreach (var kvp in this.RangeAsync(minKey, key))
        {
            if (kvp.Key.CompareTo(key) < 0)
            {
                yield return kvp;
                count++;
                if (count >= limit)
                {
                    break;
                }
            }
        }
    }

    private void ThrowIfDisposed()
    {
        if (this.disposed)
        {
            throw new ObjectDisposedException(nameof(BTreeIndex<TKey, TValue>));
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
            this.btree.Clear();
            this.disposed = true;
        }
    }
}