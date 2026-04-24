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

            return new BinaryWaveformImportResult
            {
                SignalName = string.IsNullOrWhiteSpace(signalName) ? "Signal" : signalName,
                DigitalWords = PackDigitalWords(bytes),
                SampleCount = checked(bytes.Length * 8),
                SampleInterval = sampleIntervalSeconds
            };
        }

        private static uint[] PackDigitalWords(byte[] bytes)
        {
            int sampleCount = checked(bytes.Length * 8);
            uint[] digitalWords = new uint[(sampleCount + 31) / 32];
            int sampleIndex = 0;

            for (int byteIndex = 0; byteIndex < bytes.Length; byteIndex++)
            {
                byte valueByte = bytes[byteIndex];
                for (int bitIndex = 7; bitIndex >= 0; bitIndex--)
                {
                    if (((valueByte >> bitIndex) & 0x1) != 0)
                    {
                        int wordIndex = sampleIndex / 32;
                        int bitOffset = sampleIndex % 32;
                        digitalWords[wordIndex] |= 1u << bitOffset;
                    }

                    sampleIndex++;
                }
            }

            return digitalWords;
        }
    }
}
