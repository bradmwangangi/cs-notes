using System;
using System.Text;

class Program
{
	static void Main()
	{
		Run();
	}

	static void Run()
	{
		Console.Write("Enter string to reverse: ");
		string? input = Console.ReadLine();

		if (string.IsNullOrWhiteSpace(input))
		{
			Console.WriteLine("Input cannot be empty.");
			return;
		}

		string reversed1 = StringReverser.ReverseBasic(input);
		Console.WriteLine(reversed1);

		string reversed2 = StringReverser.ReverseSB(input);
		Console.WriteLine(reversed2);

		string reversed3 = StringReverser.ReverseLinq(input);
		Console.WriteLine(reversed3);

		string reversed4 = StringReverser.ReverseSpan(input);
		Console.WriteLine(reversed4);
	}
}

class StringReverser
{
	public static string ReverseBasic(string input)
	{
		string reversed = "";

		for (int i = input.Length - 1; i >= 0; i--)
		{
			reversed += input[i];
		}

		return reversed;
	}

	public static string ReverseSB(string input)
	{
		var sb = new StringBuilder(input.Length);

		for (int i = input.Length - 1; i >= 0; i--)
		{
			sb.Append(input[i]);
		}

		return sb.ToString();
	}

	public static string ReverseLinq(string input)
	{
		// return new string(input.Reverse().ToArray());
		return new string([.. input.Reverse()]);
	}

	public static string ReverseSpan(string input)
	{
		Span<char> span = stackalloc char[input.Length];

		for (int i = 0; i < input.Length; i++)
		{
			span[i] = input[input.Length - 1 - i];
		}

		return new string(span);
	}

}