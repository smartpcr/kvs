#if !NET472
#nullable enable
#endif

using System;
using System.Collections.Concurrent;
using System.Collections.Generic;

namespace Kvs.Core.Database;

/// <summary>
/// Represents the data modified within a transaction.
/// </summary>
internal class TransactionData
{
    private readonly ConcurrentDictionary<string, TransactionDocument> documents;
    private readonly ConcurrentDictionary<string, DocumentVersion> readVersions;

    /// <summary>
    /// Initializes a new instance of the <see cref="TransactionData"/> class.
    /// </summary>
    public TransactionData()
    {
        this.documents = new ConcurrentDictionary<string, TransactionDocument>();
        this.readVersions = new ConcurrentDictionary<string, DocumentVersion>();
    }

    /// <summary>
    /// Gets the modified documents in this transaction.
    /// </summary>
    public IReadOnlyDictionary<string, TransactionDocument> Documents => this.documents;

    /// <summary>
    /// Gets the read versions for this transaction.
    /// </summary>
    public IReadOnlyDictionary<string, DocumentVersion> ReadVersions => this.readVersions;

    /// <summary>
    /// Adds or updates a document in the transaction.
    /// </summary>
    /// <param name="collectionName">The collection name.</param>
    /// <param name="documentId">The document ID.</param>
    /// <param name="document">The document.</param>
    /// <param name="operationType">The operation type.</param>
#if NET8_0_OR_GREATER
    public void SetDocument(string collectionName, string documentId, Document? document, TransactionOperationType operationType)
#else
    public void SetDocument(string collectionName, string documentId, Document document, TransactionOperationType operationType)
#endif
    {
        var key = $"{collectionName}/{documentId}";
        this.documents[key] = new TransactionDocument
        {
            Document = document,
            OperationType = operationType,
            Timestamp = DateTime.UtcNow
        };
    }

    /// <summary>
    /// Gets a document from the transaction if it exists.
    /// </summary>
    /// <param name="collectionName">The collection name.</param>
    /// <param name="documentId">The document ID.</param>
    /// <returns>The transaction document, or null if not found.</returns>
#if NET8_0_OR_GREATER
    public TransactionDocument? GetDocument(string collectionName, string documentId)
#else
    public TransactionDocument GetDocument(string collectionName, string documentId)
#endif
    {
        var key = $"{collectionName}/{documentId}";
        return this.documents.TryGetValue(key, out var doc) ? doc : null;
    }

    /// <summary>
    /// Records the version of a document read by this transaction.
    /// </summary>
    /// <param name="collectionName">The collection name.</param>
    /// <param name="documentId">The document ID.</param>
    /// <param name="version">The version read.</param>
    public void RecordReadVersion(string collectionName, string documentId, long version)
    {
        var key = $"{collectionName}/{documentId}";
        this.readVersions[key] = new DocumentVersion
        {
            Version = version,
            ReadTime = DateTime.UtcNow
        };
    }

    /// <summary>
    /// Gets the version of a document read by this transaction.
    /// </summary>
    /// <param name="collectionName">The collection name.</param>
    /// <param name="documentId">The document ID.</param>
    /// <returns>The document version, or null if not read.</returns>
#if NET8_0_OR_GREATER
    public DocumentVersion? GetReadVersion(string collectionName, string documentId)
#else
    public DocumentVersion GetReadVersion(string collectionName, string documentId)
#endif
    {
        var key = $"{collectionName}/{documentId}";
        return this.readVersions.TryGetValue(key, out var version) ? version : null;
    }

    /// <summary>
    /// Clears all transaction data.
    /// </summary>
    public void Clear()
    {
        this.documents.Clear();
        this.readVersions.Clear();
    }
}

/// <summary>
/// Represents a document within a transaction.
/// </summary>
internal class TransactionDocument
{
    /// <summary>
    /// Gets or sets the document.
    /// </summary>
#if NET8_0_OR_GREATER
    public Document? Document { get; set; }
#else
    public Document Document { get; set; }
#endif

    /// <summary>
    /// Gets or sets the operation type.
    /// </summary>
    public TransactionOperationType OperationType { get; set; }

    /// <summary>
    /// Gets or sets the timestamp of the operation.
    /// </summary>
    public DateTime Timestamp { get; set; }
}

/// <summary>
/// Represents a document version read by a transaction.
/// </summary>
internal class DocumentVersion
{
    /// <summary>
    /// Gets or sets the version number.
    /// </summary>
    public long Version { get; set; }

    /// <summary>
    /// Gets or sets the time when the version was read.
    /// </summary>
    public DateTime ReadTime { get; set; }
}

