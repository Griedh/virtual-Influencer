using System;
using System.Text;
using UnityEngine;

namespace VirtualInfluencer
{
    public static class AudioDecodeUtility
    {
        public static float[] DecodePcm16(byte[] pcm16Bytes)
        {
            var sampleCount = pcm16Bytes.Length / 2;
            var samples = new float[sampleCount];

            for (var index = 0; index < sampleCount; index++)
            {
                var low = pcm16Bytes[index * 2];
                var high = pcm16Bytes[index * 2 + 1];
                var sample = (short)(low | (high << 8));
                samples[index] = sample / 32768f;
            }

            return samples;
        }

        public static bool TryDecodeWavPcm16(byte[] wavBytes, out float[] samples, out int channels, out int sampleRateHz)
        {
            samples = Array.Empty<float>();
            channels = 1;
            sampleRateHz = 24000;

            if (wavBytes.Length < 44)
            {
                return false;
            }

            var riff = Encoding.ASCII.GetString(wavBytes, 0, 4);
            var wave = Encoding.ASCII.GetString(wavBytes, 8, 4);
            if (!string.Equals(riff, "RIFF", StringComparison.Ordinal) || !string.Equals(wave, "WAVE", StringComparison.Ordinal))
            {
                return false;
            }

            var cursor = 12;
            var bitsPerSample = 16;
            var dataOffset = -1;
            var dataLength = 0;

            while (cursor + 8 <= wavBytes.Length)
            {
                var chunkId = Encoding.ASCII.GetString(wavBytes, cursor, 4);
                var chunkSize = BitConverter.ToInt32(wavBytes, cursor + 4);
                var chunkDataOffset = cursor + 8;
                if (chunkDataOffset + chunkSize > wavBytes.Length)
                {
                    return false;
                }

                if (chunkId == "fmt ")
                {
                    var audioFormat = BitConverter.ToInt16(wavBytes, chunkDataOffset);
                    channels = BitConverter.ToInt16(wavBytes, chunkDataOffset + 2);
                    sampleRateHz = BitConverter.ToInt32(wavBytes, chunkDataOffset + 4);
                    bitsPerSample = BitConverter.ToInt16(wavBytes, chunkDataOffset + 14);

                    if (audioFormat != 1 || bitsPerSample != 16)
                    {
                        return false;
                    }
                }
                else if (chunkId == "data")
                {
                    dataOffset = chunkDataOffset;
                    dataLength = chunkSize;
                    break;
                }

                cursor = chunkDataOffset + chunkSize;
            }

            if (dataOffset < 0 || dataLength <= 0)
            {
                return false;
            }

            var data = new byte[dataLength];
            Buffer.BlockCopy(wavBytes, dataOffset, data, 0, dataLength);
            samples = DecodePcm16(data);
            return true;
        }
    }
}
