using System;
using System.IO;
using System.Text;

namespace ScriptTool
{
    public class Program
    {
        public static void Main(string[] args)
        {
            Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);

            if (args.Length == 0)
            {
                PrintUsage();
                return;
            }

            string mode = args[0].ToLower();
            var encoding = GetEncodingFromArgs(args);

            try
            {
                switch (mode)
                {
                    case "disasm":
                    case "d":
                        RunDisasm(args, encoding);
                        break;

                    case "export":
                    case "e":
                        RunExport(args, encoding);
                        break;

                    case "import":
                    case "i":
                        RunImport(args, encoding);
                        break;

                    case "batch-export":
                    case "be":
                        RunBatchExport(args, encoding);
                        break;

                    case "batch-import":
                    case "bi":
                        RunBatchImport(args, encoding);
                        break;

                    default:
                        Console.WriteLine($"Unknown mode: {mode}");
                        PrintUsage();
                        break;
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error: {ex.Message}");
                Console.WriteLine($"Stack trace: {ex.StackTrace}");
            }
        }

        private static void RunDisasm(string[] args, Encoding encoding)
        {
            if (args.Length < 2)
            {
                Console.WriteLine("Error: Input path required for disasm mode.");
                PrintUsage();
                return;
            }

            string input = args[1];
            string? output = args.Length >= 3 ? args[2] : null;

            if (!File.Exists(input))
            {
                Console.WriteLine($"Error: Input file not found: {input}");
                return;
            }

            DisassembleScript(input, output, encoding);
        }

        private static void RunExport(string[] args, Encoding encoding)
        {
            if (args.Length < 2)
            {
                Console.WriteLine("Error: Input path required for export mode.");
                PrintUsage();
                return;
            }

            string input = args[1];
            string? output = args.Length >= 3 ? args[2] : null;

            if (!File.Exists(input))
            {
                Console.WriteLine($"Error: Input file not found: {input}");
                return;
            }

            ExportText(input, output, encoding);
        }

        private static void RunImport(string[] args, Encoding encoding)
        {
            if (args.Length < 3)
            {
                Console.WriteLine("Error: Input and text file paths required for import mode.");
                PrintUsage();
                return;
            }

            string input = args[1];
            string txtFile = args[2];
            string? output = args.Length >= 4 ? args[3] : null;
            int maxLineLength = args.Length >= 5 && int.TryParse(args[4], out int val) ? val : 50;

            if (!File.Exists(input))
            {
                Console.WriteLine($"Error: Input file not found: {input}");
                return;
            }

            if (!File.Exists(txtFile))
            {
                Console.WriteLine($"Error: Translation file not found: {txtFile}");
                return;
            }

            var script = new Script { Encoding = encoding };
            script.ImportText(input, txtFile, output, encoding, maxLineLength);
        }

        private static void RunBatchExport(string[] args, Encoding encoding)
        {
            if (args.Length < 2)
            {
                Console.WriteLine("Error: Input folder required for batch-export mode.");
                PrintUsage();
                return;
            }

            string inputFolder = args[1];
            string outputFolder = args.Length >= 3 ? args[2] : inputFolder;

            if (!Directory.Exists(inputFolder))
            {
                Console.WriteLine($"Error: Input folder not found: {inputFolder}");
                return;
            }

            Directory.CreateDirectory(outputFolder);

            var files = Directory.GetFiles(inputFolder, "*.win");
            foreach (var file in files)
            {
                string output = Path.Combine(outputFolder, Path.GetFileNameWithoutExtension(file) + ".txt");
                ExportText(file, output, encoding);
            }

            Console.WriteLine($"Batch export completed! Total files processed: {files.Length}");
        }

        private static void RunBatchImport(string[] args, Encoding encoding)
        {
            if (args.Length < 3)
            {
                Console.WriteLine("Error: Input folder and text folder required for batch-import mode.");
                PrintUsage();
                return;
            }

            string inputFolder = args[1];
            string textFolder = args[2];
            string outputFolder = args.Length >= 4 ? args[3] : inputFolder;

            int maxLineLength = args.Length >= 5 && int.TryParse(args[4], out int val) ? val : 50;

            if (!Directory.Exists(inputFolder))
            {
                Console.WriteLine($"Error: Input folder not found: {inputFolder}");
                return;
            }

            if (!Directory.Exists(textFolder))
            {
                Console.WriteLine($"Error: Text folder not found: {textFolder}");
                return;
            }

            Directory.CreateDirectory(outputFolder);

            var inputFiles = Directory.GetFiles(inputFolder, "*.win");
            foreach (var inputFile in inputFiles)
            {
                string txtFile = Path.Combine(textFolder, Path.GetFileNameWithoutExtension(inputFile) + ".txt");
                if (!File.Exists(txtFile))
                {
                    Console.WriteLine($"Warning: Translation file not found for {inputFile}, skipping...");
                    continue;
                }

                string output = Path.Combine(outputFolder, Path.GetFileName(inputFile));

                var script = new Script { Encoding = encoding };
                script.ImportText(inputFile, txtFile, output, encoding, maxLineLength);
            }

            Console.WriteLine($"Batch import completed! Total files processed: {inputFiles.Length}");
        }

        private static void PrintUsage()
        {
            Console.WriteLine("\nScriptTool - Visual Novel Script Processor");
            Console.WriteLine("===========================================\n");
            Console.WriteLine("Usage:");
            Console.WriteLine("  ScriptTool disasm <input> [output] [--encoding <name>]");
            Console.WriteLine("  ScriptTool export <input> [output] [--encoding <name>]");
            Console.WriteLine("  ScriptTool import <input> <textfile> [output] [--encoding <name>] [maxLineLength]");
            Console.WriteLine("  ScriptTool batch-export <inputFolder> [outputFolder] [--encoding <name>]");
            Console.WriteLine("  ScriptTool batch-import <inputFolder> <textFolder> [outputFolder] [--encoding <name>] [maxLineLength]");
            Console.WriteLine("\nOptions:");
            Console.WriteLine("  --encoding <name>    Specify encoding (default: shift_jis/932)");
            Console.WriteLine("  maxLineLength        Optional: maximum characters per line for translations (default: 55)");
            Console.WriteLine("\nExamples:");
            Console.WriteLine("  ScriptTool disasm script.bin");
            Console.WriteLine("  ScriptTool export script.bin script_text.txt");
            Console.WriteLine("  ScriptTool import script.bin translated.txt script_new.bin 50");
            Console.WriteLine("  ScriptTool batch-export scripts_folder output_folder");
            Console.WriteLine("  ScriptTool batch-import scripts_folder texts_folder output_folder 60");
        }

        private static Encoding GetEncodingFromArgs(string[] args)
        {
            for (int i = 0; i < args.Length - 1; i++)
            {
                if (args[i].ToLower() == "--encoding")
                {
                    string encodingName = args[i + 1];
                    try
                    {
                        if (int.TryParse(encodingName, out int codePage))
                            return Encoding.GetEncoding(codePage);

                        return Encoding.GetEncoding(encodingName);
                    }
                    catch
                    {
                        Console.WriteLine($"Warning: Invalid encoding '{encodingName}', using Shift-JIS (932).");
                    }
                }
            }
            return Encoding.GetEncoding(932);
        }

        private static void DisassembleScript(string input, string? output, Encoding encoding)
        {
            Console.WriteLine($"Disassembling: {input}");
            Console.WriteLine($"Encoding: {encoding.EncodingName}");

            var script = new Script { Encoding = encoding };
            script.Load(input);

            if (string.IsNullOrEmpty(output))
            {
                output = Path.ChangeExtension(input, ".asm");
            }

            File.WriteAllText(output, script.Disassembly, Encoding.UTF8);

            Console.WriteLine($"Disassembly saved to: {output}");
            Console.WriteLine($"Total strings found: {script.CollectedStrings.Count}");
            Console.WriteLine($"Total jump references: {script.JumpReferences.Count}");
        }

        private static void ExportText(string input, string? output, Encoding encoding)
        {
            Console.WriteLine($"Exporting text from: {input}");
            Console.WriteLine($"Encoding: {encoding.EncodingName}");

            var script = new Script { Encoding = encoding };
            script.Load(input);

            if (string.IsNullOrEmpty(output))
            {
                output = Path.ChangeExtension(input, ".txt");
            }

            script.ExportText(output);

            Console.WriteLine($"Text exported to: {output}");
            Console.WriteLine($"Total strings exported: {script.CollectedStrings.Count}");
        }

        private static void ImportText(string input, string txtFile, string? output, Encoding encoding)
        {
            Console.WriteLine($"Importing text into: {input}");
            Console.WriteLine($"Translation file: {txtFile}");
            Console.WriteLine($"Encoding: {encoding.EncodingName}");

            if (!File.Exists(txtFile))
            {
                Console.WriteLine($"Error: Translation file not found: {txtFile}");
                return;
            }

            if (string.IsNullOrEmpty(output))
            {
                output = Path.Combine(
                    Path.GetDirectoryName(input) ?? ".",
                    Path.GetFileNameWithoutExtension(input) + Path.GetExtension(input)
                );
            }

            var script = new Script { Encoding = encoding };
            script.ImportText(input, txtFile, output, encoding);

            Console.WriteLine("Import completed successfully!");
        }
    }
}
