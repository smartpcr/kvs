using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;

namespace Kvs.Core.Storage
{
    /// <summary>
    /// Provides file-based storage engine implementation for persistent data storage.
    /// </summary>
    public class FileStorageEngine : IStorageEngine
    {
        private readonly string filePath;
        private readonly FileStream fileStream;
        private readonly SemaphoreSlim writeSemaphore;
        private bool disposed;

        /// <summary>
        /// Initializes a new instance of the <see cref="FileStorageEngine"/> class.
        /// </summary>
        /// <param name="filePath">The path to the storage file.</param>
        public FileStorageEngine(string filePath)
        {
            this.filePath = filePath ?? throw new ArgumentNullException(nameof(filePath));

            // Ensure directory exists
            var directory = Path.GetDirectoryName(this.filePath);
            if (!string.IsNullOrEmpty(directory) && !Directory.Exists(directory))
            {
                Directory.CreateDirectory(directory);
            }

            this.fileStream = new FileStream(
                this.filePath,
                FileMode.OpenOrCreate,
                FileAccess.ReadWrite,
                FileShare.Read,
                bufferSize: 4096,
                useAsync: true);

            this.writeSemaphore = new SemaphoreSlim(1, 1);
        }

        /// <inheritdoc />
        public async Task<ReadOnlyMemory<byte>> ReadAsync(long position, int length)
        {
            this.ThrowIfDisposed();

            if (position < 0)
            {
                throw new ArgumentOutOfRangeException(nameof(position));
            }

            if (length < 0)
            {
                throw new ArgumentOutOfRangeException(nameof(length));
            }

            if (position >= this.fileStream.Length)
            {
                return ReadOnlyMemory<byte>.Empty;
            }

            var buffer = new byte[Math.Min(length, (int)(this.fileStream.Length - position))];

            this.fileStream.Seek(position, SeekOrigin.Begin);
            var bytesRead = await this.fileStream.ReadAsync(buffer, 0, buffer.Length);

            if (bytesRead == 0)
            {
                return ReadOnlyMemory<byte>.Empty;
            }

            if (bytesRead < buffer.Length)
            {
                var result = new byte[bytesRead];
                Array.Copy(buffer, result, bytesRead);
                return result;
            }

            return buffer;
        }

        /// <inheritdoc />
        public async Task<long> WriteAsync(ReadOnlyMemory<byte> data)
        {
            this.ThrowIfDisposed();

            if (data.IsEmpty)
            {
                return this.fileStream.Position;
            }

            await this.writeSemaphore.WaitAsync();
            try
            {
                var position = this.fileStream.Length;
                this.fileStream.Seek(0, SeekOrigin.End);

#if NET472
                var bytes = data.ToArray();
                await this.fileStream.WriteAsync(bytes, 0, bytes.Length);
#else
                await this.fileStream.WriteAsync(data);
#endif

                return position;
            }
            finally
            {
                this.writeSemaphore.Release();
            }
        }

        /// <inheritdoc />
        public async Task FlushAsync()
        {
            this.ThrowIfDisposed();

            await this.fileStream.FlushAsync();
        }

        /// <inheritdoc />
        public async Task<bool> FsyncAsync()
        {
            this.ThrowIfDisposed();

            try
            {
                await this.fileStream.FlushAsync();
                this.fileStream.Flush(true); // Force OS to write to disk
                return true;
            }
            catch
            {
                return false;
            }
        }

        /// <inheritdoc />
        public Task<long> GetSizeAsync()
        {
            this.ThrowIfDisposed();

            return Task.FromResult(this.fileStream.Length);
        }

        /// <inheritdoc />
        public async Task TruncateAsync(long size)
        {
            this.ThrowIfDisposed();

            if (size < 0)
            {
                throw new ArgumentOutOfRangeException(nameof(size));
            }

            await this.writeSemaphore.WaitAsync();
            try
            {
                this.fileStream.SetLength(size);
                await this.fileStream.FlushAsync();
            }
            finally
            {
                this.writeSemaphore.Release();
            }
        }

        /// <inheritdoc />
        public Task<bool> IsOpenAsync()
        {
            return Task.FromResult(!this.disposed && this.fileStream.CanRead && this.fileStream.CanWrite);
        }

        private void ThrowIfDisposed()
        {
            if (this.disposed)
            {
                throw new ObjectDisposedException(nameof(FileStorageEngine));
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

            this.writeSemaphore?.Dispose();
            this.fileStream?.Dispose();
        }
    }
}