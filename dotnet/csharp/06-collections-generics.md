# Collections & Generics

## Generics Basics

Write code that works with any type while maintaining type safety:

```csharp
// Generic class
public class Container<T>
{
    private T value;

    public void Store(T item)
    {
        value = item;
    }

    public T Retrieve()
    {
        return value;
    }
}

// Usage
var stringContainer = new Container<string>();
stringContainer.Store("Hello");
string result = stringContainer.Retrieve();  // Type-safe, no casting

var intContainer = new Container<int>();
intContainer.Store(42);
int number = intContainer.Retrieve();
```

### Generic Methods

```csharp
public class Utilities
{
    // Generic method
    public static T GetFirst<T>(List<T> items)
    {
        return items.Count > 0 ? items[0] : default(T);
    }

    public static void Print<T>(T value)
    {
        Console.WriteLine($"{typeof(T).Name}: {value}");
    }

    // Type inference (no need to specify <T>)
    public static T Max<T>(T a, T b) where T : IComparable<T>
    {
        return a.CompareTo(b) > 0 ? a : b;
    }
}

// Usage
var strings = new List<string> { "a", "b", "c" };
string first = Utilities.GetFirst(strings);  // "a"

Utilities.Print(42);      // Int32: 42
Utilities.Print("hello"); // String: hello

int maxNum = Utilities.Max(10, 20);  // 20
```

### Type Constraints

Restrict what types can be used:

```csharp
// Constraint: T must be a reference type
public class Repository<T> where T : class
{
    public void Add(T item) { }
}

// Constraint: T must be a value type
public class Wrapper<T> where T : struct
{
    public T Value { get; set; }
}

// Constraint: T must inherit from specific class
public abstract class Entity
{
    public int Id { get; set; }
}

public class DataService<T> where T : Entity
{
    public void Save(T entity) { }
}

// Constraint: T must implement interface
public class Collection<T> where T : IComparable<T>
{
    public int Compare(T a, T b) => a.CompareTo(b);
}

// Constraint: T must have parameterless constructor
public class Factory<T> where T : new()
{
    public T Create() => new T();
}

// Multiple constraints
public class Advanced<T> 
    where T : class, IDisposable, new()
{
    // T must be a reference type, implement IDisposable, and have parameterless constructor
}
```

## Common Collections

### List<T>

Ordered, mutable collection:

```csharp
var numbers = new List<int> { 1, 2, 3, 4, 5 };

// Access
numbers[0];           // 1
numbers.Count;        // 5
numbers.Contains(3);  // true

// Add/Remove
numbers.Add(6);
numbers.Insert(0, 0);  // Insert at position
numbers.Remove(3);     // Remove by value
numbers.RemoveAt(0);   // Remove by index
numbers.Clear();       // Remove all

// Iterate
foreach (var num in numbers)
{
    Console.WriteLine(num);
}

// Find
int index = numbers.IndexOf(3);
var item = numbers.Find(n => n > 3);

// Sort
numbers.Sort();
numbers.Sort((a, b) => b.CompareTo(a));  // Descending

// Other operations
var doubled = numbers.Select(n => n * 2).ToList();
numbers.Reverse();
var range = numbers.GetRange(0, 3);  // First 3 items
```

### Dictionary<K, V>

Key-value pairs:

```csharp
var ages = new Dictionary<string, int>
{
    { "Alice", 30 },
    { "Bob", 25 }
};

// Alternative syntax
var ages2 = new Dictionary<string, int>
{
    ["Alice"] = 30,
    ["Bob"] = 25
};

// Access
ages["Alice"];           // 30
ages.TryGetValue("Alice", out int age);  // Safe access

// Add/Remove
ages["Charlie"] = 35;
ages.Add("Diana", 28);   // Throws if key exists
ages.Remove("Bob");
ages.Clear();

// Iterate
foreach (var kvp in ages)
{
    Console.WriteLine($"{kvp.Key}: {kvp.Value}");
}

// Keys and Values
var keys = ages.Keys;
var values = ages.Values;

// Contains
ages.ContainsKey("Alice");    // true
ages.ContainsValue(30);       // true
```

### HashSet<T>

Unique values, fast lookup:

```csharp
var uniqueNumbers = new HashSet<int> { 1, 2, 3, 3, 4, 4, 5 };
uniqueNumbers.Count;  // 5 (duplicates removed)

// Add
uniqueNumbers.Add(6);
uniqueNumbers.Add(3);  // Returns false (already exists)

// Set operations
var set1 = new HashSet<int> { 1, 2, 3 };
var set2 = new HashSet<int> { 2, 3, 4 };

set1.Union(set2);       // { 1, 2, 3, 4 }
set1.Intersect(set2);   // { 2, 3 }
set1.Except(set2);      // { 1 }
set1.SymmetricExcept(set2);  // { 1, 4 }
```

### Queue<T>

FIFO (First-In-First-Out):

```csharp
var queue = new Queue<string>();

queue.Enqueue("First");
queue.Enqueue("Second");
queue.Enqueue("Third");

queue.Dequeue();  // "First"
queue.Peek();     // "Second" (without removing)
queue.Count;      // 2
```

### Stack<T>

LIFO (Last-In-First-Out):

```csharp
var stack = new Stack<int>();

stack.Push(1);
stack.Push(2);
stack.Push(3);

stack.Pop();   // 3
stack.Peek();  // 2 (without removing)
stack.Count;   // 2
```

## Immutable Collections

Use when you don't want modifications:

```csharp
using System.Collections.Immutable;

// Create immutable list
var list = ImmutableList.Create(1, 2, 3);

// "Modify" creates new collection
var updated = list.Add(4);
// list is still { 1, 2, 3 }
// updated is { 1, 2, 3, 4 }

// Immutable dictionary
var dict = ImmutableDictionary.Create<string, int>();
var dict2 = dict.Add("Alice", 30);

// Immutable set
var set = ImmutableHashSet.Create(1, 2, 3);
var set2 = set.Add(4);
```

## Custom Generic Collections

```csharp
public class Stack<T>
{
    private T[] items = new T[10];
    private int count = 0;

    public void Push(T value)
    {
        if (count >= items.Length)
        {
            System.Array.Resize(ref items, items.Length * 2);
        }
        items[count++] = value;
    }

    public T Pop()
    {
        if (count == 0)
            throw new InvalidOperationException("Stack is empty");
        return items[--count];
    }

    public int Count => count;
}
```

## IEnumerable and Iterators

```csharp
public class Range : IEnumerable<int>
{
    private int start;
    private int end;

    public Range(int start, int end)
    {
        this.start = start;
        this.end = end;
    }

    public IEnumerator<int> GetEnumerator()
    {
        for (int i = start; i <= end; i++)
        {
            yield return i;
        }
    }

    System.Collections.IEnumerator System.Collections.IEnumerable.GetEnumerator()
    {
        return GetEnumerator();
    }
}

// Usage
var range = new Range(1, 5);
foreach (int num in range)
{
    Console.WriteLine(num);  // 1, 2, 3, 4, 5
}

// Works with LINQ
var doubled = range.Select(n => n * 2);
```

## Span<T> and Memory<T>

Low-level, high-performance collections:

```csharp
// Span: stack-allocated view into memory
Span<int> numbers = stackalloc int[10];
numbers[0] = 1;
numbers[1] = 2;

// Processing without allocation
void ProcessNumbers(Span<int> numbers)
{
    for (int i = 0; i < numbers.Length; i++)
    {
        numbers[i] *= 2;
    }
}

ProcessNumbers(numbers);

// Memory: heap-allocated wrapper
var memory = new Memory<int>(new[] { 1, 2, 3, 4, 5 });
var slice = memory.Slice(1, 3);  // Get subsequence
```

## Collection Initializers

```csharp
// List
var list = new List<int> { 1, 2, 3, 4, 5 };

// Dictionary
var dict = new Dictionary<string, int>
{
    ["Alice"] = 30,
    ["Bob"] = 25
};

// HashSet
var set = new HashSet<string> { "apple", "banana", "cherry" };

// Nested
var matrix = new List<List<int>>
{
    new List<int> { 1, 2, 3 },
    new List<int> { 4, 5, 6 }
};
```

## Performance Considerations

```csharp
// List<T>: Good for indexed access, append
// Array: Fastest, fixed size
// Dictionary: O(1) lookup by key
// HashSet: O(1) operations, unique values
// Queue/Stack: Specialized for FIFO/LIFO

// Avoid unnecessary allocations
var numbers = new List<int>(100);  // Pre-allocate capacity
numbers.AddRange(new[] { 1, 2, 3, 4, 5 });

// For large collections, consider capacity
var dict = new Dictionary<string, int>(10000);
```

## Practice Exercises

1. **Generic Stack**: Implement a generic stack class from scratch
2. **Custom Dictionary**: Build a simple generic dictionary with hash table
3. **Collection Operations**: Use List/Dictionary/HashSet for a real problem (e.g., word frequency counter)
4. **Generic Constraints**: Write a generic method that only works with comparable types
5. **Performance Test**: Compare performance of List vs Array vs Span for different operations

## Key Takeaways

- **Generics** provide type safety without casting and code reuse
- **Type constraints** restrict generic types based on interfaces, base classes, or other criteria
- **List<T>** for ordered mutable collections
- **Dictionary<K,V>** for fast key-value lookups
- **HashSet<T>** for unique values and set operations
- **Queue/Stack** for specialized collection behaviors
- **Immutable collections** prevent accidental modifications
- **Span<T>** and **Memory<T>** for high-performance scenarios
- Consider **capacity** and **preallocation** for large collections
