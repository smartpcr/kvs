using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

namespace Kvs.Core.Indexing;

/// <summary>
/// Implements a hash-based index for fast key-value lookups.
/// </summary>
/// <typeparam name="TKey">The type of keys in the index.</typeparam>
/// <typeparam name="TValue">The type of values in the index.</typeparam>
public class HashIndex<TKey, TValue> : IIndex<TKey, TValue>
    where TKey : IComparable<TKey>
{
    private readonly ConcurrentDictionary<TKey, TValue> index;
    private readonly object disposeLock = new object();
    private bool disposed;

    /// <summary>
    /// Initializes a new instance of the <see cref="HashIndex{TKey, TValue}"/> class.
    /// </summary>
    public HashIndex()
    {
        this.index = new ConcurrentDictionary<TKey, TValue>();
    }

    /// <summary>
    /// Initializes a new instance of the <see cref="HashIndex{TKey, TValue}"/> class with the specified concurrency level and capacity.
    /// </summary>
    /// <param name="concurrencyLevel">The estimated number of threads that will update the index concurrently.</param>
    /// <param name="capacity">The initial number of elements that the index can contain.</param>
    public HashIndex(int concurrencyLevel, int capacity)
    {
        this.index = new ConcurrentDictionary<TKey, TValue>(concurrencyLevel, capacity);
    }

    /// <summary>
    /// Gets a value indicating whether the index is empty.
    /// </summary>
    public bool IsEmpty => this.index.IsEmpty;

    /// <inheritdoc />
#if NET472
    public Task<TValue> GetAsync(TKey key)
#else
    public Task<TValue?> GetAsync(TKey key)
#endif
    {
        this.ThrowIfDisposed();

        if (key == null)
        {
            throw new ArgumentNullException(nameof(key));
        }

#if NET472
        return Task.FromResult(this.index.TryGetValue(key, out var value) ? value : default(TValue));
#else
        return Task.FromResult(this.index.TryGetValue(key, out var value) ? value : default(TValue?));
#endif
    }

    /// <inheritdoc />
    public Task PutAsync(TKey key, TValue value)
    {
        this.ThrowIfDisposed();

        if (key == null)
        {
            throw new ArgumentNullException(nameof(key));
        }

        this.index.AddOrUpdate(key, value, (_, _) => value);
        return Task.CompletedTask;
    }

    /// <inheritdoc />
    public Task<bool> DeleteAsync(TKey key)
    {
        this.ThrowIfDisposed();

        if (key == null)
        {
            throw new ArgumentNullException(nameof(key));
        }

        return Task.FromResult(this.index.TryRemove(key, out _));
    }

    /// <inheritdoc />
    public IAsyncEnumerable<KeyValuePair<TKey, TValue>> RangeAsync(TKey startKey, TKey endKey)
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

        if (startKey.CompareTo(endKey) > 0)
        {
            throw new ArgumentException("Start key must be less than or equal to end key");
        }

        return this.RangeAsyncCore(startKey, endKey);
    }

    private async IAsyncEnumerable<KeyValuePair<TKey, TValue>> RangeAsyncCore(TKey startKey, TKey endKey)
    {
        await Task.Yield(); // Ensure async execution

        // Since hash tables don't maintain order, we need to sort
        var sortedPairs = this.index
            .Where(kvp => kvp.Key.CompareTo(startKey) >= 0 && kvp.Key.CompareTo(endKey) <= 0)
            .OrderBy(kvp => kvp.Key)
            .ToList();

        foreach (var kvp in sortedPairs)
        {
            yield return kvp;
        }
    }

    /// <inheritdoc />
    public Task<long> CountAsync()
    {
        this.ThrowIfDisposed();
        return Task.FromResult((long)this.index.Count);
    }

    /// <summary>
    /// Checks if the index contains a specific key.
    /// </summary>
    /// <param name="key">The key to check.</param>
    /// <returns>A task that represents the asynchronous operation. The task result contains true if the key exists; otherwise, false.</returns>
    public Task<bool> ContainsKeyAsync(TKey key)
    {
        this.ThrowIfDisposed();

        if (key == null)
        {
            throw new ArgumentNullException(nameof(key));
        }

        return Task.FromResult(this.index.ContainsKey(key));
    }

    /// <summary>
    /// Gets all key-value pairs in the index.
    /// </summary>
    /// <returns>An async enumerable of all key-value pairs, sorted by key.</returns>
    public IAsyncEnumerable<KeyValuePair<TKey, TValue>> GetAllAsync()
    {
        this.ThrowIfDisposed();
        return this.GetAllAsyncCore();
    }

    private async IAsyncEnumerable<KeyValuePair<TKey, TValue>> GetAllAsyncCore()
    {
        await Task.Yield(); // Ensure async execution

        var sortedPairs = this.index.OrderBy(kvp => kvp.Key).ToList();
        foreach (var kvp in sortedPairs)
        {
            yield return kvp;
        }
    }

    /// <summary>
    /// Gets the minimum key in the index.
    /// </summary>
    /// <returns>A task that represents the asynchronous operation. The task result contains the minimum key, or default if empty.</returns>
#if NET472
    public Task<TKey> GetMinKeyAsync()
#else
    public Task<TKey?> GetMinKeyAsync()
#endif
    {
        this.ThrowIfDisposed();

        if (this.index.IsEmpty)
        {
#if NET472
            return Task.FromResult(default(TKey));
#else
            return Task.FromResult(default(TKey?));
#endif
        }

#if NET472
        return Task.FromResult(this.index.Keys.Min());
#else
        return Task.FromResult<TKey?>(this.index.Keys.Min());
#endif
    }

    /// <summary>
    /// Gets the maximum key in the index.
    /// </summary>
    /// <returns>A task that represents the asynchronous operation. The task result contains the maximum key, or default if empty.</returns>
#if NET472
    public Task<TKey> GetMaxKeyAsync()
#else
    public Task<TKey?> GetMaxKeyAsync()
#endif
    {
        this.ThrowIfDisposed();

        if (this.index.IsEmpty)
        {
#if NET472
            return Task.FromResult(default(TKey));
#else
            return Task.FromResult(default(TKey?));
#endif
        }

#if NET472
        return Task.FromResult(this.index.Keys.Max());
#else
        return Task.FromResult<TKey?>(this.index.Keys.Max());
#endif
    }

    /// <summary>
    /// Performs a batch insert of multiple key-value pairs.
    /// </summary>
    /// <param name="items">The items to insert.</param>
    /// <returns>A task that represents the asynchronous operation. The task result contains the number of items inserted.</returns>
    public Task<int> BatchInsertAsync(IEnumerable<KeyValuePair<TKey, TValue>> items)
    {
        this.ThrowIfDisposed();

        if (items == null)
        {
            throw new ArgumentNullException(nameof(items));
        }

        int count = 0;
        foreach (var item in items)
        {
            this.index.AddOrUpdate(item.Key, item.Value, (_, _) => item.Value);
            count++;
        }

        return Task.FromResult(count);
    }

    /// <summary>
    /// Performs a batch delete of multiple keys.
    /// </summary>
    /// <param name="keys">The keys to delete.</param>
    /// <returns>A task that represents the asynchronous operation. The task result contains the number of items deleted.</returns>
    public Task<int> BatchDeleteAsync(IEnumerable<TKey> keys)
    {
        this.ThrowIfDisposed();

        if (keys == null)
        {
            throw new ArgumentNullException(nameof(keys));
        }

        int count = 0;
        foreach (var key in keys)
        {
            if (this.index.TryRemove(key, out _))
            {
                count++;
            }
        }

        return Task.FromResult(count);
    }

    /// <summary>
    /// Finds all keys greater than the specified key.
    /// </summary>
    /// <param name="key">The key to compare against.</param>
    /// <param name="limit">The maximum number of results to return.</param>
    /// <returns>An async enumerable of key-value pairs greater than the specified key.</returns>
    public IAsyncEnumerable<KeyValuePair<TKey, TValue>> FindKeysGreaterThanAsync(TKey key, int limit = int.MaxValue)
    {
        this.ThrowIfDisposed();

        if (key == null)
        {
            throw new ArgumentNullException(nameof(key));
        }

        return this.FindKeysGreaterThanAsyncCore(key, limit);
    }

    private async IAsyncEnumerable<KeyValuePair<TKey, TValue>> FindKeysGreaterThanAsyncCore(TKey key, int limit)
    {
        await Task.Yield(); // Ensure async execution

        var results = this.index
            .Where(kvp => kvp.Key.CompareTo(key) > 0)
            .OrderBy(kvp => kvp.Key)
            .Take(limit)
            .ToList();

        foreach (var kvp in results)
        {
            yield return kvp;
        }
    }

    /// <summary>
    /// Finds all keys less than the specified key.
    /// </summary>
    /// <param name="key">The key to compare against.</param>
    /// <param name="limit">The maximum number of results to return.</param>
    /// <returns>An async enumerable of key-value pairs less than the specified key.</returns>
    public IAsyncEnumerable<KeyValuePair<TKey, TValue>> FindKeysLessThanAsync(TKey key, int limit = int.MaxValue)
    {
        this.ThrowIfDisposed();

        if (key == null)
        {
            throw new ArgumentNullException(nameof(key));
        }

        return this.FindKeysLessThanAsyncCore(key, limit);
    }

    private async IAsyncEnumerable<KeyValuePair<TKey, TValue>> FindKeysLessThanAsyncCore(TKey key, int limit)
    {
        await Task.Yield(); // Ensure async execution

        var results = this.index
            .Where(kvp => kvp.Key.CompareTo(key) < 0)
            .OrderByDescending(kvp => kvp.Key)
            .Take(limit)
            .OrderBy(kvp => kvp.Key)
            .ToList();

        foreach (var kvp in results)
        {
            yield return kvp;
        }
    }

    /// <summary>
    /// Clears all items from the index.
    /// </summary>
    public void Clear()
    {
        this.ThrowIfDisposed();
        this.index.Clear();
    }

    /// <summary>
    /// Gets statistics about the index.
    /// </summary>
    /// <returns>A dictionary containing index statistics.</returns>
    public Dictionary<string, object> GetStatistics()
    {
        this.ThrowIfDisposed();

        return new Dictionary<string, object>
        {
            ["Count"] = this.index.Count,
            ["IsEmpty"] = this.index.IsEmpty,
            ["MinKey"] = this.index.IsEmpty ? "N/A" : this.index.Keys.Min()?.ToString() ?? "null",
            ["MaxKey"] = this.index.IsEmpty ? "N/A" : this.index.Keys.Max()?.ToString() ?? "null",
            ["Type"] = "HashIndex",
            ["ConcurrencyLevel"] = Environment.ProcessorCount // Approximate
        };
    }

    /// <summary>
    /// Flushes any pending operations. For HashIndex, this is a no-op.
    /// </summary>
    /// <returns>A completed task.</returns>
    public Task FlushAsync()
    {
        this.ThrowIfDisposed();
        return Task.CompletedTask;
    }

    /// <inheritdoc />
    public void Dispose()
    {
        this.Dispose(true);
        GC.SuppressFinalize(this);
    }

    /// <summary>
    /// Releases the unmanaged resources used by the HashIndex and optionally releases the managed resources.
    /// </summary>
    /// <param name="disposing">True to release both managed and unmanaged resources; false to release only unmanaged resources.</param>
    protected virtual void Dispose(bool disposing)
    {
        if (!this.disposed && disposing)
        {
            lock (this.disposeLock)
            {
                if (!this.disposed)
                {
                    this.index.Clear();
                    this.disposed = true;
                }
            }
        }
    }

    private void ThrowIfDisposed()
    {
        if (this.disposed)
        {
            throw new ObjectDisposedException(nameof(HashIndex<TKey, TValue>));
        }
    }
}