using System;

namespace AnmCodec
{
	public class Program
	{
		public static void Main(string[] args)
		{
			if (args.Length < 3)
			{
				Console.WriteLine("ANM Codec - Command Line Tool");
				Console.WriteLine("\nUsage:");
				Console.WriteLine("  Decode: AnmCodec.exe decode <input.anm> <output_dir>");
				Console.WriteLine("  Encode: AnmCodec.exe encode <input_dir> <output.anm> [compressed]");
				Console.WriteLine("\nExample:");
				Console.WriteLine("  AnmCodec.exe decode animation.anm frames/");
				Console.WriteLine("  AnmCodec.exe encode frames/ animation.anm true");
				Console.WriteLine("\nOutput format: 8-bit indexed BMP files with embedded palette");
				return;
			}

			try
			{
				AnmCodec codec = new AnmCodec();
				string command = args[0].ToLower();

				if (command == "decode")
				{
					codec.Decode(args[1], args[2]);
				}
				else if (command == "encode")
				{
					bool compressed = args.Length > 3 ? bool.Parse(args[3]) : true;
					codec.Encode(args[1], args[2], compressed);
				}
				else
				{
					Console.WriteLine($"Unknown command: {command}");
				}
			}
			catch (Exception ex)
			{
				Console.WriteLine($"Error: {ex.Message}");
				Console.WriteLine(ex.StackTrace);
				Environment.Exit(1);
			}
		}
	}
}
