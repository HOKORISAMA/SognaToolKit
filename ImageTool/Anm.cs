using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace AnmCodec
{
    public class AnmCodec
    {
        private byte[][] palette;
        private bool compressed;
        private List<Frame> frames;

        public class Frame
        {
            public int Left { get; set; }
            public int Top { get; set; }
            public int Width { get; set; }
            public int Height { get; set; }
            public byte[] Data { get; set; } = Array.Empty<byte>();
        }

        public AnmCodec()
        {
            palette = new byte[256][];
            for (int i = 0; i < 256; i++)
            {
                palette[i] = new byte[3]; // RGB
            }
            compressed = true;
            frames = new List<Frame>();
        }

        // Decode ANM file
        public void Decode(string inputPath, string outputDir)
        {
            if (!File.Exists(inputPath))
            {
                throw new FileNotFoundException($"File not found: {inputPath}");
            }

            using (FileStream fs = File.OpenRead(inputPath))
            using (BinaryReader br = new BinaryReader(fs))
            {
                // Read palette (768 bytes: 256 colors * 3 bytes BGR)
                for (int i = 0; i < 256; i++)
                {
                    byte b = br.ReadByte();
                    byte g = br.ReadByte();
                    byte r = br.ReadByte();
                    palette[i] = new byte[] { r, g, b };
                }

                // Read frame count
                ushort frameCount = br.ReadUInt16();
                
                // Read flags
                ushort flags = br.ReadUInt16();
                compressed = (flags & 0x8000) == 0;

                Console.WriteLine($"Frame Count: {frameCount}");
                Console.WriteLine($"Compressed: {compressed}");

                // Read frame offsets
                int[] frameOffsets = new int[frameCount];
                int baseOffset = (int)fs.Position;
                
                for (int i = 0; i < frameCount; i++)
                {
                    frameOffsets[i] = baseOffset + br.ReadInt32();
                }

                // Calculate frame data sizes
                int[] frameSizes = new int[frameCount];
                for (int i = 0; i < frameCount - 1; i++)
                {
                    frameSizes[i] = frameOffsets[i + 1] - frameOffsets[i] - 8;
                }
                frameSizes[frameCount - 1] = (int)fs.Length - frameOffsets[frameCount - 1] - 8;

                // Read frames
                for (int i = 0; i < frameCount; i++)
                {
                    fs.Seek(frameOffsets[i], SeekOrigin.Begin);

                    Frame frame = new Frame
                    {
                        Left = br.ReadUInt16(),
                        Top = br.ReadUInt16(),
                        Width = br.ReadUInt16(),
                        Height = br.ReadUInt16()
                    };

                    byte[] encodedData = br.ReadBytes(frameSizes[i]);
                    frame.Data = DecodeFrameData(frame.Width, frame.Height, encodedData);
                    
                    frames.Add(frame);
                    Console.WriteLine($"Frame {i}: {frame.Width}x{frame.Height} at ({frame.Left},{frame.Top})");
                }
            }

            // Export frames
            Directory.CreateDirectory(outputDir);
            ExportPalette(Path.Combine(outputDir, "palette.txt"));
            ExportMetadata(Path.Combine(outputDir, "metadata.txt"));
            
            for (int i = 0; i < frames.Count; i++)
            {
                ExportFrameAsBMP(frames[i], Path.Combine(outputDir, $"frame_{i:D4}.bmp"));
            }

            Console.WriteLine($"\nExtracted {frames.Count} frames to {outputDir}");
        }

        // Encode to ANM file
        public void Encode(string inputDir, string outputPath, bool useCompression = true)
        {
            compressed = useCompression;
            frames.Clear();

            // Load metadata
            string metadataPath = Path.Combine(inputDir, "metadata.txt");
            Dictionary<int, (int left, int top)> metadata = new Dictionary<int, (int, int)>();
            if (File.Exists(metadataPath))
            {
                metadata = LoadMetadata(metadataPath);
            }

            // Load palette
            string palettePath = Path.Combine(inputDir, "palette.txt");
            if (File.Exists(palettePath))
            {
                LoadPalette(palettePath);
            }
            else
            {
                GenerateDefaultPalette();
            }

            // Load frames
            var frameFiles = Directory.GetFiles(inputDir, "frame_*.bmp")
                                     .OrderBy(f => f)
                                     .ToArray();

            for (int i = 0; i < frameFiles.Length; i++)
            {
                Frame frame = LoadFrameFromBMP(frameFiles[i]);
                if (metadata.ContainsKey(i))
                {
                    frame.Left = metadata[i].left;
                    frame.Top = metadata[i].top;
                }
                frames.Add(frame);
            }

            Console.WriteLine($"Loaded {frames.Count} frames");

            // Write ANM file
            using (FileStream fs = File.Create(outputPath))
            using (BinaryWriter bw = new BinaryWriter(fs))
            {
                // Write palette
                for (int i = 0; i < 256; i++)
                {
                    bw.Write(palette[i][2]); // B
                    bw.Write(palette[i][1]); // G
                    bw.Write(palette[i][0]); // R
                }

                // Write frame count
                bw.Write((ushort)frames.Count);

                // Write flags
                bw.Write((ushort)(compressed ? 0 : 0x8000));

                // Reserve space for frame offsets
                long offsetTablePos = fs.Position;
                for (int i = 0; i < frames.Count; i++)
                {
                    bw.Write(0);
                }

                // Write frames
                for (int i = 0; i < frames.Count; i++)
                {
                    long frameStart = fs.Position;
                    
                    // Update offset table
                    long currentPos = fs.Position;
                    fs.Seek(offsetTablePos + i * 4, SeekOrigin.Begin);
                    bw.Write((int)(frameStart - 772));
                    fs.Seek(currentPos, SeekOrigin.Begin);

                    Frame frame = frames[i];
                    int width = frame.Width;
                    
                    // Align width for compression
                    if (compressed)
                    {
                        while (width % 4 != 0) width++;
                    }

                    bw.Write((ushort)frame.Left);
                    bw.Write((ushort)frame.Top);
                    bw.Write((ushort)width);
                    bw.Write((ushort)frame.Height);

                    byte[] encodedData = EncodeFrameData(width, frame.Height, frame.Data);
                    bw.Write(encodedData);
                }
            }

            Console.WriteLine($"Encoded {frames.Count} frames to {outputPath}");
        }

        private byte[] DecodeFrameData(int width, int height, byte[] encoded)
        {
            byte[] decoded = new byte[width * height];

            if (!compressed)
            {
                Array.Copy(encoded, decoded, Math.Min(encoded.Length, decoded.Length));
                return decoded;
            }

            // RLE decompression (4-byte blocks)
            int readPos = 0;
            int writePos = 0;
            int col = 0;
            byte[] current = new byte[4];
            byte[] previous = new byte[4];
            bool hasPrevious = false;

            while (readPos < encoded.Length && col < width)
            {
                // Read 4-byte block
                if (readPos + 4 > encoded.Length) break;
                
                for (int i = 0; i < 4; i++)
                {
                    current[i] = encoded[readPos++];
                }

                // Check for RLE
                if (hasPrevious && ArraysEqual(current, previous))
                {
                    if (readPos >= encoded.Length) break;
                    
                    int count = encoded[readPos++];
                    if (count == 0 && readPos < encoded.Length)
                    {
                        count = 256 + encoded[readPos++];
                    }

                    for (int i = 0; i < count; i++)
                    {
                        for (int j = 0; j < 4 && writePos < decoded.Length; j++)
                        {
                            decoded[writePos++] = current[j];
                        }
                        writePos += width - 4;
                    }
                    
                    hasPrevious = false;
                }
                else
                {
                    // Write single block
                    for (int i = 0; i < 4 && writePos < decoded.Length; i++)
                    {
                        decoded[writePos++] = current[i];
                    }
                    writePos += width - 4;

                    Array.Copy(current, previous, 4);
                    hasPrevious = true;
                }

                // Move to next column
                if (writePos >= width * height)
                {
                    col += 4;
                    writePos = col;
                    hasPrevious = false;
                }
            }

            return decoded;
        }

        private byte[] EncodeFrameData(int width, int height, byte[] data)
        {
            if (!compressed)
            {
                return data;
            }

            List<byte> encoded = new List<byte>();
            
            // RLE compression (4-byte blocks column-wise)
            for (int col = 0; col < width; col += 4)
            {
                byte[]? previous = null;
                int repeatCount = 0;

                for (int row = 0; row < height; row++)
                {
                    byte[] current = new byte[4];
                    for (int i = 0; i < 4; i++)
                    {
                        int idx = row * width + col + i;
                        current[i] = (idx < data.Length) ? data[idx] : (byte)12;
                    }

                    if (previous != null && ArraysEqual(current, previous))
                    {
                        repeatCount++;
                        if (row == height - 1 || repeatCount >= 511)
                        {
                            encoded.AddRange(previous);
                            if (repeatCount >= 256)
                            {
                                encoded.Add(0);
                                encoded.Add((byte)(repeatCount - 256));
                            }
                            else
                            {
                                encoded.Add((byte)repeatCount);
                            }
                            repeatCount = 0;
                            previous = null;
                        }
                    }
                    else
                    {
                        if (previous != null && repeatCount > 0)
                        {
                            encoded.AddRange(previous);
                            if (repeatCount >= 256)
                            {
                                encoded.Add(0);
                                encoded.Add((byte)(repeatCount - 256));
                            }
                            else
                            {
                                encoded.Add((byte)repeatCount);
                            }
                            repeatCount = 0;
                        }

                        encoded.AddRange(current);
                        previous = current;
                    }
                }
            }

            return encoded.ToArray();
        }

        private void ExportFrameAsBMP(Frame frame, string path)
        {
            // BMP Header
            int rowSize = ((frame.Width * 8 + 31) / 32) * 4; // 8bpp with padding
            int pixelDataSize = rowSize * frame.Height;
            int fileSize = 14 + 40 + 1024 + pixelDataSize; // Header + DIB + Palette + Data

            using (BinaryWriter bw = new BinaryWriter(File.Create(path)))
            {
                // BMP File Header (14 bytes)
                bw.Write((byte)'B');
                bw.Write((byte)'M');
                bw.Write(fileSize);
                bw.Write(0); // Reserved
                bw.Write(14 + 40 + 1024); // Offset to pixel data

                // DIB Header (BITMAPINFOHEADER - 40 bytes)
                bw.Write(40); // Header size
                bw.Write(frame.Width);
                bw.Write(frame.Height);
                bw.Write((ushort)1); // Planes
                bw.Write((ushort)8); // Bits per pixel
                bw.Write(0); // Compression (BI_RGB)
                bw.Write(pixelDataSize);
                bw.Write(2835); // X pixels per meter
                bw.Write(2835); // Y pixels per meter
                bw.Write(256); // Colors used
                bw.Write(0); // Important colors

                // Color palette (256 colors * 4 bytes BGRA)
                for (int i = 0; i < 256; i++)
                {
                    bw.Write(palette[i][2]); // B
                    bw.Write(palette[i][1]); // G
                    bw.Write(palette[i][0]); // R
                    bw.Write((byte)255); // A
                }

                // Pixel data (bottom-up)
                for (int y = frame.Height - 1; y >= 0; y--)
                {
                    for (int x = 0; x < frame.Width; x++)
                    {
                        bw.Write(frame.Data[y * frame.Width + x]);
                    }
                    // Padding
                    for (int p = 0; p < rowSize - frame.Width; p++)
                    {
                        bw.Write((byte)0);
                    }
                }
            }
        }

        private Frame LoadFrameFromBMP(string path)
        {
            Frame frame = new Frame();
            
            using (BinaryReader br = new BinaryReader(File.OpenRead(path)))
            {
                // Read BMP header
                byte[] header = br.ReadBytes(2);
                if (header[0] != 'B' || header[1] != 'M')
                    throw new Exception("Not a valid BMP file");

                br.ReadInt32(); // File size
                br.ReadInt32(); // Reserved
                int dataOffset = br.ReadInt32();

                // Read DIB header
                int dibSize = br.ReadInt32();
                frame.Width = br.ReadInt32();
                frame.Height = br.ReadInt32();
                br.ReadUInt16(); // Planes
                ushort bpp = br.ReadUInt16();
                
                if (bpp != 8)
                    throw new Exception("Only 8-bit BMP files are supported");

                br.ReadInt32(); // Compression
                br.ReadInt32(); // Image size
                br.ReadInt32(); // X pixels per meter
                br.ReadInt32(); // Y pixels per meter
                int colorsUsed = br.ReadInt32();
                br.ReadInt32(); // Important colors

                // Read palette if present
                if (colorsUsed > 0 || dibSize >= 40)
                {
                    for (int i = 0; i < 256; i++)
                    {
                        byte b = br.ReadByte();
                        byte g = br.ReadByte();
                        byte r = br.ReadByte();
                        br.ReadByte(); // Alpha
                        palette[i] = new byte[] { r, g, b };
                    }
                }

                // Seek to pixel data
                br.BaseStream.Seek(dataOffset, SeekOrigin.Begin);

                // Read pixel data (bottom-up)
                int rowSize = ((frame.Width * 8 + 31) / 32) * 4;
                frame.Data = new byte[frame.Width * frame.Height];
                
                for (int y = frame.Height - 1; y >= 0; y--)
                {
                    byte[] row = br.ReadBytes(rowSize);
                    for (int x = 0; x < frame.Width; x++)
                    {
                        frame.Data[y * frame.Width + x] = row[x];
                    }
                }

                frame.Left = 0;
                frame.Top = 0;
            }
            
            return frame;
        }

        private void ExportPalette(string path)
        {
            using (StreamWriter sw = new StreamWriter(path))
            {
                for (int i = 0; i < 256; i++)
                {
                    sw.WriteLine($"{i:D3}: {palette[i][0]:D3} {palette[i][1]:D3} {palette[i][2]:D3}");
                }
            }
        }

        private void ExportMetadata(string path)
        {
            using (StreamWriter sw = new StreamWriter(path))
            {
                sw.WriteLine("# Frame metadata: frame_index left top width height");
                for (int i = 0; i < frames.Count; i++)
                {
                    sw.WriteLine($"{i} {frames[i].Left} {frames[i].Top} {frames[i].Width} {frames[i].Height}");
                }
            }
        }

        private Dictionary<int, (int left, int top)> LoadMetadata(string path)
        {
            var metadata = new Dictionary<int, (int, int)>();
            foreach (var line in File.ReadLines(path))
            {
                if (line.StartsWith("#") || string.IsNullOrWhiteSpace(line))
                    continue;
                    
                var parts = line.Split(' ');
                if (parts.Length >= 3)
                {
                    int idx = int.Parse(parts[0]);
                    int left = int.Parse(parts[1]);
                    int top = int.Parse(parts[2]);
                    metadata[idx] = (left, top);
                }
            }
            return metadata;
        }

        private void LoadPalette(string path)
        {
            foreach (var line in File.ReadLines(path))
            {
                if (string.IsNullOrWhiteSpace(line))
                    continue;
                    
                var parts = line.Split(new[] { ':', ' ' }, StringSplitOptions.RemoveEmptyEntries);
                if (parts.Length >= 4)
                {
                    int i = int.Parse(parts[0]);
                    palette[i][0] = byte.Parse(parts[1]);
                    palette[i][1] = byte.Parse(parts[2]);
                    palette[i][2] = byte.Parse(parts[3]);
                }
            }
        }

        private void GenerateDefaultPalette()
        {
            palette[10] = new byte[] { 0, 0, 0 };       // Black
            palette[11] = new byte[] { 255, 255, 255 }; // White
            palette[12] = new byte[] { 0, 0, 255 };     // Blue (transparent)
            
            for (int i = 13; i < 256; i++)
            {
                palette[i] = new byte[] { (byte)(i % 256), (byte)((i * 2) % 256), (byte)((i * 3) % 256) };
            }
        }

        private bool ArraysEqual(byte[] a, byte[] b)
        {
            if (a.Length != b.Length) return false;
            for (int i = 0; i < a.Length; i++)
            {
                if (a[i] != b[i]) return false;
            }
            return true;
        }
    }
}