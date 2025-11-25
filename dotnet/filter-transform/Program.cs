using System;

// Given a list of numbers, filter out the even numbers then double each one.
// Output the result as both a list and print each number individually.

class Program
{
  static void Main()
  {
    Run();
  }

  static void Run()
  {
    int[] numbers = [15, 24, 8, 37, 42, 19, 56, 11, 88, 3, 72, 50, 9, 64, 21];

    var results = FilterTransform.EvensThenDouble(numbers);

    Console.WriteLine($"\nResult as List: [{string.Join(", ", results)}]");

    Console.WriteLine("Result as each number: ");
    foreach (int number in results)
    {
      Console.WriteLine(number);
    }
  }
}

class FilterTransform
{
  public static List<int> EvensThenDouble(int[] numbers)
  {
    var evensDoubled = numbers
        .Where(n => n % 2 == 0)
        .Select(n => n * 2)
        .ToList();

    return evensDoubled;
  }

}