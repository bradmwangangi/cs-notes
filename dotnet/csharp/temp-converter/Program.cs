using System;

class Program
{
    static void Main()
    {
        Run();
    }

    static void Run()
    {
        Console.Write("Enter degrees in Celsius: ");
        string? celsiusInput = Console.ReadLine();

        if(!double.TryParse(celsiusInput, out double celsius))
        {
            Console.WriteLine("Invalid input. Please enter a valid number.");
            return;
        }

        double fahr = TempConverter.CelsiusToFahr(celsius);

        Console.WriteLine($"Celsius {celsius} becomes Fahrenheit {fahr}");
    }
}

class TempConverter
{
    public static double FahrToCelsius(double farh)
    {
      // C = (F - 32) * 5/9
        return (farh - 32) * 5.0 / 9.0;
    }

    public static double CelsiusToFahr(double celsius)
    {
      // F = (C × 9/5) + 32
        return (celsius * 9.0 / 5.0) + 32;
    }
}