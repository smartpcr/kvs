using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace Kvs.Core.Storage;

/// <summary>
/// Defines the contract for managing pages in the storage system.
/// </summary>
public interface IPageManager : IDisposable
{
    /// <summary>
    /// Allocates a new page of the specified type.
    /// </summary>
    /// <param name="pageType">The type of page to allocate.</param>
    /// <returns>A task that represents the asynchronous operation. The task result contains the newly allocated page.</returns>
    Task<Page> AllocatePageAsync(PageType pageType);

    /// <summary>
    /// Retrieves a page by its ID.
    /// </summary>
    /// <param name="pageId">The ID of the page to retrieve.</param>
    /// <returns>A task that represents the asynchronous operation. The task result contains the requested page.</returns>
    Task<Page> GetPageAsync(long pageId);

    /// <summary>
    /// Writes a page to persistent storage.
    /// </summary>
    /// <param name="page">The page to write.</param>
    /// <returns>A task that represents the asynchronous write operation.</returns>
    Task WritePageAsync(Page page);

    /// <summary>
    /// Frees a page, making it available for reuse.
    /// </summary>
    /// <param name="pageId">The ID of the page to free.</param>
    /// <returns>A task that represents the asynchronous free operation.</returns>
    Task FreePageAsync(long pageId);

    /// <summary>
    /// Gets the total number of pages in the storage.
    /// </summary>
    /// <returns>A task that represents the asynchronous operation. The task result contains the number of pages.</returns>
    Task<long> GetPageCountAsync();

    /// <summary>
    /// Flushes all cached pages to persistent storage.
    /// </summary>
    /// <returns>A task that represents the asynchronous flush operation.</returns>
    Task FlushAsync();

    /// <summary>
    /// Checks whether a page with the specified ID exists and is not freed.
    /// </summary>
    /// <param name="pageId">The ID of the page to check.</param>
    /// <returns>A task that represents the asynchronous operation. The task result indicates whether the page exists.</returns>
    Task<bool> PageExistsAsync(long pageId);
}

/// <summary>
/// Manages pages in the storage system with caching support.
/// </summary>
public class PageManager(IStorageEngine storageEngine, int pageSize = Page.DefaultPageSize, int maxCacheSize = 1000) : IPageManager
{
    private readonly IStorageEngine storageEngine = storageEngine ?? throw new ArgumentNullException(nameof(storageEngine));
    private readonly ConcurrentDictionary<long, Page> pageCache = new ConcurrentDictionary<long, Page>();
    private readonly Queue<long> freePages = new Queue<long>();
    private readonly ReaderWriterLockSlim freeListLock = new ReaderWriterLockSlim();
    private readonly SemaphoreSlim flushSemaphore = new SemaphoreSlim(1, 1);
    private long nextPageId = 0;
    private readonly int pageSize = pageSize;
    private readonly int maxCacheSize = maxCacheSize;
    private bool disposed;

    /// <inheritdoc />
    public async Task<Page> AllocatePageAsync(PageType pageType)
    {
        this.ThrowIfDisposed();

        long pageId;

        this.freeListLock.EnterWriteLock();
        try
        {
            pageId = this.freePages.Count > 0 ? this.freePages.Dequeue() : Interlocked.Increment(ref this.nextPageId);
        }
        finally
        {
            this.freeListLock.ExitWriteLock();
        }

        var page = new Page(pageId, pageType, this.pageSize);
        await this.WritePageAsync(page);

        return page;
    }

    /// <inheritdoc />
    public async Task<Page> GetPageAsync(long pageId)
    {
        this.ThrowIfDisposed();

        if (this.pageCache.TryGetValue(pageId, out var cachedPage))
        {
            return cachedPage;
        }

        var position = pageId * this.pageSize;
        var data = await this.storageEngine.ReadAsync(position, this.pageSize);

        if (data.Length == 0)
        {
            throw new InvalidOperationException($"Page {pageId} does not exist");
        }

        var page = new Page(data.Span);

        // Add to cache if there's space
        if (this.pageCache.Count < this.maxCacheSize)
        {
            this.pageCache.TryAdd(pageId, page);
        }

        return page;
    }

    /// <inheritdoc />
    public async Task WritePageAsync(Page page)
    {
        this.ThrowIfDisposed();

        var position = page.PageId * this.pageSize;
        await this.storageEngine.WriteAsync(page.BufferMemory);

        // Update cache
#if NET472
        this.pageCache.AddOrUpdate(page.PageId, page, (key, oldValue) => page);
#else
        this.pageCache.AddOrUpdate(page.PageId, page, (_, _) => page);
#endif
    }

    /// <inheritdoc />
    public async Task FreePageAsync(long pageId)
    {
        this.ThrowIfDisposed();

        this.freeListLock.EnterWriteLock();
        try
        {
            this.freePages.Enqueue(pageId);
        }
        finally
        {
            this.freeListLock.ExitWriteLock();
        }

        // Clear the page on disk
        var emptyPage = new Page(pageId, PageType.Free, this.pageSize);
        var position = pageId * this.pageSize;
        await this.storageEngine.WriteAsync(emptyPage.BufferMemory);

        // Remove from cache after writing to disk
        this.pageCache.TryRemove(pageId, out _);
    }

    /// <inheritdoc />
    public async Task<long> GetPageCountAsync()
    {
        this.ThrowIfDisposed();

        var fileSize = await this.storageEngine.GetSizeAsync();
        return fileSize / this.pageSize;
    }

    /// <inheritdoc />
    public async Task<bool> PageExistsAsync(long pageId)
    {
        this.ThrowIfDisposed();

        if (this.pageCache.ContainsKey(pageId))
        {
            return true;
        }

        var fileSize = await this.storageEngine.GetSizeAsync();
        var maxPageId = (fileSize / this.pageSize) - 1;

        if (pageId < 0 || pageId > maxPageId)
        {
            return false;
        }

        // Check if the page is marked as free by reading directly from storage
        try
        {
            var position = pageId * this.pageSize;
            var pageData = await this.storageEngine.ReadAsync(position, this.pageSize);
            var page = new Page(pageData.Span);
            return page.PageType != PageType.Free;
        }
        catch
        {
            return false;
        }
    }

    /// <inheritdoc />
    public async Task FlushAsync()
    {
        this.ThrowIfDisposed();

        await this.flushSemaphore.WaitAsync();
        try
        {
            // Write all cached pages to disk
            var tasks = this.pageCache.Values.Select(async page =>
            {
                var position = page.PageId * this.pageSize;
                await this.storageEngine.WriteAsync(page.BufferMemory);
            });

            await Task.WhenAll(tasks);
            await this.storageEngine.FlushAsync();
        }
        finally
        {
            this.flushSemaphore.Release();
        }
    }

    private void ThrowIfDisposed()
    {
        if (this.disposed)
        {
            throw new ObjectDisposedException(nameof(PageManager));
        }
    }

    /// <inheritdoc />
    public void Dispose()
    {
        if (this.disposed)
        {
            return;
        }

        this.disposed = true;

        this.flushSemaphore?.Dispose();
        this.freeListLock?.Dispose();
        this.pageCache?.Clear();
    }

/* Unmerged change from project 'Kvs.Core(net8.0)'
Before:
}
After:
}
*/
}
