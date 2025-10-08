using System;
using System.IO;
using ArcTool;

class Program
{
    static void Main(string[] args)
    {
        if (args.Length < 1)
        {
            ShowUsage();
            return;
        }

        string command = args[0].ToLower();
        var tool = new ArcTool.ArcTool();

        try
        {
            switch (command)
            {
                case "extract":
                    if (args.Length < 3)
                    {
                        Console.WriteLine("Error: Missing arguments for extract.");
                        ShowUsage();
                        return;
                    }
                    ExtractArchive(tool, args[1], args[2]);
                    break;

                case "pack":
                    if (args.Length < 3)
                    {
                        Console.WriteLine("Error: Missing arguments for pack.");
                        ShowUsage();
                        return;
                    }
                    PackFolder(tool, args[1], args[2]);
                    break;

                default:
                    Console.WriteLine($"Unknown command: {command}");
                    ShowUsage();
                    break;
            }
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Error: {ex.Message}");
        }
    }

    static void ExtractArchive(ArcTool.ArcTool tool, string archivePath, string outputFolder)
    {
        if (!File.Exists(archivePath))
        {
            Console.WriteLine($"Error: Archive file '{archivePath}' does not exist.");
            return;
        }

        Directory.CreateDirectory(outputFolder);
        Console.WriteLine($"Extracting '{archivePath}' to '{outputFolder}'...");
        tool.Extract(archivePath, outputFolder);
        Console.WriteLine("Extraction completed successfully.");
    }

    static void PackFolder(ArcTool.ArcTool tool, string inputFolder, string outputArchive)
    {
        if (!Directory.Exists(inputFolder))
        {
            Console.WriteLine($"Error: Input folder '{inputFolder}' does not exist.");
            return;
        }

        Console.WriteLine($"Packing '{inputFolder}' into '{outputArchive}'...");
        tool.Pack(inputFolder, outputArchive, compress: false);
        Console.WriteLine("Packing completed successfully.");
    }

    static void ShowUsage()
    {
        Console.WriteLine("Usage:");
        Console.WriteLine("  ArcTool extract <archive-file> <output-folder>");
        Console.WriteLine("  ArcTool pack <input-folder> <output-file>");
    }
}
