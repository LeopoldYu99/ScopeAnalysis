using System;
using System.Collections.Generic;
using System.Windows.Media;
using Arction.Wpf.Charting;

namespace InteractiveExamples
{
    internal static class DecodeLogic
    {
        public static List<ProtocolSegment> BuildProtocolSegments(
            SeriesPoint[] history,
            int uartBaudRate,
            int uartDataBits,
            int uartStopBits)
        {
            List<ProtocolSegment> segments = new List<ProtocolSegment>();
            if (history == null || history.Length == 0)
            {
                return segments;
            }

            double bitDurationUs = 1000000.0 / uartBaudRate;

            bool[] sampleValues;
            double[] sampleTimes;
            CollapseToSamples(history, out sampleTimes, out sampleValues);
            if (sampleTimes.Length < 2)
            {
                return segments;
            }

            for (int sampleIndex = 1; sampleIndex < sampleTimes.Length; sampleIndex++)
            {
                if (sampleValues[sampleIndex - 1] == false || sampleValues[sampleIndex])
                {
                    continue;
                }

                double frameStartX = sampleTimes[sampleIndex];
                if (TryDecodeFrame(
                    sampleTimes,
                    sampleValues,
                    frameStartX,
                    bitDurationUs,
                    uartDataBits,
                    uartStopBits,
                    out byte decodedValue,
                    out double frameEndX) == false)
                {
                    continue;
                }

                AddUartSegments(
                    segments,
                    frameStartX,
                    frameEndX,
                    bitDurationUs,
                    uartDataBits,
                    uartStopBits,
                    decodedValue);
                // Resume scanning from the latter half of the stop bit so a back-to-back
                // frame transition at the stop/start boundary is still seen on the next pass.
                sampleIndex = FindLastSampleBefore(sampleTimes, frameEndX - 0.5 * bitDurationUs);
            }

            return segments;
        }

        private static void CollapseToSamples(SeriesPoint[] history, out double[] sampleTimes, out bool[] sampleValues)
        {
            const double epsilon = 1e-12;
            List<double> times = new List<double>(history.Length);
            List<bool> values = new List<bool>(history.Length);

            for (int i = 0; i < history.Length; i++)
            {
                double x = history[i].X;
                bool isHigh = history[i].Y >= 0.5f;
                if (times.Count == 0 || Math.Abs(times[times.Count - 1] - x) > epsilon)
                {
                    times.Add(x);
                    values.Add(isHigh);
                }
                else
                {
                    values[values.Count - 1] = isHigh;
                }
            }

            sampleTimes = times.ToArray();
            sampleValues = values.ToArray();
        }

        private static bool TryDecodeFrame(
            double[] sampleTimes,
            bool[] sampleValues,
            double frameStartX,
            double bitDurationUs,
            int uartDataBits,
            int uartStopBits,
            out byte decodedValue,
            out double frameEndX)
        {
            decodedValue = 0;
            frameEndX = frameStartX;

            if (SampleAt(sampleTimes, sampleValues, frameStartX + 0.5 * bitDurationUs, out bool startBitHigh) == false
                || startBitHigh)
            {
                return false;
            }

            for (int bitIndex = 0; bitIndex < uartDataBits; bitIndex++)
            {
                double sampleX = frameStartX + (1.5 + bitIndex) * bitDurationUs;
                if (SampleAt(sampleTimes, sampleValues, sampleX, out bool bitHigh) == false)
                {
                    return false;
                }

                if (bitHigh)
                {
                    decodedValue |= (byte)(1 << bitIndex);
                }
            }

            for (int stopIndex = 0; stopIndex < uartStopBits; stopIndex++)
            {
                double sampleX = frameStartX + (1.5 + uartDataBits + stopIndex) * bitDurationUs;
                if (SampleAt(sampleTimes, sampleValues, sampleX, out bool stopBitHigh) == false || stopBitHigh == false)
                {
                    return false;
                }
            }

            frameEndX = frameStartX + (1 + uartDataBits + uartStopBits) * bitDurationUs;
            return true;
        }

        private static bool SampleAt(double[] sampleTimes, bool[] sampleValues, double targetX, out bool value)
        {
            value = false;
            if (sampleTimes.Length == 0 || targetX < sampleTimes[0] || targetX > sampleTimes[sampleTimes.Length - 1])
            {
                return false;
            }

            int index = Array.BinarySearch(sampleTimes, targetX);
            if (index >= 0)
            {
                value = sampleValues[index];
                return true;
            }

            index = ~index;
            if (index <= 0)
            {
                value = sampleValues[0];
                return true;
            }

            if (index >= sampleTimes.Length)
            {
                value = sampleValues[sampleValues.Length - 1];
                return true;
            }

            double previousDistance = Math.Abs(targetX - sampleTimes[index - 1]);
            double nextDistance = Math.Abs(sampleTimes[index] - targetX);
            value = previousDistance <= nextDistance ? sampleValues[index - 1] : sampleValues[index];
            return true;
        }

        private static int FindLastSampleBefore(double[] sampleTimes, double targetX)
        {
            int index = Array.BinarySearch(sampleTimes, targetX);
            if (index >= 0)
            {
                return index;
            }

            index = ~index;
            return Math.Max(0, index - 1);
        }

        private static void AddUartSegments(
            List<ProtocolSegment> segments,
            double startX,
            double endX,
            double bitDurationUs,
            int uartDataBits,
            int uartStopBits,
            byte decodedValue)
        {
            double startBitEndX = startX + bitDurationUs;
            double dataEndX = startBitEndX + uartDataBits * bitDurationUs;
            double stopStartX = dataEndX;
            double stopEndX = stopStartX + uartStopBits * bitDurationUs;

            AddSegment(segments, startX, startBitEndX, "T", true);
            AddSegment(segments, startBitEndX, dataEndX, FormatLabel(decodedValue), false);
            AddSegment(segments, stopStartX, Math.Min(stopEndX, endX), "S", true);
        }

        private static void AddSegment(
            List<ProtocolSegment> segments,
            double startX,
            double endX,
            string label,
            bool isMarker)
        {
            if (endX <= startX)
            {
                return;
            }

            ProtocolSegment segment = new ProtocolSegment
            {
                StartX = startX,
                EndX = endX,
                IsMarker = isMarker,
                Label = label
            };

            if (isMarker)
            {
                segment.FillColor = Color.FromRgb(156, 231, 73);
                segment.BorderColor = Color.FromRgb(209, 255, 145);
                segment.ForegroundColor = Color.FromRgb(18, 38, 12);
            }
            else
            {
                segment.FillColor = Color.FromRgb(82, 142, 255);
                segment.BorderColor = Color.FromRgb(182, 214, 255);
                segment.ForegroundColor = Color.FromRgb(250, 252, 255);
            }

            segments.Add(segment);
        }

        private static string FormatLabel(byte value)
        {
            return value.ToString("X2");
        }
    }
}
