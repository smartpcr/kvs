using System;
using System.Runtime.InteropServices;

/* Unmerged change from project 'Kvs.Core(net8.0)'
Before:
namespace Kvs.Core.Storage
{
    /// <summary>
    /// Represents the type of a page in the storage system.
    /// </summary>
    public enum PageType : byte
    {
        /// <summary>
        /// A free page available for allocation.
        /// </summary>
        Free = 0,

        /// <summary>
        /// A header page containing metadata.
        /// </summary>
        Header = 1,

        /// <summary>
        /// An internal node in a B-tree structure.
        /// </summary>
        InternalNode = 2,

        /// <summary>
        /// A leaf node in a B-tree structure.
        /// </summary>
        LeafNode = 3,

        /// <summary>
        /// A data page containing record data.
        /// </summary>
        Data = 4,

        /// <summary>
        /// An overflow page for large data.
        /// </summary>
        Overflow = 5
    }

    /// <summary>
    /// Represents the header information for a page.
    /// </summary>
    [StructLayout(LayoutKind.Sequential, Pack = 1)]
    public readonly struct PageHeader
    {
        /// <summary>
        /// The size of the page header in bytes.
        /// </summary>
        public const int Size = 32;

        /// <summary>
        /// Initializes a new instance of the <see cref="PageHeader"/> struct.
        /// </summary>
        /// <param name="pageId">The unique identifier of the page.</param>
        /// <param name="pageType">The type of the page.</param>
        /// <param name="dataSize">The size of data stored in the page.</param>
        /// <param name="nextPageId">The ID of the next page in the chain (-1 if none).</param>
        /// <param name="prevPageId">The ID of the previous page in the chain (-1 if none).</param>
        public PageHeader(long pageId, PageType pageType, int dataSize, long nextPageId = -1, long prevPageId = -1)
        {
            this.PageId = pageId;
            this.PageType = pageType;
            this.DataSize = dataSize;
            this.NextPageId = nextPageId;
            this.PrevPageId = prevPageId;
            this.Checksum = CalculateChecksum(pageId, pageType, dataSize, nextPageId, prevPageId);
            this.Reserved1 = 0;
            this.Reserved2 = 0;
        }

        /// <summary>
        /// Gets the unique identifier of the page.
        /// </summary>
        public long PageId { get; }

        /// <summary>
        /// Gets the type of the page.
        /// </summary>
        public PageType PageType { get; }

        /// <summary>
        /// Gets the size of data stored in the page.
        /// </summary>
        public int DataSize { get; }

        /// <summary>
        /// Gets the ID of the next page in the chain (-1 if none).
        /// </summary>
        public long NextPageId { get; }

        /// <summary>
        /// Gets the ID of the previous page in the chain (-1 if none).
        /// </summary>
        public long PrevPageId { get; }

        /// <summary>
        /// Gets the checksum for data integrity verification.
        /// </summary>
        public uint Checksum { get; }

        /// <summary>
        /// Gets the first reserved byte for future use.
        /// </summary>
        public byte Reserved1 { get; }

        /// <summary>
        /// Gets the second reserved byte for future use.
        /// </summary>
        public byte Reserved2 { get; }

        /// <summary>
        /// Gets a value indicating whether the page header is valid based on checksum verification.
        /// </summary>
        public bool IsValid => this.Checksum == CalculateChecksum(this.PageId, this.PageType, this.DataSize, this.NextPageId, this.PrevPageId);

        private static uint CalculateChecksum(long pageId, PageType pageType, int dataSize, long nextPageId, long prevPageId)
        {
            uint checksum = 0;
            checksum ^= (uint)(pageId ^ (pageId >> 32));
            checksum ^= (uint)pageType;
            checksum ^= (uint)dataSize;
            checksum ^= (uint)(nextPageId ^ (nextPageId >> 32));
            checksum ^= (uint)(prevPageId ^ (prevPageId >> 32));
            return checksum;
        }
    }

    /// <summary>
    /// Represents a page in the storage system.
    /// </summary>
    public class Page
    {
        /// <summary>
        /// The default size of a page in bytes.
        /// </summary>
        public const int DefaultPageSize = 4096;

        /// <summary>
        /// The maximum amount of data that can be stored in a page.
        /// </summary>
        public const int MaxDataSize = DefaultPageSize - PageHeader.Size;

        private readonly byte[] buffer;
        private PageHeader header;

        /// <summary>
        /// Initializes a new instance of the <see cref="Page"/> class with the specified ID and type.
        /// </summary>
        /// <param name="pageId">The unique identifier of the page.</param>
        /// <param name="pageType">The type of the page.</param>
        /// <param name="pageSize">The size of the page in bytes.</param>
        public Page(long pageId, PageType pageType, int pageSize = DefaultPageSize)
        {
            if (pageSize < PageHeader.Size + 1)
            {
                throw new ArgumentException($"Page size must be at least {PageHeader.Size + 1} bytes", nameof(pageSize));
            }

            this.buffer = new byte[pageSize];
            this.header = new PageHeader(pageId, pageType, 0);
            this.WriteHeader();
        }

        /// <summary>
        /// Initializes a new instance of the <see cref="Page"/> class from existing page data.
        /// </summary>
        /// <param name="data">The raw page data to load.</param>
        public Page(ReadOnlySpan<byte> data)
        {
            if (data.Length < PageHeader.Size)
            {
                throw new ArgumentException($"Data must be at least {PageHeader.Size} bytes", nameof(data));
            }

            this.buffer = data.ToArray();
            this.header = this.ReadHeader();

            if (!this.header.IsValid)
            {
                throw new InvalidOperationException("Page header checksum is invalid");
            }
        }

        /// <summary>
        /// Gets the unique identifier of the page.
        /// </summary>
        public long PageId => this.header.PageId;

        /// <summary>
        /// Gets the type of the page.
        /// </summary>
        public PageType PageType => this.header.PageType;

        /// <summary>
        /// Gets the size of data currently stored in the page.
        /// </summary>
        public int DataSize => this.header.DataSize;

        /// <summary>
        /// Gets the ID of the next page in the chain (-1 if none).
        /// </summary>
        public long NextPageId => this.header.NextPageId;

        /// <summary>
        /// Gets the ID of the previous page in the chain (-1 if none).
        /// </summary>
        public long PrevPageId => this.header.PrevPageId;

        /// <summary>
        /// Gets the total size of the page in bytes.
        /// </summary>
        public int PageSize => this.buffer.Length;

        /// <summary>
        /// Gets the amount of free space available in the page.
        /// </summary>
        public int AvailableSpace => MaxDataSize - this.header.DataSize;

        /// <summary>
        /// Gets the data portion of the page as a read-only span.
        /// </summary>
        public ReadOnlySpan<byte> Data => this.buffer.AsSpan(PageHeader.Size, this.header.DataSize);

        /// <summary>
        /// Gets the entire page buffer as a read-only span.
        /// </summary>
        public ReadOnlySpan<byte> Buffer => this.buffer;

        /// <summary>
        /// Gets the entire page buffer as read-only memory.
        /// </summary>
        public ReadOnlyMemory<byte> BufferMemory => this.buffer;

        /// <summary>
        /// Writes data to the page at the specified offset.
        /// </summary>
        /// <param name="data">The data to write.</param>
        /// <param name="offset">The offset within the data area to start writing at.</param>
        public void WriteData(ReadOnlySpan<byte> data, int offset = 0)
        {
            if (offset < 0 || offset + data.Length > MaxDataSize)
            {
                throw new ArgumentOutOfRangeException(nameof(offset), "Data exceeds page capacity");
            }

            data.CopyTo(this.buffer.AsSpan(PageHeader.Size + offset));

            if (offset + data.Length > this.header.DataSize)
            {
                this.header = new PageHeader(this.header.PageId, this.header.PageType, offset + data.Length, this.header.NextPageId, this.header.PrevPageId);
                this.WriteHeader();
            }
        }

        /// <summary>
        /// Sets the ID of the next page in the chain.
        /// </summary>
        /// <param name="nextPageId">The ID of the next page (-1 for none).</param>
        public void SetNextPageId(long nextPageId)
        {
            this.header = new PageHeader(this.header.PageId, this.header.PageType, this.header.DataSize, nextPageId, this.header.PrevPageId);
            this.WriteHeader();
        }

        /// <summary>
        /// Sets the ID of the previous page in the chain.
        /// </summary>
        /// <param name="prevPageId">The ID of the previous page (-1 for none).</param>
        public void SetPrevPageId(long prevPageId)
        {
            this.header = new PageHeader(this.header.PageId, this.header.PageType, this.header.DataSize, this.header.NextPageId, prevPageId);
            this.WriteHeader();
        }

        /// <summary>
        /// Clears all data from the page while preserving the header information.
        /// </summary>
        public void Clear()
        {
            this.header = new PageHeader(this.header.PageId, this.header.PageType, 0, this.header.NextPageId, this.header.PrevPageId);
            this.WriteHeader();
            Array.Clear(this.buffer, PageHeader.Size, MaxDataSize);
        }

        private void WriteHeader()
        {
#if NET472
            // Manual serialization for .NET Framework 4.7.2
            var headerBytes = new byte[PageHeader.Size];
            BitConverter.GetBytes(this.header.PageId).CopyTo(headerBytes, 0);
            headerBytes[8] = (byte)this.header.PageType;
            BitConverter.GetBytes(this.header.DataSize).CopyTo(headerBytes, 9);
            BitConverter.GetBytes(this.header.NextPageId).CopyTo(headerBytes, 13);
            BitConverter.GetBytes(this.header.PrevPageId).CopyTo(headerBytes, 21);
            BitConverter.GetBytes(this.header.Checksum).CopyTo(headerBytes, 29);
            headerBytes.CopyTo(this.buffer, 0);
#else
            var headerRef = this.header;
            var headerBytes = MemoryMarshal.AsBytes(MemoryMarshal.CreateReadOnlySpan(ref headerRef, 1));
            headerBytes[..Math.Min(headerBytes.Length, PageHeader.Size)].CopyTo(this.buffer);
#endif
        }

        private PageHeader ReadHeader()
        {
            return MemoryMarshal.Read<PageHeader>(this.buffer);
        }
After:
namespace Kvs.Core.Storage;

/// <summary>
/// Represents the type of a page in the storage system.
/// </summary>
public enum PageType : byte
{
    /// <summary>
    /// A free page available for allocation.
    /// </summary>
    Free = 0,

    /// <summary>
    /// A header page containing metadata.
    /// </summary>
    Header = 1,

    /// <summary>
    /// An internal node in a B-tree structure.
    /// </summary>
    InternalNode = 2,

    /// <summary>
    /// A leaf node in a B-tree structure.
    /// </summary>
    LeafNode = 3,

    /// <summary>
    /// A data page containing record data.
    /// </summary>
    Data = 4,

    /// <summary>
    /// An overflow page for large data.
    /// </summary>
    Overflow = 5
}

/// <summary>
/// Represents the header information for a page.
/// </summary>
[StructLayout(LayoutKind.Sequential, Pack = 1)]
public readonly struct PageHeader
{
    /// <summary>
    /// The size of the page header in bytes.
    /// </summary>
    public const int Size = 32;

    /// <summary>
    /// Initializes a new instance of the <see cref="PageHeader"/> struct.
    /// </summary>
    /// <param name="pageId">The unique identifier of the page.</param>
    /// <param name="pageType">The type of the page.</param>
    /// <param name="dataSize">The size of data stored in the page.</param>
    /// <param name="nextPageId">The ID of the next page in the chain (-1 if none).</param>
    /// <param name="prevPageId">The ID of the previous page in the chain (-1 if none).</param>
    public PageHeader(long pageId, PageType pageType, int dataSize, long nextPageId = -1, long prevPageId = -1)
    {
        this.PageId = pageId;
        this.PageType = pageType;
        this.DataSize = dataSize;
        this.NextPageId = nextPageId;
        this.PrevPageId = prevPageId;
        this.Checksum = CalculateChecksum(pageId, pageType, dataSize, nextPageId, prevPageId);
        this.Reserved1 = 0;
        this.Reserved2 = 0;
    }

    /// <summary>
    /// Gets the unique identifier of the page.
    /// </summary>
    public long PageId { get; }

    /// <summary>
    /// Gets the type of the page.
    /// </summary>
    public PageType PageType { get; }

    /// <summary>
    /// Gets the size of data stored in the page.
    /// </summary>
    public int DataSize { get; }

    /// <summary>
    /// Gets the ID of the next page in the chain (-1 if none).
    /// </summary>
    public long NextPageId { get; }

    /// <summary>
    /// Gets the ID of the previous page in the chain (-1 if none).
    /// </summary>
    public long PrevPageId { get; }

    /// <summary>
    /// Gets the checksum for data integrity verification.
    /// </summary>
    public uint Checksum { get; }

    /// <summary>
    /// Gets the first reserved byte for future use.
    /// </summary>
    public byte Reserved1 { get; }

    /// <summary>
    /// Gets the second reserved byte for future use.
    /// </summary>
    public byte Reserved2 { get; }

    /// <summary>
    /// Gets a value indicating whether the page header is valid based on checksum verification.
    /// </summary>
    public bool IsValid => this.Checksum == CalculateChecksum(this.PageId, this.PageType, this.DataSize, this.NextPageId, this.PrevPageId);

    private static uint CalculateChecksum(long pageId, PageType pageType, int dataSize, long nextPageId, long prevPageId)
    {
        uint checksum = 0;
        checksum ^= (uint)(pageId ^ (pageId >> 32));
        checksum ^= (uint)pageType;
        checksum ^= (uint)dataSize;
        checksum ^= (uint)(nextPageId ^ (nextPageId >> 32));
        checksum ^= (uint)(prevPageId ^ (prevPageId >> 32));
        return checksum;
    }
}

/// <summary>
/// Represents a page in the storage system.
/// </summary>
public class Page
{
    /// <summary>
    /// The default size of a page in bytes.
    /// </summary>
    public const int DefaultPageSize = 4096;

    /// <summary>
    /// The maximum amount of data that can be stored in a page.
    /// </summary>
    public const int MaxDataSize = DefaultPageSize - PageHeader.Size;

    private readonly byte[] buffer;
    private PageHeader header;

    /// <summary>
    /// Initializes a new instance of the <see cref="Page"/> class with the specified ID and type.
    /// </summary>
    /// <param name="pageId">The unique identifier of the page.</param>
    /// <param name="pageType">The type of the page.</param>
    /// <param name="pageSize">The size of the page in bytes.</param>
    public Page(long pageId, PageType pageType, int pageSize = DefaultPageSize)
    {
        if (pageSize < PageHeader.Size + 1)
        {
            throw new ArgumentException($"Page size must be at least {PageHeader.Size + 1} bytes", nameof(pageSize));
        }

        this.buffer = new byte[pageSize];
        this.header = new PageHeader(pageId, pageType, 0);
        this.WriteHeader();
    }

    /// <summary>
    /// Initializes a new instance of the <see cref="Page"/> class from existing page data.
    /// </summary>
    /// <param name="data">The raw page data to load.</param>
    public Page(ReadOnlySpan<byte> data)
    {
        if (data.Length < PageHeader.Size)
        {
            throw new ArgumentException($"Data must be at least {PageHeader.Size} bytes", nameof(data));
        }

        this.buffer = data.ToArray();
        this.header = this.ReadHeader();

        if (!this.header.IsValid)
        {
            throw new InvalidOperationException("Page header checksum is invalid");
        }
    }

    /// <summary>
    /// Gets the unique identifier of the page.
    /// </summary>
    public long PageId => this.header.PageId;

    /// <summary>
    /// Gets the type of the page.
    /// </summary>
    public PageType PageType => this.header.PageType;

    /// <summary>
    /// Gets the size of data currently stored in the page.
    /// </summary>
    public int DataSize => this.header.DataSize;

    /// <summary>
    /// Gets the ID of the next page in the chain (-1 if none).
    /// </summary>
    public long NextPageId => this.header.NextPageId;

    /// <summary>
    /// Gets the ID of the previous page in the chain (-1 if none).
    /// </summary>
    public long PrevPageId => this.header.PrevPageId;

    /// <summary>
    /// Gets the total size of the page in bytes.
    /// </summary>
    public int PageSize => this.buffer.Length;

    /// <summary>
    /// Gets the amount of free space available in the page.
    /// </summary>
    public int AvailableSpace => MaxDataSize - this.header.DataSize;

    /// <summary>
    /// Gets the data portion of the page as a read-only span.
    /// </summary>
    public ReadOnlySpan<byte> Data => this.buffer.AsSpan(PageHeader.Size, this.header.DataSize);

    /// <summary>
    /// Gets the entire page buffer as a read-only span.
    /// </summary>
    public ReadOnlySpan<byte> Buffer => this.buffer;

    /// <summary>
    /// Gets the entire page buffer as read-only memory.
    /// </summary>
    public ReadOnlyMemory<byte> BufferMemory => this.buffer;

    /// <summary>
    /// Writes data to the page at the specified offset.
    /// </summary>
    /// <param name="data">The data to write.</param>
    /// <param name="offset">The offset within the data area to start writing at.</param>
    public void WriteData(ReadOnlySpan<byte> data, int offset = 0)
    {
        if (offset < 0 || offset + data.Length > MaxDataSize)
        {
            throw new ArgumentOutOfRangeException(nameof(offset), "Data exceeds page capacity");
        }

        data.CopyTo(this.buffer.AsSpan(PageHeader.Size + offset));

        if (offset + data.Length > this.header.DataSize)
        {
            this.header = new PageHeader(this.header.PageId, this.header.PageType, offset + data.Length, this.header.NextPageId, this.header.PrevPageId);
            this.WriteHeader();
        }
    }

    /// <summary>
    /// Sets the ID of the next page in the chain.
    /// </summary>
    /// <param name="nextPageId">The ID of the next page (-1 for none).</param>
    public void SetNextPageId(long nextPageId)
    {
        this.header = new PageHeader(this.header.PageId, this.header.PageType, this.header.DataSize, nextPageId, this.header.PrevPageId);
        this.WriteHeader();
    }

    /// <summary>
    /// Sets the ID of the previous page in the chain.
    /// </summary>
    /// <param name="prevPageId">The ID of the previous page (-1 for none).</param>
    public void SetPrevPageId(long prevPageId)
    {
        this.header = new PageHeader(this.header.PageId, this.header.PageType, this.header.DataSize, this.header.NextPageId, prevPageId);
        this.WriteHeader();
    }

    /// <summary>
    /// Clears all data from the page while preserving the header information.
    /// </summary>
    public void Clear()
    {
        this.header = new PageHeader(this.header.PageId, this.header.PageType, 0, this.header.NextPageId, this.header.PrevPageId);
        this.WriteHeader();
        Array.Clear(this.buffer, PageHeader.Size, MaxDataSize);
    }

    private void WriteHeader()
    {
#if NET472
        // Manual serialization for .NET Framework 4.7.2
        var headerBytes = new byte[PageHeader.Size];
        BitConverter.GetBytes(this.header.PageId).CopyTo(headerBytes, 0);
        headerBytes[8] = (byte)this.header.PageType;
        BitConverter.GetBytes(this.header.DataSize).CopyTo(headerBytes, 9);
        BitConverter.GetBytes(this.header.NextPageId).CopyTo(headerBytes, 13);
        BitConverter.GetBytes(this.header.PrevPageId).CopyTo(headerBytes, 21);
        BitConverter.GetBytes(this.header.Checksum).CopyTo(headerBytes, 29);
        headerBytes.CopyTo(this.buffer, 0);
#else
        var headerRef = this.header;
        var headerBytes = MemoryMarshal.AsBytes(MemoryMarshal.CreateReadOnlySpan(ref headerRef, 1));
        headerBytes[..Math.Min(headerBytes.Length, PageHeader.Size)].CopyTo(this.buffer);
#endif
    }

    private PageHeader ReadHeader()
    {
        return MemoryMarshal.Read<PageHeader>(this.buffer);
*/
namespace Kvs.Core.Storage;

/// <summary>
/// Represents the type of a page in the storage system.
/// </summary>
public enum PageType : byte
{
    /// <summary>
    /// A free page available for allocation.
    /// </summary>
    Free = 0,

    /// <summary>
    /// A header page containing metadata.
    /// </summary>
    Header = 1,

    /// <summary>
    /// An internal node in a B-tree structure.
    /// </summary>
    InternalNode = 2,

    /// <summary>
    /// A leaf node in a B-tree structure.
    /// </summary>
    LeafNode = 3,

    /// <summary>
    /// A data page containing record data.
    /// </summary>
    Data = 4,

    /// <summary>
    /// An overflow page for large data.
    /// </summary>
    Overflow = 5
}

/// <summary>
/// Represents the header information for a page.
/// </summary>
[StructLayout(LayoutKind.Sequential, Pack = 1)]
public readonly struct PageHeader
{
    /// <summary>
    /// The size of the page header in bytes.
    /// </summary>
    public const int Size = 32;

    /// <summary>
    /// Initializes a new instance of the <see cref="PageHeader"/> struct.
    /// </summary>
    /// <param name="pageId">The unique identifier of the page.</param>
    /// <param name="pageType">The type of the page.</param>
    /// <param name="dataSize">The size of data stored in the page.</param>
    /// <param name="nextPageId">The ID of the next page in the chain (-1 if none).</param>
    /// <param name="prevPageId">The ID of the previous page in the chain (-1 if none).</param>
    public PageHeader(long pageId, PageType pageType, int dataSize, long nextPageId = -1, long prevPageId = -1)
    {
        this.PageId = pageId;
        this.PageType = pageType;
        this.DataSize = dataSize;
        this.NextPageId = nextPageId;
        this.PrevPageId = prevPageId;
        this.Checksum = CalculateChecksum(pageId, pageType, dataSize, nextPageId, prevPageId);
        this.Reserved1 = 0;
        this.Reserved2 = 0;
    }

    /// <summary>
    /// Gets the unique identifier of the page.
    /// </summary>
    public long PageId { get; }

    /// <summary>
    /// Gets the type of the page.
    /// </summary>
    public PageType PageType { get; }

    /// <summary>
    /// Gets the size of data stored in the page.
    /// </summary>
    public int DataSize { get; }

    /// <summary>
    /// Gets the ID of the next page in the chain (-1 if none).
    /// </summary>
    public long NextPageId { get; }

    /// <summary>
    /// Gets the ID of the previous page in the chain (-1 if none).
    /// </summary>
    public long PrevPageId { get; }

    /// <summary>
    /// Gets the checksum for data integrity verification.
    /// </summary>
    public uint Checksum { get; }

    /// <summary>
    /// Gets the first reserved byte for future use.
    /// </summary>
    public byte Reserved1 { get; }

    /// <summary>
    /// Gets the second reserved byte for future use.
    /// </summary>
    public byte Reserved2 { get; }

    /// <summary>
    /// Gets a value indicating whether the page header is valid based on checksum verification.
    /// </summary>
    public bool IsValid => this.Checksum == CalculateChecksum(this.PageId, this.PageType, this.DataSize, this.NextPageId, this.PrevPageId);

    private static uint CalculateChecksum(long pageId, PageType pageType, int dataSize, long nextPageId, long prevPageId)
    {
        uint checksum = 0;
        checksum ^= (uint)(pageId ^ (pageId >> 32));
        checksum ^= (uint)pageType;
        checksum ^= (uint)dataSize;
        checksum ^= (uint)(nextPageId ^ (nextPageId >> 32));
        checksum ^= (uint)(prevPageId ^ (prevPageId >> 32));
        return checksum;
    }
}

/// <summary>
/// Represents a page in the storage system.
/// </summary>
public class Page
{
    /// <summary>
    /// The default size of a page in bytes.
    /// </summary>
    public const int DefaultPageSize = 4096;

    /// <summary>
    /// The maximum amount of data that can be stored in a page.
    /// </summary>
    public const int MaxDataSize = DefaultPageSize - PageHeader.Size;

    private readonly byte[] buffer;
    private PageHeader header;

    /// <summary>
    /// Initializes a new instance of the <see cref="Page"/> class with the specified ID and type.
    /// </summary>
    /// <param name="pageId">The unique identifier of the page.</param>
    /// <param name="pageType">The type of the page.</param>
    /// <param name="pageSize">The size of the page in bytes.</param>
    public Page(long pageId, PageType pageType, int pageSize = DefaultPageSize)
    {
        if (pageSize < PageHeader.Size + 1)
        {
            throw new ArgumentException($"Page size must be at least {PageHeader.Size + 1} bytes", nameof(pageSize));
        }

        this.buffer = new byte[pageSize];
        this.header = new PageHeader(pageId, pageType, 0);
        this.WriteHeader();
    }

    /// <summary>
    /// Initializes a new instance of the <see cref="Page"/> class from existing page data.
    /// </summary>
    /// <param name="data">The raw page data to load.</param>
    public Page(ReadOnlySpan<byte> data)
    {
        if (data.Length < PageHeader.Size)
        {
            throw new ArgumentException($"Data must be at least {PageHeader.Size} bytes", nameof(data));
        }

        this.buffer = data.ToArray();
        this.header = this.ReadHeader();

        if (!this.header.IsValid)
        {
            throw new InvalidOperationException("Page header checksum is invalid");
        }
    }

    /// <summary>
    /// Gets the unique identifier of the page.
    /// </summary>
    public long PageId => this.header.PageId;

    /// <summary>
    /// Gets the type of the page.
    /// </summary>
    public PageType PageType => this.header.PageType;

    /// <summary>
    /// Gets the size of data currently stored in the page.
    /// </summary>
    public int DataSize => this.header.DataSize;

    /// <summary>
    /// Gets the ID of the next page in the chain (-1 if none).
    /// </summary>
    public long NextPageId => this.header.NextPageId;

    /// <summary>
    /// Gets the ID of the previous page in the chain (-1 if none).
    /// </summary>
    public long PrevPageId => this.header.PrevPageId;

    /// <summary>
    /// Gets the total size of the page in bytes.
    /// </summary>
    public int PageSize => this.buffer.Length;

    /// <summary>
    /// Gets the amount of free space available in the page.
    /// </summary>
    public int AvailableSpace => MaxDataSize - this.header.DataSize;

    /// <summary>
    /// Gets the data portion of the page as a read-only span.
    /// </summary>
    public ReadOnlySpan<byte> Data => this.buffer.AsSpan(PageHeader.Size, this.header.DataSize);

    /// <summary>
    /// Gets the entire page buffer as a read-only span.
    /// </summary>
    public ReadOnlySpan<byte> Buffer => this.buffer;

    /// <summary>
    /// Gets the entire page buffer as read-only memory.
    /// </summary>
    public ReadOnlyMemory<byte> BufferMemory => this.buffer;

    /// <summary>
    /// Writes data to the page at the specified offset.
    /// </summary>
    /// <param name="data">The data to write.</param>
    /// <param name="offset">The offset within the data area to start writing at.</param>
    public void WriteData(ReadOnlySpan<byte> data, int offset = 0)
    {
        if (offset < 0 || offset + data.Length > MaxDataSize)
        {
            throw new ArgumentOutOfRangeException(nameof(offset), "Data exceeds page capacity");
        }

        data.CopyTo(this.buffer.AsSpan(PageHeader.Size + offset));

        if (offset + data.Length > this.header.DataSize)
        {
            this.header = new PageHeader(this.header.PageId, this.header.PageType, offset + data.Length, this.header.NextPageId, this.header.PrevPageId);
            this.WriteHeader();
        }
    }

    /// <summary>
    /// Sets the ID of the next page in the chain.
    /// </summary>
    /// <param name="nextPageId">The ID of the next page (-1 for none).</param>
    public void SetNextPageId(long nextPageId)
    {
        this.header = new PageHeader(this.header.PageId, this.header.PageType, this.header.DataSize, nextPageId, this.header.PrevPageId);
        this.WriteHeader();
    }

    /// <summary>
    /// Sets the ID of the previous page in the chain.
    /// </summary>
    /// <param name="prevPageId">The ID of the previous page (-1 for none).</param>
    public void SetPrevPageId(long prevPageId)
    {
        this.header = new PageHeader(this.header.PageId, this.header.PageType, this.header.DataSize, this.header.NextPageId, prevPageId);
        this.WriteHeader();
    }

    /// <summary>
    /// Clears all data from the page while preserving the header information.
    /// </summary>
    public void Clear()
    {
        this.header = new PageHeader(this.header.PageId, this.header.PageType, 0, this.header.NextPageId, this.header.PrevPageId);
        this.WriteHeader();
        Array.Clear(this.buffer, PageHeader.Size, MaxDataSize);
    }

    private void WriteHeader()
    {
#if NET472
        // Manual serialization for .NET Framework 4.7.2
        var headerBytes = new byte[PageHeader.Size];
        BitConverter.GetBytes(this.header.PageId).CopyTo(headerBytes, 0);
        headerBytes[8] = (byte)this.header.PageType;
        BitConverter.GetBytes(this.header.DataSize).CopyTo(headerBytes, 9);
        BitConverter.GetBytes(this.header.NextPageId).CopyTo(headerBytes, 13);
        BitConverter.GetBytes(this.header.PrevPageId).CopyTo(headerBytes, 21);
        BitConverter.GetBytes(this.header.Checksum).CopyTo(headerBytes, 29);
        headerBytes.CopyTo(this.buffer, 0);
#else
        var headerRef = this.header;
        var headerBytes = MemoryMarshal.AsBytes(MemoryMarshal.CreateReadOnlySpan(ref headerRef, 1));
        headerBytes[..Math.Min(headerBytes.Length, PageHeader.Size)].CopyTo(this.buffer);
#endif
    }

    private PageHeader ReadHeader()
    {
        return MemoryMarshal.Read<PageHeader>(this.buffer);
    }
}
