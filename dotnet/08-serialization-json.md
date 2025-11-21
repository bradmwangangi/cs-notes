# Serialization & JSON

Convert objects to/from text formats for storage, transmission, and APIs.

## JSON Serialization (System.Text.Json)

Modern, built-in JSON library:

```csharp
using System.Text.Json;
using System.Text.Json.Serialization;

public class Person
{
    public string Name { get; set; }
    public int Age { get; set; }
}

// Serialize (object to JSON string)
var person = new Person { Name = "Alice", Age = 30 };
string json = JsonSerializer.Serialize(person);
// {"name":"alice","age":30}

// Deserialize (JSON string to object)
string jsonInput = @"{ ""name"": ""Bob"", ""age"": 25 }";
var person = JsonSerializer.Deserialize<Person>(jsonInput);
// person.Name = "Bob", person.Age = 25
```

### Formatting Options

```csharp
var options = new JsonSerializerOptions
{
    WriteIndented = true,  // Pretty print
    PropertyNamingPolicy = JsonNamingPolicy.CamelCase  // camelCase
};

string prettyJson = JsonSerializer.Serialize(person, options);
// {
//   "name": "Alice",
//   "age": 30
// }

// Other policies
// JsonNamingPolicy.SnakeCaseUpper  // SNAKE_CASE
// JsonNamingPolicy.KebabCaseLower  // kebab-case
```

### Custom Property Names

```csharp
public class User
{
    [JsonPropertyName("full_name")]
    public string Name { get; set; }

    [JsonPropertyName("email_address")]
    public string Email { get; set; }

    [JsonIgnore]  // Don't serialize
    public string InternalId { get; set; }
}

var user = new User
{
    Name = "Alice",
    Email = "alice@example.com",
    InternalId = "secret123"
};

string json = JsonSerializer.Serialize(user);
// {"full_name":"Alice","email_address":"alice@example.com"}
```

### Nullable Reference Types

```csharp
public class Article
{
    public string Title { get; set; }
    public string? Description { get; set; }  // Can be null
    public DateTime CreatedAt { get; set; }
}

var article = new Article { Title = "C#", Description = null };
string json = JsonSerializer.Serialize(article);
// {"title":"C#","description":null,"createdAt":"2024-01-01T00:00:00"}

// Ignore null values
var options = new JsonSerializerOptions { DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull };
json = JsonSerializer.Serialize(article, options);
// {"title":"C#","createdAt":"2024-01-01T00:00:00"}
```

## Working with JSON Documents

Parse JSON without mapping to classes:

```csharp
string json = @"
{
    ""name"": ""Alice"",
    ""age"": 30,
    ""email"": ""alice@example.com"",
    ""tags"": [""dev"", ""designer""]
}";

using JsonDocument doc = JsonDocument.Parse(json);
JsonElement root = doc.RootElement;

string name = root.GetProperty("name").GetString();
int age = root.GetProperty("age").GetInt32();
var tags = root.GetProperty("tags").EnumerateArray();

foreach (var tag in tags)
{
    Console.WriteLine(tag.GetString());
}
```

## Collections and Arrays

```csharp
public class Team
{
    public string Name { get; set; }
    public List<Person> Members { get; set; }
}

var team = new Team
{
    Name = "Alpha",
    Members = new List<Person>
    {
        new Person { Name = "Alice", Age = 30 },
        new Person { Name = "Bob", Age = 25 }
    }
};

string json = JsonSerializer.Serialize(team);
// {"name":"Alpha","members":[{"name":"Alice","age":30},{"name":"Bob","age":25}]}

var deserialized = JsonSerializer.Deserialize<Team>(json);
Console.WriteLine(deserialized.Members.Count);  // 2
```

## Nested Objects

```csharp
public class Address
{
    public string Street { get; set; }
    public string City { get; set; }
}

public class Customer
{
    public string Name { get; set; }
    public Address Address { get; set; }
}

var customer = new Customer
{
    Name = "Alice",
    Address = new Address { Street = "123 Main St", City = "New York" }
};

string json = JsonSerializer.Serialize(customer);
// {"name":"Alice","address":{"street":"123 Main St","city":"New York"}}

var deserialized = JsonSerializer.Deserialize<Customer>(json);
Console.WriteLine(deserialized.Address.City);  // "New York"
```

## Custom Serialization

```csharp
public class Temperature
{
    public double Celsius { get; set; }
}

public class TemperatureConverter : JsonConverter<Temperature>
{
    public override Temperature Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        if (reader.TokenType != JsonTokenType.StartObject)
            throw new JsonException("Expected object");

        reader.Read();  // Move to property
        reader.Read();  // Move to value
        double celsius = reader.GetDouble();

        reader.Read();  // Move past value
        return new Temperature { Celsius = celsius };
    }

    public override void Write(Utf8JsonWriter writer, Temperature value, JsonSerializerOptions options)
    {
        writer.WriteStartObject();
        writer.WriteNumber("celsius", value.Celsius);
        writer.WriteNumber("fahrenheit", value.Celsius * 9 / 5 + 32);
        writer.WriteEndObject();
    }
}

var options = new JsonSerializerOptions();
options.Converters.Add(new TemperatureConverter());

var temp = new Temperature { Celsius = 25 };
string json = JsonSerializer.Serialize(temp, options);
// {"celsius":25,"fahrenheit":77}
```

## File I/O with JSON

```csharp
using System.Text.Json;
using System.IO;

public class ConfigManager
{
    public class Config
    {
        public string AppName { get; set; }
        public string DatabaseUrl { get; set; }
        public int Port { get; set; }
    }

    // Write to file
    public static void SaveConfig(Config config, string path)
    {
        string json = JsonSerializer.Serialize(config, new JsonSerializerOptions { WriteIndented = true });
        File.WriteAllText(path, json);
    }

    // Read from file
    public static Config LoadConfig(string path)
    {
        string json = File.ReadAllText(path);
        return JsonSerializer.Deserialize<Config>(json);
    }
}

// Usage
var config = new ConfigManager.Config
{
    AppName = "MyApp",
    DatabaseUrl = "localhost",
    Port = 5432
};

ConfigManager.SaveConfig(config, "config.json");
var loaded = ConfigManager.LoadConfig("config.json");
```

## Async File Operations

```csharp
using System.Text.Json;
using System.IO;

public class AsyncJsonHandler
{
    public static async Task SaveAsync<T>(T data, string filePath)
    {
        using var stream = File.Create(filePath);
        await JsonSerializer.SerializeAsync(stream, data);
    }

    public static async Task<T> LoadAsync<T>(string filePath)
    {
        using var stream = File.OpenRead(filePath);
        return await JsonSerializer.DeserializeAsync<T>(stream);
    }
}

// Usage
var data = new Person { Name = "Alice", Age = 30 };
await AsyncJsonHandler.SaveAsync(data, "person.json");
var loaded = await AsyncJsonHandler.LoadAsync<Person>("person.json");
```

## JSON Schema Validation

Validate JSON against a schema:

```csharp
using System.Text.Json;
using System.Text.Json.Schema;

public class JsonValidator
{
    public static bool Validate(string json, string schema)
    {
        // Simple validation (for complex schemas, use NJsonSchema library)
        try
        {
            JsonDocument.Parse(json);
            return true;
        }
        catch
        {
            return false;
        }
    }

    // Check if JSON has required fields
    public static bool HasRequiredFields(string json, string[] requiredFields)
    {
        using JsonDocument doc = JsonDocument.Parse(json);
        var root = doc.RootElement;

        return requiredFields.All(field =>
            root.TryGetProperty(field, out _)
        );
    }
}

string json = @"{ ""name"": ""Alice"", ""age"": 30 }";
bool valid = JsonValidator.Validate(json, "");
bool hasRequired = JsonValidator.HasRequiredFields(json, new[] { "name", "age" });
```

## Polymorphic Serialization

```csharp
[JsonPolymorphic(TypeDiscriminatorPropertyName = "$type")]
[JsonDerivedType(typeof(Circle), "circle")]
[JsonDerivedType(typeof(Square), "square")]
public abstract class Shape
{
    public string Color { get; set; }
}

public class Circle : Shape
{
    public double Radius { get; set; }
}

public class Square : Shape
{
    public double Side { get; set; }
}

var shapes = new Shape[]
{
    new Circle { Color = "Red", Radius = 5 },
    new Square { Color = "Blue", Side = 4 }
};

string json = JsonSerializer.Serialize(shapes);
// [
//   {"$type":"circle","color":"Red","radius":5},
//   {"$type":"square","color":"Blue","side":4}
// ]

var deserialized = JsonSerializer.Deserialize<Shape[]>(json);
```

## Dictionary Serialization

```csharp
var data = new Dictionary<string, int>
{
    { "Alice", 30 },
    { "Bob", 25 },
    { "Charlie", 35 }
};

string json = JsonSerializer.Serialize(data);
// {"Alice":30,"Bob":25,"Charlie":35}

var loaded = JsonSerializer.Deserialize<Dictionary<string, int>>(json);
```

## Streaming Large JSON

```csharp
using System.Text.Json;
using System.IO;

// Write large dataset in chunks
public async Task WriteJsonStream(string filePath, IEnumerable<Person> items)
{
    using var stream = File.Create(filePath);
    using var writer = new Utf8JsonWriter(stream, new JsonWriterOptions { Indented = true });

    writer.WriteStartArray();
    foreach (var item in items)
    {
        JsonSerializer.Serialize(writer, item);
    }
    writer.WriteEndArray();
    await writer.FlushAsync();
}

// Read large JSON array efficiently
public async IAsyncEnumerable<Person> ReadJsonStream(string filePath)
{
    using var stream = File.OpenRead(filePath);
    using var reader = new Utf8JsonReader(new StreamReader(stream).ReadToEndAsync().Result.AsMemory());

    while (reader.Read())
    {
        if (reader.TokenType == JsonTokenType.StartObject)
        {
            var person = JsonSerializer.Deserialize<Person>(ref reader);
            yield return person;
        }
    }
}
```

## Common Patterns

```csharp
// Deserialize with default values
var json = @"{ ""name"": ""Alice"" }";
var person = JsonSerializer.Deserialize<Person>(json) ?? new Person();

// Serialize only changed properties
var original = new Person { Name = "Alice", Age = 30 };
var updated = original with { Name = "Bob" };  // Record copy
var diff = JsonSerializer.Serialize(updated);

// Merge JSON objects
string json1 = @"{ ""name"": ""Alice"" }";
string json2 = @"{ ""age"": 30 }";
using var doc1 = JsonDocument.Parse(json1);
using var doc2 = JsonDocument.Parse(json2);
// Merge logic needed (use JsonDocument or JsonElement)
```

## Practice Exercises

1. **API Response Parser**: Parse JSON from a public API and map to classes
2. **Config File Manager**: Save/load application configuration as JSON
3. **Data Export**: Export a list of objects to JSON with formatting options
4. **Custom Converter**: Write a custom JSON converter for a complex type
5. **Streaming Handler**: Process a large JSON file line-by-line

## Key Takeaways

- **System.Text.Json** is the modern, built-in JSON library
- **JsonSerializer.Serialize()** converts objects to JSON strings
- **JsonSerializer.Deserialize<T>()** parses JSON into typed objects
- **JsonPropertyName** customizes JSON field names
- **JsonIgnore** excludes properties from serialization
- **JsonDocument** parses JSON without mapping to classes
- **Custom converters** handle complex or special types
- **Async versions** available for file I/O operations
- **Polymorphic types** need type discriminators for correct deserialization
