using System;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using FluentAssertions;
using Kvs.Core.Database;
using Xunit;

namespace Kvs.Core.UnitTests.DatabaseTests;

/// <summary>
/// Tests for deadlock detection and resolution.
/// </summary>
public class DeadlockTests : IDisposable
{
    private readonly string testDbPath;
    private readonly Core.Database.Database database;
    private readonly List<string> tempFiles;

    public DeadlockTests()
    {
        this.testDbPath = Path.Combine(Path.GetTempPath(), $"kvs_test_deadlock_{Guid.NewGuid()}.db");
        this.database = new Core.Database.Database(this.testDbPath);
        this.tempFiles = new List<string> { this.testDbPath };
    }

    [Fact(Timeout = 5000)]
    public async Task SimpleDeadlock_ShouldBeDetectedAndResolved()
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

        var deadlockDetected = false;
        Exception? txn1Exception = null;
        Exception? txn2Exception = null;

        // Act - Create a deadlock scenario
        var txn1 = await this.database.BeginTransactionAsync();
        var txn2 = await this.database.BeginTransactionAsync();

        // Transaction 1 locks doc1
        var doc1InTxn1 = await txn1.ReadAsync<Document>("test/doc1");
        doc1InTxn1.Should().NotBeNull();
        doc1InTxn1!.Set("value", 10);
        await txn1.WriteAsync("test/doc1", doc1InTxn1);

        // Transaction 2 locks doc2
        var doc2InTxn2 = await txn2.ReadAsync<Document>("test/doc2");
        doc2InTxn2.Should().NotBeNull();
        doc2InTxn2!.Set("value", 20);
        await txn2.WriteAsync("test/doc2", doc2InTxn2);

        // Wait for locks to be established
        await Task.Delay(200);

        // Create the deadlock by having each transaction try to access the other's locked resource
        var task1 = Task.Run(async () =>
        {
            try
            {
                // Transaction 1 tries to lock doc2 (held by txn2)
                var doc2InTxn1 = await txn1.ReadAsync<Document>("test/doc2");
                if (doc2InTxn1 != null)
                {
                    doc2InTxn1.Set("value", 30);
                    await txn1.WriteAsync("test/doc2", doc2InTxn1);
                }

                await txn1.CommitAsync();
            }
            catch (Exception ex)
            {
                txn1Exception = ex;
                if (ex is DeadlockException)
                {
                    deadlockDetected = true;
                }
            }
        });

        var task2 = Task.Run(async () =>
        {
            try
            {
                // Transaction 2 tries to lock doc1 (held by txn1)
                await Task.Delay(50); // Small delay to ensure proper ordering
                var doc1InTxn2 = await txn2.ReadAsync<Document>("test/doc1");
                if (doc1InTxn2 != null)
                {
                    doc1InTxn2.Set("value", 40);
                    await txn2.WriteAsync("test/doc1", doc1InTxn2);
                }

                await txn2.CommitAsync();
            }
            catch (Exception ex)
            {
                txn2Exception = ex;
                if (ex is DeadlockException)
                {
                    deadlockDetected = true;
                }
            }
        });

        // Wait for both tasks (with timeout to prevent test hanging)
        await Task.WhenAll(
            Task.WhenAny(task1, Task.Delay(3000)),
            Task.WhenAny(task2, Task.Delay(3000)));

        // Assert - At least one transaction should have detected deadlock
        deadlockDetected.Should().BeTrue("deadlock should be detected");

        // One transaction should succeed, one should fail with DeadlockException
        var exceptions = new[] { txn1Exception, txn2Exception }.Where(e => e != null).ToList();
        exceptions.Should().HaveCountGreaterOrEqualTo(1, "at least one transaction should fail");
        exceptions.Exists(e => e is DeadlockException).Should().BeTrue("at least one exception should be DeadlockException");

        // Cleanup
        try
        {
            if (txn1.State == TransactionState.Active)
            {
                await txn1.RollbackAsync();
            }
        }
        catch
        {
            // Ignore
        }

        try
        {
            if (txn2.State == TransactionState.Active)
            {
                await txn2.RollbackAsync();
            }
        }
        catch
        {
            // Ignore
        }
    }

    [Fact(Timeout = 10000)]
    public async Task NoDeadlock_WithProperLockOrdering_ShouldSucceed()
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

        // Act - Both transactions acquire locks in the same order
        var txn1 = await this.database.BeginTransactionAsync();
        var txn2 = await this.database.BeginTransactionAsync();

        var task1 = Task.Run(async () =>
        {
            try
            {
                // Lock doc1 first, then doc2
                var d1 = await txn1.ReadAsync<Document>("test/doc1");
                d1!.Set("value", 10);
                await txn1.WriteAsync("test/doc1", d1);

                await Task.Delay(50);

                var d2 = await txn1.ReadAsync<Document>("test/doc2");
                d2!.Set("value", 20);
                await txn1.WriteAsync("test/doc2", d2);

                await txn1.CommitAsync();
            }
            catch (Exception ex)
            {
                throw new Exception($"Task1 failed: {ex.Message}", ex);
            }
        });

        var task2 = Task.Run(async () =>
        {
            try
            {
                // Also lock doc1 first, then doc2 (same order as txn1)
                await Task.Delay(25); // Small delay to let txn1 go first

                var d1 = await txn2.ReadAsync<Document>("test/doc1");
                d1!.Set("value", 30);
                await txn2.WriteAsync("test/doc1", d1);

                var d2 = await txn2.ReadAsync<Document>("test/doc2");
                d2!.Set("value", 40);
                await txn2.WriteAsync("test/doc2", d2);

                await txn2.CommitAsync();
            }
            catch (Exception ex)
            {
                throw new Exception($"Task2 failed: {ex.Message}", ex);
            }
        });

        // Assert - Both should complete without deadlock
        var allTasks = Task.WhenAll(task1, task2);
        await allTasks;

        allTasks.IsCompletedSuccessfully.Should().BeTrue("both transactions should complete successfully");
    }

    [Fact(Timeout = 5000)]
    public async Task DeadlockVictimSelection_ShouldChooseYoungestTransaction()
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

        string? victimId = null;

        // Act - Create transactions with time gap
        var txn1 = await this.database.BeginTransactionAsync();
        await Task.Delay(100); // Make txn1 older
        var txn2 = await this.database.BeginTransactionAsync();

        // Lock in opposite order
        var d1 = await txn1.ReadAsync<Document>("test/doc1");
        d1!.Set("value", 10);
        await txn1.WriteAsync("test/doc1", d1);

        var d2 = await txn2.ReadAsync<Document>("test/doc2");
        d2!.Set("value", 20);
        await txn2.WriteAsync("test/doc2", d2);

        await Task.Delay(200);

        // Create deadlock
        var task1 = Task.Run(async () =>
        {
            try
            {
                var d = await txn1.ReadAsync<Document>("test/doc2");
                d!.Set("value", 30);
                await txn1.WriteAsync("test/doc2", d);
                await txn1.CommitAsync();
            }
            catch (DeadlockException)
            {
                victimId = txn1.Id;
            }
        });

        var task2 = Task.Run(async () =>
        {
            try
            {
                await Task.Delay(50);
                var d = await txn2.ReadAsync<Document>("test/doc1");
                d!.Set("value", 40);
                await txn2.WriteAsync("test/doc1", d);
                await txn2.CommitAsync();
            }
            catch (DeadlockException)
            {
                victimId = txn2.Id;
            }
        });

        await Task.WhenAll(
            Task.WhenAny(task1, Task.Delay(3000)),
            Task.WhenAny(task2, Task.Delay(3000)));

        // Assert - Younger transaction (txn2) should be the victim
        victimId.Should().Be(txn2.Id, "younger transaction should be chosen as deadlock victim");

        // Cleanup
        try
        {
            if (txn1.State == TransactionState.Active)
            {
                await txn1.RollbackAsync();
            }
        }
        catch
        {
            // Ignore
        }

        try
        {
            if (txn2.State == TransactionState.Active)
            {
                await txn2.RollbackAsync();
            }
        }
        catch
        {
            // Ignore
        }
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
