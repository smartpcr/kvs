using System;
using System.Collections.Generic;
using System.IO;
using System.Threading.Tasks;
using Kvs.Core.Database;
using Xunit;

namespace Kvs.Core.UnitTests.Database;

/// <summary>
/// Tests for transaction isolation levels.
/// </summary>
public class IsolationTests : IDisposable
{
    private readonly string testDbPath;
    private readonly Database database;
    private readonly List<string> tempFiles;

    public IsolationTests()
    {
        this.testDbPath = Path.Combine(Path.GetTempPath(), $"kvs_test_isolation_{Guid.NewGuid()}.db");
        this.database = new Database(this.testDbPath);
        this.tempFiles = new List<string> { this.testDbPath };
    }

    [Fact]
    public async Task ReadCommitted_ShouldNotSeeDirtyReads()
    {
        // Arrange
        await this.database.OpenAsync();
        var collection = await this.database.CreateCollectionAsync("test");
        var doc1 = new Document { Id = "doc1", Data = new { value = 1 } };
        await collection.InsertAsync(doc1);

        // Act - Start transaction 1 and modify without committing
        var txn1 = await this.database.BeginTransactionAsync(IsolationLevel.ReadCommitted);
        var doc1InTxn1 = await collection.GetAsync("doc1", txn1);
        doc1InTxn1.Data = new { value = 2 };
        await collection.UpdateAsync(doc1InTxn1, txn1);

        // Start transaction 2 and try to read
        var txn2 = await this.database.BeginTransactionAsync(IsolationLevel.ReadCommitted);
        var doc1InTxn2 = await collection.GetAsync("doc1", txn2);

        // Assert - Transaction 2 should see original value
        Assert.Equal(1, ((dynamic)doc1InTxn2.Data).value);

        // Cleanup
        await txn1.RollbackAsync();
        await txn2.RollbackAsync();
    }

    [Fact]
    public async Task ReadCommitted_ShouldSeeCommittedChanges()
    {
        // Arrange
        await this.database.OpenAsync();
        var collection = await this.database.CreateCollectionAsync("test");
        var doc1 = new Document { Id = "doc1", Data = new { value = 1 } };
        await collection.InsertAsync(doc1);

        // Act - Transaction 1 commits a change
        var txn1 = await this.database.BeginTransactionAsync(IsolationLevel.ReadCommitted);
        var doc1InTxn1 = await collection.GetAsync("doc1", txn1);
        doc1InTxn1.Data = new { value = 2 };
        await collection.UpdateAsync(doc1InTxn1, txn1);
        await txn1.CommitAsync();

        // Transaction 2 reads after commit
        var txn2 = await this.database.BeginTransactionAsync(IsolationLevel.ReadCommitted);
        var doc1InTxn2 = await collection.GetAsync("doc1", txn2);

        // Assert - Transaction 2 should see committed value
        Assert.Equal(2, ((dynamic)doc1InTxn2.Data).value);

        // Cleanup
        await txn2.RollbackAsync();
    }

    [Fact]
    public async Task Serializable_ShouldPreventPhantomReads()
    {
        // Arrange
        await this.database.OpenAsync();
        var collection = await this.database.CreateCollectionAsync("test");
        
        // Insert initial documents
        await collection.InsertAsync(new Document { Id = "doc1", Data = new { category = "A", value = 1 } });
        await collection.InsertAsync(new Document { Id = "doc2", Data = new { category = "A", value = 2 } });

        // Act - Transaction 1 queries category A
        var txn1 = await this.database.BeginTransactionAsync(IsolationLevel.Serializable);
        var docsInTxn1 = await collection.QueryAsync(d => ((dynamic)d.Data).category == "A", txn1);
        var count1 = docsInTxn1.Count;

        // Transaction 2 tries to insert into category A
        var txn2 = await this.database.BeginTransactionAsync(IsolationLevel.Serializable);
        var newDoc = new Document { Id = "doc3", Data = new { category = "A", value = 3 } };
        
        // This should block or fail due to serializable isolation
        var insertTask = collection.InsertAsync(newDoc, txn2);

        // Transaction 1 queries again
        var docsInTxn1Again = await collection.QueryAsync(d => ((dynamic)d.Data).category == "A", txn1);
        var count2 = docsInTxn1Again.Count;

        // Assert - No phantom read should occur
        Assert.Equal(count1, count2);
        Assert.Equal(2, count1);

        // Cleanup
        await txn1.RollbackAsync();
        await txn2.RollbackAsync();
    }

    [Fact]
    public async Task Serializable_ShouldPreventNonRepeatableReads()
    {
        // Arrange
        await this.database.OpenAsync();
        var collection = await this.database.CreateCollectionAsync("test");
        var doc1 = new Document { Id = "doc1", Data = new { value = 1 } };
        await collection.InsertAsync(doc1);

        // Act - Transaction 1 reads twice
        var txn1 = await this.database.BeginTransactionAsync(IsolationLevel.Serializable);
        var firstRead = await collection.GetAsync("doc1", txn1);
        var value1 = ((dynamic)firstRead.Data).value;

        // Transaction 2 tries to modify
        var txn2 = await this.database.BeginTransactionAsync(IsolationLevel.Serializable);
        var doc1InTxn2 = await collection.GetAsync("doc1", txn2);
        doc1InTxn2.Data = new { value = 2 };
        
        // This should block or fail due to serializable isolation
        var updateTask = collection.UpdateAsync(doc1InTxn2, txn2);

        // Transaction 1 reads again
        var secondRead = await collection.GetAsync("doc1", txn1);
        var value2 = ((dynamic)secondRead.Data).value;

        // Assert - Values should be the same (no non-repeatable read)
        Assert.Equal(value1, value2);
        Assert.Equal(1, value1);

        // Cleanup
        await txn1.RollbackAsync();
        await txn2.RollbackAsync();
    }

    [Fact]
    public async Task DifferentIsolationLevels_ShouldCoexist()
    {
        // Arrange
        await this.database.OpenAsync();
        var collection = await this.database.CreateCollectionAsync("test");
        await collection.InsertAsync(new Document { Id = "doc1", Data = new { value = 1 } });
        await collection.InsertAsync(new Document { Id = "doc2", Data = new { value = 2 } });

        // Act - Start multiple transactions with different isolation levels
        var txnReadCommitted = await this.database.BeginTransactionAsync(IsolationLevel.ReadCommitted);
        var txnSerializable = await this.database.BeginTransactionAsync(IsolationLevel.Serializable);

        // Perform operations
        var doc1RC = await collection.GetAsync("doc1", txnReadCommitted);
        var doc1S = await collection.GetAsync("doc1", txnSerializable);

        // Assert - Both should succeed
        Assert.NotNull(doc1RC);
        Assert.NotNull(doc1S);
        Assert.Equal(1, ((dynamic)doc1RC.Data).value);
        Assert.Equal(1, ((dynamic)doc1S.Data).value);

        // Cleanup
        await txnReadCommitted.RollbackAsync();
        await txnSerializable.RollbackAsync();
    }

    public void Dispose()
    {
        this.database?.Dispose();
        foreach (var file in this.tempFiles)
        {
            try
            {
                if (File.Exists(file))
                {
                    File.Delete(file);
                }
                var walFile = Path.ChangeExtension(file, ".wal");
                if (File.Exists(walFile))
                {
                    File.Delete(walFile);
                }
            }
            catch
            {
                // Ignore cleanup errors
            }
        }
    }
}