using System;
using System.Threading.Tasks;

namespace Kvs.Core.Serialization;

/// <summary>
/// Defines the contract for serializing and deserializing objects to and from binary format.
/// </summary>
public interface ISerializer
{
    /// <summary>
    /// Serializes an object to a binary format.
    /// </summary>
    /// <typeparam name="T">The type of the object to serialize.</typeparam>
    /// <param name="value">The object to serialize.</param>
    /// <returns>A <see cref="ReadOnlyMemory{T}"/> containing the serialized binary data.</returns>
    ReadOnlyMemory<byte> Serialize<T>(T value);

    /// <summary>
    /// Deserializes binary data to an object of the specified type.
    /// </summary>
    /// <typeparam name="T">The type of the object to deserialize to.</typeparam>
    /// <param name="data">The binary data to deserialize.</param>
    /// <returns>The deserialized object of type <typeparamref name="T"/>.</returns>
    T Deserialize<T>(ReadOnlyMemory<byte> data);

    /// <summary>
    /// Gets the type information from serialized binary data.
    /// </summary>
    /// <param name="data">The serialized binary data.</param>
    /// <returns>The <see cref="Type"/> of the serialized object.</returns>
    Type GetSerializedType(ReadOnlyMemory<byte> data);
}

/// <summary>
/// Defines the contract for asynchronously serializing and deserializing objects to and from binary format.
/// </summary>
public interface IAsyncSerializer
{
    /// <summary>
    /// Asynchronously serializes an object to a binary format.
    /// </summary>
    /// <typeparam name="T">The type of the object to serialize.</typeparam>
    /// <param name="value">The object to serialize.</param>
    /// <returns>A task that represents the asynchronous operation. The task result contains a <see cref="ReadOnlyMemory{T}"/> with the serialized binary data.</returns>
    Task<ReadOnlyMemory<byte>> SerializeAsync<T>(T value);

    /// <summary>
    /// Asynchronously deserializes binary data to an object of the specified type.
    /// </summary>
    /// <typeparam name="T">The type of the object to deserialize to.</typeparam>
    /// <param name="data">The binary data to deserialize.</param>
    /// <returns>A task that represents the asynchronous operation. The task result contains the deserialized object of type <typeparamref name="T"/>.</returns>
    Task<T> DeserializeAsync<T>(ReadOnlyMemory<byte> data);

    /// <summary>
    /// Asynchronously gets the type information from serialized binary data.
    /// </summary>
    /// <param name="data">The serialized binary data.</param>
    /// <returns>A task that represents the asynchronous operation. The task result contains the <see cref="Type"/> of the serialized object.</returns>
    Task<Type> GetSerializedTypeAsync(ReadOnlyMemory<byte> data);
}
