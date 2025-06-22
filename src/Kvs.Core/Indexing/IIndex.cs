#nullable enable
using System;
using System.Collections.Generic;
using System.Threading.Tasks;

namespace Kvs.Core.Indexing;

/// <summary>
/// Defines the contract for an index that provides fast key-value lookups and range queries.
/// </summary>
/// <typeparam name="TKey">The type of the key.</typeparam>
/// <typeparam name="TValue">The type of the value.</typeparam>
public interface IIndex<TKey, TValue> : IDisposable
    where TKey : IComparable<TKey>
{
    /// <summary>
    /// Gets the value associated with the specified key.
    /// </summary>
    /// <param name="key">The key to search for.</param>
    /// <returns>A task that represents the asynchronous operation. The task result contains the value if found; otherwise, the default value.</returns>
    Task<TValue?> GetAsync(TKey key);

    /// <summary>
    /// Inserts or updates a key-value pair in the index.
    /// </summary>
    /// <param name="key">The key to insert or update.</param>
    /// <param name="value">The value to associate with the key.</param>
    /// <returns>A task that represents the asynchronous operation.</returns>
    Task PutAsync(TKey key, TValue value);

    /// <summary>
    /// Removes the entry with the specified key from the index.
    /// </summary>
    /// <param name="key">The key to remove.</param>
    /// <returns>A task that represents the asynchronous operation. The task result indicates whether the key was found and removed.</returns>
    Task<bool> DeleteAsync(TKey key);

    /// <summary>
    /// Determines whether the index contains the specified key.
    /// </summary>
    /// <param name="key">The key to search for.</param>
    /// <returns>A task that represents the asynchronous operation. The task result indicates whether the key exists.</returns>
    Task<bool> ContainsKeyAsync(TKey key);

    /// <summary>
    /// Returns an async enumerable of key-value pairs within the specified range.
    /// </summary>
    /// <param name="startKey">The start key of the range (inclusive).</param>
    /// <param name="endKey">The end key of the range (inclusive).</param>
    /// <returns>An async enumerable of key-value pairs within the range.</returns>
    IAsyncEnumerable<KeyValuePair<TKey, TValue>> RangeAsync(TKey startKey, TKey endKey);

    /// <summary>
    /// Returns an async enumerable of all key-value pairs in the index.
    /// </summary>
    /// <returns>An async enumerable of all key-value pairs.</returns>
    IAsyncEnumerable<KeyValuePair<TKey, TValue>> GetAllAsync();

    /// <summary>
    /// Gets the number of entries in the index.
    /// </summary>
    /// <returns>A task that represents the asynchronous operation. The task result contains the count of entries.</returns>
    Task<long> CountAsync();

    /// <summary>
    /// Flushes any cached changes to persistent storage.
    /// </summary>
    /// <returns>A task that represents the asynchronous flush operation.</returns>
    Task FlushAsync();

    /// <summary>
    /// Gets the first (minimum) key in the index.
    /// </summary>
    /// <returns>A task that represents the asynchronous operation. The task result contains the minimum key if the index is not empty.</returns>
    Task<TKey?> GetMinKeyAsync();

    /// <summary>
    /// Gets the last (maximum) key in the index.
    /// </summary>
    /// <returns>A task that represents the asynchronous operation. The task result contains the maximum key if the index is not empty.</returns>
    Task<TKey?> GetMaxKeyAsync();
}
