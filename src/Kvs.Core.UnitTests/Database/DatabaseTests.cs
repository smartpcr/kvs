using System;
using System.Data;
using System.IO;
using System.Threading.Tasks;
using FluentAssertions;
using Kvs.Core.Database;
using Xunit;

namespace Kvs.Core.UnitTests.DatabaseTests;

/// <summary>
/// Tests for the Database class.
/// </summary>
public class DatabaseTests : IDisposable
{
    private readonly string testDbPath;
    private readonly Core.Database.Database database;

    /// <summary>
    /// Initializes a new instance of the <see cref="DatabaseTests"/> class.
    /// </summary>
    public DatabaseTests()
    {
        this.testDbPath = Path.Combine(Path.GetTempPath(), $"test_{Guid.NewGuid()}.db");
        this.database = new Core.Database.Database(this.testDbPath);
    }

    /// <summary>
    /// Tests that OpenAsync creates a database file.
    /// </summary>
    /// <returns>A <see cref="Task"/> representing the asynchronous unit test.</returns>
    [Fact(Timeout = 5000)]
    public async Task OpenAsync_Should_Create_Database_File()
    {
        // Act
        await this.database.OpenAsync();

        // Assert
        this.database.IsOpen.Should().BeTrue();
        File.Exists(this.testDbPath).Should().BeTrue();
    }

    /// <summary>
    /// Tests that CloseAsync closes the database.
    /// </summary>
    /// <returns>A <see cref="Task"/> representing the asynchronous unit test.</returns>
    [Fact(Timeout = 5000)]
    public async Task CloseAsync_Should_Close_Database()
    {
        // Arrange
        await this.database.OpenAsync();

        // Act
        await this.database.CloseAsync();

        // Assert
        this.database.IsOpen.Should().BeFalse();
    }

    /// <summary>
    /// Tests that GetCollection returns a collection instance.
    /// </summary>
    /// <returns>A <see cref="Task"/> representing the asynchronous unit test.</returns>
    [Fact(Timeout = 5000)]
    public async Task GetCollection_Should_Return_Collection_Instance()
    {
        // Arrange
        await this.database.OpenAsync();

        // Act
        var collection = this.database.GetCollection<TestDocument>("test_collection");

        // Assert
        collection.Should().NotBeNull();
        collection.Name.Should().Be("test_collection");
    }

    /// <summary>
    /// Tests that GetCollection returns the same instance for the same name.
    /// </summary>
    /// <returns>A <see cref="Task"/> representing the asynchronous unit test.</returns>
    [Fact(Timeout = 5000)]
    public async Task GetCollection_Should_Return_Same_Instance_For_Same_Name()
    {
        // Arrange
        await this.database.OpenAsync();

        // Act
        var collection1 = this.database.GetCollection<TestDocument>("test_collection");
        var collection2 = this.database.GetCollection<TestDocument>("test_collection");

        // Assert
        collection1.Should().BeSameAs(collection2);
    }

    /// <summary>
    /// Tests that BeginTransactionAsync creates a transaction.
    /// </summary>
    /// <returns>A <see cref="Task"/> representing the asynchronous unit test.</returns>
    [Fact(Timeout = 5000)]
    public async Task BeginTransactionAsync_Should_Create_Transaction()
    {
        // Arrange
        await this.database.OpenAsync();

        // Act
        using var transaction = await this.database.BeginTransactionAsync();

        // Assert
        transaction.Should().NotBeNull();
        transaction.Id.Should().NotBeNullOrEmpty();
        transaction.State.Should().Be(TransactionState.Active);
        transaction.IsolationLevel.Should().Be(IsolationLevel.Serializable);
    }

    /// <summary>
    /// Tests that BeginTransactionAsync with isolation level uses the specified level.
    /// </summary>
    /// <returns>A <see cref="Task"/> representing the asynchronous unit test.</returns>
    [Fact(Timeout = 5000)]
    public async Task BeginTransactionAsync_With_IsolationLevel_Should_Use_Specified_Level()
    {
        // Arrange
        await this.database.OpenAsync();

        // Act
        using var transaction = await this.database.BeginTransactionAsync(IsolationLevel.ReadCommitted);

        // Assert
        transaction.IsolationLevel.Should().Be(IsolationLevel.ReadCommitted);
    }

    /// <summary>
    /// Tests that CheckpointAsync completes successfully.
    /// </summary>
    /// <returns>A <see cref="Task"/> representing the asynchronous unit test.</returns>
    [Fact(Timeout = 5000)]
    public async Task CheckpointAsync_Should_Complete_Successfully()
    {
        // Arrange
        await this.database.OpenAsync();

        // Act
        var result = await this.database.CheckpointAsync();

        // Assert
        result.Should().BeTrue();
    }

    /// <summary>
    /// Tests that RecoverAsync completes successfully.
    /// </summary>
    /// <returns>A <see cref="Task"/> representing the asynchronous unit test.</returns>
    [Fact(Timeout = 5000)]
    public async Task RecoverAsync_Should_Complete_Successfully()
    {
        // Arrange
        await this.database.OpenAsync();

        // Act
        var result = await this.database.RecoverAsync();

        // Assert
        result.Should().BeTrue();
    }

    /// <summary>
    /// Tests that GetCollection without open throws.
    /// </summary>
    [Fact(Timeout = 5000)]
    public void GetCollection_Without_Open_Should_Throw()
    {
        // Act
        var action = () => this.database.GetCollection<TestDocument>("test");

        // Assert
        action.Should().Throw<InvalidOperationException>()
            .WithMessage("Database is not open.");
    }

    /// <summary>
    /// Tests that BeginTransactionAsync without open throws.
    /// </summary>
    /// <returns>A <see cref="Task"/> representing the asynchronous unit test.</returns>
    [Fact(Timeout = 5000)]
    public async Task BeginTransactionAsync_Without_Open_Should_Throw()
    {
        // Act
        Func<Task> action = async () => await this.database.BeginTransactionAsync();

        // Assert
        await action.Should().ThrowAsync<InvalidOperationException>()
            .WithMessage("Database is not open.");
    }

    /// <summary>
    /// Tests that multiple transactions have unique IDs.
    /// </summary>
    /// <returns>A <see cref="Task"/> representing the asynchronous unit test.</returns>
    [Fact(Timeout = 5000)]
    public async Task Multiple_Transactions_Should_Have_Unique_Ids()
    {
        // Arrange
        await this.database.OpenAsync();

        // Act
        using var transaction1 = await this.database.BeginTransactionAsync();
        using var transaction2 = await this.database.BeginTransactionAsync();

        // Assert
        transaction1.Id.Should().NotBe(transaction2.Id);
    }

    /// <summary>
    /// Disposes of the test resources.
    /// </summary>
    public void Dispose()
    {
        this.database?.Dispose();

        if (File.Exists(this.testDbPath))
        {
            File.Delete(this.testDbPath);
        }

        var walPath = Path.ChangeExtension(this.testDbPath, ".wal");
        if (File.Exists(walPath))
        {
            File.Delete(walPath);
        }
    }

    private class TestDocument
    {
#if NET8_0_OR_GREATER
        public string? Id { get; set; } = string.Empty;

        public string? Name { get; set; } = string.Empty;
#else
        public string Id { get; set; } = string.Empty;

        public string Name { get; set; } = string.Empty;
#endif

        public int Value { get; set; }
    }
}
