using System;
using System.Collections.Generic;
using System.Windows.Media;
using Arction.Wpf.Charting;

namespace InteractiveExamples
{
    internal static class DecodeLogic
    {
        public static List<ProtocolSegment> BuildProtocolSegments(
            ChartSignal signal,
            double visibleMin,
            double visibleMax,
            double currentSampleIntervalSeconds,
            double importedSampleRate,
            double decodeHighRatioThreshold)
        {
            List<ProtocolSegment> segments = new List<ProtocolSegment>();
            if (signal == null || signal.Kind == SignalValueKind.Analog)
            {
                return segments;
            }

            double paddingSeconds = Math.Max(1.0, currentSampleIntervalSeconds * 4.0);
            SeriesPoint[] history = signal.GetPointsSnapshot(visibleMin, visibleMax, paddingSeconds);
            if (history == null || history.Length == 0)
            {
                return segments;
            }

            const double epsilon = 1e-9;
            int firstBucket = (int)Math.Floor(visibleMin);
            int lastBucketExclusive = Math.Max(firstBucket + 1, (int)Math.Ceiling(visibleMax));
            int historyIndex = 0;
            bool hasOpenSegment = false;
            bool currentHigh = false;
            double segmentStart = 0;
            double segmentEnd = 0;

            for (int bucket = firstBucket; bucket < lastBucketExclusive; bucket++)
            {
                double bucketStart = bucket;
                double bucketEnd = bucket + 1.0;

                while (historyIndex < history.Length && history[historyIndex].X <= bucketStart)
                {
                    historyIndex++;
                }

                bool bucketHasValue = false;
                bool bucketHigh = false;
                int sampleCount = 0;
                int highSampleCount = 0;
                double currentSampleX = double.NaN;
                bool currentSampleHigh = false;
                int scanIndex = historyIndex;
                while (scanIndex < history.Length && history[scanIndex].X < bucketEnd)
                {
                    double sampleX = history[scanIndex].X;
                    bool sampleHigh = history[scanIndex].Y >= 0.5f;
                    if (double.IsNaN(currentSampleX) || Math.Abs(sampleX - currentSampleX) > epsilon)
                    {
                        if (double.IsNaN(currentSampleX) == false)
                        {
                            sampleCount++;
                            if (currentSampleHigh)
                            {
                                highSampleCount++;
                            }
                        }

                        currentSampleX = sampleX;
                        currentSampleHigh = sampleHigh;
                    }
                    else
                    {
                        currentSampleHigh = sampleHigh;
                    }

                    scanIndex++;
                }

                if (double.IsNaN(currentSampleX) == false)
                {
                    sampleCount++;
                    if (currentSampleHigh)
                    {
                        highSampleCount++;
                    }
                }

                historyIndex = scanIndex;
                bucketHasValue = sampleCount > 0;
                bucketHigh = bucketHasValue
                    && ((double)highSampleCount / Math.Max(sampleCount, importedSampleRate)) > decodeHighRatioThreshold;

                if (bucketHasValue == false)
                {
                    if (hasOpenSegment)
                    {
                        AddProtocolSegment(segments, segmentStart, segmentEnd, currentHigh, visibleMin, visibleMax, currentSampleIntervalSeconds);
                        hasOpenSegment = false;
                    }

                    continue;
                }

                if (hasOpenSegment == false)
                {
                    hasOpenSegment = true;
                    currentHigh = bucketHigh;
                    segmentStart = bucketStart;
                    segmentEnd = bucketEnd;
                    continue;
                }

                if (bucketHigh != currentHigh)
                {
                    AddProtocolSegment(segments, segmentStart, segmentEnd, currentHigh, visibleMin, visibleMax, currentSampleIntervalSeconds);
                    currentHigh = bucketHigh;
                    segmentStart = bucketStart;
                }

                segmentEnd = bucketEnd;
            }

            if (hasOpenSegment)
            {
                AddProtocolSegment(segments, segmentStart, segmentEnd, currentHigh, visibleMin, visibleMax, currentSampleIntervalSeconds);
            }

            return segments;
        }

        private static void AddProtocolSegment(
            List<ProtocolSegment> segments,
            double startX,
            double endX,
            bool isHigh,
            double visibleMin,
            double visibleMax,
            double currentSampleIntervalSeconds)
        {
            if (endX <= startX || endX < visibleMin || startX > visibleMax)
            {
                return;
            }

            double clippedStart = Math.Max(startX, visibleMin);
            double clippedEnd = Math.Min(endX, visibleMax);
            if (clippedEnd <= clippedStart)
            {
                return;
            }

            double durationMs = (endX - startX) * 1000.0;
            bool isMarker = durationMs <= currentSampleIntervalSeconds * 1000.0 * 3.5;

            ProtocolSegment segment = new ProtocolSegment
            {
                StartX = clippedStart,
                EndX = clippedEnd,
                IsMarker = isMarker,
                Label = isMarker ? (isHigh ? "T" : "S") : FormatProtocolDuration(durationMs)
            };

            if (isMarker)
            {
                segment.FillColor = isHigh ? Color.FromRgb(156, 231, 73) : Color.FromRgb(121, 205, 75);
                segment.BorderColor = isHigh ? Color.FromRgb(209, 255, 145) : Color.FromRgb(180, 240, 145);
                segment.ForegroundColor = Color.FromRgb(18, 38, 12);
            }
            else
            {
                segment.FillColor = isHigh ? Color.FromRgb(92, 214, 160) : Color.FromRgb(65, 174, 124);
                segment.BorderColor = isHigh ? Color.FromRgb(165, 245, 209) : Color.FromRgb(129, 223, 183);
                segment.ForegroundColor = Color.FromRgb(12, 34, 24);
            }

            segments.Add(segment);
        }

        private static string FormatProtocolDuration(double durationMs)
        {
            if (durationMs >= 1000.0)
            {
                return (durationMs / 1000.0).ToString("0.0");
            }

            return Math.Max(1.0, Math.Round(durationMs)).ToString("0");
        }
    }
}
