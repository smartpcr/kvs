#if !NET472
#nullable enable
#endif

using System.Collections.Generic;
using System.Threading.Tasks;

namespace Kvs.Core.Database;

/// <summary>
/// Represents a collection of documents in the database.
/// </summary>
/// <typeparam name="T">The type of documents in the collection.</typeparam>
public interface ICollection<T>
    where T : class
{
    /// <summary>
    /// Gets the name of the collection.
    /// </summary>
    string Name { get; }

    /// <summary>
    /// Inserts a document into the collection.
    /// </summary>
    /// <param name="document">The document to insert.</param>
    /// <returns>A task that represents the asynchronous operation. The task result contains the document ID.</returns>
    Task<string> InsertAsync(T document);

    /// <summary>
    /// Inserts multiple documents into the collection.
    /// </summary>
    /// <param name="documents">The documents to insert.</param>
    /// <returns>A task that represents the asynchronous operation. The task result contains the document IDs.</returns>
    Task<IEnumerable<string>> InsertManyAsync(IEnumerable<T> documents);

    /// <summary>
    /// Updates a document in the collection.
    /// </summary>
    /// <param name="id">The ID of the document to update.</param>
    /// <param name="document">The updated document.</param>
    /// <returns>A task that represents the asynchronous operation. The task result indicates whether the update was successful.</returns>
    Task<bool> UpdateAsync(string id, T document);

    /// <summary>
    /// Deletes a document from the collection.
    /// </summary>
    /// <param name="id">The ID of the document to delete.</param>
    /// <returns>A task that represents the asynchronous operation. The task result indicates whether the deletion was successful.</returns>
    Task<bool> DeleteAsync(string id);

    /// <summary>
    /// Finds a document by its ID.
    /// </summary>
    /// <param name="id">The ID of the document to find.</param>
    /// <returns>A task that represents the asynchronous operation. The task result contains the document, or null if not found.</returns>
#if NET472
    Task<T> FindByIdAsync(string id);
#else
    Task<T?> FindByIdAsync(string id);
#endif

    /// <summary>
    /// Finds all documents in the collection.
    /// </summary>
    /// <returns>An async enumerable of all documents in the collection.</returns>
    IAsyncEnumerable<T> FindAllAsync();

    /// <summary>
    /// Counts the number of documents in the collection.
    /// </summary>
    /// <returns>A task that represents the asynchronous operation. The task result contains the document count.</returns>
    Task<long> CountAsync();

    /// <summary>
    /// Creates an index on the specified field.
    /// </summary>
    /// <param name="fieldName">The name of the field to index.</param>
    /// <returns>A task that represents the asynchronous operation.</returns>
    Task CreateIndexAsync(string fieldName);

    /// <summary>
    /// Drops an index on the specified field.
    /// </summary>
    /// <param name="fieldName">The name of the field to drop the index on.</param>
    /// <returns>A task that represents the asynchronous operation.</returns>
    Task DropIndexAsync(string fieldName);

    /// <summary>
    /// Clears all documents from the collection.
    /// </summary>
    /// <returns>A task that represents the asynchronous operation.</returns>
    Task ClearAsync();
}
