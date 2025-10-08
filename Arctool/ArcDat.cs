using System;
using System.Collections.Generic;
using System.IO;
using System.Text;

namespace ArcTool
{
    public class Entry
    {
        public required string Name { get; set; }
        public required bool IsPacked { get; set; }
        public required byte[] Data { get; set; }
        public required byte[] PackedData { get; set; }
        public required uint Offset { get; set; }

        // Computed properties
        public uint Size => (uint)(IsPacked ? PackedData.Length : Data.Length);
        public uint UnpackedSize => (uint)Data.Length;

        public bool CheckPlacement(long fileLength) => Offset + Size <= fileLength;
    }

    public class ArcTool
    {
        // -------------------- EXTRACTOR --------------------
        public void Extract(string archiveFile, string outputFolder)
        {
            var entries = Unpack(archiveFile);

            using var file = File.OpenRead(archiveFile);

            foreach (var entry in entries)
            {
                string outFile = Path.Combine(outputFolder, entry.Name);
                var dir = Path.GetDirectoryName(outFile);
                if (!string.IsNullOrEmpty(dir))
                    Directory.CreateDirectory(dir);

				file.Seek(entry.Offset, SeekOrigin.Begin);
				byte[] data = new byte[entry.Size];
				file.ReadExactly(data); // Reads exactly data.Length bytes or throws

                if (entry.IsPacked)
                {
                    byte[] unpacked = new byte[entry.UnpackedSize];
                    using (var ms = new MemoryStream(data))
                        LzUnpack(ms, unpacked);

                    File.WriteAllBytes(outFile, unpacked);
                }
                else
                {
                    File.WriteAllBytes(outFile, data);
                }

                Console.WriteLine($"Extracted: {entry.Name} ({entry.Size} bytes)");
            }
        }

        public List<Entry> Unpack(string filename)
        {
            var entries = new List<Entry>();

            using var file = File.OpenRead(filename);
            using var reader = new BinaryReader(file, Encoding.UTF8);

            if (!AsciiStringEqual(reader, 4, "DAT 1.00"))
                throw new InvalidDataException("Invalid archive magic (expected DAT 1.00).");

            file.Seek(12, SeekOrigin.Begin);
            uint fileCount = reader.ReadUInt32();

            long indexOffset = 0x10;
            for (int i = 0; i < fileCount; i++)
            {
                file.Seek(indexOffset, SeekOrigin.Begin);

                byte[] nameBytes = reader.ReadBytes(0x10);
                string name = Encoding.UTF8.GetString(nameBytes).TrimEnd('\0');

                file.Seek(indexOffset + 0x13, SeekOrigin.Begin);
                bool isPacked = reader.ReadByte() != 0;

                file.Seek(indexOffset + 0x14, SeekOrigin.Begin);
                uint size = reader.ReadUInt32();
                uint unpackedSize = reader.ReadUInt32();
                uint offset = reader.ReadUInt32();

                var entry = new Entry
                {
                    Name = name,
                    IsPacked = isPacked,
                    Offset = offset,
                    Data = new byte[unpackedSize],
                    PackedData = new byte[size]
                };

                if (!entry.CheckPlacement(file.Length))
                    throw new Exception($"Invalid entry placement for {name}");

                entries.Add(entry);
                indexOffset += 0x20;
            }

            return entries;
        }

        private static bool AsciiStringEqual(BinaryReader reader, long offset, string expectedAscii)
        {
            reader.BaseStream.Seek(offset, SeekOrigin.Begin);
            var bytes = reader.ReadBytes(expectedAscii.Length);
            var got = Encoding.ASCII.GetString(bytes);
            return string.Equals(got, expectedAscii, StringComparison.Ordinal);
        }

        private void LzUnpack(Stream input, byte[] output)
        {
            int dst = 0;
            int bits = 0;
            byte mask = 0;

            using var reader = new BinaryReader(input);
            while (dst < output.Length)
            {
                mask >>= 1;
                if (mask == 0)
                {
                    bits = reader.ReadByte();
                    mask = 0x80;
                }

                if ((mask & bits) != 0)
                {
                    int offset = reader.ReadUInt16();
                    int count = (offset >> 12) + 1;
                    offset &= 0xFFF;
                    CopyOverlapped(output, dst - offset, dst, count);
                    dst += count;
                }
                else
                {
                    output[dst++] = reader.ReadByte();
                }
            }
        }

        private void CopyOverlapped(byte[] buffer, int srcOffset, int dstOffset, int count)
        {
            for (int i = 0; i < count; i++)
                buffer[dstOffset + i] = buffer[srcOffset + i];
        }

        // -------------------- PACKER --------------------
        public void Pack(string inputFolder, string outputFile, bool compress = false)
		{
			var entries = new List<Entry>();

			foreach (var filePath in Directory.GetFiles(inputFolder, "*.*", SearchOption.AllDirectories))
			{
				byte[] data = File.ReadAllBytes(filePath);
				byte[] packedData = compress ? LzPack(data) : data;

				var entry = new Entry
				{
					Name = Path.GetRelativePath(inputFolder, filePath).Replace("\\", "/"),
					Data = data,
					PackedData = packedData,
					IsPacked = compress,
					Offset = 0 // filled later
				};

				entries.Add(entry);
			}

			using var fs = File.Create(outputFile);
			using var writer = new BinaryWriter(fs, Encoding.UTF8);

			// --- Header ---
			// Write "SGS." + "DAT 1.00" + 0x00 padding up to 12 bytes total
			writer.Write(Encoding.ASCII.GetBytes("SGS.DAT 1.00"));
			if (fs.Position < 12)
				writer.Write(new byte[12 - fs.Position]);

			writer.Write((uint)entries.Count);

			long indexOffset = fs.Position;               // should now be 0x10
			long dataOffset = indexOffset + entries.Count * 0x20;

			// --- Index ---
			foreach (var entry in entries)
			{
				fs.Seek(indexOffset, SeekOrigin.Begin);

				// name (0x00–0x0F)
				byte[] nameBytes = new byte[0x10];
				Encoding.UTF8.GetBytes(entry.Name, 0, Math.Min(entry.Name.Length, 16), nameBytes, 0);
				writer.Write(nameBytes);

				// padding 0x10–0x12
				writer.Write(new byte[3]);

				// isPacked (0x13)
				writer.Write((byte)(entry.IsPacked ? 1 : 0));

				// size, unpacked size, offset
				writer.Write(entry.Size);
				writer.Write(entry.UnpackedSize);
				writer.Write((uint)dataOffset);

				entry.Offset = (uint)dataOffset;

				indexOffset += 0x20;
				dataOffset += entry.Size;
			}

			// --- Data ---
			foreach (var entry in entries)
			{
				fs.Seek(entry.Offset, SeekOrigin.Begin);
				writer.Write(entry.IsPacked ? entry.PackedData : entry.Data);
			}

			Console.WriteLine($"Packed {entries.Count} files into {outputFile}");
		}

        private byte[] LzPack(byte[] input)
        {
            using var output = new MemoryStream();
            int src = 0;

            while (src < input.Length)
            {
                int count = 1;
                int dst = src;

                while (src + count < input.Length && count < 0xFFF)
                {
                    if (input[src + count] != input[src])
                        break;
                    count++;
                }

                if (count > 2)
                {
                    output.WriteByte((byte)(count >> 8));
                    output.WriteByte((byte)(count & 0xFF));
                    output.WriteByte(input[src]);
                    src += count;
                }
                else
                {
                    count = 1;
                    while (src + count < input.Length && count < 0xFFF && input[src + count] != input[src])
                        count++;

                    output.WriteByte((byte)(count >> 8));
                    output.WriteByte((byte)(count & 0xFF));
                    output.Write(input, src, count);
                    src += count;
                }
            }

            return output.ToArray();
        }
    }
}
