#if !NET472
#nullable enable
#endif

using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Data;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace Kvs.Core.Database;

/// <summary>
/// Manages document versions for multi-version concurrency control (MVCC).
/// </summary>
internal class VersionManager
{
    private readonly ConcurrentDictionary<string, VersionChain> versionChains;
    private readonly SemaphoreSlim cleanupLock;
    private long globalVersionCounter;

    /// <summary>
    /// Initializes a new instance of the <see cref="VersionManager"/> class.
    /// </summary>
    public VersionManager()
    {
        this.versionChains = new ConcurrentDictionary<string, VersionChain>();
        this.cleanupLock = new SemaphoreSlim(1, 1);
        this.globalVersionCounter = 0;
    }

    /// <summary>
    /// Gets the next version number.
    /// </summary>
    /// <returns>The next version number.</returns>
    public long GetNextVersion()
    {
        return Interlocked.Increment(ref this.globalVersionCounter);
    }

    /// <summary>
    /// Adds a new version of a document.
    /// </summary>
    /// <param name="key">The document key (collection/id).</param>
    /// <param name="document">The document.</param>
    /// <param name="transactionId">The transaction that created this version.</param>
    /// <param name="commitTime">The commit time of the transaction.</param>
    public void AddVersion(string key, Document document, string transactionId, DateTime commitTime)
    {
        var chain = this.versionChains.GetOrAdd(key, k => new VersionChain(k));
        var version = new DocumentVersionEntry
        {
            Document = document.Clone(),
            Version = document.Version,
            TransactionId = transactionId,
            CommitTime = commitTime,
            IsDeleted = false
        };
        chain.AddVersion(version);
    }

    /// <summary>
    /// Marks a document as deleted.
    /// </summary>
    /// <param name="key">The document key (collection/id).</param>
    /// <param name="transactionId">The transaction that deleted the document.</param>
    /// <param name="commitTime">The commit time of the transaction.</param>
    public void MarkDeleted(string key, string transactionId, DateTime commitTime)
    {
        var chain = this.versionChains.GetOrAdd(key, k => new VersionChain(k));
        var version = new DocumentVersionEntry
        {
            Document = null,
            Version = this.GetNextVersion(),
            TransactionId = transactionId,
            CommitTime = commitTime,
            IsDeleted = true
        };
        chain.AddVersion(version);
    }

    /// <summary>
    /// Gets the visible version of a document for a transaction.
    /// </summary>
    /// <param name="key">The document key (collection/id).</param>
    /// <param name="transactionId">The current transaction ID.</param>
    /// <param name="transactionStartTime">The transaction start time.</param>
    /// <param name="isolationLevel">The isolation level.</param>
    /// <returns>The visible document, or null if not visible.</returns>
#if NET8_0_OR_GREATER
    public Document? GetVisibleVersion(string key, string transactionId, DateTime transactionStartTime, IsolationLevel isolationLevel)
#else
    public Document GetVisibleVersion(string key, string transactionId, DateTime transactionStartTime, IsolationLevel isolationLevel)
#endif
    {
        if (!this.versionChains.TryGetValue(key, out var chain))
        {
            return null;
        }

        var version = chain.GetVisibleVersion(transactionId, transactionStartTime, isolationLevel);
        return version?.Document;
    }

    /// <summary>
    /// Cleans up old versions that are no longer needed.
    /// </summary>
    /// <param name="activeTransactions">The list of active transaction IDs.</param>
    /// <returns>A task that represents the asynchronous operation.</returns>
    public async Task CleanupOldVersionsAsync(HashSet<string> activeTransactions)
    {
        await this.cleanupLock.WaitAsync().ConfigureAwait(false);
        try
        {
            foreach (var chain in this.versionChains.Values)
            {
                chain.CleanupOldVersions(activeTransactions);
            }

            // Remove empty chains
            var emptyChains = this.versionChains.Where(kvp => kvp.Value.IsEmpty).Select(kvp => kvp.Key).ToList();
            foreach (var key in emptyChains)
            {
                this.versionChains.TryRemove(key, out _);
            }
        }
        finally
        {
            this.cleanupLock.Release();
        }
    }
}

/// <summary>
/// Represents a chain of versions for a single document.
/// </summary>
internal class VersionChain
{
    private readonly string key;
    private readonly List<DocumentVersionEntry> versions;
    private readonly ReaderWriterLockSlim versionLock;

    /// <summary>
    /// Initializes a new instance of the <see cref="VersionChain"/> class.
    /// </summary>
    /// <param name="key">The document key.</param>
    public VersionChain(string key)
    {
        this.key = key;
        this.versions = new List<DocumentVersionEntry>();
        this.versionLock = new ReaderWriterLockSlim();
    }

    /// <summary>
    /// Gets a value indicating whether the chain is empty.
    /// </summary>
    public bool IsEmpty
    {
        get
        {
            this.versionLock.EnterReadLock();
            try
            {
                return this.versions.Count == 0;
            }
            finally
            {
                this.versionLock.ExitReadLock();
            }
        }
    }

    /// <summary>
    /// Adds a new version to the chain.
    /// </summary>
    /// <param name="version">The version to add.</param>
    public void AddVersion(DocumentVersionEntry version)
    {
        this.versionLock.EnterWriteLock();
        try
        {
            this.versions.Add(version);

            // Keep versions sorted by commit time
            this.versions.Sort((a, b) => a.CommitTime.CompareTo(b.CommitTime));
        }
        finally
        {
            this.versionLock.ExitWriteLock();
        }
    }

    /// <summary>
    /// Gets the visible version for a transaction based on isolation level.
    /// </summary>
    /// <param name="transactionId">The current transaction ID.</param>
    /// <param name="transactionStartTime">The transaction start time.</param>
    /// <param name="isolationLevel">The isolation level.</param>
    /// <returns>The visible version entry, or null if not visible.</returns>
#if NET8_0_OR_GREATER
    public DocumentVersionEntry? GetVisibleVersion(string transactionId, DateTime transactionStartTime, IsolationLevel isolationLevel)
#else
    public DocumentVersionEntry GetVisibleVersion(string transactionId, DateTime transactionStartTime, IsolationLevel isolationLevel)
#endif
    {
        this.versionLock.EnterReadLock();
        try
        {
            // For Read Uncommitted, return the latest version
            if (isolationLevel == IsolationLevel.ReadUncommitted)
            {
                return this.versions.LastOrDefault();
            }

            // For other isolation levels, find the latest version committed before transaction start
#if NET8_0_OR_GREATER
            DocumentVersionEntry? visibleVersion = null;
#else
            DocumentVersionEntry visibleVersion = null;
#endif
            foreach (var version in this.versions)
            {
                // Skip deleted versions unless they are the latest
                if (version.IsDeleted && version != this.versions.LastOrDefault())
                {
                    continue;
                }

                // Always allow reading own changes
                if (version.TransactionId == transactionId)
                {
                    visibleVersion = version;
                    continue;
                }

                // For ReadCommitted, see all committed versions (versions are only added after commit)
                if (isolationLevel == IsolationLevel.ReadCommitted)
                {
                    visibleVersion = version;
                    continue;
                }

                // For Serializable and RepeatableRead, only see versions committed before transaction start
                if ((isolationLevel == IsolationLevel.Serializable || isolationLevel == IsolationLevel.RepeatableRead) &&
                    version.CommitTime > transactionStartTime)
                {
                    continue;
                }

                visibleVersion = version;
            }

            return visibleVersion;
        }
        finally
        {
            this.versionLock.ExitReadLock();
        }
    }

    /// <summary>
    /// Cleans up old versions that are no longer needed by any active transaction.
    /// </summary>
    /// <param name="activeTransactions">The set of active transaction IDs.</param>
    public void CleanupOldVersions(HashSet<string> activeTransactions)
    {
        this.versionLock.EnterWriteLock();
        try
        {
            if (this.versions.Count <= 1)
            {
                return;
            }

            // Keep at least the latest version
            var latestVersion = this.versions[this.versions.Count - 1];
            var versionsToKeep = new List<DocumentVersionEntry> { latestVersion };

            // Keep versions that might be needed by active transactions
            foreach (var version in this.versions.Take(this.versions.Count - 1).Where(v => activeTransactions.Contains(v.TransactionId)))
            {
                versionsToKeep.Add(version);
            }

            this.versions.Clear();
            this.versions.AddRange(versionsToKeep.OrderBy(v => v.CommitTime));
        }
        finally
        {
            this.versionLock.ExitWriteLock();
        }
    }
}

/// <summary>
/// Represents a version entry for a document.
/// </summary>
internal class DocumentVersionEntry
{
    /// <summary>
    /// Gets or sets the document (null if deleted).
    /// </summary>
#if NET8_0_OR_GREATER
    public Document? Document { get; set; }
#else
    public Document Document { get; set; }
#endif

    /// <summary>
    /// Gets or sets the version number.
    /// </summary>
    public long Version { get; set; }

    /// <summary>
    /// Gets or sets the transaction ID that created this version.
    /// </summary>
    public string TransactionId { get; set; } = string.Empty;

    /// <summary>
    /// Gets or sets the commit time of the transaction.
    /// </summary>
    public DateTime CommitTime { get; set; }

    /// <summary>
    /// Gets or sets a value indicating whether this version represents a deletion.
    /// </summary>
    public bool IsDeleted { get; set; }
}

