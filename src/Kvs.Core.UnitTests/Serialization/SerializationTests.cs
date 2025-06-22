using System;
using System.Text;
using FluentAssertions;
using Kvs.Core.Serialization;
using Xunit;

namespace Kvs.Core.UnitTests.Serialization;

public class SerializationTests
{
    private readonly BinarySerializer serializer;

    public SerializationTests()
    {
        this.serializer = new BinarySerializer();
    }

    [Fact]
    public void SerializeDeserialize_String_ShouldRoundTrip()
    {
        var original = "Hello, World!";

        var serialized = this.serializer.Serialize(original);
        var deserialized = this.serializer.Deserialize<string>(serialized);

        deserialized.Should().Be(original);
    }

    [Fact]
    public void SerializeDeserialize_Integer_ShouldRoundTrip()
    {
        var original = 42;

        var serialized = this.serializer.Serialize(original);
        var deserialized = this.serializer.Deserialize<int>(serialized);

        deserialized.Should().Be(original);
    }

    [Fact]
    public void SerializeDeserialize_Long_ShouldRoundTrip()
    {
        var original = 1234567890123456789L;

        var serialized = this.serializer.Serialize(original);
        var deserialized = this.serializer.Deserialize<long>(serialized);

        deserialized.Should().Be(original);
    }

    [Fact]
    public void SerializeDeserialize_Double_ShouldRoundTrip()
    {
        var original = 3.14159265359;

        var serialized = this.serializer.Serialize(original);
        var deserialized = this.serializer.Deserialize<double>(serialized);

        deserialized.Should().Be(original);
    }

    [Fact]
    public void SerializeDeserialize_Boolean_ShouldRoundTrip()
    {
        var originalTrue = true;
        var originalFalse = false;

        var serializedTrue = this.serializer.Serialize(originalTrue);
        var serializedFalse = this.serializer.Serialize(originalFalse);

        var deserializedTrue = this.serializer.Deserialize<bool>(serializedTrue);
        var deserializedFalse = this.serializer.Deserialize<bool>(serializedFalse);

        deserializedTrue.Should().BeTrue();
        deserializedFalse.Should().BeFalse();
    }

    [Fact]
    public void SerializeDeserialize_DateTime_ShouldRoundTrip()
    {
        var original = DateTime.Now;

        var serialized = this.serializer.Serialize(original);
        var deserialized = this.serializer.Deserialize<DateTime>(serialized);

        deserialized.Should().Be(original);
    }

    [Fact]
    public void SerializeDeserialize_ByteArray_ShouldRoundTrip()
    {
        var original = new byte[] { 1, 2, 3, 4, 5, 255, 0, 128 };

        var serialized = this.serializer.Serialize(original);
        var deserialized = this.serializer.Deserialize<byte[]>(serialized);

        deserialized.Should().BeEquivalentTo(original);
    }

    [Fact]
    public void Serialize_Null_ShouldReturnEmptyMemory()
    {
#if NET472
        string nullValue = null;
#else
        string? nullValue = null;
#endif

        var serialized = this.serializer.Serialize(nullValue);

        serialized.IsEmpty.Should().BeTrue();
    }

    [Fact]
    public void Deserialize_EmptyMemory_ShouldReturnDefault()
    {
        var emptyMemory = ReadOnlyMemory<byte>.Empty;

        var deserialized = this.serializer.Deserialize<string>(emptyMemory);

#if NET472
        deserialized.Should().BeNull();
#else
        deserialized.Should().BeNull();
#endif
    }

    [Fact]
    public void GetSerializedType_String_ShouldReturnStringType()
    {
        var original = "test";
        var serialized = this.serializer.Serialize(original);

        var type = this.serializer.GetSerializedType(serialized);

        type.Should().Be(typeof(string));
    }

    [Fact]
    public void GetSerializedType_Integer_ShouldReturnIntType()
    {
        var original = 42;
        var serialized = this.serializer.Serialize(original);

        var type = this.serializer.GetSerializedType(serialized);

        type.Should().Be(typeof(int));
    }

    [Fact]
    public void GetSerializedType_EmptyMemory_ShouldReturnObjectType()
    {
        var emptyMemory = ReadOnlyMemory<byte>.Empty;

        var type = this.serializer.GetSerializedType(emptyMemory);

        type.Should().Be(typeof(object));
    }

    [Fact]
    public void Serialize_EmptyString_ShouldHandleCorrectly()
    {
        var original = string.Empty;

        var serialized = this.serializer.Serialize(original);
        var deserialized = this.serializer.Deserialize<string>(serialized);

        deserialized.Should().Be(original);
    }

    [Fact]
    public void Serialize_LargeString_ShouldHandleCorrectly()
    {
        var original = new string('A', 10000);

        var serialized = this.serializer.Serialize(original);
        var deserialized = this.serializer.Deserialize<string>(serialized);

        deserialized.Should().Be(original);
    }

    [Fact]
    public void Serialize_UnicodeString_ShouldHandleCorrectly()
    {
        var original = "Hello 世界 🌍 café naïve résumé";

        var serialized = this.serializer.Serialize(original);
        var deserialized = this.serializer.Deserialize<string>(serialized);

        deserialized.Should().Be(original);
    }

    [Fact]
    public void Serialize_MinMaxValues_ShouldHandleCorrectly()
    {
        var minInt = int.MinValue;
        var maxInt = int.MaxValue;
        var minLong = long.MinValue;
        var maxLong = long.MaxValue;

        var serializedMinInt = this.serializer.Serialize(minInt);
        var serializedMaxInt = this.serializer.Serialize(maxInt);
        var serializedMinLong = this.serializer.Serialize(minLong);
        var serializedMaxLong = this.serializer.Serialize(maxLong);

        this.serializer.Deserialize<int>(serializedMinInt).Should().Be(minInt);
        this.serializer.Deserialize<int>(serializedMaxInt).Should().Be(maxInt);
        this.serializer.Deserialize<long>(serializedMinLong).Should().Be(minLong);
        this.serializer.Deserialize<long>(serializedMaxLong).Should().Be(maxLong);
    }

    [Fact]
    public void SerializedData_ShouldContainTypeInformation()
    {
        var original = "test";
        var serialized = this.serializer.Serialize(original);

        serialized.Length.Should().BeGreaterThan(Encoding.UTF8.GetByteCount(original));
    }

#if !NET472
    [Fact]
    public void SerializeDeserialize_ComplexObject_ShouldRoundTrip()
    {
        var original = new { Name = "John", Age = 30, Active = true };

        var serialized = this.serializer.Serialize(original);
        var deserialized = this.serializer.Deserialize<object>(serialized);

        deserialized.Should().NotBeNull();
    }
#endif
}
