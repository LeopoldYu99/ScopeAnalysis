using Arction.Wpf.Charting;
using Arction.Wpf.Charting.SeriesXY;
using System;
using System.Collections.Generic;
using System.IO;

namespace InteractiveExamples
{
    internal sealed class BinaryWaveformImportResult
    {
        public string SignalName { get; set; }
        public SeriesPoint[] Points { get; set; }
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

            SeriesPoint[] points = ImportBinaryWaveformAsZeroOne(bytes, sampleIntervalSeconds);
            if (points == null || points.Length == 0)
            {
                return null;
            }

            return new BinaryWaveformImportResult
            {
                SignalName = string.IsNullOrWhiteSpace(signalName) ? "Signal" : signalName,
                Points = points
            };
        }

        private static SeriesPoint[] ImportBinaryWaveformAsZeroOne(byte[] bytes, double sampleIntervalSeconds)
        {
            if (bytes == null || bytes.Length == 0)
            {
                return null;
            }
            //List<SeriesPoint> aa = new List<SeriesPoint>(bytes.Length * 8 + 1);
            List<SeriesPoint> points = new List<SeriesPoint>(bytes.Length * 8 + 1);
            double time = 0;
            bool hasPreviousValue = false;
            float previousValue = 0;

            for (int byteIndex = 0; byteIndex < bytes.Length; byteIndex++)
            {
                byte valueByte = bytes[byteIndex];
                for (int bitIndex = 7; bitIndex >= 0; bitIndex--)
                {
                    float value = ((valueByte >> bitIndex) & 0x1) == 0x1 ? 1f : 0f;
                    if (hasPreviousValue == false)
                    {
                        points.Add(new SeriesPoint(time, value));
                        hasPreviousValue = true;
                    }
                    else
                    {
                        points.Add(new SeriesPoint(time, previousValue));
                        if (Math.Abs(value - previousValue) > float.Epsilon)
                        {
                            points.Add(new SeriesPoint(time, value));
                        }
                    }

                    previousValue = value;
                    time += sampleIntervalSeconds;
                }
            }

            if (hasPreviousValue)
            {
                points.Add(new SeriesPoint(time, previousValue));
            }

            return points.ToArray();
        }
    }
}
