using System;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
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
    public async Task SimpleDeadlock_ShouldBeDetected()
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
        var victimTransactionId = string.Empty;

        // Act - Create a deadlock scenario
        var txn1 = await this.database.BeginTransactionAsync();
        var txn2 = await this.database.BeginTransactionAsync();

        // Transaction 1 locks doc1
        var doc1InTxn1 = await txn1.ReadAsync<Document>("test/doc1");
        if (doc1InTxn1 != null)
        {
            doc1InTxn1.Set("value", 10);
            await txn1.WriteAsync("test/doc1", doc1InTxn1);
        }

        // Transaction 2 locks doc2
        var doc2InTxn2 = await txn2.ReadAsync<Document>("test/doc2");
        if (doc2InTxn2 != null)
        {
            doc2InTxn2.Set("value", 20);
            await txn2.WriteAsync("test/doc2", doc2InTxn2);
        }

        // Give transactions time to acquire their initial locks
        await Task.Delay(100);

        // Now create the deadlock
        var task1 = Task.Run(async () =>
        {
            // Transaction 1 tries to lock doc2 (held by txn2)
            try
            {
                var doc2InTxn1 = await txn1.ReadAsync<Document>("test/doc2");
                if (doc2InTxn1 != null)
                {
                    doc2InTxn1.Set("value", 30);
                    await txn1.WriteAsync("test/doc2", doc2InTxn1);
                }
            }
            catch (DeadlockException)
            {
                deadlockDetected = true;
                victimTransactionId = txn1.Id;
            }
        });

        // Start the second task with a small delay to ensure proper ordering
        await Task.Delay(50);
        var task2 = Task.Run(async () =>
        {
            // Transaction 2 tries to lock doc1 (held by txn1)
            try
            {
                var doc1InTxn2 = await txn2.ReadAsync<Document>("test/doc1");
                if (doc1InTxn2 != null)
                {
                    doc1InTxn2.Set("value", 40);
                    await txn2.WriteAsync("test/doc1", doc1InTxn2);
                }
            }
            catch (DeadlockException)
            {
                deadlockDetected = true;
                victimTransactionId = txn2.Id;
            }
        });

        // Wait for deadlock detection with timeout
        var timeoutTask = Task.Delay(3000);
        var completedTask = await Task.WhenAny(Task.WhenAll(task1, task2), timeoutTask);

        if (completedTask == timeoutTask)
        {
            // Force abort if timeout
            try
            {
                await txn1.RollbackAsync();
            }
            catch
            {
                // Ignore errors
            }

            try
            {
                await txn2.RollbackAsync();
            }
            catch
            {
                // Ignore errors
            }

            Assert.Fail("Deadlock detection timed out");
        }

        // Assert
        Assert.True(deadlockDetected);
        Assert.True(victimTransactionId == txn1.Id || victimTransactionId == txn2.Id);

        // Cleanup - only rollback transactions that weren't victims
        if (txn1.State != TransactionState.Aborted)
        {
            await txn1.RollbackAsync();
        }

        if (txn2.State != TransactionState.Aborted)
        {
            await txn2.RollbackAsync();
        }
    }

    [Fact(Timeout = 5000)]
    public async Task CircularDeadlock_ShouldBeDetected()
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
        var doc3 = new Document { Id = "doc3" };
        doc3.Set("value", 3);
        await collection.InsertAsync(doc3);

        var deadlockCount = 0;

        // Act - Create a circular deadlock with 3 transactions
        var txn1 = await this.database.BeginTransactionAsync();
        var txn2 = await this.database.BeginTransactionAsync();
        var txn3 = await this.database.BeginTransactionAsync();

        // Each transaction locks one document
        var d1 = await txn1.ReadAsync<Document>("test/doc1");
        if (d1 != null)
        {
            d1.Set("value", 10);
            await txn1.WriteAsync("test/doc1", d1);
        }

        var d2 = await txn2.ReadAsync<Document>("test/doc2");
        if (d2 != null)
        {
            d2.Set("value", 20);
            await txn2.WriteAsync("test/doc2", d2);
        }

        var d3 = await txn3.ReadAsync<Document>("test/doc3");
        if (d3 != null)
        {
            d3.Set("value", 30);
            await txn3.WriteAsync("test/doc3", d3);
        }

        // Create circular wait: txn1 -> doc2, txn2 -> doc3, txn3 -> doc1
        var task1 = Task.Run(async () =>
        {
            try
            {
                var d = await txn1.ReadAsync<Document>("test/doc2");
                if (d != null)
                {
                    d.Set("value", 40);
                    await txn1.WriteAsync("test/doc2", d);
                }
            }
            catch (DeadlockException)
            {
                deadlockCount++;
            }
        });

        var task2 = Task.Run(async () =>
        {
            try
            {
                var d = await txn2.ReadAsync<Document>("test/doc3");
                if (d != null)
                {
                    d.Set("value", 50);
                    await txn2.WriteAsync("test/doc3", d);
                }
            }
            catch (DeadlockException)
            {
                deadlockCount++;
            }
        });

        var task3 = Task.Run(async () =>
        {
            try
            {
                var d = await txn3.ReadAsync<Document>("test/doc1");
                if (d != null)
                {
                    d.Set("value", 60);
                    await txn3.WriteAsync("test/doc1", d);
                }
            }
            catch (DeadlockException)
            {
                deadlockCount++;
            }
        });

        // Wait for deadlock detection with timeout
        var timeoutTask = Task.Delay(3000);
        var completedTask = await Task.WhenAny(Task.WhenAll(task1, task2, task3), timeoutTask);

        if (completedTask == timeoutTask)
        {
            // Force abort if timeout
            try
            {
                await txn1.RollbackAsync();
            }
            catch
            {
                // Ignore errors
            }

            try
            {
                await txn2.RollbackAsync();
            }
            catch
            {
                // Ignore errors
            }

            try
            {
                await txn3.RollbackAsync();
            }
            catch
            {
                // Ignore errors
            }

            Assert.Fail("Deadlock detection timed out");
        }

        // Assert
        Assert.True(deadlockCount > 0);

        // Cleanup - only rollback transactions that weren't victims
        if (txn1.State != TransactionState.Aborted)
        {
            await txn1.RollbackAsync();
        }

        if (txn2.State != TransactionState.Aborted)
        {
            await txn2.RollbackAsync();
        }

        if (txn3.State != TransactionState.Aborted)
        {
            await txn3.RollbackAsync();
        }
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
        if (d1 != null)
        {
            d1.Set("value", 10);
            await txn1.WriteAsync("test/doc1", d1);
        }

        var d2 = await txn2.ReadAsync<Document>("test/doc2");
        if (d2 != null)
        {
            d2.Set("value", 20);
            await txn2.WriteAsync("test/doc2", d2);
        }

        // Create deadlock
        var task1 = Task.Run(async () =>
        {
            try
            {
                var d = await txn1.ReadAsync<Document>("test/doc2");
                if (d != null)
                {
                    d.Set("value", 30);
                    await txn1.WriteAsync("test/doc2", d);
                }
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
                var d = await txn2.ReadAsync<Document>("test/doc1");
                if (d != null)
                {
                    d.Set("value", 40);
                    await txn2.WriteAsync("test/doc1", d);
                }
            }
            catch (DeadlockException)
            {
                victimId = txn2.Id;
            }
        });

        var timeoutTask = Task.Delay(3000);
        var completedTask = await Task.WhenAny(Task.WhenAll(task1, task2), timeoutTask);

        if (completedTask == timeoutTask)
        {
            // Force abort if timeout
            try
            {
                await txn1.RollbackAsync();
            }
            catch
            {
                // Ignore errors
            }

            try
            {
                await txn2.RollbackAsync();
            }
            catch
            {
                // Ignore errors
            }

            Assert.Fail("Deadlock detection timed out");
        }

        // Assert - Younger transaction (txn2) should be the victim
        Assert.Equal(txn2.Id, victimId);

        // Cleanup - only rollback transactions that weren't victims
        if (txn1.State != TransactionState.Aborted)
        {
            await txn1.RollbackAsync();
        }

        if (txn2.State != TransactionState.Aborted)
        {
            await txn2.RollbackAsync();
        }
    }

    [Fact(Timeout = 5000)]
    public async Task DeadlockRecovery_VictimShouldRetrySuccessfully()
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

        var deadlockOccurred = false;
        var retrySucceeded = false;

        // Act
        var txn1 = await this.database.BeginTransactionAsync();
        var txn2 = await this.database.BeginTransactionAsync();

        // Set up deadlock scenario
        var d1 = await txn1.ReadAsync<Document>("test/doc1");
        if (d1 != null)
        {
            d1.Set("value", 10);
            await txn1.WriteAsync("test/doc1", d1);
        }

        var d2 = await txn2.ReadAsync<Document>("test/doc2");
        if (d2 != null)
        {
            d2.Set("value", 20);
            await txn2.WriteAsync("test/doc2", d2);
        }

        // Transaction 2 will be victim and retry
        var task2 = Task.Run(async () =>
        {
            try
            {
                var d = await txn2.ReadAsync<Document>("test/doc1");
                if (d != null)
                {
                    d.Set("value", 30);
                    await txn2.WriteAsync("test/doc1", d);
                }
            }
            catch (DeadlockException)
            {
                deadlockOccurred = true;

                // Rollback and retry
                await txn2.RollbackAsync();

                // Wait for txn1 to complete
                await Task.Delay(200);

                // Retry with new transaction
                var txn2Retry = await this.database.BeginTransactionAsync();
                var dr1 = await txn2Retry.ReadAsync<Document>("test/doc1");
                if (dr1 != null)
                {
                    dr1.Set("value", 30);
                    await txn2Retry.WriteAsync("test/doc1", dr1);
                }

                var dr2 = await txn2Retry.ReadAsync<Document>("test/doc2");
                if (dr2 != null)
                {
                    dr2.Set("value", 40);
                    await txn2Retry.WriteAsync("test/doc2", dr2);
                }

                await txn2Retry.CommitAsync();
                retrySucceeded = true;
            }
        });

        // Transaction 1 causes deadlock then completes
        await Task.Delay(50);
        var dd = await txn1.ReadAsync<Document>("test/doc2");
        if (dd != null)
        {
            dd.Set("value", 50);
            await txn1.WriteAsync("test/doc2", dd);
        }

        await txn1.CommitAsync();

        await task2;

        // Assert
        Assert.True(deadlockOccurred);
        Assert.True(retrySucceeded);
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
