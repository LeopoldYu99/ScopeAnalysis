using System;
using System.Collections.Generic;
using System.Windows.Media;
using Arction.Wpf.Charting;

namespace InteractiveExamples
{
    internal static class DecodeLogic
    {
        private struct FixedWidthSegmentCandidate
        {
            public double StartX;
            public double EndX;
            public byte DecodedValue;
            public string[] BitLabels;
            public bool IsUniform;
            public bool UniformHigh;
        }

        public static List<ProtocolSegment> BuildProtocolSegments(
            SeriesPoint[] history,
            int uartBaudRate,
            int uartDataBits,
            double uartStopBits,
            UartParityMode uartParityMode,
            int uartIdleBits)
        {
            List<ProtocolSegment> segments = new List<ProtocolSegment>();
            if (history == null || history.Length == 0 || uartBaudRate <= 0 || uartDataBits <= 0 || uartStopBits <= 0)
            {
                return segments;
            }

            double bitDurationUs = 1000000.0 / uartBaudRate;
            double minimumIdleDurationUs = Math.Max(0, uartIdleBits) * bitDurationUs;

            bool[] sampleValues;
            double[] sampleTimes;
            CollapseToSamples(history, out sampleTimes, out sampleValues);
            if (sampleTimes.Length < 2)
            {
                return segments;
            }

            double sampleIntervalUs = EstimateSampleInterval(sampleTimes);
            double previousFrameEndX = double.NaN;
            for (int sampleIndex = 1; sampleIndex < sampleTimes.Length; sampleIndex++)
            {
                if (sampleValues[sampleIndex - 1] == false || sampleValues[sampleIndex])
                {
                    continue;
                }

                double frameStartX = sampleTimes[sampleIndex];
                if (HasRequiredIdleBeforeStart(sampleTimes, sampleValues, sampleIndex - 1, frameStartX, minimumIdleDurationUs, sampleIntervalUs) == false)
                {
                    continue;
                }

                if (TryDecodeFrame(
                    sampleTimes,
                    sampleValues,
                    frameStartX,
                    bitDurationUs,
                    uartDataBits,
                    uartStopBits,
                    uartParityMode,
                    out byte decodedValue,
                    out double frameEndX) == false)
                {
                    continue;
                }

                AddIdleSegment(segments, previousFrameEndX, frameStartX, sampleIntervalUs);
                AddUartSegments(
                    segments,
                    frameStartX,
                    frameEndX,
                    bitDurationUs,
                    uartDataBits,
                    uartStopBits,
                    uartParityMode,
                    decodedValue);
                previousFrameEndX = frameEndX;
                // Resume scanning from the latter half of the stop bit so a back-to-back
                // frame transition at the stop/start boundary is still seen on the next pass.
                sampleIndex = FindLastSampleBefore(sampleTimes, frameEndX - 0.5 * bitDurationUs);
            }

            return segments;
        }

        public static List<ProtocolSegment> BuildFixedWidthSegments(
            SeriesPoint[] history,
            int bitsPerSegment,
            int emptyDataRunSegmentThreshold)
        {
            List<ProtocolSegment> segments = new List<ProtocolSegment>();
            if (history == null || history.Length == 0 || bitsPerSegment <= 0 || bitsPerSegment > 8)
            {
                return segments;
            }

            bool[] sampleValues;
            double[] sampleTimes;
            CollapseToSamples(history, out sampleTimes, out sampleValues);
            if (sampleTimes.Length < 2)
            {
                return segments;
            }

            double sampleInterval = EstimateSampleInterval(sampleTimes);
            if (sampleInterval <= 0)
            {
                return segments;
            }

            double startX = sampleTimes[0];
            double endX = sampleTimes[sampleTimes.Length - 1];
            int bitCount = (int)Math.Round((endX - startX) / sampleInterval, MidpointRounding.AwayFromZero);
            int segmentCount = bitCount / bitsPerSegment;
            List<FixedWidthSegmentCandidate> candidates = new List<FixedWidthSegmentCandidate>(segmentCount);
            for (int segmentIndex = 0; segmentIndex < segmentCount; segmentIndex++)
            {
                double segmentStartX = startX + segmentIndex * bitsPerSegment * sampleInterval;
                byte decodedValue = 0;
                string[] bitLabels = new string[bitsPerSegment];
                bool hasCompleteSegment = true;
                bool? firstBitHigh = null;
                bool isUniform = true;

                for (int bitIndex = 0; bitIndex < bitsPerSegment; bitIndex++)
                {
                    double sampleX = segmentStartX + (bitIndex + 0.5) * sampleInterval;
                    if (SampleAt(sampleTimes, sampleValues, sampleX, out bool bitHigh) == false)
                    {
                        hasCompleteSegment = false;
                        break;
                    }

                    if (bitHigh)
                    {
                        decodedValue |= (byte)(1 << (bitsPerSegment - bitIndex - 1));
                    }

                    if (firstBitHigh.HasValue == false)
                    {
                        firstBitHigh = bitHigh;
                    }
                    else if (firstBitHigh.Value != bitHigh)
                    {
                        isUniform = false;
                    }

                    bitLabels[bitIndex] = bitHigh ? "1" : "0";
                }

                if (hasCompleteSegment == false)
                {
                    break;
                }

                candidates.Add(new FixedWidthSegmentCandidate
                {
                    StartX = segmentStartX,
                    EndX = segmentStartX + bitsPerSegment * sampleInterval,
                    DecodedValue = decodedValue,
                    BitLabels = bitLabels,
                    IsUniform = isUniform,
                    UniformHigh = firstBitHigh.HasValue && firstBitHigh.Value
                });
            }

            AddFilteredFixedWidthSegments(segments, candidates, emptyDataRunSegmentThreshold);
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

        private static bool TryDecodeFrame(  double[] sampleTimes, bool[] sampleValues, double frameStartX, double bitDurationUs, int uartDataBits, double uartStopBits,  
            UartParityMode uartParityMode, out byte decodedValue, out double frameEndX)
        {
            decodedValue = 0;
            frameEndX = frameStartX;
            int parityBitCount = uartParityMode == UartParityMode.None ? 0 : 1;

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

            if (parityBitCount > 0)
            {
                double paritySampleX = frameStartX + (1.5 + uartDataBits) * bitDurationUs;
                if (SampleAt(sampleTimes, sampleValues, paritySampleX, out bool parityBitHigh) == false)
                {
                    return false;
                }

                if (parityBitHigh != CalculateParityBit(decodedValue, uartDataBits, uartParityMode))
                {
                    return false;
                }
            }

            if (ValidateStopBits(sampleTimes, sampleValues, frameStartX, bitDurationUs, uartDataBits, parityBitCount, uartStopBits) == false)
            {
                return false;
            }

            frameEndX = frameStartX + (1 + uartDataBits + parityBitCount + uartStopBits) * bitDurationUs;
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
            double uartStopBits,
            UartParityMode uartParityMode,
            byte decodedValue)
        {
            double startBitEndX = startX + bitDurationUs;
            double dataEndX = startBitEndX + uartDataBits * bitDurationUs;
            double stopStartX = dataEndX;
            if (uartParityMode != UartParityMode.None)
            {
                double parityEndX = stopStartX + bitDurationUs;
                AddSegment(segments, stopStartX, parityEndX, "P", true);
                stopStartX = parityEndX;
            }

            double stopEndX = stopStartX + uartStopBits * bitDurationUs;

            AddSegment(segments, startX, startBitEndX, "T", true);
            AddSegment(segments, startBitEndX, dataEndX, FormatLabel(decodedValue), false, BuildBitLabels(decodedValue, uartDataBits));
            AddSegment(segments, stopStartX, Math.Min(stopEndX, endX), "S", true);
        }

        private static void AddIdleSegment(List<ProtocolSegment> segments, double previousFrameEndX, double nextFrameStartX, double sampleIntervalUs)
        {
            if (double.IsNaN(previousFrameEndX) || nextFrameStartX <= previousFrameEndX)
            {
                return;
            }

            double minimumGapUs = sampleIntervalUs > 0 ? sampleIntervalUs * 0.5 : 1e-9;
            if (nextFrameStartX - previousFrameEndX < minimumGapUs)
            {
                return;
            }

            AddSegment(segments, previousFrameEndX, nextFrameStartX, "I", true);
        }

        private static bool HasRequiredIdleBeforeStart(
            double[] sampleTimes,
            bool[] sampleValues,
            int highSampleIndex,
            double frameStartX,
            double minimumIdleDurationUs,
            double sampleIntervalUs)
        {
            if (minimumIdleDurationUs <= 0)
            {
                return true;
            }

            if (highSampleIndex < 0 || highSampleIndex >= sampleTimes.Length || sampleValues[highSampleIndex] == false)
            {
                return false;
            }

            int runStartIndex = highSampleIndex;
            while (runStartIndex > 0 && sampleValues[runStartIndex - 1])
            {
                runStartIndex--;
            }

            double idleStartX = sampleTimes[runStartIndex];
            double actualIdleDurationUs = frameStartX - idleStartX;
            if (sampleIntervalUs <= 0)
            {
                return actualIdleDurationUs + 1e-9 >= minimumIdleDurationUs;
            }

            double quantizedRequiredIdleDurationUs = Math.Round(minimumIdleDurationUs / sampleIntervalUs) * sampleIntervalUs;
            return actualIdleDurationUs + 1e-9 >= quantizedRequiredIdleDurationUs;
        }

        private static bool ValidateStopBits(
            double[] sampleTimes,
            bool[] sampleValues,
            double frameStartX,
            double bitDurationUs,
            int uartDataBits,
            int parityBitCount,
            double uartStopBits)
        {
            double stopStartBitOffset = 1 + uartDataBits + parityBitCount;
            int wholeStopBits = (int)Math.Floor(uartStopBits);
            for (int stopIndex = 0; stopIndex < wholeStopBits; stopIndex++)
            {
                double sampleX = frameStartX + (stopStartBitOffset + stopIndex + 0.5) * bitDurationUs;
                if (SampleAt(sampleTimes, sampleValues, sampleX, out bool stopBitHigh) == false || stopBitHigh == false)
                {
                    return false;
                }
            }

            double fractionalStopBits = uartStopBits - wholeStopBits;
            if (fractionalStopBits > 1e-9)
            {
                double sampleX = frameStartX + (stopStartBitOffset + wholeStopBits + fractionalStopBits / 2.0) * bitDurationUs;
                if (SampleAt(sampleTimes, sampleValues, sampleX, out bool stopBitHigh) == false || stopBitHigh == false)
                {
                    return false;
                }
            }

            return true;
        }

        private static bool CalculateParityBit(byte decodedValue, int uartDataBits, UartParityMode uartParityMode)
        {
            switch (uartParityMode)
            {
                case UartParityMode.Odd:
                    return (CountSetBits(decodedValue, uartDataBits) & 0x1) == 0;
                case UartParityMode.Even:
                    return (CountSetBits(decodedValue, uartDataBits) & 0x1) != 0;
                case UartParityMode.Mark:
                    return true;
                case UartParityMode.Space:
                    return false;
                default:
                    return false;
            }
        }

        private static int CountSetBits(byte value, int bitCount)
        {
            int ones = 0;
            for (int bitIndex = 0; bitIndex < bitCount; bitIndex++)
            {
                if (((value >> bitIndex) & 0x1) != 0)
                {
                    ones++;
                }
            }

            return ones;
        }

        private static double EstimateSampleInterval(double[] sampleTimes)
        {
            double minimumDelta = double.MaxValue;
            for (int i = 1; i < sampleTimes.Length; i++)
            {
                double delta = sampleTimes[i] - sampleTimes[i - 1];
                if (delta > 0 && delta < minimumDelta)
                {
                    minimumDelta = delta;
                }
            }

            return minimumDelta == double.MaxValue ? 0 : minimumDelta;
        }

        private static string[] BuildBitLabels(byte value, int bitCount)
        {
            if (bitCount <= 0)
            {
                return null;
            }

            string[] labels = new string[bitCount];
            for (int bitIndex = 0; bitIndex < bitCount; bitIndex++)
            {
                labels[bitIndex] = ((value >> bitIndex) & 0x1) != 0 ? "1" : "0";
            }

            return labels;
        }

        private static void AddFilteredFixedWidthSegments(
            List<ProtocolSegment> segments,
            List<FixedWidthSegmentCandidate> candidates,
            int emptyDataRunSegmentThreshold)
        {
            if (segments == null || candidates == null || candidates.Count == 0)
            {
                return;
            }

            int minimumEmptyRunSegments = Math.Max(1, emptyDataRunSegmentThreshold);

            int segmentIndex = 0;
            while (segmentIndex < candidates.Count)
            {
                FixedWidthSegmentCandidate candidate = candidates[segmentIndex];
                if (candidate.IsUniform == false)
                {
                    AddFixedWidthSegment(segments, candidate);
                    segmentIndex++;
                    continue;
                }

                int runEndIndex = segmentIndex;
                while (runEndIndex + 1 < candidates.Count
                    && candidates[runEndIndex + 1].IsUniform
                    && candidates[runEndIndex + 1].UniformHigh == candidate.UniformHigh)
                {
                    runEndIndex++;
                }

                int runLength = runEndIndex - segmentIndex + 1;
                if (runLength >= minimumEmptyRunSegments)
                {
                    segmentIndex = runEndIndex + 1;
                    continue;
                }

                for (int index = segmentIndex; index <= runEndIndex; index++)
                {
                    AddFixedWidthSegment(segments, candidates[index]);
                }

                segmentIndex = runEndIndex + 1;
            }
        }

        private static void AddFixedWidthSegment(List<ProtocolSegment> segments, FixedWidthSegmentCandidate candidate)
        {
            AddSegment(
                segments,
                candidate.StartX,
                candidate.EndX,
                FormatLabel(candidate.DecodedValue),
                false,
                candidate.BitLabels);
        }

        private static void AddSegment(
            List<ProtocolSegment> segments,
            double startX,
            double endX,
            string label,
            bool isMarker,
            string[] bitLabels = null)
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
                Label = label,
                BitLabels = bitLabels
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
