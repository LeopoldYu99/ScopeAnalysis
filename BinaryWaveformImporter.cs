using System;
using System.IO;

namespace InteractiveExamples
{
    internal sealed class BinaryWaveformImportResult
    {
        public string SignalName { get; set; }
        public uint[] DigitalWords { get; set; }
        public int SampleCount { get; set; }
        public double SampleInterval { get; set; }
    }

    internal static class BinaryWaveformImporter
    {
        public static BinaryWaveformImportResult ImportFile(string filePath, double sampleIntervalSeconds)
        {
            if (string.IsNullOrWhiteSpace(filePath) || File.Exists(filePath) == false)
            {
                return null;
            }
            byte[] bytes = File.ReadAllBytes(filePath);
            return ImportBytes(Path.GetFileNameWithoutExtension(filePath), bytes, sampleIntervalSeconds);
        }

        public static BinaryWaveformImportResult ImportBytes(string signalName, byte[] bytes, double sampleIntervalSeconds)
        {
            if (bytes == null || bytes.Length == 0)
            {
                return null;
            }

            int sampleCount = checked(bytes.Length * 8);
            return ImportBytes(signalName, bytes, sampleIntervalSeconds, sampleCount);
        }

        public static BinaryWaveformImportResult ImportBytes(string signalName, byte[] bytes, double sampleIntervalSeconds, int sampleCount)
        {
            if (bytes == null || bytes.Length == 0 || sampleCount <= 0)
            {
                return null;
            }

            int maximumSampleCount = checked(bytes.Length * 8);
            if (sampleCount > maximumSampleCount)
            {
                return null;
            }

            return new BinaryWaveformImportResult
            {
                SignalName = string.IsNullOrWhiteSpace(signalName) ? "Signal" : signalName,
                DigitalWords = PackDigitalWords(bytes, sampleCount),
                SampleCount = sampleCount,
                SampleInterval = sampleIntervalSeconds
            };
        }

        private static uint[] PackDigitalWords(byte[] bytes, int sampleCount)
        {
            uint[] digitalWords = new uint[(sampleCount + 31) / 32];
            for (int sampleIndex = 0; sampleIndex < sampleCount; sampleIndex++)
            {
                int byteIndex = sampleIndex / 8;
                int bitIndex = 7 - (sampleIndex % 8);
                if (((bytes[byteIndex] >> bitIndex) & 0x1) != 0)
                {
                    int wordIndex = sampleIndex / 32;
                    int bitOffset = sampleIndex % 32;
                    digitalWords[wordIndex] |= 1u << bitOffset;
                }
            }

            return digitalWords;
        }
    }
}
