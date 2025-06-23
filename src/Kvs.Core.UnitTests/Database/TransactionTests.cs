using System;
using System.Data;
using System.IO;
using System.Threading.Tasks;
using FluentAssertions;
using Kvs.Core.Database;
using Xunit;

namespace Kvs.Core.UnitTests.DatabaseTests;

/// <summary>
/// Tests for the Transaction class.
/// </summary>
public class TransactionTests : IDisposable
{
    private readonly string testDbPath;
    private readonly Core.Database.Database database;

    /// <summary>
    /// Initializes a new instance of the <see cref="TransactionTests"/> class.
    /// </summary>
    public TransactionTests()
    {
        this.testDbPath = Path.Combine(Path.GetTempPath(), $"test_{Guid.NewGuid()}.db");
        this.database = new Core.Database.Database(this.testDbPath);
    }

    /// <summary>
    /// Tests that transaction has unique ID.
    /// </summary>
    /// <returns>A <see cref="Task"/> representing the asynchronous unit test.</returns>
    [Fact(Timeout = 5000)]
    public async Task Transaction_Should_Have_Unique_Id()
    {
        // Arrange
        await this.database.OpenAsync();

        // Act
        using var transaction = await this.database.BeginTransactionAsync();

        // Assert
        transaction.Id.Should().NotBeNullOrEmpty();
        transaction.Id.Should().StartWith("TXN_");
    }

    /// <summary>
    /// Tests that transaction starts in active state.
    /// </summary>
    /// <returns>A <see cref="Task"/> representing the asynchronous unit test.</returns>
    [Fact(Timeout = 5000)]
    public async Task Transaction_Should_Start_In_Active_State()
    {
        // Arrange
        await this.database.OpenAsync();

        // Act
        using var transaction = await this.database.BeginTransactionAsync();

        // Assert
        transaction.State.Should().Be(TransactionState.Active);
    }

    /// <summary>
    /// Tests that CommitAsync changes state to committed.
    /// </summary>
    /// <returns>A <see cref="Task"/> representing the asynchronous unit test.</returns>
    [Fact(Timeout = 5000)]
    public async Task CommitAsync_Should_Change_State_To_Committed()
    {
        // Arrange
        await this.database.OpenAsync();
        using var transaction = await this.database.BeginTransactionAsync();

        // Act
        await transaction.CommitAsync();

        // Assert
        transaction.State.Should().Be(TransactionState.Committed);
    }

    /// <summary>
    /// Tests that RollbackAsync changes state to aborted.
    /// </summary>
    /// <returns>A <see cref="Task"/> representing the asynchronous unit test.</returns>
    [Fact(Timeout = 5000)]
    public async Task RollbackAsync_Should_Change_State_To_Aborted()
    {
        // Arrange
        await this.database.OpenAsync();
        using var transaction = await this.database.BeginTransactionAsync();

        // Act
        await transaction.RollbackAsync();

        // Assert
        transaction.State.Should().Be(TransactionState.Aborted);
    }

    /// <summary>
    /// Tests that WriteAsync and ReadAsync work within transaction.
    /// </summary>
    /// <returns>A <see cref="Task"/> representing the asynchronous unit test.</returns>
    [Fact(Timeout = 5000)]
    public async Task WriteAsync_And_ReadAsync_Should_Work_Within_Transaction()
    {
        // Arrange
        await this.database.OpenAsync();
        using var transaction = await this.database.BeginTransactionAsync();

        // Act
        await transaction.WriteAsync("key1", new TestData { Value = "test" });
        var retrieved = await transaction.ReadAsync<TestData>("key1");

        // Assert
        retrieved.Should().NotBeNull();
        retrieved!.Value.Should().Be("test");
    }

    /// <summary>
    /// Tests that DeleteAsync removes value.
    /// </summary>
    /// <returns>A <see cref="Task"/> representing the asynchronous unit test.</returns>
    [Fact(Timeout = 5000)]
    public async Task DeleteAsync_Should_Remove_Value()
    {
        // Arrange
        await this.database.OpenAsync();
        using var transaction = await this.database.BeginTransactionAsync();
        await transaction.WriteAsync("key1", new TestData { Value = "test" });

        // Act
        await transaction.DeleteAsync("key1");
        var retrieved = await transaction.ReadAsync<TestData>("key1");

        // Assert
        retrieved.Should().BeNull();
    }

    /// <summary>
    /// Tests that transaction has isolation level.
    /// </summary>
    /// <returns>A <see cref="Task"/> representing the asynchronous unit test.</returns>
    [Fact(Timeout = 5000)]
    public async Task Transaction_Should_Have_Isolation_Level()
    {
        // Arrange
        await this.database.OpenAsync();

        // Act
        using var transaction = await this.database.BeginTransactionAsync(Kvs.Core.Database.IsolationLevel.ReadCommitted);

        // Assert
        transaction.IsolationLevel.Should().Be(Kvs.Core.Database.IsolationLevel.ReadCommitted);
    }

    /// <summary>
    /// Tests that multiple commits are idempotent.
    /// </summary>
    /// <returns>A <see cref="Task"/> representing the asynchronous unit test.</returns>
    [Fact(Timeout = 5000)]
    public async Task Multiple_Commits_Should_Be_Idempotent()
    {
        // Arrange
        await this.database.OpenAsync();
        using var transaction = await this.database.BeginTransactionAsync();

        // Act
        await transaction.CommitAsync();
        await transaction.CommitAsync(); // Second commit

        // Assert
        transaction.State.Should().Be(TransactionState.Committed);
    }

    /// <summary>
    /// Tests that operations after commit throw.
    /// </summary>
    /// <returns>A <see cref="Task"/> representing the asynchronous unit test.</returns>
    [Fact(Timeout = 5000)]
    public async Task Operations_After_Commit_Should_Throw()
    {
        // Arrange
        await this.database.OpenAsync();
        using var transaction = await this.database.BeginTransactionAsync();
        await transaction.CommitAsync();

        // Act
        Func<Task> action = async () => await transaction.WriteAsync("key", new TestData());

        // Assert
        await action.Should().ThrowAsync<InvalidOperationException>();
    }

    /// <summary>
    /// Tests that operations after rollback throw.
    /// </summary>
    /// <returns>A <see cref="Task"/> representing the asynchronous unit test.</returns>
    [Fact(Timeout = 5000)]
    public async Task Operations_After_Rollback_Should_Throw()
    {
        // Arrange
        await this.database.OpenAsync();
        using var transaction = await this.database.BeginTransactionAsync();
        await transaction.RollbackAsync();

        // Act
        Func<Task> action = async () => await transaction.ReadAsync<TestData>("key");

        // Assert
        await action.Should().ThrowAsync<InvalidOperationException>();
    }

    /// <summary>
    /// Tests that dispose rolls back active transaction.
    /// </summary>
    /// <returns>A <see cref="Task"/> representing the asynchronous unit test.</returns>
    [Fact(Timeout = 5000)]
    public async Task Dispose_Should_Rollback_Active_Transaction()
    {
        // Arrange
        await this.database.OpenAsync();
        var transaction = await this.database.BeginTransactionAsync();
        await transaction.WriteAsync("key", new TestData { Value = "test" });

        // Act
        transaction.Dispose();

        // Assert
        transaction.State.Should().Be(TransactionState.Aborted);
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

    private class TestData
    {
        public string Value { get; set; } = string.Empty;
    }
}
