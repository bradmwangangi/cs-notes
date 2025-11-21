using System;

class Program
{
    static void Main()
    {
        Run();
    }

    static void Run()
    {
        Console.Write("Enter score: ");
        string? scoreInput = Console.ReadLine();

        if(!double.TryParse(scoreInput, out double score))
        {
            Console.WriteLine("Invalid input. Please enter a valid number.");
            return;
        }

        char grade = GradeCalculator.CalculateGrade(score);

        Console.WriteLine(grade);
    }
}

class GradeCalculator
{
    public static char CalculateGrade(double score)
    {
        return score switch
        {
            >= 90.00 => 'A',
            >= 80.0 => 'B',
            >= 70.0 => 'C',
            >= 60.0 => 'D',
            _ => 'F'
        };
    }
}

