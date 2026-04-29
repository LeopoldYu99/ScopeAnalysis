using System;
using System.Collections.Generic;
using System.Windows.Media;

namespace ScopeAnalysis
{
    internal static class DecodeLogic
    {
        public static List<ProtocolSegment> BuildProtocolSegments(
            uint[] digitalWords,
            int sampleCount,
            double sampleIntervalUs,
            int uartBaudRate,
            int uartDataBits,
            double uartStopBits,
            UartParityMode uartParityMode,
            int uartIdleBits,
            int samplesPerBit,
            int leadingIdleSamples)
        {
            List<ProtocolSegment> segments = new List<ProtocolSegment>();
            if (digitalWords == null || sampleCount <= 0 || sampleIntervalUs <= 0 || uartBaudRate <= 0 || uartDataBits <= 0 || uartStopBits <= 0)
            {
                return segments;
            }

            double bitDurationUs = samplesPerBit > 1
                ? sampleIntervalUs * samplesPerBit
                : 1000000.0 / uartBaudRate;
            double minimumIdleDurationUs = Math.Max(0, uartIdleBits) * bitDurationUs;
            double previousFrameEndX = double.NaN;
            int scanStartSample = 1;
            if (TryDecodeLeadingFrame(
                digitalWords,
                sampleCount,
                sampleIntervalUs,
                bitDurationUs,
                uartDataBits,
                uartStopBits,
                uartParityMode,
                minimumIdleDurationUs,
                leadingIdleSamples,
                out byte leadingDecodedValue,
                out double leadingFrameEndX))
            {
                AddUartSegments(
                    segments,
                    0,
                    leadingFrameEndX,
                    bitDurationUs,
                    uartDataBits,
                    uartStopBits,
                    uartParityMode,
                    leadingDecodedValue);
                previousFrameEndX = leadingFrameEndX;
                scanStartSample = Math.Max(1, FindLastSampleBefore(sampleCount, sampleIntervalUs, leadingFrameEndX - 0.5 * bitDurationUs) + 1);
            }

            for (int sampleIndex = scanStartSample; sampleIndex < sampleCount; sampleIndex++)
            {
                bool previousHigh = GetSampleValue(digitalWords, sampleCount, sampleIndex - 1);
                bool currentHigh = GetSampleValue(digitalWords, sampleCount, sampleIndex);
                if (previousHigh == false || currentHigh)
                {
                    continue;
                }

                double frameStartX = sampleIndex * sampleIntervalUs;
                if (HasRequiredIdleBeforeStart(digitalWords, sampleCount, sampleIndex - 1, frameStartX, minimumIdleDurationUs, sampleIntervalUs) == false)
                {
                    continue;
                }

                if (TryDecodeFrame(
                    digitalWords,
                    sampleCount,
                    sampleIntervalUs,
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
                sampleIndex = FindLastSampleBefore(sampleCount, sampleIntervalUs, frameEndX - 0.5 * bitDurationUs);
            }

            return segments;
        }

        private static bool TryDecodeLeadingFrame(
            uint[] digitalWords,
            int sampleCount,
            double sampleIntervalUs,
            double bitDurationUs,
            int uartDataBits,
            double uartStopBits,
            UartParityMode uartParityMode,
            double minimumIdleDurationUs,
            int leadingIdleSamples,
            out byte decodedValue,
            out double frameEndX)
        {
            decodedValue = 0;
            frameEndX = 0;
            if (leadingIdleSamples <= 0 || GetSampleValue(digitalWords, sampleCount, 0))
            {
                return false;
            }

            double actualIdleDurationUs = leadingIdleSamples * sampleIntervalUs;
            if (sampleIntervalUs <= 0)
            {
                if (actualIdleDurationUs + 1e-9 < minimumIdleDurationUs)
                {
                    return false;
                }
            }
            else
            {
                double quantizedRequiredIdleDurationUs = Math.Round(minimumIdleDurationUs / sampleIntervalUs) * sampleIntervalUs;
                if (actualIdleDurationUs + 1e-9 < quantizedRequiredIdleDurationUs)
                {
                    return false;
                }
            }

            return TryDecodeFrame(
                digitalWords,
                sampleCount,
                sampleIntervalUs,
                0,
                bitDurationUs,
                uartDataBits,
                uartStopBits,
                uartParityMode,
                out decodedValue,
                out frameEndX);
        }

        public static List<ProtocolSegment> BuildFixedWidthSegments(
            uint[] digitalWords,
            int sampleCount,
            double sampleInterval,
            int bitsPerSegment,
            int emptyDataRunSegmentThreshold,
            int samplesPerBit,
            ProtocolBitOrder bitOrder)
        {
            List<ProtocolSegment> segments = new List<ProtocolSegment>();
            if (digitalWords == null || sampleCount <= 0 || sampleInterval <= 0 || bitsPerSegment <= 0 || bitsPerSegment > 8)
            {
                return segments;
            }

            int normalizedSamplesPerBit = Math.Max(1, samplesPerBit);
            int samplesPerSegment = bitsPerSegment * normalizedSamplesPerBit;
            int minimumEmptyRunSamples = Math.Max(1, emptyDataRunSegmentThreshold) * samplesPerSegment;
            int decodeStartSample = 0;
            int runStartSample = 0;
            bool currentValue = GetSampleValue(digitalWords, sampleCount, 0);

            for (int sampleIndex = 1; sampleIndex <= sampleCount; sampleIndex++)
            {
                bool isRunEnd = sampleIndex == sampleCount;
                if (isRunEnd == false)
                {
                    bool nextValue = GetSampleValue(digitalWords, sampleCount, sampleIndex);
                    if (nextValue == currentValue)
                    {
                        continue;
                    }
                }

                int runLength = sampleIndex - runStartSample;
                if (runLength >= minimumEmptyRunSamples)
                {
                    int decodeEndSample = AlignDownToMultiple(runStartSample, samplesPerSegment);
                    int resumeSample = AlignUpToMultiple(sampleIndex, samplesPerSegment) - samplesPerSegment;
                    if (resumeSample < 0)
                    {
                        resumeSample = 0;
                    }

                    AddFixedWidthSegmentsForRange(
                        segments,
                        digitalWords,
                        sampleCount,
                        sampleInterval,
                        bitsPerSegment,
                        normalizedSamplesPerBit,
                        bitOrder,
                        decodeStartSample,
                        decodeEndSample);

                    decodeStartSample = Math.Max(decodeEndSample, resumeSample);
                }

                if (sampleIndex < sampleCount)
                {
                    runStartSample = sampleIndex;
                    currentValue = GetSampleValue(digitalWords, sampleCount, sampleIndex);
                }
            }

            AddFixedWidthSegmentsForRange(
                segments,
                digitalWords,
                sampleCount,
                sampleInterval,
                bitsPerSegment,
                normalizedSamplesPerBit,
                bitOrder,
                decodeStartSample,
                sampleCount);
            return segments;
        }

        private static bool TryDecodeFrame(
            uint[] digitalWords,
            int sampleCount,
            double sampleIntervalUs,
            double frameStartX,
            double bitDurationUs,
            int uartDataBits,
            double uartStopBits,
            UartParityMode uartParityMode, out byte decodedValue, out double frameEndX)
        {
            decodedValue = 0;
            frameEndX = frameStartX;
            int parityBitCount = uartParityMode == UartParityMode.None ? 0 : 1;

            if (SampleAt(digitalWords, sampleCount, sampleIntervalUs, frameStartX + 0.5 * bitDurationUs, out bool startBitHigh) == false
                || startBitHigh)
            {
                return false;
            }

            for (int bitIndex = 0; bitIndex < uartDataBits; bitIndex++)
            {
                double sampleX = frameStartX + (1.5 + bitIndex) * bitDurationUs;
                if (SampleAt(digitalWords, sampleCount, sampleIntervalUs, sampleX, out bool bitHigh) == false)
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
                if (SampleAt(digitalWords, sampleCount, sampleIntervalUs, paritySampleX, out bool parityBitHigh) == false)
                {
                    return false;
                }

                if (parityBitHigh != CalculateParityBit(decodedValue, uartDataBits, uartParityMode))
                {
                    return false;
                }
            }

            if (ValidateStopBits(digitalWords, sampleCount, sampleIntervalUs, frameStartX, bitDurationUs, uartDataBits, parityBitCount, uartStopBits) == false)
            {
                return false;
            }

            frameEndX = frameStartX + (1 + uartDataBits + parityBitCount + uartStopBits) * bitDurationUs;
            return true;
        }

        private static bool SampleAt(uint[] digitalWords, int sampleCount, double sampleInterval, double targetX, out bool value)
        {
            value = false;
            if (digitalWords == null || sampleCount <= 0 || sampleInterval <= 0 || targetX < 0)
            {
                return false;
            }

            double totalDuration = sampleCount * sampleInterval;
            if (targetX > totalDuration)
            {
                return false;
            }

            double scaledIndex = targetX / sampleInterval;
            int lowerBoundaryIndex = (int)Math.Floor(scaledIndex);
            if (lowerBoundaryIndex < 0)
            {
                lowerBoundaryIndex = 0;
            }

            if (lowerBoundaryIndex >= sampleCount)
            {
                lowerBoundaryIndex = sampleCount;
            }

            double lowerBoundaryX = lowerBoundaryIndex * sampleInterval;
            double upperBoundaryX = Math.Min(totalDuration, (lowerBoundaryIndex + 1) * sampleInterval);
            int boundaryIndex = targetX - lowerBoundaryX <= upperBoundaryX - targetX
                ? lowerBoundaryIndex
                : Math.Min(sampleCount, lowerBoundaryIndex + 1);
            value = GetBoundaryValue(digitalWords, sampleCount, boundaryIndex);
            return true;
        }

        private static int FindLastSampleBefore(int sampleCount, double sampleInterval, double targetX)
        {
            if (sampleCount <= 0 || sampleInterval <= 0)
            {
                return 0;
            }

            if (targetX <= 0)
            {
                return 0;
            }

            return Math.Max(0, Math.Min(sampleCount - 1, (int)Math.Floor(targetX / sampleInterval)));
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

            AddSegment(segments, startX, startBitEndX, "T", true);
            AddSegment(segments, startBitEndX, dataEndX, FormatLabel(decodedValue), false, BuildBitLabels(decodedValue, uartDataBits));
            if (uartParityMode != UartParityMode.None)
            {
                double parityEndX = stopStartX + bitDurationUs;
                AddSegment(segments, stopStartX, parityEndX, "P", true);
                stopStartX = parityEndX;
            }

            double stopEndX = stopStartX + uartStopBits * bitDurationUs;
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
            uint[] digitalWords,
            int sampleCount,
            int highSampleIndex,
            double frameStartX,
            double minimumIdleDurationUs,
            double sampleIntervalUs)
        {
            if (minimumIdleDurationUs <= 0)
            {
                return true;
            }

            if (highSampleIndex < 0 || highSampleIndex >= sampleCount || GetSampleValue(digitalWords, sampleCount, highSampleIndex) == false)
            {
                return false;
            }

            int runStartIndex = highSampleIndex;
            while (runStartIndex > 0 && GetSampleValue(digitalWords, sampleCount, runStartIndex - 1))
            {
                runStartIndex--;
            }

            double idleStartX = runStartIndex * sampleIntervalUs;
            double actualIdleDurationUs = frameStartX - idleStartX;
            if (sampleIntervalUs <= 0)
            {
                return actualIdleDurationUs + 1e-9 >= minimumIdleDurationUs;
            }

            double quantizedRequiredIdleDurationUs = Math.Round(minimumIdleDurationUs / sampleIntervalUs) * sampleIntervalUs;
            return actualIdleDurationUs + 1e-9 >= quantizedRequiredIdleDurationUs;
        }

        private static bool ValidateStopBits(
            uint[] digitalWords,
            int sampleCount,
            double sampleIntervalUs,
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
                if (SampleAt(digitalWords, sampleCount, sampleIntervalUs, sampleX, out bool stopBitHigh) == false || stopBitHigh == false)
                {
                    return false;
                }
            }

            double fractionalStopBits = uartStopBits - wholeStopBits;
            if (fractionalStopBits > 1e-9)
            {
                double sampleX = frameStartX + (stopStartBitOffset + wholeStopBits + fractionalStopBits / 2.0) * bitDurationUs;
                if (SampleAt(digitalWords, sampleCount, sampleIntervalUs, sampleX, out bool stopBitHigh) == false || stopBitHigh == false)
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

        private static bool GetBoundaryValue(uint[] digitalWords, int sampleCount, int boundaryIndex)
        {
            if (sampleCount <= 0)
            {
                return false;
            }

            int sampleIndex = boundaryIndex >= sampleCount ? sampleCount - 1 : boundaryIndex;
            return GetSampleValue(digitalWords, sampleCount, sampleIndex);
        }

        private static bool GetSampleValue(uint[] digitalWords, int sampleCount, int sampleIndex)
        {
            if (digitalWords == null || sampleIndex < 0 || sampleIndex >= sampleCount)
            {
                return false;
            }

            int wordIndex = sampleIndex / 32;
            int bitOffset = sampleIndex % 32;
            return (digitalWords[wordIndex] & (1u << bitOffset)) != 0;
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

        private static void AddFixedWidthSegmentsForRange(
            List<ProtocolSegment> segments,
            uint[] digitalWords,
            int sampleCount,
            double sampleInterval,
            int bitsPerSegment,
            int samplesPerBit,
            ProtocolBitOrder bitOrder,
            int startSampleInclusive,
            int endSampleExclusive)
        {
            if (segments == null
                || digitalWords == null
                || sampleCount <= 0
                || sampleInterval <= 0
                || bitsPerSegment <= 0
                || samplesPerBit <= 0
                || startSampleInclusive >= endSampleExclusive)
            {
                return;
            }

            int samplesPerSegment = bitsPerSegment * samplesPerBit;
            int alignedStartSample = AlignUpToMultiple(startSampleInclusive, samplesPerSegment);
            int alignedEndSample = AlignDownToMultiple(endSampleExclusive, samplesPerSegment);
            for (int segmentStartSample = alignedStartSample;
                segmentStartSample + samplesPerSegment <= alignedEndSample;
                segmentStartSample += samplesPerSegment)
            {
                byte decodedValue = 0;
                string[] bitLabels = new string[bitsPerSegment];
                for (int bitIndex = 0; bitIndex < bitsPerSegment; bitIndex++)
                {
                    int bitSampleIndex = segmentStartSample + (bitIndex * samplesPerBit) + (samplesPerBit / 2);
                    bool bitHigh = GetSampleValue(digitalWords, sampleCount, bitSampleIndex);
                    if (bitHigh)
                    {
                        int decodedBitIndex = bitOrder == ProtocolBitOrder.LittleEndian
                            ? bitIndex
                            : bitsPerSegment - bitIndex - 1;
                        decodedValue |= (byte)(1 << decodedBitIndex);
                    }

                    bitLabels[bitIndex] = bitHigh ? "1" : "0";
                }

                AddSegment(
                    segments,
                    segmentStartSample * sampleInterval,
                    (segmentStartSample + samplesPerSegment) * sampleInterval,
                    FormatLabel(decodedValue),
                    false,
                    bitLabels);
            }
        }

        private static int AlignUpToMultiple(int value, int multiple)
        {
            if (multiple <= 0)
            {
                return value;
            }

            int remainder = value % multiple;
            return remainder == 0 ? value : value + (multiple - remainder);
        }

        private static int AlignDownToMultiple(int value, int multiple)
        {
            if (multiple <= 0)
            {
                return value;
            }

            return value - (value % multiple);
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
                OriginalStartX = startX,
                OriginalEndX = endX,
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

