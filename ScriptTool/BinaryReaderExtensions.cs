using System;
using System.IO;
using System.Text;
using System.Collections.Generic;

namespace ScriptTool
{
    /// <summary>
    /// Provides helper extension methods for BinaryReader to simplify reading
    /// null-terminated strings, multi-word arrays, and peek operations while preserving stream position.
    /// </summary>
    public static class BinaryReaderExtensions
    {
        /// <summary>
        /// Reads a null-terminated string from the current position of the BinaryReader.
        /// Bytes are read until a 0x00 terminator is found. 
        /// The collected bytes are then decoded using the specified <paramref name="encoding"/>.
        /// </summary>
        /// <param name="reader">The BinaryReader to read from.</param>
        /// <param name="encoding">The text encoding (e.g., Encoding.GetEncoding(932) for CP932).</param>
        /// <returns>The decoded string (excluding the null terminator).</returns>
        public static string ReadNullTerminatedString(this BinaryReader reader, Encoding encoding)
        {
            using var ms = new MemoryStream();
            int rb;
            while ((rb = reader.ReadByte()) != 0x00)
                ms.WriteByte((byte)rb);

            return encoding.GetString(ms.ToArray());
        }

        /// <summary>
        /// Reads a sequence of byte pairs (2 bytes each) until a zero (0x00) is encountered.
        /// Each pair is stored as a packed 32-bit integer (first byte in high word, second byte in low word).
        /// <para>Example: bytes [0x12, 0x34] → value = 0x00120034</para>
        /// </summary>
        /// <param name="reader">The BinaryReader to read from.</param>
        /// <returns>A list of packed 32-bit pair values.</returns>
        public static List<uint> ReadPairs(this BinaryReader reader)
        {
            var pairs = new List<uint>();
            while (true)
            {
                byte first = reader.ReadByte();
                if (first == 0) break;
                byte second = reader.ReadByte();
                pairs.Add((uint)(first << 16) | second);
            }
            return pairs;
        }

        /// <summary>
        /// Reads a double-word (4-byte unsigned integer) array from the BinaryReader.
        /// The first byte represents the count of subsequent UInt32 entries.
        /// </summary>
        /// <param name="reader">The BinaryReader to read from.</param>
        /// <returns>A list of 32-bit unsigned integers (UInt32).</returns>
        public static List<uint> ReadDoubleWordArray(this BinaryReader reader)
        {
            byte count = reader.ReadByte();
            var values = new List<uint>();
            for (int i = 0; i < count; i++)
            {
                values.Add(reader.ReadUInt32());
            }
            return values;
        }

        /// <summary>
        /// Peeks ahead in the BinaryReader by reading a value using the specified read function,
        /// then restores the stream position to its original location.
        /// </summary>
        /// <typeparam name="T">The data type being read (e.g., byte, ushort, int).</typeparam>
        /// <param name="reader">The BinaryReader to read from.</param>
        /// <param name="readFunc">A delegate that performs the read operation.</param>
        /// <returns>The value read from the stream, without changing its position.</returns>
        private static T Peek<T>(this BinaryReader reader, Func<BinaryReader, T> readFunc)
        {
            var stream = reader.BaseStream;
            long pos = stream.Position;
            T value = readFunc(reader);
            stream.Position = pos;
            return value;
        }

        /// <summary>Peeks a single unsigned byte without advancing the stream.</summary>
        public static byte PeekByte(this BinaryReader reader) =>
            reader.Peek(r => r.ReadByte());

        /// <summary>Peeks a signed byte without advancing the stream.</summary>
        public static sbyte PeekSByte(this BinaryReader reader) =>
            reader.Peek(r => r.ReadSByte());

        /// <summary>Peeks a 16-bit signed integer without advancing the stream.</summary>
        public static short PeekInt16(this BinaryReader reader) =>
            reader.Peek(r => r.ReadInt16());

        /// <summary>Peeks a 16-bit unsigned integer without advancing the stream.</summary>
        public static ushort PeekUInt16(this BinaryReader reader) =>
            reader.Peek(r => r.ReadUInt16());

        /// <summary>Peeks a 32-bit signed integer without advancing the stream.</summary>
        public static int PeekInt32(this BinaryReader reader) =>
            reader.Peek(r => r.ReadInt32());

        /// <summary>Peeks a 32-bit unsigned integer without advancing the stream.</summary>
        public static uint PeekUInt32(this BinaryReader reader) =>
            reader.Peek(r => r.ReadUInt32());

        /// <summary>Peeks a 64-bit signed integer without advancing the stream.</summary>
        public static long PeekInt64(this BinaryReader reader) =>
            reader.Peek(r => r.ReadInt64());

        /// <summary>Peeks a 64-bit unsigned integer without advancing the stream.</summary>
        public static ulong PeekUInt64(this BinaryReader reader) =>
            reader.Peek(r => r.ReadUInt64());

        /// <summary>Peeks a single-precision floating-point value without advancing the stream.</summary>
        public static float PeekSingle(this BinaryReader reader) =>
            reader.Peek(r => r.ReadSingle());

        /// <summary>Peeks a double-precision floating-point value without advancing the stream.</summary>
        public static double PeekDouble(this BinaryReader reader) =>
            reader.Peek(r => r.ReadDouble());
    }
}
