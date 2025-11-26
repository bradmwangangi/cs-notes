# Object-Oriented Programming

## Classes & Objects

### Basic Class

```csharp
public class Person
{
    // Fields (data)
    public string Name;
    public int Age;

    // Constructor
    public Person(string name, int age)
    {
        Name = name;
        Age = age;
    }

    // Method
    public void Introduce()
    {
        Console.WriteLine($"Hi, I'm {Name} and I'm {Age} years old.");
    }
}

// Creating an object
Person alice = new Person("Alice", 30);
alice.Introduce();  // Hi, I'm Alice and I'm 30 years old.
```

### Properties (Preferred Over Fields)

Properties provide encapsulation and control over field access:

```csharp
public class Person
{
    private int _age;  // Private backing field

    public string Name { get; set; }  // Auto-property

    // Property with logic
    public int Age
    {
        get { return _age; }
        set
        {
            if (value < 0)
                throw new ArgumentException("Age cannot be negative");
            _age = value;
        }
    }

    // Read-only property
    public string InitialName => Name[0].ToString();

    // Init-only property (can only be set during construction)
    public string Id { get; init; }
}

// Usage
var person = new Person { Name = "Bob", Age = 25, Id = "123" };
person.Age = -5;  // Throws exception
```

### Constructors

```csharp
public class Rectangle
{
    public double Width { get; set; }
    public double Height { get; set; }

    // Default constructor
    public Rectangle()
    {
        Width = 0;
        Height = 0;
    }

    // Parameterized constructor
    public Rectangle(double width, double height)
    {
        Width = width;
        Height = height;
    }

    // Constructor chaining
    public Rectangle(double side) : this(side, side)
    {
    }

    public double Area() => Width * Height;
}

var rect1 = new Rectangle();              // 0, 0
var rect2 = new Rectangle(5, 10);         // 5, 10
var rect3 = new Rectangle(5);             // 5, 5 (square)
```

## Access Modifiers

| Modifier | Access |
|----------|--------|
| `public` | Anywhere |
| `private` | Only within class (default for members) |
| `protected` | Within class and derived classes |
| `internal` | Within same assembly |

```csharp
public class BankAccount
{
    public string AccountNumber { get; private set; }  // Public read, private set
    private decimal _balance;  // Private to this class

    public BankAccount(string accountNumber, decimal initialBalance)
    {
        AccountNumber = accountNumber;
        _balance = initialBalance;
    }

    public decimal GetBalance()
    {
        return _balance;
    }

    public void Deposit(decimal amount)
    {
        _balance += amount;
    }
}
```

## Inheritance

```csharp
// Base class
public class Animal
{
    public string Name { get; set; }

    public virtual void MakeSound()  // Virtual allows override
    {
        Console.WriteLine("Some generic sound");
    }
}

// Derived class
public class Dog : Animal
{
    public string Breed { get; set; }

    public override void MakeSound()  // Override base method
    {
        Console.WriteLine("Woof!");
    }

    public void Fetch()
    {
        Console.WriteLine($"{Name} is fetching!");
    }
}

// Usage
Dog dog = new Dog { Name = "Rex", Breed = "Labrador" };
dog.MakeSound();  // Woof!

// Base class reference to derived object
Animal animal = dog;
animal.MakeSound();  // Woof! (uses Dog's implementation)
```

### Base Keyword

```csharp
public class Employee
{
    public string Name { get; set; }
    public decimal Salary { get; set; }

    public virtual void PrintInfo()
    {
        Console.WriteLine($"Name: {Name}, Salary: {Salary}");
    }
}

public class Manager : Employee
{
    public List<Employee> DirectReports { get; set; }

    public override void PrintInfo()
    {
        base.PrintInfo();  // Call parent implementation
        Console.WriteLine($"Reports: {DirectReports.Count}");
    }
}
```

## Interfaces

Contracts defining what a class must implement:

```csharp
public interface IPayable
{
    decimal GetPayment();
}

public interface IDrivable
{
    void Drive();
    void Stop();
}

public class Car : IDrivable
{
    public void Drive()
    {
        Console.WriteLine("Car is driving");
    }

    public void Stop()
    {
        Console.WriteLine("Car stopped");
    }
}

public class Consultant : IPayable
{
    public decimal HourlyRate { get; set; }
    public int Hours { get; set; }

    public decimal GetPayment()
    {
        return HourlyRate * Hours;
    }
}

// Multiple interface implementation
public class Employee : IPayable
{
    public decimal Salary { get; set; }

    public decimal GetPayment()
    {
        return Salary;
    }
}
```

### Interface Segregation

Keep interfaces focused:

```csharp
// Bad: Fat interface
public interface IWorker
{
    void Work();
    void Eat();
    void Sleep();
}

// Better: Segregated interfaces
public interface IWorker
{
    void Work();
}

public interface ILivingBeing
{
    void Eat();
    void Sleep();
}

public class Employee : IWorker, ILivingBeing
{
    public void Work() { }
    public void Eat() { }
    public void Sleep() { }
}
```

## Polymorphism

### Method Overloading

Same method name, different parameters:

```csharp
public class Calculator
{
    public int Add(int a, int b)
    {
        return a + b;
    }

    public double Add(double a, double b)
    {
        return a + b;
    }

    public int Add(int a, int b, int c)
    {
        return a + b + c;
    }
}

var calc = new Calculator();
calc.Add(1, 2);           // Uses int version: 3
calc.Add(1.5, 2.5);       // Uses double version: 4.0
calc.Add(1, 2, 3);        // Uses three-parameter version: 6
```

### Method Overriding (Runtime Polymorphism)

```csharp
public class Shape
{
    public virtual double GetArea()
    {
        return 0;
    }
}

public class Circle : Shape
{
    public double Radius { get; set; }

    public override double GetArea()
    {
        return Math.PI * Radius * Radius;
    }
}

public class Square : Shape
{
    public double Side { get; set; }

    public override double GetArea()
    {
        return Side * Side;
    }
}

List<Shape> shapes = new List<Shape>
{
    new Circle { Radius = 5 },
    new Square { Side = 4 }
};

foreach (var shape in shapes)
{
    Console.WriteLine(shape.GetArea());  // Each calls its own GetArea
}
```

## Abstract Classes

Base class that can't be instantiated:

```csharp
public abstract class Vehicle
{
    public string Model { get; set; }

    // Abstract method (must be implemented by derived classes)
    public abstract void Start();

    // Concrete method
    public void Stop()
    {
        Console.WriteLine("Vehicle stopped");
    }
}

public class Car : Vehicle
{
    public override void Start()
    {
        Console.WriteLine("Car engine started");
    }
}

// Can't do: new Vehicle();  // Compiler error

Car car = new Car();
car.Start();  // Car engine started
```

## Static Members

Belong to the class, not instances:

```csharp
public class Counter
{
    public static int Count { get; set; } = 0;

    public Counter()
    {
        Count++;
    }

    public static void PrintCount()
    {
        Console.WriteLine($"Total instances: {Count}");
    }
}

var c1 = new Counter();
var c2 = new Counter();
Counter.PrintCount();  // Total instances: 2
```

## Sealed Classes & Methods

Prevent inheritance:

```csharp
public sealed class FinalClass
{
    // Can't be derived from
}

public class Parent
{
    public sealed override void Method()
    {
        // Can't be overridden further
    }
}
```

## Records (C# 9+)

Immutable reference types, great for data:

```csharp
// Records are immutable by default
public record Person(string Name, int Age);

var alice = new Person("Alice", 30);
// alice.Age = 31;  // Compiler error

// With-expressions for creating modified copies
var bob = alice with { Name = "Bob" };

// Automatic equality by value
var alice2 = new Person("Alice", 30);
Console.WriteLine(alice == alice2);  // true (same data)
```

## Practice Exercises

1. **Bank Account**: Create `BankAccount` class with deposit/withdraw, interest calculation
2. **Library System**: Design `Book`, `Author`, `Library` classes with proper relationships
3. **Employee Hierarchy**: Create `Employee`, `Manager`, `Contractor` classes with inheritance
4. **Shape Calculator**: Build shape classes (Circle, Rectangle, Triangle) with common interface
5. **Game Character**: Create base `Character` class with derived `Warrior`, `Mage`, `Archer`

## Key Takeaways

- Use **properties** instead of public fields for encapsulation
- **Inheritance** enables code reuse; use `virtual` and `override`
- **Interfaces** define contracts; implement multiple interfaces
- **Abstract classes** are halfway between interfaces and concrete classes
- **Polymorphism** allows objects of different types to be used interchangeably
- **Records** are perfect for immutable data objects
