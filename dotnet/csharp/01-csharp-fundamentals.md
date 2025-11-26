# C# Fundamentals

## Hello World

```csharp
Console.WriteLine("Hello, World!");
```

Run this:
```bash
dotnet new console -n HelloWorld
cd HelloWorld
dotnet run
```

## Types & Variables

### Basic Types
C# is **statically typed**. Common types:

| Type | Example | Range |
|------|---------|-------|
| `int` | `42` | -2.1B to 2.1B |
| `long` | `42L` | Much larger |
| `float` | `3.14f` | 32-bit decimal |
| `double` | `3.14` | 64-bit decimal |
| `decimal` | `3.14m` | High precision (financial) |
| `bool` | `true` / `false` | Boolean |
| `string` | `"hello"` | Text |
| `char` | `'a'` | Single character |

### Variable Declaration

```csharp
int age = 25;
string name = "Alice";
double price = 19.99;
bool isActive = true;

// Type inference with var
var count = 10;  // Compiler knows it's int
var message = "Hello";  // Compiler knows it's string
```

### Constants

```csharp
const int MaxRetries = 3;
const string AppName = "MyApp";

// Can't reassign constants
// MaxRetries = 5;  // Compiler error
```

### Nullable Types

```csharp
int? age = null;  // int can be null
string? name = null;  // string can be null

if (age.HasValue)
{
    Console.WriteLine(age.Value);
}

// Null-coalescing operator
int actualAge = age ?? 0;  // Use 0 if age is null
```

## Strings

### Basic Operations

```csharp
string first = "Hello";
string second = "World";

// Concatenation
string greeting = first + " " + second;

// String interpolation (preferred)
string message = $"{first} {second}!";

// Escape sequences
string path = "C:\\Users\\Alice\\file.txt";
string multiline = "Line 1\nLine 2\nLine 3";
```

### Common String Methods

```csharp
string text = "  Hello, World!  ";

text.Length;           // 16
text.ToLower();        // "  hello, world!  "
text.ToUpper();        // "  HELLO, WORLD!  "
text.Trim();           // "Hello, World!"
text.Replace("World", "C#");  // "  Hello, C#!  "
text.StartsWith("  H");       // true
text.Contains("World");       // true
text.Substring(2, 5);         // "Hello"

// Split
string csv = "apple,banana,orange";
string[] fruits = csv.Split(',');  // ["apple", "banana", "orange"]
```

## Control Flow

### If/Else

```csharp
int score = 85;

if (score >= 90)
{
    Console.WriteLine("A");
}
else if (score >= 80)
{
    Console.WriteLine("B");
}
else
{
    Console.WriteLine("C");
}
```

### Switch

```csharp
string day = "Monday";

switch (day)
{
    case "Monday":
    case "Tuesday":
    case "Wednesday":
    case "Thursday":
    case "Friday":
        Console.WriteLine("Weekday");
        break;
    case "Saturday":
    case "Sunday":
        Console.WriteLine("Weekend");
        break;
    default:
        Console.WriteLine("Unknown");
        break;
}

// Switch expression (newer C#)
string dayType = day switch
{
    "Monday" or "Tuesday" or "Wednesday" or "Thursday" or "Friday" => "Weekday",
    "Saturday" or "Sunday" => "Weekend",
    _ => "Unknown"
};
```

### Loops

```csharp
// For loop
for (int i = 0; i < 5; i++)
{
    Console.WriteLine(i);  // 0, 1, 2, 3, 4
}

// While loop
int count = 0;
while (count < 5)
{
    Console.WriteLine(count);
    count++;
}

// Do-While (runs at least once)
do
{
    Console.WriteLine(count);
} while (count < 5);

// Foreach (iterating over collections)
int[] numbers = { 1, 2, 3, 4, 5 };
foreach (int num in numbers)
{
    Console.WriteLine(num);
}
```

## Methods

### Defining Methods

```csharp
// Basic method
void Greet(string name)
{
    Console.WriteLine($"Hello, {name}!");
}

// Method with return value
int Add(int a, int b)
{
    return a + b;
}

// Using methods
Greet("Alice");
int result = Add(3, 5);  // result = 8
```

### Parameters

```csharp
// Default parameters
void PrintMessage(string message, int times = 1)
{
    for (int i = 0; i < times; i++)
    {
        Console.WriteLine(message);
    }
}

PrintMessage("Hi");           // Prints once
PrintMessage("Hi", 3);        // Prints 3 times
PrintMessage("Hi", times: 3); // Named parameter

// Out parameter (method can modify argument)
void ParseNumber(string input, out int result)
{
    result = int.Parse(input);
}

ParseNumber("42", out int number);
Console.WriteLine(number);  // 42

// Ref parameter (pass by reference)
void Increment(ref int value)
{
    value++;
}

int x = 5;
Increment(ref x);
Console.WriteLine(x);  // 6

// Params (variable arguments)
int Sum(params int[] numbers)
{
    int total = 0;
    foreach (int num in numbers)
    {
        total += num;
    }
    return total;
}

Sum(1, 2, 3, 4, 5);  // 15
```

## Operators

### Arithmetic
```csharp
int a = 10;
int b = 3;

a + b;   // 13
a - b;   // 7
a * b;   // 30
a / b;   // 3 (integer division)
a % b;   // 1 (remainder)
a ** 2;  // 100 (power)
```

### Comparison
```csharp
5 == 5;      // true
5 != 3;      // true
5 > 3;       // true
5 >= 5;      // true
5 < 10;      // true
```

### Logical
```csharp
true && true;    // AND: true
true || false;   // OR: true
!true;           // NOT: false
```

### Assignment
```csharp
int x = 5;
x += 3;   // x = 8 (x = x + 3)
x -= 2;   // x = 6 (x = x - 2)
x *= 2;   // x = 12 (x = x * 2)
x /= 4;   // x = 3 (x = x / 4)
```

## Comments

```csharp
// Single-line comment

/* Multi-line
   comment */

/// XML documentation (used by IntelliSense)
/// <summary>
/// Calculates the sum of two numbers.
/// </summary>
/// <param name="a">First number</param>
/// <param name="b">Second number</param>
/// <returns>The sum</returns>
int Add(int a, int b)
{
    return a + b;
}
```

## Input/Output

### Console Output
```csharp
Console.Write("No newline");
Console.WriteLine("With newline");

// Formatting
int value = 42;
Console.WriteLine($"Value: {value}");
Console.WriteLine("Value: {0}", value);  // Older style
```

### Console Input
```csharp
Console.Write("Enter your name: ");
string? name = Console.ReadLine();

Console.Write("Enter your age: ");
string? ageInput = Console.ReadLine();
int age = int.Parse(ageInput);

// Safer parsing
Console.Write("Enter a number: ");
if (int.TryParse(Console.ReadLine(), out int number))
{
    Console.WriteLine($"You entered: {number}");
}
else
{
    Console.WriteLine("Invalid number");
}
```

## Arrays

```csharp
// Declaration and initialization
int[] numbers = { 10, 20, 30, 40, 50 };
string[] names = new string[3];

// Accessing elements (0-indexed)
numbers[0];   // 10
numbers[1];   // 20

// Length
numbers.Length;  // 5

// Modifying
numbers[0] = 15;

// Multidimensional
int[,] matrix = new int[3, 3];
matrix[0, 0] = 1;
matrix[0, 1] = 2;

// Jagged (array of arrays)
int[][] jagged = new int[3][];
jagged[0] = new int[5];
```

## Practice Exercises

1. **Temperature Converter**: Write a program that converts Celsius to Fahrenheit. Formula: F = (C × 9/5) + 32
2. **Grade Calculator**: Read a score (0-100), output letter grade (A: 90+, B: 80-89, C: 70-79, etc.)
3. **Multiplication Table**: Print the multiplication table for a given number
4. **Fibonacci**: Write a method that returns the first N Fibonacci numbers
5. **String Reversal**: Write a method that reverses a string

## Key Takeaways

- C# is strongly typed and case-sensitive
- Variables must be declared with a type (or use `var`)
- Methods are the basic building block for organizing code
- String interpolation (`$"..."`) is cleaner than concatenation
- Use `Console.WriteLine()` for output and `Console.ReadLine()` for input
- Arrays are zero-indexed collections
