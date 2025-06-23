using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using FluentAssertions;
using Kvs.Core.Database;
using Kvs.Core.TestUtilities;
using Xunit;

namespace Kvs.Core.UnitTests.DatabaseTests;

/// <summary>
/// Tests for the Collection class.
/// </summary>
public class CollectionTests : IDisposable
{
    private readonly string testDbPath;
    private readonly Core.Database.Database database;

    /// <summary>
    /// Initializes a new instance of the <see cref="CollectionTests"/> class.
    /// </summary>
    public CollectionTests()
    {
        this.testDbPath = Path.Combine(Path.GetTempPath(), $"test_{Guid.NewGuid()}.db");
        this.database = new Core.Database.Database(this.testDbPath);
    }

    /// <summary>
    /// Tests that InsertAsync adds a document and returns an ID.
    /// </summary>
    /// <returns>A <see cref="Task"/> representing the asynchronous unit test.</returns>
    [Fact(Timeout = 5000)]
    public async Task InsertAsync_Should_Add_Document_And_Return_Id()
    {
        // Arrange
        await this.database.OpenAsync();
        var collection = this.database.GetCollection<TestDocument>("test");
        var document = new TestDocument { Name = "Test", Value = 42 };

        // Act
        var id = await collection.InsertAsync(document);

        // Assert
        id.Should().NotBeNullOrEmpty();
    }

    /// <summary>
    /// Tests that FindByIdAsync returns an inserted document.
    /// </summary>
    /// <returns>A <see cref="Task"/> representing the asynchronous unit test.</returns>
    [Fact(Timeout = 5000)]
    public async Task FindByIdAsync_Should_Return_Inserted_Document()
    {
        // Arrange
        await this.database.OpenAsync();
        var collection = this.database.GetCollection<TestDocument>("test");
        var document = new TestDocument { Name = "Test", Value = 42 };
        var id = await collection.InsertAsync(document);

        // Act
        var retrieved = await collection.FindByIdAsync(id);

        // Assert
        retrieved.Should().NotBeNull();
        retrieved!.Name.Should().Be("Test");
        retrieved.Value.Should().Be(42);
    }

    /// <summary>
    /// Tests that FindByIdAsync returns null for a non-existent ID.
    /// </summary>
    /// <returns>A <see cref="Task"/> representing the asynchronous unit test.</returns>
    [Fact(Timeout = 5000)]
    public async Task FindByIdAsync_Should_Return_Null_For_NonExistent_Id()
    {
        // Arrange
        await this.database.OpenAsync();
        var collection = this.database.GetCollection<TestDocument>("test");

        // Act
        var retrieved = await collection.FindByIdAsync("non-existent");

        // Assert
        retrieved.Should().BeNull();
    }

    /// <summary>
    /// Tests that UpdateAsync updates an existing document.
    /// </summary>
    /// <returns>A <see cref="Task"/> representing the asynchronous unit test.</returns>
    [Fact(Timeout = 5000)]
    public async Task UpdateAsync_Should_Update_Existing_Document()
    {
        // Arrange
        await this.database.OpenAsync();
        var collection = this.database.GetCollection<TestDocument>("test");
        var document = new TestDocument { Name = "Original", Value = 42 };
        var id = await collection.InsertAsync(document);

        // Act
        var updated = new TestDocument { Name = "Updated", Value = 100 };
        var result = await collection.UpdateAsync(id, updated);

        // Assert
        result.Should().BeTrue();
        var retrieved = await collection.FindByIdAsync(id);
        retrieved!.Name.Should().Be("Updated");
        retrieved.Value.Should().Be(100);
    }

    /// <summary>
    /// Tests that UpdateAsync returns false for a non-existent document.
    /// </summary>
    /// <returns>A <see cref="Task"/> representing the asynchronous unit test.</returns>
    [Fact(Timeout = 5000)]
    public async Task UpdateAsync_Should_Return_False_For_NonExistent_Document()
    {
        // Arrange
        await this.database.OpenAsync();
        var collection = this.database.GetCollection<TestDocument>("test");
        var document = new TestDocument { Name = "Test", Value = 42 };

        // Act
        var result = await collection.UpdateAsync("non-existent", document);

        // Assert
        result.Should().BeFalse();
    }

    /// <summary>
    /// Tests that DeleteAsync removes a document.
    /// </summary>
    /// <returns>A <see cref="Task"/> representing the asynchronous unit test.</returns>
    [Fact(Timeout = 5000)]
    public async Task DeleteAsync_Should_Remove_Document()
    {
        // Arrange
        await this.database.OpenAsync();
        var collection = this.database.GetCollection<TestDocument>("test");
        var document = new TestDocument { Name = "Test", Value = 42 };
        var id = await collection.InsertAsync(document);

        // Act
        var result = await collection.DeleteAsync(id);

        // Assert
        result.Should().BeTrue();
        var retrieved = await collection.FindByIdAsync(id);
        retrieved.Should().BeNull();
    }

    /// <summary>
    /// Tests that DeleteAsync returns false for a non-existent document.
    /// </summary>
    /// <returns>A <see cref="Task"/> representing the asynchronous unit test.</returns>
    [Fact(Timeout = 5000)]
    public async Task DeleteAsync_Should_Return_False_For_NonExistent_Document()
    {
        // Arrange
        await this.database.OpenAsync();
        var collection = this.database.GetCollection<TestDocument>("test");

        // Act
        var result = await collection.DeleteAsync("non-existent");

        // Assert
        result.Should().BeFalse();
    }

    /// <summary>
    /// Tests that InsertManyAsync adds multiple documents.
    /// </summary>
    /// <returns>A <see cref="Task"/> representing the asynchronous unit test.</returns>
    [Fact(Timeout = 5000)]
    public async Task InsertManyAsync_Should_Add_Multiple_Documents()
    {
        // Arrange
        await this.database.OpenAsync();
        var collection = this.database.GetCollection<TestDocument>("test");
        var documents = new[]
        {
            new TestDocument { Name = "Doc1", Value = 1 },
            new TestDocument { Name = "Doc2", Value = 2 },
            new TestDocument { Name = "Doc3", Value = 3 }
        };

        // Act
        var ids = (await collection.InsertManyAsync(documents)).ToList();

        // Assert
        ids.Should().HaveCount(3);
        ids.Should().OnlyContain(id => !string.IsNullOrEmpty(id));
    }

    /// <summary>
    /// Tests that FindAllAsync returns all documents.
    /// </summary>
    /// <returns>A <see cref="Task"/> representing the asynchronous unit test.</returns>
    [Fact(Timeout = 5000)]
    public async Task FindAllAsync_Should_Return_All_Documents()
    {
        // Arrange
        await this.database.OpenAsync();
        var collection = this.database.GetCollection<TestDocument>("test");
        await collection.InsertAsync(new TestDocument { Name = "Doc1", Value = 1 });
        await collection.InsertAsync(new TestDocument { Name = "Doc2", Value = 2 });
        await collection.InsertAsync(new TestDocument { Name = "Doc3", Value = 3 });

        // Act
        var documents = new List<TestDocument>();
        await foreach (var doc in collection.FindAllAsync())
        {
            documents.Add(doc);
        }

        // Assert
        documents.Should().HaveCount(3);
        documents.Select(d => d.Name).Should().BeEquivalentTo("Doc1", "Doc2", "Doc3");
    }

    /// <summary>
    /// Tests that CountAsync returns the document count.
    /// </summary>
    /// <returns>A <see cref="Task"/> representing the asynchronous unit test.</returns>
    [Fact(Timeout = 5000)]
    public async Task CountAsync_Should_Return_Document_Count()
    {
        // Arrange
        await this.database.OpenAsync();
        var collection = this.database.GetCollection<TestDocument>("test");

        // Act & Assert - Empty collection
        var count1 = await collection.CountAsync();
        count1.Should().Be(0);

        // Add documents
        await collection.InsertAsync(new TestDocument { Name = "Doc1", Value = 1 });
        await collection.InsertAsync(new TestDocument { Name = "Doc2", Value = 2 });

        var count2 = await collection.CountAsync();
        count2.Should().Be(2);

        // Delete one
        var id = await collection.InsertAsync(new TestDocument { Name = "Doc3", Value = 3 });
        await collection.DeleteAsync(id);

        var count3 = await collection.CountAsync();
        count3.Should().Be(2);
    }

    /// <summary>
    /// Tests that ClearAsync removes all documents.
    /// </summary>
    /// <returns>A <see cref="Task"/> representing the asynchronous unit test.</returns>
    [Fact(Timeout = 5000)]
    public async Task ClearAsync_Should_Remove_All_Documents()
    {
        // Arrange
        await this.database.OpenAsync();
        var collection = this.database.GetCollection<TestDocument>("test");
        await collection.InsertAsync(new TestDocument { Name = "Doc1", Value = 1 });
        await collection.InsertAsync(new TestDocument { Name = "Doc2", Value = 2 });
        await collection.InsertAsync(new TestDocument { Name = "Doc3", Value = 3 });

        // Act
        await collection.ClearAsync();

        // Assert
        var count = await collection.CountAsync();
        count.Should().Be(0);
        var documents = new List<TestDocument>();
        await foreach (var doc in collection.FindAllAsync())
        {
            documents.Add(doc);
        }

        documents.Should().BeEmpty();
    }

    /// <summary>
    /// Tests that CreateIndexAsync completes successfully.
    /// </summary>
    /// <returns>A <see cref="Task"/> representing the asynchronous unit test.</returns>
    [Fact(Timeout = 5000)]
    public async Task CreateIndexAsync_Should_Complete_Successfully()
    {
        // Arrange
        await this.database.OpenAsync();
        var collection = this.database.GetCollection<TestDocument>("test");
        await collection.InsertAsync(new TestDocument { Name = "Doc1", Value = 1 });

        // Act
        Func<Task> action = async () => await collection.CreateIndexAsync("Name");

        // Assert
        await action.Should().NotThrowAsync();
    }

    /// <summary>
    /// Tests that DropIndexAsync completes successfully.
    /// </summary>
    /// <returns>A <see cref="Task"/> representing the asynchronous unit test.</returns>
    [Fact(Timeout = 5000)]
    public async Task DropIndexAsync_Should_Complete_Successfully()
    {
        // Arrange
        await this.database.OpenAsync();
        var collection = this.database.GetCollection<TestDocument>("test");
        await collection.CreateIndexAsync("Name");

        // Act
        Func<Task> action = async () => await collection.DropIndexAsync("Name");

        // Assert
        await action.Should().NotThrowAsync();
    }

    /// <summary>
    /// Tests that collection works with Document type.
    /// </summary>
    /// <returns>A <see cref="Task"/> representing the asynchronous unit test.</returns>
    [Fact(Timeout = 5000)]
    public async Task Collection_Should_Work_With_Document_Type()
    {
        // Arrange
        await this.database.OpenAsync();
        var collection = this.database.GetCollection<Document>("documents");
        var doc = new Document("test-id");
        doc.Set("name", "Test Document");
        doc.Set("value", 42);
        doc.Set("active", true);

        // Act
        var id = await collection.InsertAsync(doc);
        var retrieved = await collection.FindByIdAsync(id);

        // Assert
        retrieved.Should().NotBeNull();
        retrieved!.Get<string>("name").Should().Be("Test Document");
        retrieved.Get<int>("value").Should().Be(42);
        retrieved.Get<bool>("active").Should().BeTrue();
    }

    /// <summary>
    /// Disposes of the test resources.
    /// </summary>
    public void Dispose()
    {
        this.database?.Dispose();

        FileHelper.DeleteFileWithRetry(this.testDbPath);
        var walPath = Path.ChangeExtension(this.testDbPath, ".wal");
        FileHelper.DeleteFileWithRetry(walPath);
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
