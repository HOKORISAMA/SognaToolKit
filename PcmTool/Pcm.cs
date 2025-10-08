using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace PcmSoundCodec
{
    public enum SoundFormat
    {
        PCM,
        Waveform
    }

    public enum GameVersion
    {
        Unrestricted,
        PreGTB,
        GTB,
        PostGTB
    }

    public class PcmSound
    {
        private byte[] _bytes;
        private SoundFormat _format = SoundFormat.PCM;
        private int _channels = 1;
        private int _sampleRate = 22050;
        private int _bitsPerSample = 8;
        private bool _signed;
        private int _sampleCount;
        private TimeSpan _duration = TimeSpan.Zero;
        private GameVersion _version = GameVersion.Unrestricted;

        public int BitsPerSample => _bitsPerSample;
        public int Channels => _channels;
        public TimeSpan Duration => _duration;
        public SoundFormat Format => _format;
        public int SampleCount => _sampleCount;
        public int SampleRate => _sampleRate;
        public bool Signed => _signed;
        public GameVersion Version => _version;

        public PcmSound(string path, GameVersion version = GameVersion.Unrestricted)
        {
            if (!File.Exists(path))
                throw new FileNotFoundException($"File not found: {path}");

            _version = version;
            _bytes = File.ReadAllBytes(path);
            SetProperties();
        }

        public PcmSound(byte[] bytes, GameVersion version = GameVersion.Unrestricted)
        {
            if (bytes == null)
                throw new ArgumentNullException(nameof(bytes));

            _version = version;
            _bytes = new byte[bytes.Length];
            Array.Copy(bytes, _bytes, bytes.Length);
            SetProperties();
        }

        public PcmSound(Stream stream, int byteCount, GameVersion version = GameVersion.Unrestricted)
        {
            if (stream == null || !stream.CanRead)
                throw new ArgumentException("Invalid stream");

            if (byteCount <= 0)
                throw new ArgumentOutOfRangeException(nameof(byteCount));

            _version = version;
            _bytes = new byte[byteCount];
            stream.ReadExactly(_bytes, 0, byteCount);
            SetProperties();
        }

        private PcmSound(byte[] bytes)
        {
            _bytes = bytes;
            SetProperties();
        }

        public PcmSound Convert(SoundFormat format, GameVersion version = GameVersion.Unrestricted)
        {
            int sourceHeaderSize = (_format == SoundFormat.Waveform) ? 44 : 0;
            int sourceBytesPerSample = (int)Math.Ceiling(_bitsPerSample / 8.0);
            int targetHeaderSize = (format == SoundFormat.Waveform) ? 44 : 0;
            
            bool targetSigned = false;
            int targetChannels, targetBitsPerSample, targetSampleRate;

            if (format == SoundFormat.Waveform)
            {
                // Keep original properties for WAV
                targetChannels = _channels;
                targetBitsPerSample = _bitsPerSample;
                targetSampleRate = _sampleRate;
                targetSigned = (targetBitsPerSample == 8);
            }
            else
            {
                // PCM format constraints
                targetChannels = 1;
                targetSampleRate = 22050;
                targetBitsPerSample = (version != GameVersion.Unrestricted && version < GameVersion.GTB) ? 8 : 16;
            }

            int targetBytesPerSample = (int)Math.Ceiling(targetBitsPerSample / 8.0);

            // Check if conversion is needed
            if (_format == format && 
                _channels == targetChannels && 
                _bitsPerSample == targetBitsPerSample && 
                _sampleRate == targetSampleRate && 
                _signed == targetSigned)
            {
                return new PcmSound(_bytes);
            }

            // Calculate output size
            int outputSize = _sampleCount * targetChannels * targetBytesPerSample + targetHeaderSize;
            List<byte> output = new List<byte>(outputSize);

            // Write WAV header if needed
            if (format == SoundFormat.Waveform)
            {
                WriteWavHeader(output, outputSize - 8, targetChannels, targetSampleRate, targetBitsPerSample, targetBytesPerSample);
            }

            // Convert audio data
            int resampleAccumulator = 0;
            int sourceStride = sourceBytesPerSample * _channels;

            for (int i = sourceHeaderSize; i < _bytes.Length; i += sourceStride)
            {
                resampleAccumulator += targetSampleRate;
                
                while (resampleAccumulator >= _sampleRate)
                {
                    resampleAccumulator -= _sampleRate;

                    if (_channels == targetChannels)
                    {
                        // Same channel count - process each channel
                        for (int ch = 0; ch < targetChannels; ch++)
                        {
                            int sample = ReadSample(i + ch * sourceBytesPerSample, sourceBytesPerSample);
                            WriteSample(output, sample, sourceBytesPerSample, targetBytesPerSample, _signed, targetSigned);
                        }
                    }
                    else
                    {
                        // Different channel count - mix/duplicate
                        long mixedSample = 0;
                        for (int ch = 0; ch < _channels; ch++)
                        {
                            mixedSample += ReadSample(i + ch * sourceBytesPerSample, sourceBytesPerSample);
                        }
                        
                        long avgSample = mixedSample / targetChannels;

                        // Adjust bit depth
                        for (int shift = targetBytesPerSample; shift < sourceBytesPerSample; shift++)
                            avgSample >>= 8;
                        
                        for (int shift = sourceBytesPerSample; shift < targetBytesPerSample; shift++)
                        {
                            avgSample <<= 8;
                            if ((avgSample & 0x100) == 0x100)
                                avgSample |= 0xFF;
                        }

                        // Write to all target channels
                        for (int ch = 0; ch < targetChannels; ch++)
                        {
                            long channelSample = avgSample;
                            for (int b = 1; b <= targetBytesPerSample; b++)
                            {
                                long byteVal = channelSample & 0xFF;
                                if (b == targetBytesPerSample && _signed != targetSigned)
                                    byteVal ^= 0x80;
                                
                                output.Add((byte)byteVal);
                                channelSample >>= 8;
                            }
                        }
                    }
                }
            }

            // Fix WAV header sizes if actual output differs
            if (format == SoundFormat.Waveform && outputSize != output.Count)
            {
                UpdateWavHeaderSizes(output);
            }

            return new PcmSound(output.ToArray());
        }

        public byte[] GetBytes()
        {
            byte[] copy = new byte[_bytes.Length];
            Array.Copy(_bytes, copy, _bytes.Length);
            return copy;
        }

        public void SaveToFile(string path)
        {
            File.WriteAllBytes(path, _bytes);
        }

        private void SetProperties()
        {
            int headerSize = 0;

            // Check for WAV format
            if (_bytes.Length >= 44)
            {
                // Standard WAV (RIFF little-endian)
                if (IsWavHeader(_bytes, 0, "RIFF", "WAVE"))
                {
                    _format = SoundFormat.Waveform;
                    headerSize = 44;
                    ParseWavHeader(false);
                }
                // Big-endian WAV (RIFX)
                else if (IsWavHeader(_bytes, 0, "RIFX", "WAVE"))
                {
                    _format = SoundFormat.Waveform;
                    headerSize = 44;
                    ParseWavHeader(true);
                }
            }

            // PCM format bit depth detection
            if (headerSize == 0 && (_version == GameVersion.Unrestricted || _version >= GameVersion.GTB))
            {
                _bitsPerSample = 16;
            }

            // Calculate sample count and duration
            int bytesPerSample = (int)Math.Ceiling((_channels * _bitsPerSample) / 8.0);
            _sampleCount = (_bytes.Length - headerSize) / bytesPerSample;
            _duration = TimeSpan.FromSeconds((double)_sampleCount / _sampleRate);
        }

        private bool IsWavHeader(byte[] data, int offset, string riffTag, string waveTag)
        {
            return data[offset] == riffTag[0] && data[offset + 1] == riffTag[1] &&
                   data[offset + 2] == riffTag[2] && data[offset + 3] == riffTag[3] &&
                   data[offset + 8] == waveTag[0] && data[offset + 9] == waveTag[1] &&
                   data[offset + 10] == waveTag[2] && data[offset + 11] == waveTag[3] &&
                   data[offset + 12] == 'f' && data[offset + 13] == 'm' &&
                   data[offset + 14] == 't' && data[offset + 15] == ' ' &&
                   data[offset + 36] == 'd' && data[offset + 37] == 'a' &&
                   data[offset + 38] == 't' && data[offset + 39] == 'a';
        }

        private void ParseWavHeader(bool bigEndian)
        {
            if (bigEndian)
            {
                _channels = ReadUInt16BE(_bytes, 22);
                _sampleRate = ReadInt32BE(_bytes, 24) & 0x7FFFFFFF;
                _bitsPerSample = ReadUInt16BE(_bytes, 34);
            }
            else
            {
                _channels = ReadUInt16LE(_bytes, 22);
                _sampleRate = ReadInt32LE(_bytes, 24) & 0x7FFFFFFF;
                _bitsPerSample = ReadUInt16LE(_bytes, 34);
            }

            if (_channels == 0) _channels = 1;
            if (_bitsPerSample < 8) _bitsPerSample = 8;
            else if (_bitsPerSample > 8) _bitsPerSample = 16;

            _signed = (_bitsPerSample == 8);

            int dataSize = bigEndian ? ReadInt32BE(_bytes, 40) : ReadInt32LE(_bytes, 40);
            dataSize &= 0x7FFFFFFF;

            bool needsPadding = (dataSize % 2 != 0);
            if (needsPadding) dataSize++;

            // Resize array if needed
            if (_bytes.Length != dataSize + 44)
            {
                Array.Resize(ref _bytes, dataSize + 44);
                if (needsPadding)
                {
                    _bytes[_bytes.Length - 1] = (byte)(_signed ? 0x80 : 0x00);
                }
            }

            // Swap bytes for 16-bit big-endian
            if (bigEndian && _bitsPerSample == 16)
            {
                for (int i = 44; i < _bytes.Length; i += 2)
                {
                    byte temp = _bytes[i];
                    _bytes[i] = _bytes[i + 1];
                    _bytes[i + 1] = temp;
                }
            }
        }

        private int ReadSample(int offset, int bytesPerSample)
        {
            int sample = 0;
            try
            {
                for (int b = bytesPerSample - 1; b >= 0; b--)
                {
                    sample <<= 8;
                    sample |= _bytes[offset + b];
                }
            }
            catch (IndexOutOfRangeException)
            {
                return 0;
            }
            return sample;
        }

        private void WriteSample(List<byte> output, int sample, int sourceBytesPerSample, 
                                 int targetBytesPerSample, bool sourceSigned, bool targetSigned)
        {
            // Adjust bit depth
            for (int shift = targetBytesPerSample; shift < sourceBytesPerSample; shift++)
                sample >>= 8;

            for (int shift = sourceBytesPerSample; shift < targetBytesPerSample; shift++)
            {
                sample <<= 8;
                if ((sample & 0x100) == 0x100)
                    sample |= 0xFF;
            }

            // Write bytes
            for (int b = 1; b <= targetBytesPerSample; b++)
            {
                int byteVal = sample & 0xFF;
                if (b == targetBytesPerSample && sourceSigned != targetSigned)
                    byteVal ^= 0x80;

                output.Add((byte)byteVal);
                sample >>= 8;
            }
        }

        private void WriteWavHeader(List<byte> output, int dataSize, int channels, 
                                   int sampleRate, int bitsPerSample, int bytesPerSample)
        {
            // RIFF header
            output.AddRange(new byte[] { (byte)'R', (byte)'I', (byte)'F', (byte)'F' });
            WriteInt32LE(output, dataSize);
            output.AddRange(new byte[] { (byte)'W', (byte)'A', (byte)'V', (byte)'E' });

            // fmt chunk
            output.AddRange(new byte[] { (byte)'f', (byte)'m', (byte)'t', (byte)' ' });
            WriteInt32LE(output, 16); // fmt chunk size
            WriteUInt16LE(output, 1); // PCM format
            WriteUInt16LE(output, (ushort)channels);
            WriteInt32LE(output, sampleRate);
            WriteInt32LE(output, sampleRate * channels * bytesPerSample); // byte rate
            WriteUInt16LE(output, (ushort)(channels * bytesPerSample)); // block align
            WriteUInt16LE(output, (ushort)bitsPerSample);

            // data chunk
            output.AddRange(new byte[] { (byte)'d', (byte)'a', (byte)'t', (byte)'a' });
            WriteInt32LE(output, Math.Max(dataSize - 36, 0));
        }

        private void UpdateWavHeaderSizes(List<byte> output)
        {
            int fileSize = output.Count - 8;
            output[4] = (byte)(fileSize & 0xFF);
            output[5] = (byte)((fileSize >> 8) & 0xFF);
            output[6] = (byte)((fileSize >> 16) & 0xFF);
            output[7] = (byte)((fileSize >> 24) & 0xFF);

            int dataSize = output.Count - 44;
            output[40] = (byte)(dataSize & 0xFF);
            output[41] = (byte)((dataSize >> 8) & 0xFF);
            output[42] = (byte)((dataSize >> 16) & 0xFF);
            output[43] = (byte)((dataSize >> 24) & 0xFF);
        }

        // Helper methods for reading/writing multi-byte values
        private static ushort ReadUInt16LE(byte[] data, int offset) =>
            (ushort)(data[offset] | (data[offset + 1] << 8));

        private static ushort ReadUInt16BE(byte[] data, int offset) =>
            (ushort)((data[offset] << 8) | data[offset + 1]);

        private static int ReadInt32LE(byte[] data, int offset) =>
            data[offset] | (data[offset + 1] << 8) | (data[offset + 2] << 16) | (data[offset + 3] << 24);

        private static int ReadInt32BE(byte[] data, int offset) =>
            (data[offset] << 24) | (data[offset + 1] << 16) | (data[offset + 2] << 8) | data[offset + 3];

        private static void WriteUInt16LE(List<byte> output, ushort value)
        {
            output.Add((byte)(value & 0xFF));
            output.Add((byte)((value >> 8) & 0xFF));
        }

        private static void WriteInt32LE(List<byte> output, int value)
        {
            output.Add((byte)(value & 0xFF));
            output.Add((byte)((value >> 8) & 0xFF));
            output.Add((byte)((value >> 16) & 0xFF));
            output.Add((byte)((value >> 24) & 0xFF));
        }
    }
}