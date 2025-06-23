#if !NET472
#nullable enable
#endif

using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Data;
using System.Linq;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Kvs.Core.DataStructures;
using Kvs.Core.Indexing;
using Kvs.Core.Serialization;
using Kvs.Core.Storage;

namespace Kvs.Core.Database;

/// <summary>
/// Represents a collection of documents in the database.
/// </summary>
/// <typeparam name="T">The type of documents in the collection.</typeparam>
public class Collection<T> : ICollection<T>
    where T : class
{
    private readonly string name;
    private readonly Database database;
    private readonly ConcurrentDictionary<string, IIndex<string, long>> indexes;
    private readonly ISerializer serializer;
    private readonly SemaphoreSlim collectionLock;
#if NET8_0_OR_GREATER
    private BTreeIndex<string, Document>? primaryIndex;
#else
    private BTreeIndex<string, Document> primaryIndex;
#endif
    private long documentCount;

    /// <summary>
    /// Gets the name of the collection.
    /// </summary>
    public string Name => this.name;

    /// <summary>
    /// Initializes a new instance of the <see cref="Collection{T}"/> class.
    /// </summary>
    /// <param name="name">The name of the collection.</param>
    /// <param name="database">The database instance.</param>
    internal Collection(string name, Database database)
    {
        this.name = name ?? throw new ArgumentNullException(nameof(name));
        this.database = database ?? throw new ArgumentNullException(nameof(database));
        this.indexes = new ConcurrentDictionary<string, IIndex<string, long>>();
        this.serializer = new BinarySerializer();
        this.collectionLock = new SemaphoreSlim(1, 1);

        this.InitializeAsync().GetAwaiter().GetResult();
    }

    private async Task InitializeAsync()
    {
        this.primaryIndex = new BTreeIndex<string, Document>(32);

        var rootPageId = await this.GetCollectionRootPageIdAsync().ConfigureAwait(false);
        if (rootPageId > 0)
        {
            await this.LoadCollectionMetadataAsync(rootPageId).ConfigureAwait(false);
        }
        else
        {
            await this.CreateCollectionMetadataAsync().ConfigureAwait(false);
        }
    }

    /// <summary>
    /// Inserts a document into the collection.
    /// </summary>
    /// <param name="document">The document to insert.</param>
    /// <returns>A task that represents the asynchronous operation. The task result contains the document ID.</returns>
    public async Task<string> InsertAsync(T document)
    {
        if (document == null)
        {
            throw new ArgumentNullException(nameof(document));
        }

        await this.collectionLock.WaitAsync().ConfigureAwait(false);
        try
        {
            var doc = this.ConvertToDocument(document);
            doc.Version = 1;
            doc.Created = DateTime.UtcNow;
            doc.Updated = DateTime.UtcNow;

            if (await this.primaryIndex!.GetAsync(doc.Id).ConfigureAwait(false) != null)
            {
                throw new InvalidOperationException($"Document with ID '{doc.Id}' already exists.");
            }

            await this.primaryIndex.PutAsync(doc.Id, doc).ConfigureAwait(false);

            await this.UpdateIndexesAsync(doc, null).ConfigureAwait(false);

            var entry = new TransactionLogEntry
            {
                TransactionId = Guid.NewGuid().ToString(),
                Type = TransactionLogEntryType.Insert,
                CollectionName = this.name,
                Key = doc.Id,
                Data = this.serializer.Serialize(doc),
                Timestamp = DateTime.UtcNow
            };

            var wal = this.database.GetDatabaseWAL();
            await wal.WriteEntryAsync(entry).ConfigureAwait(false);

            // Add to version manager for MVCC
            if (this.database is ITransactionContext transactionContext)
            {
                var versionManager = transactionContext.VersionManager;
                versionManager.AddVersion($"{this.name}/{doc.Id}", doc, entry.TransactionId, entry.Timestamp);
            }

            Interlocked.Increment(ref this.documentCount);

            return doc.Id;
        }
        finally
        {
            this.collectionLock.Release();
        }
    }

    /// <summary>
    /// Inserts multiple documents into the collection.
    /// </summary>
    /// <param name="documents">The documents to insert.</param>
    /// <returns>A task that represents the asynchronous operation. The task result contains the document IDs.</returns>
    public async Task<IEnumerable<string>> InsertManyAsync(IEnumerable<T> documents)
    {
        if (documents == null)
        {
            throw new ArgumentNullException(nameof(documents));
        }

        var ids = new List<string>();
        foreach (var document in documents)
        {
            var id = await this.InsertAsync(document).ConfigureAwait(false);
            ids.Add(id);
        }

        return ids;
    }

    /// <summary>
    /// Updates a document in the collection.
    /// </summary>
    /// <param name="id">The ID of the document to update.</param>
    /// <param name="document">The updated document.</param>
    /// <returns>A task that represents the asynchronous operation. The task result indicates whether the update was successful.</returns>
    public async Task<bool> UpdateAsync(string id, T document)
    {
        if (string.IsNullOrEmpty(id))
        {
            throw new ArgumentException("Document ID cannot be null or empty.", nameof(id));
        }

        if (document == null)
        {
            throw new ArgumentNullException(nameof(document));
        }

        await this.collectionLock.WaitAsync().ConfigureAwait(false);
        try
        {
            var existingDoc = await this.primaryIndex!.GetAsync(id).ConfigureAwait(false);
            if (existingDoc == null)
            {
                return false;
            }

            var newDoc = this.ConvertToDocument(document);
            newDoc.Id = id;
            newDoc.Version = existingDoc.Version + 1;
            newDoc.Created = existingDoc.Created;
            newDoc.Updated = DateTime.UtcNow;

            await this.primaryIndex.PutAsync(id, newDoc).ConfigureAwait(false);

            await this.UpdateIndexesAsync(newDoc, existingDoc).ConfigureAwait(false);

            var entry = new TransactionLogEntry
            {
                TransactionId = Guid.NewGuid().ToString(),
                Type = TransactionLogEntryType.Update,
                CollectionName = this.name,
                Key = id,
                Data = this.serializer.Serialize(newDoc),
                OldData = this.serializer.Serialize(existingDoc),
                Timestamp = DateTime.UtcNow
            };

            var wal = this.database.GetDatabaseWAL();
            await wal.WriteEntryAsync(entry).ConfigureAwait(false);

            // Add to version manager for MVCC
            if (this.database is ITransactionContext transactionContext)
            {
                var versionManager = transactionContext.VersionManager;
                versionManager.AddVersion($"{this.name}/{id}", newDoc, entry.TransactionId, entry.Timestamp);
            }

            return true;
        }
        finally
        {
            this.collectionLock.Release();
        }
    }

    /// <summary>
    /// Deletes a document from the collection.
    /// </summary>
    /// <param name="id">The ID of the document to delete.</param>
    /// <returns>A task that represents the asynchronous operation. The task result indicates whether the deletion was successful.</returns>
    public async Task<bool> DeleteAsync(string id)
    {
        if (string.IsNullOrEmpty(id))
        {
            throw new ArgumentException("Document ID cannot be null or empty.", nameof(id));
        }

        await this.collectionLock.WaitAsync().ConfigureAwait(false);
        try
        {
            var existingDoc = await this.primaryIndex!.GetAsync(id).ConfigureAwait(false);
            if (existingDoc == null)
            {
                return false;
            }

            var deleted = await this.primaryIndex.DeleteAsync(id).ConfigureAwait(false);
            if (!deleted)
            {
                return false;
            }

            await this.RemoveFromIndexesAsync(existingDoc).ConfigureAwait(false);

            var entry = new TransactionLogEntry
            {
                TransactionId = Guid.NewGuid().ToString(),
                Type = TransactionLogEntryType.Delete,
                CollectionName = this.name,
                Key = id,
                OldData = this.serializer.Serialize(existingDoc),
                Timestamp = DateTime.UtcNow
            };

            var wal = this.database.GetDatabaseWAL();
            await wal.WriteEntryAsync(entry).ConfigureAwait(false);

            // Mark as deleted in version manager for MVCC
            if (this.database is ITransactionContext transactionContext)
            {
                var versionManager = transactionContext.VersionManager;
                versionManager.MarkDeleted($"{this.name}/{id}", entry.TransactionId, entry.Timestamp);
            }

            Interlocked.Decrement(ref this.documentCount);

            return true;
        }
        finally
        {
            this.collectionLock.Release();
        }
    }

    /// <summary>
    /// Finds a document by its ID.
    /// </summary>
    /// <param name="id">The ID of the document to find.</param>
    /// <returns>A task that represents the asynchronous operation. The task result contains the document, or null if not found.</returns>
#if NET472
    public async Task<T> FindByIdAsync(string id)
#else
    public async Task<T?> FindByIdAsync(string id)
#endif
    {
        if (string.IsNullOrEmpty(id))
        {
            throw new ArgumentException("Document ID cannot be null or empty.", nameof(id));
        }

        var doc = await this.primaryIndex!.GetAsync(id).ConfigureAwait(false);
        if (doc == null)
        {
            return null;
        }

        // Clone the document to prevent modifications from affecting the stored version
        var clonedDoc = doc.Clone();
        return this.ConvertFromDocument(clonedDoc);
    }

    /// <summary>
    /// Finds a document by its ID with transaction context.
    /// </summary>
    /// <param name="id">The ID of the document to find.</param>
    /// <param name="transactionId">The transaction ID.</param>
    /// <returns>A task that represents the asynchronous operation. The task result contains the document, or null if not found.</returns>
#if NET472
    internal async Task<T> FindByIdWithTransactionAsync(string id, string transactionId)
#else
    internal async Task<T?> FindByIdWithTransactionAsync(string id, string transactionId)
#endif
    {
        if (string.IsNullOrEmpty(id))
        {
            throw new ArgumentException("Document ID cannot be null or empty.", nameof(id));
        }

        // Check if the database supports transaction context
        if (this.database is ITransactionContext transactionContext && !string.IsNullOrEmpty(transactionId))
        {
            // Check transaction data first - this contains uncommitted changes
            var transactionData = transactionContext.GetTransactionData(transactionId);
            if (transactionData != null)
            {
                var transactionDoc = transactionData.GetDocument(this.name, id);
                if (transactionDoc != null)
                {
                    if (transactionDoc.OperationType == TransactionOperationType.Delete)
                    {
                        return null;
                    }

                    if (transactionDoc.Document != null)
                    {
                        return this.ConvertFromDocument(transactionDoc.Document);
                    }
                }
            }

            // Get visible version from version manager based on isolation level
            var isolationLevel = transactionContext.GetIsolationLevel(transactionId);
            var transactionStartTime = transactionContext.GetTransactionStartTime(transactionId);
            var versionManager = transactionContext.VersionManager;
            var key = $"{this.name}/{id}";

            var visibleDoc = versionManager.GetVisibleVersion(key, transactionId, transactionStartTime, isolationLevel);
            if (visibleDoc != null)
            {
                // Record read version for repeatable read and serializable
                if (transactionData != null && (isolationLevel == IsolationLevel.RepeatableRead || isolationLevel == IsolationLevel.Serializable))
                {
                    transactionData.RecordReadVersion(this.name, id, visibleDoc.Version);
                }

                // Clone to prevent modifications from affecting the version manager
                var clonedVisibleDoc = visibleDoc.Clone();
                return this.ConvertFromDocument(clonedVisibleDoc);
            }

            // If no version in version manager, check primary index
            var doc = await this.primaryIndex!.GetAsync(id).ConfigureAwait(false);
            if (doc == null)
            {
                return null;
            }

            // For repeatable read and serializable, check if document was created after transaction start
            if ((isolationLevel == IsolationLevel.RepeatableRead || isolationLevel == IsolationLevel.Serializable) &&
                doc.Created > transactionStartTime)
            {
                return null; // Document not visible to this transaction
            }

            // Clone the document to prevent modifications from affecting the stored version
            var clonedDoc = doc.Clone();

            // Record read version for repeatable read and serializable
            if (transactionData != null && (isolationLevel == IsolationLevel.RepeatableRead || isolationLevel == IsolationLevel.Serializable))
            {
                transactionData.RecordReadVersion(this.name, id, clonedDoc.Version);
            }

            return this.ConvertFromDocument(clonedDoc);
        }

        // No transaction context, use regular read
        var document = await this.primaryIndex!.GetAsync(id).ConfigureAwait(false);
        if (document == null)
        {
            return null;
        }

        // Clone the document to prevent modifications from affecting the stored version
        var clonedDocument = document.Clone();
        return this.ConvertFromDocument(clonedDocument);
    }

    /// <summary>
    /// Finds all documents in the collection.
    /// </summary>
    /// <returns>An async enumerable of all documents in the collection.</returns>
    public async IAsyncEnumerable<T> FindAllAsync()
    {
        await foreach (var kvp in this.primaryIndex!.RangeAsync(string.Empty, "￿"))
        {
            yield return this.ConvertFromDocument(kvp.Value);
        }
    }

    /// <summary>
    /// Finds all documents in the collection with transaction context.
    /// </summary>
    /// <param name="transactionId">The transaction ID.</param>
    /// <returns>An async enumerable of all documents in the collection.</returns>
    internal async IAsyncEnumerable<T> FindAllWithTransactionAsync(string transactionId)
    {
        // Check if we need range locking for serializable isolation
        if (this.database is ITransactionContext transactionContext && !string.IsNullOrEmpty(transactionId))
        {
            var isolationLevel = transactionContext.GetIsolationLevel(transactionId);
            if (isolationLevel == IsolationLevel.Serializable)
            {
                // Acquire range lock for the entire collection
                var lockManager = this.database.GetLockManager();
                var lockAcquired = await lockManager.AcquireRangeLockAsync(
                    transactionId, this.name, string.Empty, "￿", LockType.Read, TimeSpan.FromSeconds(30)).ConfigureAwait(false);

                if (!lockAcquired)
                {
                    throw new TimeoutException($"Failed to acquire range lock for collection '{this.name}'");
                }
            }
        }

        await foreach (var kvp in this.primaryIndex!.RangeAsync(string.Empty, "￿"))
        {
            // Use transaction-aware read for each document
            if (!string.IsNullOrEmpty(transactionId))
            {
                var doc = await this.FindByIdWithTransactionAsync(kvp.Value.Id, transactionId).ConfigureAwait(false);
                if (doc != null)
                {
                    yield return doc;
                }
            }
            else
            {
                yield return this.ConvertFromDocument(kvp.Value);
            }
        }
    }

    /// <summary>
    /// Counts the number of documents in the collection.
    /// </summary>
    /// <returns>A task that represents the asynchronous operation. The task result contains the document count.</returns>
    public Task<long> CountAsync()
    {
        return Task.FromResult(Interlocked.Read(ref this.documentCount));
    }

    /// <summary>
    /// Creates an index on the specified field.
    /// </summary>
    /// <param name="fieldName">The name of the field to index.</param>
    /// <returns>A task that represents the asynchronous operation.</returns>
    public async Task CreateIndexAsync(string fieldName)
    {
        if (string.IsNullOrEmpty(fieldName))
        {
            throw new ArgumentException("Field name cannot be null or empty.", nameof(fieldName));
        }

        if (this.indexes.ContainsKey(fieldName))
        {
            return;
        }

        await this.collectionLock.WaitAsync().ConfigureAwait(false);
        try
        {
            if (this.indexes.ContainsKey(fieldName))
            {
                return;
            }

            var index = new BTreeIndex<string, long>(32);

            await foreach (var kvp in this.primaryIndex!.RangeAsync(string.Empty, "￿"))
            {
                var doc = kvp.Value;
                var fieldValue = this.GetFieldValue(doc, fieldName);
                if (fieldValue != null)
                {
                    await index.PutAsync(fieldValue, kvp.Key.GetHashCode()).ConfigureAwait(false);
                }
            }

            this.indexes[fieldName] = index;
        }
        finally
        {
            this.collectionLock.Release();
        }
    }

    /// <summary>
    /// Drops an index on the specified field.
    /// </summary>
    /// <param name="fieldName">The name of the field to drop the index on.</param>
    /// <returns>A task that represents the asynchronous operation.</returns>
    public async Task DropIndexAsync(string fieldName)
    {
        if (string.IsNullOrEmpty(fieldName))
        {
            throw new ArgumentException("Field name cannot be null or empty.", nameof(fieldName));
        }

        await this.collectionLock.WaitAsync().ConfigureAwait(false);
        try
        {
            if (this.indexes.TryRemove(fieldName, out var index))
            {
                index.Dispose();
            }
        }
        finally
        {
            this.collectionLock.Release();
        }
    }

    /// <summary>
    /// Clears all documents from the collection.
    /// </summary>
    /// <returns>A task that represents the asynchronous operation.</returns>
    public async Task ClearAsync()
    {
        await this.collectionLock.WaitAsync().ConfigureAwait(false);
        try
        {
            var ids = new List<string>();
            await foreach (var kvp in this.primaryIndex!.RangeAsync(string.Empty, "￿"))
            {
                ids.Add(kvp.Key);
            }

            foreach (var id in ids)
            {
                await this.primaryIndex.DeleteAsync(id).ConfigureAwait(false);
            }

            foreach (var index in this.indexes.Values)
            {
                await foreach (var kvp in index.RangeAsync(string.Empty, "￿"))
                {
                    await index.DeleteAsync(kvp.Key).ConfigureAwait(false);
                }
            }

            Interlocked.Exchange(ref this.documentCount, 0);

            var entry = new TransactionLogEntry
            {
                TransactionId = Guid.NewGuid().ToString(),
                Type = TransactionLogEntryType.Clear,
                CollectionName = this.name,
                Timestamp = DateTime.UtcNow
            };

            var wal = this.database.GetDatabaseWAL();
            await wal.WriteEntryAsync(entry).ConfigureAwait(false);
        }
        finally
        {
            this.collectionLock.Release();
        }
    }

    private Document ConvertToDocument(T obj)
    {
        if (obj is Document doc)
        {
            return doc;
        }

        return Document.FromObject(obj);
    }

    private T ConvertFromDocument(Document doc)
    {
        if (typeof(T) == typeof(Document))
        {
            return (T)(object)doc;
        }

        return doc.ToObject<T>();
    }

#if NET8_0_OR_GREATER
    private async Task UpdateIndexesAsync(Document newDoc, Document? oldDoc)
#else
    private async Task UpdateIndexesAsync(Document newDoc, Document oldDoc)
#endif
    {
        foreach (var kvp in this.indexes)
        {
            var fieldName = kvp.Key;
            var index = kvp.Value;

            if (oldDoc != null)
            {
                var oldValue = this.GetFieldValue(oldDoc, fieldName);
                if (oldValue != null)
                {
                    await index.DeleteAsync(oldValue).ConfigureAwait(false);
                }
            }

            var newValue = this.GetFieldValue(newDoc, fieldName);
            if (newValue != null)
            {
                await index.PutAsync(newValue, newDoc.Id.GetHashCode()).ConfigureAwait(false);
            }
        }
    }

    private async Task RemoveFromIndexesAsync(Document doc)
    {
        foreach (var kvp in this.indexes)
        {
            var fieldName = kvp.Key;
            var index = kvp.Value;

            var value = this.GetFieldValue(doc, fieldName);
            if (value != null)
            {
                await index.DeleteAsync(value).ConfigureAwait(false);
            }
        }
    }

#if NET8_0_OR_GREATER
    private string? GetFieldValue(Document doc, string fieldName)
#else
    private string GetFieldValue(Document doc, string fieldName)
#endif
    {
        var value = doc.Get<object>(fieldName);
        return value?.ToString();
    }

    private Task<long> GetCollectionRootPageIdAsync()
    {
        return Task.FromResult(0L);
    }

    private async Task LoadCollectionMetadataAsync(long rootPageId)
    {
        // rootPageId parameter will be used in future implementation
        _ = rootPageId;
        this.documentCount = await this.primaryIndex!.CountAsync().ConfigureAwait(false);
    }

    private async Task CreateCollectionMetadataAsync()
    {
        var pageManager = this.database.GetPageManager();
        var page = await pageManager.AllocatePageAsync(PageType.Data).ConfigureAwait(false);

        var metadata = new Dictionary<string, object>
        {
            ["collection_name"] = this.name,
            ["created_at"] = DateTime.UtcNow,
            ["document_count"] = 0L
        };

        var data = this.serializer.Serialize(metadata);
        page.WriteData(data.Span);
        await pageManager.WritePageAsync(page).ConfigureAwait(false);
    }
}
