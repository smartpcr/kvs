using System;
using System.Collections.Generic;
using System.Data;
using System.IO;
using System.Threading.Tasks;
using Kvs.Core.Database;
using Xunit;

namespace Kvs.Core.UnitTests.DatabaseTests;

/// <summary>
/// Tests for transaction isolation levels.
/// </summary>
public class IsolationTests : IDisposable
{
    private readonly string testDbPath;
    private readonly Core.Database.Database database;
    private readonly List<string> tempFiles;

    public IsolationTests()
    {
        this.testDbPath = Path.Combine(Path.GetTempPath(), $"kvs_test_isolation_{Guid.NewGuid()}.db");
        this.database = new Core.Database.Database(this.testDbPath);
        this.tempFiles = new List<string> { this.testDbPath };
    }

    [Fact(Timeout = 5000)]
    public async Task ReadCommitted_ShouldNotSeeDirtyReads()
    {
        // Arrange
        await this.database.OpenAsync();
        var collection = this.database.GetCollection<Document>("test");
        var doc1 = new Document { Id = "doc1" };
        doc1.Set("value", 1);
        await collection.InsertAsync(doc1);

        // Act - Start transaction 1 and modify without committing
        var txn1 = await this.database.BeginTransactionAsync(IsolationLevel.ReadCommitted);
        var doc1InTxn1 = await txn1.ReadAsync<Document>("test/doc1");
        if (doc1InTxn1 != null)
        {
            doc1InTxn1.Set("value", 2);
            await txn1.WriteAsync("test/doc1", doc1InTxn1);
        }

        // Start transaction 2 and try to read
        var txn2 = await this.database.BeginTransactionAsync(IsolationLevel.ReadCommitted);
        var doc1InTxn2 = await txn2.ReadAsync<Document>("test/doc1");

        // Assert - Transaction 2 should see original value
        Assert.NotNull(doc1InTxn2);
        Assert.Equal(1, doc1InTxn2.Get<int>("value"));

        // Cleanup
        await txn1.RollbackAsync();
        await txn2.RollbackAsync();
    }

    [Fact(Timeout = 5000)]
    public async Task ReadCommitted_ShouldSeeCommittedChanges()
    {
        // Arrange
        await this.database.OpenAsync();
        var collection = this.database.GetCollection<Document>("test");
        var doc1 = new Document { Id = "doc1" };
        doc1.Set("value", 1);
        await collection.InsertAsync(doc1);

        // Act - Transaction 1 commits a change
        var txn1 = await this.database.BeginTransactionAsync(IsolationLevel.ReadCommitted);
        var doc1InTxn1 = await txn1.ReadAsync<Document>("test/doc1");
        if (doc1InTxn1 != null)
        {
            doc1InTxn1.Set("value", 2);
            await txn1.WriteAsync("test/doc1", doc1InTxn1);
        }

        await txn1.CommitAsync();

        // Transaction 2 reads after commit
        var txn2 = await this.database.BeginTransactionAsync(IsolationLevel.ReadCommitted);
        var doc1InTxn2 = await txn2.ReadAsync<Document>("test/doc1");

        // Assert - Transaction 2 should see committed value
        Assert.NotNull(doc1InTxn2);
        Assert.Equal(2, doc1InTxn2.Get<int>("value"));

        // Cleanup
        await txn2.RollbackAsync();
    }

    [Fact(Timeout = 5000)]
    public async Task Serializable_ShouldPreventPhantomReads()
    {
        // Arrange
        await this.database.OpenAsync();
        var collection = this.database.GetCollection<Document>("test");

        // Insert initial documents
        var doc1 = new Document { Id = "doc1" };
        doc1.Set("category", "A");
        doc1.Set("value", 1);
        await collection.InsertAsync(doc1);

        var doc2 = new Document { Id = "doc2" };
        doc2.Set("category", "A");
        doc2.Set("value", 2);
        await collection.InsertAsync(doc2);

        // Act - Transaction 1 queries category A
        var txn1 = await this.database.BeginTransactionAsync(IsolationLevel.Serializable);

        // Note: QueryAsync doesn't exist, we'll count manually
        var count1 = 0;
        var d1 = await txn1.ReadAsync<Document>("test/doc1");
        if (d1 != null && d1.Get<string>("category") == "A")
        {
            count1++;
        }

        var d2 = await txn1.ReadAsync<Document>("test/doc2");
        if (d2 != null && d2.Get<string>("category") == "A")
        {
            count1++;
        }

        // Transaction 2 tries to insert into category A
        var txn2 = await this.database.BeginTransactionAsync(IsolationLevel.Serializable);
        var newDoc = new Document { Id = "doc3" };
        newDoc.Set("category", "A");
        newDoc.Set("value", 3);

        // This should block or fail due to serializable isolation
        // Note: Collections don't support transactional operations directly,
        // so we'll simulate by writing directly via transaction
        await txn2.WriteAsync("test/doc3", newDoc);

        // Transaction 1 queries again
        var count2 = 0;
        d1 = await txn1.ReadAsync<Document>("test/doc1");
        if (d1 != null && d1.Get<string>("category") == "A")
        {
            count2++;
        }

        d2 = await txn1.ReadAsync<Document>("test/doc2");
        if (d2 != null && d2.Get<string>("category") == "A")
        {
            count2++;
        }

        // Try to read the new doc - should not be visible
        var d3 = await txn1.ReadAsync<Document>("test/doc3");
        if (d3 != null && d3.Get<string>("category") == "A")
        {
            count2++;
        }

        // Assert - No phantom read should occur
        Assert.Equal(count1, count2);
        Assert.Equal(2, count1);

        // Cleanup
        await txn1.RollbackAsync();
        await txn2.RollbackAsync();
    }

    [Fact(Timeout = 5000)]
    public async Task Serializable_ShouldPreventNonRepeatableReads()
    {
        // Arrange
        await this.database.OpenAsync();
        var collection = this.database.GetCollection<Document>("test");
        var doc1 = new Document { Id = "doc1" };
        doc1.Set("value", 1);
        await collection.InsertAsync(doc1);

        // Act - Transaction 1 reads twice
        var txn1 = await this.database.BeginTransactionAsync(IsolationLevel.Serializable);
        var firstRead = await txn1.ReadAsync<Document>("test/doc1");
        Assert.NotNull(firstRead);
        var value1 = firstRead.Get<int>("value");

        // Transaction 2 tries to modify
        var txn2 = await this.database.BeginTransactionAsync(IsolationLevel.Serializable);
        var doc1InTxn2 = await txn2.ReadAsync<Document>("test/doc1");
        if (doc1InTxn2 != null)
        {
            doc1InTxn2.Set("value", 2);

            // This should block or fail due to serializable isolation
            await txn2.WriteAsync("test/doc1", doc1InTxn2);
        }

        // Transaction 1 reads again
        var secondRead = await txn1.ReadAsync<Document>("test/doc1");
        Assert.NotNull(secondRead);
        var value2 = secondRead.Get<int>("value");

        // Assert - Values should be the same (no non-repeatable read)
        Assert.Equal(value1, value2);
        Assert.Equal(1, value1);

        // Cleanup
        await txn1.RollbackAsync();
        await txn2.RollbackAsync();
    }

    [Fact(Timeout = 5000)]
    public async Task DifferentIsolationLevels_ShouldCoexist()
    {
        // Arrange
        await this.database.OpenAsync();
        var collection = this.database.GetCollection<Document>("test");
        var doc1 = new Document { Id = "doc1" };
        doc1.Set("value", 1);
        await collection.InsertAsync(doc1);
        var doc2 = new Document { Id = "doc2" };
        doc2.Set("value", 2);
        await collection.InsertAsync(doc2);

        // Act - Start multiple transactions with different isolation levels
        var txnReadCommitted = await this.database.BeginTransactionAsync(IsolationLevel.ReadCommitted);
        var txnSerializable = await this.database.BeginTransactionAsync(IsolationLevel.Serializable);

        // Perform operations
        var doc1RC = await txnReadCommitted.ReadAsync<Document>("test/doc1");
        var doc1S = await txnSerializable.ReadAsync<Document>("test/doc1");

        // Assert - Both should succeed
        Assert.NotNull(doc1RC);
        Assert.NotNull(doc1S);
        Assert.Equal(1, doc1RC.Get<int>("value"));
        Assert.Equal(1, doc1S.Get<int>("value"));

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
