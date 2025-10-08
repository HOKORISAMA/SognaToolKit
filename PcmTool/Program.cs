using System;

namespace PcmSoundCodec
{
    class Program
    {
        static void Main(string[] args)
        {
            if (args.Length < 2)
            {
                Console.WriteLine("PCM Sound Codec - Command Line Tool");
                Console.WriteLine("\nUsage:");
                Console.WriteLine("  Info:    PcmSoundCodec.exe info <input_file>");
                Console.WriteLine("  To WAV:  PcmSoundCodec.exe towav <input.pcm> <output.wav> [version]");
                Console.WriteLine("  To PCM:  PcmSoundCodec.exe topcm <input.wav> <output.pcm> [version]");
                Console.WriteLine("\nVersions: unrestricted, pregtb, gtb, postgtb (default: unrestricted)");
                Console.WriteLine("\nExamples:");
                Console.WriteLine("  PcmSoundCodec.exe info sound.pcm");
                Console.WriteLine("  PcmSoundCodec.exe towav sound.pcm sound.wav");
                Console.WriteLine("  PcmSoundCodec.exe topcm sound.wav sound.pcm gtb");
                return;
            }

            try
            {
                string command = args[0].ToLower();
                string inputFile = args[1];

                GameVersion version = GameVersion.Unrestricted;
                if (args.Length > 3)
                {
                    version = ParseVersion(args[3]);
                }

                switch (command)
                {
                    case "info":
                        ShowInfo(inputFile);
                        break;

                    case "towav":
                        if (args.Length < 3)
                        {
                            Console.WriteLine("Error: Output file required");
                            return;
                        }
                        ConvertToWav(inputFile, args[2], version);
                        break;

                    case "topcm":
                        if (args.Length < 3)
                        {
                            Console.WriteLine("Error: Output file required");
                            return;
                        }
                        ConvertToPcm(inputFile, args[2], version);
                        break;

                    default:
                        Console.WriteLine($"Unknown command: {command}");
                        break;
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error: {ex.Message}");
                Environment.Exit(1);
            }
        }

        static void ShowInfo(string inputFile)
        {
            PcmSound sound = new PcmSound(inputFile);
            
            Console.WriteLine($"File: {Path.GetFileName(inputFile)}");
            Console.WriteLine($"Format: {sound.Format}");
            Console.WriteLine($"Channels: {sound.Channels}");
            Console.WriteLine($"Sample Rate: {sound.SampleRate} Hz");
            Console.WriteLine($"Bits per Sample: {sound.BitsPerSample}");
            Console.WriteLine($"Signed: {sound.Signed}");
            Console.WriteLine($"Sample Count: {sound.SampleCount:N0}");
            Console.WriteLine($"Duration: {sound.Duration:mm\\:ss\\.fff}");
            Console.WriteLine($"File Size: {new FileInfo(inputFile).Length:N0} bytes");
        }

        static void ConvertToWav(string inputFile, string outputFile, GameVersion version)
        {
            Console.WriteLine($"Converting {inputFile} → {outputFile}...");
            var sound = new PcmSound(inputFile, version);
            var wav = sound.Convert(SoundFormat.Waveform, version);
            wav.SaveToFile(outputFile);
            Console.WriteLine("Done.");
        }

        static void ConvertToPcm(string inputFile, string outputFile, GameVersion version)
        {
            Console.WriteLine($"Converting {inputFile} → {outputFile}...");
            var sound = new PcmSound(inputFile, version);
            var pcm = sound.Convert(SoundFormat.PCM, version);
            pcm.SaveToFile(outputFile);
            Console.WriteLine("Done.");
        }
		static GameVersion ParseVersion(string s) => s.ToLower() switch
        {
            "pregtb" => GameVersion.PreGTB,
            "gtb" => GameVersion.GTB,
            "postgtb" => GameVersion.PostGTB,
            _ => GameVersion.Unrestricted
        };
	}
}