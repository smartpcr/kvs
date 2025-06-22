#if !NET472
#nullable enable
#endif

using System;
using System.Collections.Generic;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Kvs.Core.Database;

/// <summary>
/// Represents a document in the database with JSON support.
/// </summary>
public class Document
{
#if NET8_0_OR_GREATER
    private readonly Dictionary<string, object?> data;
#else
    private readonly Dictionary<string, object> data;
#endif

    /// <summary>
    /// Gets or sets the document ID.
    /// </summary>
    [JsonPropertyName("_id")]
    public string Id { get; set; }

    /// <summary>
    /// Gets or sets the document version for optimistic concurrency control.
    /// </summary>
    [JsonPropertyName("_version")]
    public long Version { get; set; }

    /// <summary>
    /// Gets or sets the timestamp when the document was created.
    /// </summary>
    [JsonPropertyName("_created")]
    public DateTime Created { get; set; }

    /// <summary>
    /// Gets or sets the timestamp when the document was last updated.
    /// </summary>
    [JsonPropertyName("_updated")]
    public DateTime Updated { get; set; }

    /// <summary>
    /// Initializes a new instance of the <see cref="Document"/> class.
    /// </summary>
    public Document()
    {
#if NET8_0_OR_GREATER
        this.data = new Dictionary<string, object?>();
#else
        this.data = new Dictionary<string, object>();
#endif
        this.Id = Guid.NewGuid().ToString();
        this.Version = 1;
        this.Created = DateTime.UtcNow;
        this.Updated = DateTime.UtcNow;
    }

    /// <summary>
    /// Initializes a new instance of the <see cref="Document"/> class with the specified ID.
    /// </summary>
    /// <param name="id">The document ID.</param>
    public Document(string id)
        : this()
    {
        this.Id = id;
    }

    /// <summary>
    /// Creates a document from an object.
    /// </summary>
    /// <typeparam name="T">The type of the object.</typeparam>
    /// <param name="obj">The object to convert.</param>
    /// <returns>A new document instance.</returns>
    public static Document FromObject<T>(T obj)
        where T : class
    {
        var options = new JsonSerializerOptions
        {
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase
        };
        var json = JsonSerializer.Serialize(obj, options);
        return FromJson(json);
    }

    /// <summary>
    /// Creates a document from a JSON string.
    /// </summary>
    /// <param name="json">The JSON string.</param>
    /// <returns>A new document instance.</returns>
    public static Document FromJson(string json)
    {
        var data = JsonSerializer.Deserialize<Dictionary<string, JsonElement>>(json) ?? new Dictionary<string, JsonElement>();
        var document = new Document();

        foreach (var kvp in data)
        {
            switch (kvp.Key)
            {
                case "_id":
                    document.Id = kvp.Value.GetString() ?? Guid.NewGuid().ToString();
                    break;
                case "_version":
                    document.Version = kvp.Value.GetInt64();
                    break;
                case "_created":
                    document.Created = kvp.Value.GetDateTime();
                    break;
                case "_updated":
                    document.Updated = kvp.Value.GetDateTime();
                    break;
                default:
                    document.data[kvp.Key] = kvp.Value;
                    break;
            }
        }

        return document;
    }

    /// <summary>
    /// Gets or sets a field value in the document.
    /// </summary>
    /// <param name="key">The field name.</param>
    /// <returns>The field value.</returns>
#if NET8_0_OR_GREATER
    public object? this[string key]
#else
    public object this[string key]
#endif
    {
        get => this.data.TryGetValue(key, out var value) ? value : null;
        set
        {
            this.data[key] = value;
            this.Updated = DateTime.UtcNow;
        }
    }

    /// <summary>
    /// Gets a field value as the specified type.
    /// </summary>
    /// <typeparam name="T">The type to convert the value to.</typeparam>
    /// <param name="key">The field name.</param>
    /// <returns>The field value converted to the specified type.</returns>
#if NET8_0_OR_GREATER
    public T? Get<T>(string key)
#else
    public T Get<T>(string key)
#endif
    {
        if (!this.data.TryGetValue(key, out var value))
        {
            return default;
        }

        if (value is T typedValue)
        {
            return typedValue;
        }

        if (value is JsonElement jsonElement)
        {
            try
            {
                return JsonSerializer.Deserialize<T>(jsonElement.GetRawText());
            }
            catch
            {
                // Try to get the value directly from JsonElement
                if (typeof(T) == typeof(string))
                {
                    return (T)(object)jsonElement.GetString()!;
                }
                else if (typeof(T) == typeof(int))
                {
                    return (T)(object)jsonElement.GetInt32();
                }
                else if (typeof(T) == typeof(bool))
                {
                    return (T)(object)jsonElement.GetBoolean();
                }
                else if (typeof(T) == typeof(double))
                {
                    return (T)(object)jsonElement.GetDouble();
                }
                else if (typeof(T) == typeof(long))
                {
                    return (T)(object)jsonElement.GetInt64();
                }
            }
        }

        try
        {
#if NET8_0_OR_GREATER
            return (T?)Convert.ChangeType(value, typeof(T));
#else
            return (T)Convert.ChangeType(value, typeof(T));
#endif
        }
        catch
        {
            return default;
        }
    }

    /// <summary>
    /// Sets a field value in the document.
    /// </summary>
    /// <typeparam name="T">The type of the value.</typeparam>
    /// <param name="key">The field name.</param>
    /// <param name="value">The field value.</param>
    public void Set<T>(string key, T value)
    {
#if NET8_0_OR_GREATER
        this.data[key] = value;
#else
        this.data[key] = (object)value;
#endif
        this.Updated = DateTime.UtcNow;
    }

    /// <summary>
    /// Removes a field from the document.
    /// </summary>
    /// <param name="key">The field name.</param>
    /// <returns>true if the field was removed; otherwise, false.</returns>
    public bool Remove(string key)
    {
        var removed = this.data.Remove(key);
        if (removed)
        {
            this.Updated = DateTime.UtcNow;
        }

        return removed;
    }

    /// <summary>
    /// Checks if the document contains a field.
    /// </summary>
    /// <param name="key">The field name.</param>
    /// <returns>true if the document contains the field; otherwise, false.</returns>
    public bool ContainsKey(string key)
    {
        return this.data.ContainsKey(key);
    }

    /// <summary>
    /// Gets all field names in the document.
    /// </summary>
    /// <returns>A collection of field names.</returns>
    public IEnumerable<string> GetKeys()
    {
        return this.data.Keys;
    }

    /// <summary>
    /// Converts the document to a JSON string.
    /// </summary>
    /// <returns>The JSON representation of the document.</returns>
    public string ToJson()
    {
#if NET8_0_OR_GREATER
        var fullData = new Dictionary<string, object?>(this.data)
#else
        var fullData = new Dictionary<string, object>(this.data)
#endif
        {
            ["_id"] = this.Id,
            ["_version"] = this.Version,
            ["_created"] = this.Created,
            ["_updated"] = this.Updated
        };

        return JsonSerializer.Serialize(fullData, new JsonSerializerOptions
        {
            WriteIndented = true,
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase
        });
    }

    /// <summary>
    /// Converts the document to an object of the specified type.
    /// </summary>
    /// <typeparam name="T">The type to convert to.</typeparam>
    /// <returns>The object representation of the document.</returns>
    public T ToObject<T>()
        where T : class
    {
        var json = this.ToJson();
        var options = new JsonSerializerOptions
        {
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase
        };
        return JsonSerializer.Deserialize<T>(json, options) ?? throw new InvalidOperationException("Failed to deserialize document");
    }

    /// <summary>
    /// Creates a deep copy of the document.
    /// </summary>
    /// <returns>A new document instance with the same data.</returns>
    public Document Clone()
    {
        var json = this.ToJson();
        return FromJson(json);
    }

    /// <summary>
    /// Merges another document into this document.
    /// </summary>
    /// <param name="other">The document to merge.</param>
    public void Merge(Document other)
    {
        foreach (var key in other.GetKeys())
        {
            this.data[key] = other[key];
        }

        this.Updated = DateTime.UtcNow;
    }

    /// <summary>
    /// Clears all fields from the document except system fields.
    /// </summary>
    public void Clear()
    {
        this.data.Clear();
        this.Updated = DateTime.UtcNow;
    }
}
