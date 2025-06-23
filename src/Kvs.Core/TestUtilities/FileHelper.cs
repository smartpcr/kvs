#if !NET472
#nullable enable
#endif

using System;
using System.IO;
using System.Threading;

namespace Kvs.Core.TestUtilities;

/// <summary>
/// Helper class for file operations in tests.
/// </summary>
public static class FileHelper
{
    /// <summary>
    /// Deletes a file with retry logic for Windows file locking issues.
    /// </summary>
    /// <param name="filePath">The path to the file to delete.</param>
    /// <param name="retries">Number of retries (default: 3).</param>
    /// <param name="delayMs">Delay between retries in milliseconds (default: 100).</param>
    public static void DeleteFileWithRetry(string filePath, int retries = 3, int delayMs = 100)
    {
        if (string.IsNullOrEmpty(filePath) || !File.Exists(filePath))
        {
            return;
        }

        for (int i = 0; i < retries; i++)
        {
            try
            {
                File.Delete(filePath);
                return;
            }
            catch (IOException) when (i < retries - 1)
            {
                // Windows may still have the file locked, wait a bit
                Thread.Sleep(delayMs);
            }
            catch (UnauthorizedAccessException) when (i < retries - 1)
            {
                // Try to remove readonly attribute
                try
                {
                    File.SetAttributes(filePath, FileAttributes.Normal);
                }
                catch
                {
                    // Ignore attribute errors
                }

                Thread.Sleep(delayMs);
            }
        }
    }

    /// <summary>
    /// Deletes a directory with retry logic for Windows file locking issues.
    /// </summary>
    /// <param name="directoryPath">The path to the directory to delete.</param>
    /// <param name="retries">Number of retries (default: 3).</param>
    /// <param name="delayMs">Delay between retries in milliseconds (default: 100).</param>
    public static void DeleteDirectoryWithRetry(string directoryPath, int retries = 3, int delayMs = 100)
    {
        if (string.IsNullOrEmpty(directoryPath) || !Directory.Exists(directoryPath))
        {
            return;
        }

        for (int i = 0; i < retries; i++)
        {
            try
            {
                Directory.Delete(directoryPath, recursive: true);
                return;
            }
            catch (IOException) when (i < retries - 1)
            {
                Thread.Sleep(delayMs);
            }
            catch (UnauthorizedAccessException) when (i < retries - 1)
            {
                // Try to remove readonly attributes from all files
                try
                {
                    foreach (var file in Directory.GetFiles(directoryPath, "*", SearchOption.AllDirectories))
                    {
                        File.SetAttributes(file, FileAttributes.Normal);
                    }
                }
                catch
                {
                    // Ignore attribute errors
                }

                Thread.Sleep(delayMs);
            }
        }
    }
}