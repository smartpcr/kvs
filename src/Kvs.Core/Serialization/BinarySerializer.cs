using System;
using System.IO;
using System.Text;
#if !NET472
using System.Text.Json;
#endif
using Kvs.Core.Storage;

namespace Kvs.Core.Serialization;

/// <summary>
/// Provides binary serialization and deserialization functionality with type information.
/// </summary>
public class BinarySerializer : ISerializer
{
#if !NET472
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        WriteIndented = false
    };
#endif

    /// <inheritdoc />
    public ReadOnlyMemory<byte> Serialize<T>(T value)
    {
        if (value == null)
        {
            return ReadOnlyMemory<byte>.Empty;
        }

        var typeInfo = GetTypeInfo<T>();
        var typeInfoBytes = Encoding.UTF8.GetBytes(typeInfo);
        var typeInfoLength = BitConverter.GetBytes(typeInfoBytes.Length);

        byte[] dataBytes;

        // Handle primitive types efficiently
        if (typeof(T) == typeof(string))
        {
            dataBytes = Encoding.UTF8.GetBytes(value as string ?? string.Empty);
        }
        else if (typeof(T) == typeof(int))
        {
            dataBytes = BitConverter.GetBytes((int)(object)value);
        }
        else if (typeof(T) == typeof(long))
        {
            dataBytes = BitConverter.GetBytes((long)(object)value);
        }
        else if (typeof(T) == typeof(double))
        {
            dataBytes = BitConverter.GetBytes((double)(object)value);
        }
        else if (typeof(T) == typeof(bool))
        {
            dataBytes = BitConverter.GetBytes((bool)(object)value);
        }
        else if (typeof(T) == typeof(DateTime))
        {
            dataBytes = BitConverter.GetBytes(((DateTime)(object)value).ToBinary());
        }
        else if (typeof(T) == typeof(byte[]))
        {
            dataBytes = value as byte[] ?? [];
        }
        else if (typeof(T) == typeof(TransactionLogEntry))
        {
            // Custom serialization for TransactionLogEntry
            var entry = (TransactionLogEntry)(object)value;
            using var stream = new MemoryStream();
            using var writer = new BinaryWriter(stream);

            writer.Write(entry.Lsn);
            writer.Write(entry.TransactionId ?? string.Empty);
            writer.Write((byte)entry.OperationType);
            writer.Write(entry.PageId);
            writer.Write(entry.BeforeImage.Length);
            if (!entry.BeforeImage.IsEmpty)
            {
#if NET472
                writer.Write(entry.BeforeImage.ToArray());
#else
                writer.Write(entry.BeforeImage.Span);
#endif
            }

            writer.Write(entry.AfterImage.Length);
            if (!entry.AfterImage.IsEmpty)
            {
#if NET472
                writer.Write(entry.AfterImage.ToArray());
#else
                writer.Write(entry.AfterImage.Span);
#endif
            }

            writer.Write(entry.Timestamp.ToBinary());
            writer.Write(entry.Checksum);

            dataBytes = stream.ToArray();
        }
        else
        {
#if NET472
            // Use simple string representation for .NET Framework
            dataBytes = Encoding.UTF8.GetBytes(value.ToString() ?? string.Empty);
#else
            // Use JSON for complex types
            dataBytes = JsonSerializer.SerializeToUtf8Bytes(value, JsonOptions);
#endif
        }

        var result = new byte[typeInfoLength.Length + typeInfoBytes.Length + dataBytes.Length];
        var offset = 0;

        typeInfoLength.CopyTo(result, offset);
        offset += typeInfoLength.Length;

        typeInfoBytes.CopyTo(result, offset);
        offset += typeInfoBytes.Length;

        dataBytes.CopyTo(result, offset);

        return result;
    }

    /// <inheritdoc />
    public T Deserialize<T>(ReadOnlyMemory<byte> data)
    {
        if (data.IsEmpty)
        {
#if NET472
            return default;
#else
            return default!;
#endif
        }

        var span = data.Span;
#if NET472
        var typeInfoLength = BitConverter.ToInt32(span.Slice(0, 4).ToArray(), 0);
        var dataBytes = span.Slice(4 + typeInfoLength);
#else
        var typeInfoLength = BitConverter.ToInt32(span[..4]);
        var dataBytes = span[(4 + typeInfoLength)..];
#endif

        // Handle primitive types efficiently
        if (typeof(T) == typeof(string))
        {
#if NET472
            return (T)(object)Encoding.UTF8.GetString(dataBytes.ToArray());
#else
            return (T)(object)Encoding.UTF8.GetString(dataBytes);
#endif
        }
        else if (typeof(T) == typeof(int))
        {
#if NET472
            return (T)(object)BitConverter.ToInt32(dataBytes.ToArray(), 0);
#else
            return (T)(object)BitConverter.ToInt32(dataBytes);
#endif
        }
        else if (typeof(T) == typeof(long))
        {
#if NET472
            return (T)(object)BitConverter.ToInt64(dataBytes.ToArray(), 0);
#else
            return (T)(object)BitConverter.ToInt64(dataBytes);
#endif
        }
        else if (typeof(T) == typeof(double))
        {
#if NET472
            return (T)(object)BitConverter.ToDouble(dataBytes.ToArray(), 0);
#else
            return (T)(object)BitConverter.ToDouble(dataBytes);
#endif
        }
        else if (typeof(T) == typeof(bool))
        {
#if NET472
            return (T)(object)BitConverter.ToBoolean(dataBytes.ToArray(), 0);
#else
            return (T)(object)BitConverter.ToBoolean(dataBytes);
#endif
        }
        else if (typeof(T) == typeof(DateTime))
        {
#if NET472
            return (T)(object)DateTime.FromBinary(BitConverter.ToInt64(dataBytes.ToArray(), 0));
#else
            return (T)(object)DateTime.FromBinary(BitConverter.ToInt64(dataBytes));
#endif
        }
        else if (typeof(T) == typeof(byte[]))
        {
            return (T)(object)dataBytes.ToArray();
        }
        else if (typeof(T) == typeof(TransactionLogEntry))
        {
            // Custom deserialization for TransactionLogEntry
            using var stream = new MemoryStream(dataBytes.ToArray());
            using var reader = new BinaryReader(stream);

            var lsn = reader.ReadInt64();
            var transactionId = reader.ReadString();
            var operationType = (OperationType)reader.ReadByte();
            var pageId = reader.ReadInt64();

            var beforeImageLength = reader.ReadInt32();
            var beforeImage = beforeImageLength > 0 ?
                new ReadOnlyMemory<byte>(reader.ReadBytes(beforeImageLength)) :
                ReadOnlyMemory<byte>.Empty;

            var afterImageLength = reader.ReadInt32();
            var afterImage = afterImageLength > 0 ?
                new ReadOnlyMemory<byte>(reader.ReadBytes(afterImageLength)) :
                ReadOnlyMemory<byte>.Empty;

            var timestamp = DateTime.FromBinary(reader.ReadInt64());

            // Skip the stored checksum - it will be recalculated
            reader.ReadUInt32();

            var entry = new TransactionLogEntry(lsn, transactionId, operationType, pageId, beforeImage, afterImage, timestamp);
            return (T)(object)entry;
        }
        else
        {
#if NET472
            // For .NET Framework, return default for complex types
            return default;
#else
            // Use JSON for complex types
            return JsonSerializer.Deserialize<T>(dataBytes, JsonOptions) ?? default!;
#endif
        }
    }

    /// <inheritdoc />
    public Type GetSerializedType(ReadOnlyMemory<byte> data)
    {
        if (data.IsEmpty)
        {
            return typeof(object);
        }

        var span = data.Span;
#if NET472
        var typeInfoLength = BitConverter.ToInt32(span.Slice(0, 4).ToArray(), 0);
        var typeInfo = Encoding.UTF8.GetString(span.Slice(4, typeInfoLength).ToArray());
#else
        var typeInfoLength = BitConverter.ToInt32(span[..4]);
        var typeInfo = Encoding.UTF8.GetString(span.Slice(4, typeInfoLength));
#endif

        return Type.GetType(typeInfo) ?? typeof(object);
    }

    private static string GetTypeInfo<T>()
    {
        var type = typeof(T);
        return $"{type.FullName}, {type.Assembly.GetName().Name}";
    }
}
