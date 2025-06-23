using System;
using System.Threading.Tasks;
using Kvs.Core.TestUtilities;

namespace Kvs.Core.UnitTests.TestBase;

/// <summary>
/// Base class for tests that need async disposal support.
/// </summary>
public abstract class AsyncTestBase : IDisposable
{
    private bool disposed;

    /// <summary>
    /// Performs cleanup of test resources.
    /// </summary>
    public void Dispose()
    {
        if (!this.disposed)
        {
            this.disposed = true;

            // Run async cleanup synchronously
            try
            {
                this.DisposeAsyncCore().GetAwaiter().GetResult();
            }
            catch
            {
                // Ignore cleanup errors
            }

            this.DisposeSyncCore();
            GC.SuppressFinalize(this);
        }
    }

    /// <summary>
    /// Override to perform async cleanup.
    /// </summary>
    /// <returns>A task representing the asynchronous cleanup operation.</returns>
    protected virtual async Task DisposeAsyncCore()
    {
        await Task.CompletedTask;
    }

    /// <summary>
    /// Override to perform synchronous cleanup.
    /// </summary>
    protected virtual void DisposeSyncCore()
    {
    }

    /// <summary>
    /// Deletes a file with retry logic for Windows.
    /// </summary>
    /// <param name="filePath">The path to the file to delete.</param>
    protected void DeleteFileWithRetry(string filePath)
    {
        FileHelper.DeleteFileWithRetry(filePath);
    }

    /// <summary>
    /// Deletes a directory with retry logic for Windows.
    /// </summary>
    /// <param name="directoryPath">The path to the directory to delete.</param>
    protected void DeleteDirectoryWithRetry(string directoryPath)
    {
        FileHelper.DeleteDirectoryWithRetry(directoryPath);
    }
}