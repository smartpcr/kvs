using System;
using System.Linq;
using System.Threading.Tasks;
using FluentAssertions;
using Kvs.Core.Database;
using Xunit;

namespace Kvs.Core.UnitTests.DatabaseTests;

/// <summary>
/// Tests for the Document class.
/// </summary>
public class DocumentTests
{
    /// <summary>
    /// Tests that constructor initializes with new GUID.
    /// </summary>
    [Fact(Timeout = 5000)]
    public void Constructor_Should_Initialize_With_New_Guid()
    {
        // Act
        var doc = new Document();

        // Assert
        doc.Id.Should().NotBeNullOrEmpty();
        Guid.TryParse(doc.Id, out _).Should().BeTrue();
        doc.Version.Should().Be(1);
        doc.Created.Should().BeCloseTo(DateTime.UtcNow, TimeSpan.FromSeconds(1));
        doc.Updated.Should().BeCloseTo(DateTime.UtcNow, TimeSpan.FromSeconds(1));
    }

    /// <summary>
    /// Tests that constructor with ID uses provided ID.
    /// </summary>
    [Fact(Timeout = 5000)]
    public void Constructor_With_Id_Should_Use_Provided_Id()
    {
        // Arrange
        const string id = "custom-id";

        // Act
        var doc = new Document(id);

        // Assert
        doc.Id.Should().Be(id);
    }

    /// <summary>
    /// Tests that indexer sets and gets values.
    /// </summary>
    [Fact(Timeout = 5000)]
    public void Indexer_Should_Set_And_Get_Values()
    {
        // Arrange
        var doc = new Document();

        // Act
        doc["name"] = "Test";
        doc["value"] = 42;
        doc["active"] = true;

        // Assert
        doc["name"].Should().Be("Test");
        doc["value"].Should().Be(42);
        doc["active"].Should().Be(true);
    }

    /// <summary>
    /// Tests that indexer returns null for non-existent key.
    /// </summary>
    [Fact(Timeout = 5000)]
    public void Indexer_Should_Return_Null_For_NonExistent_Key()
    {
        // Arrange
        var doc = new Document();

        // Act
        var value = doc["nonexistent"];

        // Assert
        value.Should().BeNull();
    }

    /// <summary>
    /// Tests that indexer updates the updated timestamp.
    /// </summary>
    /// <returns>A <see cref="Task"/> representing the asynchronous unit test.</returns>
    [Fact(Timeout = 5000)]
    public async Task Indexer_Should_Update_Updated_Timestamp()
    {
        // Arrange
        var doc = new Document();
        var originalUpdated = doc.Updated;

        // Act
        await Task.Delay(10); // Ensure time difference
        doc["test"] = "value";

        // Assert
        doc.Updated.Should().BeAfter(originalUpdated);
    }

    /// <summary>
    /// Tests that Get returns typed value.
    /// </summary>
    [Fact(Timeout = 5000)]
    public void Get_Should_Return_Typed_Value()
    {
        // Arrange
        var doc = new Document();
        doc["string"] = "Test";
        doc["int"] = 42;
        doc["bool"] = true;
        doc["double"] = 3.14;

        // Act & Assert
        doc.Get<string>("string").Should().Be("Test");
        doc.Get<int>("int").Should().Be(42);
        doc.Get<bool>("bool").Should().BeTrue();
        doc.Get<double>("double").Should().Be(3.14);
    }

    /// <summary>
    /// Tests that Get returns default for non-existent key.
    /// </summary>
    [Fact(Timeout = 5000)]
    public void Get_Should_Return_Default_For_NonExistent_Key()
    {
        // Arrange
        var doc = new Document();

        // Act & Assert
        doc.Get<string>("nonexistent").Should().BeNull();
        doc.Get<int>("nonexistent").Should().Be(0);
        doc.Get<bool>("nonexistent").Should().BeFalse();
    }

    /// <summary>
    /// Tests that Set adds or updates value.
    /// </summary>
    [Fact(Timeout = 5000)]
    public void Set_Should_Add_Or_Update_Value()
    {
        // Arrange
        var doc = new Document();

        // Act
        doc.Set("key", "initial");
        doc.Set("key", "updated");

        // Assert
        doc.Get<string>("key").Should().Be("updated");
    }

    /// <summary>
    /// Tests that Remove deletes field.
    /// </summary>
    [Fact(Timeout = 5000)]
    public void Remove_Should_Delete_Field()
    {
        // Arrange
        var doc = new Document();
        doc.Set("key", "value");

        // Act
        var removed = doc.Remove("key");

        // Assert
        removed.Should().BeTrue();
        doc.ContainsKey("key").Should().BeFalse();
        doc["key"].Should().BeNull();
    }

    /// <summary>
    /// Tests that Remove returns false for non-existent key.
    /// </summary>
    [Fact(Timeout = 5000)]
    public void Remove_Should_Return_False_For_NonExistent_Key()
    {
        // Arrange
        var doc = new Document();

        // Act
        var removed = doc.Remove("nonexistent");

        // Assert
        removed.Should().BeFalse();
    }

    /// <summary>
    /// Tests that ContainsKey checks field existence.
    /// </summary>
    [Fact(Timeout = 5000)]
    public void ContainsKey_Should_Check_Field_Existence()
    {
        // Arrange
        var doc = new Document();
        doc.Set("exists", "value");

        // Act & Assert
        doc.ContainsKey("exists").Should().BeTrue();
        doc.ContainsKey("nonexistent").Should().BeFalse();
    }

    /// <summary>
    /// Tests that GetKeys returns all field names.
    /// </summary>
    [Fact(Timeout = 5000)]
    public void GetKeys_Should_Return_All_Field_Names()
    {
        // Arrange
        var doc = new Document();
        doc.Set("field1", "value1");
        doc.Set("field2", "value2");
        doc.Set("field3", "value3");

        // Act
        var keys = doc.GetKeys().ToList();

        // Assert
        keys.Should().BeEquivalentTo("field1", "field2", "field3");
    }

    /// <summary>
    /// Tests that ToJson serializes to JSON string.
    /// </summary>
    [Fact(Timeout = 5000)]
    public void ToJson_Should_Serialize_To_Json_String()
    {
        // Arrange
        var doc = new Document("test-id");
        doc.Set("name", "Test");
        doc.Set("value", 42);

        // Act
        var json = doc.ToJson();

        // Assert
        json.Should().Contain("\"_id\": \"test-id\"");
        json.Should().Contain("\"name\": \"Test\"");
        json.Should().Contain("\"value\": 42");
        json.Should().Contain("\"_version\": 1");
    }

    /// <summary>
    /// Tests that FromJson deserializes from JSON string.
    /// </summary>
    [Fact(Timeout = 5000)]
    public void FromJson_Should_Deserialize_From_Json_String()
    {
        // Arrange
        var json = @"{
                ""_id"": ""test-id"",
                ""_version"": 2,
                ""name"": ""Test"",
                ""value"": 42,
                ""active"": true
            }";

        // Act
        var doc = Document.FromJson(json);

        // Assert
        doc.Id.Should().Be("test-id");
        doc.Version.Should().Be(2);
        doc.Get<string>("name").Should().Be("Test");
        doc.Get<int>("value").Should().Be(42);
        doc.Get<bool>("active").Should().BeTrue();
    }

    /// <summary>
    /// Tests that FromObject converts object to Document.
    /// </summary>
    [Fact(Timeout = 5000)]
    public void FromObject_Should_Convert_Object_To_Document()
    {
        // Arrange
        var obj = new
        {
            Name = "Test",
            Value = 42,
            Active = true
        };

        // Act
        var doc = Document.FromObject(obj);

        // Assert - check the actual keys that were created
        var keys = doc.GetKeys().ToList();
        keys.Should().Contain("name");
        keys.Should().Contain("value");
        keys.Should().Contain("active");

        doc["name"].Should().NotBeNull();
        doc["value"].Should().NotBeNull();
        doc["active"].Should().NotBeNull();
    }

    /// <summary>
    /// Tests that ToObject converts Document to object.
    /// </summary>
    [Fact(Timeout = 5000)]
    public void ToObject_Should_Convert_Document_To_Object()
    {
        // Arrange
        var doc = new Document();
        doc.Set("name", "Test");
        doc.Set("value", 42);
        doc.Set("active", true);

        // Act
        var obj = doc.ToObject<TestObject>();

        // Assert
        obj.Name.Should().Be("Test");
        obj.Value.Should().Be(42);
        obj.Active.Should().BeTrue();
    }

    /// <summary>
    /// Tests that Clone creates deep copy.
    /// </summary>
    [Fact(Timeout = 5000)]
    public void Clone_Should_Create_Deep_Copy()
    {
        // Arrange
        var doc = new Document("original-id");
        doc.Set("name", "Test");
        doc.Set("value", 42);

        // Act
        var clone = doc.Clone();
        clone.Set("name", "Modified");

        // Assert
        clone.Id.Should().Be("original-id");
        doc.Get<string>("name").Should().Be("Test");
        clone.Get<string>("name").Should().Be("Modified");
    }

    /// <summary>
    /// Tests that Merge combines documents.
    /// </summary>
    [Fact(Timeout = 5000)]
    public void Merge_Should_Combine_Documents()
    {
        // Arrange
        var doc1 = new Document();
        doc1.Set("field1", "value1");
        doc1.Set("field2", "value2");

        var doc2 = new Document();
        doc2.Set("field2", "updated");
        doc2.Set("field3", "value3");

        // Act
        doc1.Merge(doc2);

        // Assert
        doc1.Get<string>("field1").Should().Be("value1");
        doc1.Get<string>("field2").Should().Be("updated");
        doc1.Get<string>("field3").Should().Be("value3");
    }

    /// <summary>
    /// Tests that Clear removes all fields.
    /// </summary>
    [Fact(Timeout = 5000)]
    public void Clear_Should_Remove_All_Fields()
    {
        // Arrange
        var doc = new Document();
        doc.Set("field1", "value1");
        doc.Set("field2", "value2");
        doc.Set("field3", "value3");

        // Act
        doc.Clear();

        // Assert
        doc.GetKeys().Should().BeEmpty();
        doc["field1"].Should().BeNull();
    }

    private class TestObject
    {
#if NET8_0_OR_GREATER
        public string? Name { get; set; } = string.Empty;
#else
        public string Name { get; set; } = string.Empty;
#endif

        public int Value { get; set; } = 0;

        public bool Active { get; set; } = false;
    }
}
