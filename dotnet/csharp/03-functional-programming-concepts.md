# Functional Programming Concepts

## Delegates

Type-safe function pointers. A delegate is a type that represents references to methods:

```csharp
// Define a delegate type
public delegate int Operation(int a, int b);

// Implement methods matching the delegate signature
int Add(int a, int b) => a + b;
int Multiply(int a, int b) => a * b;

// Use the delegate
Operation op = Add;
Console.WriteLine(op(5, 3));      // 8

op = Multiply;
Console.WriteLine(op(5, 3));      // 15

// Delegate with void
public delegate void Logger(string message);

void PrintLog(string msg) => Console.WriteLine($"[LOG] {msg}");
void WriteLog(string msg) => Console.WriteLine($"[FILE] {msg}");

Logger log = PrintLog;
log += WriteLog;  // Multicast: both methods called
log("Application started");
```

## Anonymous Methods & Lambdas

Inline function definitions:

```csharp
// Anonymous method (older syntax)
Operation op = delegate (int a, int b) { return a + b; };

// Lambda expression (modern syntax, preferred)
Operation op = (a, b) => a + b;

// Multi-line lambda
Func<int, int, int> calculate = (a, b) =>
{
    var sum = a + b;
    return sum * 2;
};

// Lambda with no parameters
Action greet = () => Console.WriteLine("Hello!");
greet();

// Lambda with single parameter
Func<int, int> square = x => x * x;
Console.WriteLine(square(5));  // 25

// Lambda with multiple parameters
Func<string, int, string> repeat = (text, times) =>
    string.Concat(Enumerable.Repeat(text, times));
Console.WriteLine(repeat("Hi ", 3));  // Hi Hi Hi
```

## Func & Action

Common delegate types in the standard library:

```csharp
// Func<input, output>: Has return value
Func<int, int, int> add = (a, b) => a + b;
int result = add(3, 5);  // 8

// Action<input>: No return value (void)
Action<string> print = msg => Console.WriteLine(msg);
print("Hello");

// Func with one input, one output
Func<int, string> formatNumber = n => $"Number: {n}";
Console.WriteLine(formatNumber(42));

// Func with no input
Func<DateTime> now = () => DateTime.Now;
Console.WriteLine(now());
```

## Higher-Order Functions

Functions that accept or return other functions:

```csharp
// Function that accepts a function
public int Execute(Func<int, int, int> operation, int a, int b)
{
    return operation(a, b);
}

Console.WriteLine(Execute((x, y) => x + y, 10, 5));      // 15
Console.WriteLine(Execute((x, y) => x * y, 10, 5));      // 50

// Function that returns a function
public Func<int, int> CreateMultiplier(int factor)
{
    return x => x * factor;
}

var double = CreateMultiplier(2);
var triple = CreateMultiplier(3);
Console.WriteLine(double(5));   // 10
Console.WriteLine(triple(5));   // 15
```

## LINQ Basics

Language Integrated Query - declarative data processing:

```csharp
int[] numbers = { 1, 2, 3, 4, 5, 6, 7, 8, 9, 10 };

// Where: filter
var evens = numbers.Where(n => n % 2 == 0);
// { 2, 4, 6, 8, 10 }

// Select: transform
var squared = numbers.Select(n => n * n);
// { 1, 4, 9, 16, 25, 36, 49, 64, 81, 100 }

// Chaining
var result = numbers
    .Where(n => n > 3)
    .Select(n => n * 2)
    .ToList();
// { 8, 10, 12, 14, 16, 18, 20 }

// FirstOrDefault: get single item
int? first = numbers.FirstOrDefault(n => n > 5);  // 6
int? notFound = numbers.FirstOrDefault(n => n > 100);  // null

// Any: check if any match
bool hasEven = numbers.Any(n => n % 2 == 0);  // true

// All: check if all match
bool allPositive = numbers.All(n => n > 0);  // true

// Count
int count = numbers.Count(n => n > 5);  // 5

// Sum, Average, Max, Min
int sum = numbers.Sum();  // 55
double average = numbers.Average();  // 5.5
int max = numbers.Max();  // 10

// OrderBy, OrderByDescending
var sorted = numbers.OrderBy(n => n).ToList();
var descending = numbers.OrderByDescending(n => n).ToList();

// GroupBy
var grouped = numbers.GroupBy(n => n % 2)
    .ToDictionary(
        g => g.Key == 0 ? "Even" : "Odd",
        g => g.ToList()
    );
// { "Even": [2,4,6,8,10], "Odd": [1,3,5,7,9] }
```

## LINQ with Objects

```csharp
public record Product(string Name, double Price, string Category);

var products = new[]
{
    new Product("Laptop", 1200, "Electronics"),
    new Product("Mouse", 25, "Electronics"),
    new Product("Desk", 300, "Furniture"),
    new Product("Chair", 150, "Furniture"),
};

// Filter by category
var electronics = products
    .Where(p => p.Category == "Electronics")
    .ToList();

// Order by price, get top 2
var expensive = products
    .OrderByDescending(p => p.Price)
    .Take(2)
    .ToList();

// Group by category, get name and average price
var grouped = products
    .GroupBy(p => p.Category)
    .Select(g => new
    {
        Category = g.Key,
        Count = g.Count(),
        AvgPrice = g.Average(p => p.Price)
    })
    .ToList();

// Join two collections
var categories = new[] { "Electronics", "Furniture", "Clothing" };
var matched = categories
    .Where(c => products.Any(p => p.Category == c))
    .ToList();
// ["Electronics", "Furniture"]

// Check if specific product exists
bool hasExpensive = products.Any(p => p.Price > 500);  // true
```

## Method vs Query Syntax

```csharp
int[] numbers = { 1, 2, 3, 4, 5, 6, 7, 8, 9, 10 };

// Method syntax (preferred in modern C#)
var methodResult = numbers
    .Where(n => n > 3)
    .Select(n => n * 2)
    .OrderBy(n => n)
    .ToList();

// Query syntax (more SQL-like)
var queryResult = (from n in numbers
                   where n > 3
                   select n * 2)
    .OrderBy(n => n)
    .ToList();

// Both produce the same result
```

## Lazy Evaluation

LINQ uses deferred execution - computation happens when you enumerate:

```csharp
var numbers = Enumerable.Range(1, 10);

// This doesn't execute immediately
var query = numbers.Where(n => n > 5);

// Execution happens here
foreach (var n in query)
{
    Console.WriteLine(n);  // Prints 6, 7, 8, 9, 10
}

// ToList() forces immediate evaluation
var list = numbers.Where(n => n > 5).ToList();
```

## Map, Filter, Reduce Pattern

```csharp
int[] numbers = { 1, 2, 3, 4, 5 };

// Map: transform each element
var mapped = numbers.Select(n => n * 2).ToList();
// { 2, 4, 6, 8, 10 }

// Filter: keep elements matching condition
var filtered = numbers.Where(n => n > 2).ToList();
// { 3, 4, 5 }

// Reduce: combine elements into single value
int sum = numbers.Aggregate(0, (acc, n) => acc + n);
// 15

// Reduce with multiplication
int product = numbers.Aggregate(1, (acc, n) => acc * n);
// 120

// Reduce to string
string concatenated = numbers.Aggregate("", (acc, n) => acc + n);
// "12345"
```

## Immutability

Functional programming favors immutable data:

```csharp
// Mutable (avoid when possible)
List<int> mutable = new List<int> { 1, 2, 3 };
mutable.Add(4);  // Changes the list

// Immutable
ImmutableList<int> immutable = ImmutableList.Create(1, 2, 3);
var updated = immutable.Add(4);  // Creates new list, original unchanged

// With records (immutable by default)
record Person(string Name, int Age);

var alice = new Person("Alice", 30);
var bob = alice with { Name = "Bob" };  // Creates new record
// alice is still Alice, bob is Bob
```

## Pure Functions

Functions with no side effects:

```csharp
// Pure: same input always produces same output
public int Add(int a, int b) => a + b;
public bool IsEven(int n) => n % 2 == 0;

// Impure: modifies state, has side effects
private int sum = 0;
public void AddToSum(int n)  // Changes sum state
{
    sum += n;
}

public void PrintMessage(string msg)  // Side effect (console output)
{
    Console.WriteLine(msg);
}

// Prefer pure when possible for easier testing and reasoning about code
```

## Practice Exercises

1. **Filter & Transform**: Given a list of numbers, filter even numbers and double them
2. **String Processing**: Use LINQ to find the longest word in a list of strings
3. **Statistics**: Calculate mean, median, and mode of a dataset using LINQ
4. **GroupBy**: Group students by grade level, get average score per level
5. **Higher-Order**: Create a function that takes a filtering function and applies it to multiple lists

## Key Takeaways

- **Delegates** are type-safe function pointers; **Func** and **Action** are standard types
- **Lambdas** provide concise syntax for inline functions
- **LINQ** enables declarative data querying and transformation
- **Method chaining** makes complex operations readable
- **Immutability** and **pure functions** reduce bugs and make code easier to reason about
- **Lazy evaluation** in LINQ means computation happens only when needed
