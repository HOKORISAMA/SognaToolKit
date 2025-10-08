using System;
using System.IO;
using System.Text;
using System.Linq;

namespace ScriptTool
{
    public class Program
    {
        public static void Main(string[] args)
        {
            Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);

            Console.WriteLine("Sogna Script Tool");
            Console.WriteLine("================\n");

            if (args.Length == 0)
            {
                PrintUsage();
                return;
            }

            string mode = args[0].ToLower();

            switch (mode)
            {
                case "disasm":
                case "d":
                    DisassembleScript(args);
                    break;

                case "export":
                case "e":
                    ExportText(args);
                    break;

                case "import":
                case "i":
                    ImportText(args);
                    break;

                default:
                    Console.WriteLine($"Unknown mode: {mode}");
                    PrintUsage();
                    break;
            }
        }

        private static void PrintUsage()
        {
            Console.WriteLine("Usage:");
            Console.WriteLine("  ScriptTool <mode> <input> [output] [options]\n");
            Console.WriteLine("Modes:");
            Console.WriteLine("  disasm|d <script_file> [output_file]  - Disassemble script to text");
            Console.WriteLine("  export|e <script_file> [text_file]    - Export translatable text");
            Console.WriteLine("  import|i <script_file> <text_file>    - Import translated text\n");
            Console.WriteLine("Options:");
            Console.WriteLine("  --encoding <codepage>  - Specify text encoding (default: 932/Shift-JIS)");
            Console.WriteLine("\nExamples:");
            Console.WriteLine("  ScriptTool disasm script.bin");
            Console.WriteLine("  ScriptTool disasm script.bin output.txt");
            Console.WriteLine("  ScriptTool export script.bin text.txt");
            Console.WriteLine("  ScriptTool import script.bin translated.txt");
        }

        private static void DisassembleScript(string[] args)
        {
            if (args.Length < 2)
            {
                Console.WriteLine("Error: Input file required for disassembly");
                return;
            }

            string inputFile = args[1];
            string outputFile = args.Length >= 3 ? args[2] : Path.ChangeExtension(inputFile, ".asm.txt");

            if (!File.Exists(inputFile))
            {
                Console.WriteLine($"Error: Input file not found: {inputFile}");
                return;
            }

            try
            {
                Console.WriteLine($"Disassembling: {inputFile}");

                var encoding = GetEncodingFromArgs(args);
                var script = new Script { Encoding = encoding };

                script.Load(inputFile);

                File.WriteAllText(outputFile, script.Disassembly, Encoding.UTF8);

                Console.WriteLine($"Disassembly saved to: {outputFile}");
                Console.WriteLine($"Total instructions: {script.Disassembly.Split('\n').Length}");
                Console.WriteLine($"Jump references found: {script.JumpReferences.Count}");
                Console.WriteLine($"Text strings found: {script.CollectedStrings.Count}");
                Console.WriteLine("\nDisassembly complete!");
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error during disassembly: {ex.Message}");
            }
        }

        private static void ExportText(string[] args)
        {
            if (args.Length < 2)
            {
                Console.WriteLine("Error: Input file required for text export");
                return;
            }

            string inputFile = args[1];
            string outputFile = args.Length >= 3 ? args[2] : Path.ChangeExtension(inputFile, ".txt");

            if (!File.Exists(inputFile))
            {
                Console.WriteLine($"Error: Input file not found: {inputFile}");
                return;
            }

            try
            {
                Console.WriteLine($"Exporting text from: {inputFile}");

                var encoding = GetEncodingFromArgs(args);
                var script = new Script { Encoding = encoding };

                script.Load(inputFile);
                script.ExportText(outputFile);

                Console.WriteLine($"Text exported to: {outputFile}");
                Console.WriteLine($"Total text entries: {script.CollectedStrings.Count}");

                int displayTextCount = script.CollectedStrings.Count(s => s.Type == 0);
                int tokenTextCount = script.CollectedStrings.Count(s => s.Type == 1);
                int hotZoneTextCount = script.CollectedStrings.Count(s => s.Type == 2);

                Console.WriteLine($"  - Display text: {displayTextCount}");
                Console.WriteLine($"  - Token text: {tokenTextCount}");
                Console.WriteLine($"  - Hot zone text: {hotZoneTextCount}");
                Console.WriteLine("\nExport complete!");
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error during text export: {ex.Message}");
            }
        }

        private static void ImportText(string[] args)
        {
            if (args.Length < 3)
            {
                Console.WriteLine("Error: Both script file and text file required for import");
                return;
            }

            string scriptFile = args[1];
            string textFile = args[2];

            if (!File.Exists(scriptFile))
            {
                Console.WriteLine($"Error: Script file not found: {scriptFile}");
                return;
            }

            if (!File.Exists(textFile))
            {
                Console.WriteLine($"Error: Text file not found: {textFile}");
                return;
            }

            try
            {
                Console.WriteLine($"Importing text into: {scriptFile}");
                Console.WriteLine($"From text file: {textFile}");

                var encoding = GetEncodingFromArgs(args);
                var script = new Script { Encoding = encoding };

                script.Load(scriptFile);

                // Save modified script
                string outputFile = Path.Combine(
                    Path.GetDirectoryName(scriptFile) ?? ".",
                    Path.GetFileNameWithoutExtension(scriptFile) + "_translated" + Path.GetExtension(scriptFile)
                );
				
                script.ImportText(
								originalFilePath: scriptFile, 
								translationFilePath: textFile,
								outputFilePath: outputFile,
								encoding: encoding
							);

                Console.WriteLine($"Translated script saved to: {outputFile}");
                Console.WriteLine("\nImport complete!");
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error during text import: {ex.Message}");
            }
        }

        private static Encoding GetEncodingFromArgs(string[] args)
        {
            for (int i = 0; i < args.Length - 1; i++)
            {
                if (args[i].Equals("--encoding", StringComparison.OrdinalIgnoreCase))
                {
                    try
                    {
                        int codepage = int.Parse(args[i + 1]);
                        return Encoding.GetEncoding(codepage);
                    }
                    catch
                    {
                        Console.WriteLine($"Warning: Invalid encoding '{args[i + 1]}', using default (932)");
                    }
                }
            }

            return Encoding.GetEncoding(932); // Default Shift-JIS
        }
    }
}
