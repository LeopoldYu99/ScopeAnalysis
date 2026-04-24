using System;
using System.Collections.Generic;
using System.Runtime.Remoting;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Shapes;
using Arction.Wpf.Charting;
using Arction.Wpf.Charting.Axes;
using Arction.Wpf.Charting.SeriesXY;
using Arction.Wpf.Charting.Views.ViewXY;

namespace ScopeAnalysis
{
    public partial class MainWindow
    {
        private double? TryGetXAxisValueAt(double controlX)
        {
            if (_chart == null)
            {
                return null;
            }

            AxisX xAxis = _chart.ViewXY.XAxes[0];
            Thickness margins = _chart.ViewXY.Margins;
            double plotWidth = _chart.ActualWidth - margins.Left - margins.Right;
            if (plotWidth <= 0)
            {
                return null;
            }

            double normalizedX = (controlX - margins.Left) / plotWidth;
            normalizedX = Math.Max(0, Math.Min(1, normalizedX));
            return xAxis.Minimum + (xAxis.Maximum - xAxis.Minimum) * normalizedX;
        }

        private bool TryGetPlotAreaBounds(out double plotLeft, out double plotTop, out double plotRight, out double plotBottom)
        {
            plotLeft = 0;
            plotTop = 0;
            plotRight = 0;
            plotBottom = 0;

            if (_chart == null)
            {
                return false;
            }

            Thickness margins = _chart.ViewXY.Margins;
            plotLeft = margins.Left;
            plotTop = margins.Top;
            plotRight = _chart.ActualWidth - margins.Right;
            plotBottom = _chart.ActualHeight - margins.Bottom;
            return plotRight > plotLeft && plotBottom > plotTop;
        }

        private bool IsPointInsidePlotArea(Point position)
        {
            double plotLeft;
            double plotTop;
            double plotRight;
            double plotBottom;
            if (TryGetPlotAreaBounds(out plotLeft, out plotTop, out plotRight, out plotBottom) == false)
            {
                return false;
            }

            return position.X >= plotLeft
                && position.X <= plotRight
                && position.Y >= plotTop
                && position.Y <= plotBottom;
        }

        private void UpdateMeasurementFromControlPosition(double controlX, double controlY)
        {
            double? xValue = TryGetXAxisValueAt(controlX);
            if (xValue.HasValue == false)
            {
                _isMeasurementHovering = false;
                UpdateMeasurementVisual();
                return;
            }

            ChartSignal targetSignal;
            if (TryGetMeasurementSignalAtPosition(controlX, controlY, xValue.Value, out targetSignal) == false)
            {
                _isMeasurementHovering = false;
                UpdateMeasurementVisual();
                return;
            }

            _measurementHoverXValue = xValue.Value;
            _measurementSignal = targetSignal;
            _isMeasurementHovering = true;

            UpdateMeasurementVisual();
        }

        private bool TryGetMeasurementSignalAtPosition(double controlX, double controlY, double measurementXValue, out ChartSignal signal)
        {
            signal = null;

            if (IsPointInsideVisibleDecodeRow(controlY))
            {
                return false;
            }

            AxisY targetAxis = TryGetYAxisAt(controlY);
            ChartSignal targetSignal = TryGetSignalByAxis(targetAxis);
            if (targetSignal == null
                || targetSignal.AxisY == null
                || targetSignal.AxisY.Visible == false)
            {
                return false;
            }

            signal = targetSignal;
            return true;
        }

        private ChartSignal TryGetSignalByAxis(AxisY axisY)
        {
            if (axisY == null)
            {
                return null;
            }

            for (int i = 0; i < _chartSignals.Count; i++)
            {
                ChartSignal signal = _chartSignals[i];
                if (signal != null && ReferenceEquals(signal.AxisY, axisY))
                {
                    return signal;
                }
            }

            return null;
        }

        private ChartSignal FindPreferredMeasurementSignal()
        {
            if (_measurementSignal != null
                && _measurementSignal.AxisY != null
                && _measurementSignal.AxisY.Visible)
            {
                return _measurementSignal;
            }

            for (int i = 0; i < _chartSignals.Count; i++)
            {
                ChartSignal signal = _chartSignals[i];
                if (signal != null
                    && signal.AxisY != null
                    && signal.AxisY.Visible)
                {
                    return signal;
                }
            }

            return null;
        }

        private enum DigitalEdgeDirection
        {
            Rising,
            Falling
        }

        private struct DigitalEdge
        {
            public double X;
            public double ValueAfter;
            public DigitalEdgeDirection Direction;
        }

        private sealed class MeasurementResult
        {
            public ChartSignal Signal { get; set; }
            public DigitalEdgeDirection Direction { get; set; }
            public double Width { get; set; }
            public double Period { get; set; }
            public double FrequencyHz { get; set; }
            public double DutyCyclePercent { get; set; }
            public double DisplayYValue { get; set; }
            public bool IsHighPulseWidth { get; set; }
            public double PeriodStartX { get; set; }
            public double PeriodEndX { get; set; }
            public int MatchRank { get; set; }
            public double DistanceToMeasurementX { get; set; }
        }

        private bool TryGetMeasurement(double measurementXValue, out MeasurementResult measurement)
        {
            measurement = null;
            if (_chart == null)
            {
                return false;
            }

            ChartSignal signal = FindPreferredMeasurementSignal();
            if (signal == null
                || signal.AxisY == null
                || signal.AxisY.Visible == false)
            {
                return false;
            }

            _measurementSignal = signal;
            if (TryBuildMeasurement(signal, measurementXValue, out measurement) == false)
            {
                return false;
            }

            return measurement != null;
        }

        private bool TryBuildMeasurement(ChartSignal signal, double measurementXValue, out MeasurementResult measurement)
        {
            measurement = null;
            if (signal == null)
            {
                return false;
            }

            DigitalEdge[] edges;
            if (TryGetMeasurementEdges(signal, out edges) == false || edges.Length < 3)
            {
                return false;
            }

            MeasurementResult bestMeasurement = null;
            int edgeIndex = FindFirstEdgeIndexAtOrAfter(edges, measurementXValue);
            EvaluateMeasurementCandidate(signal, edges, edgeIndex - 2, measurementXValue, ref bestMeasurement);
            EvaluateMeasurementCandidate(signal, edges, edgeIndex - 1, measurementXValue, ref bestMeasurement);
            EvaluateMeasurementCandidate(signal, edges, edgeIndex, measurementXValue, ref bestMeasurement);

            int centerIndex = FindFirstMeasurementWindowCenterAtOrAfter(edges, measurementXValue);
            EvaluateMeasurementCandidate(signal, edges, centerIndex - 1, measurementXValue, ref bestMeasurement);
            EvaluateMeasurementCandidate(signal, edges, centerIndex, measurementXValue, ref bestMeasurement);

            measurement = bestMeasurement;
            return measurement != null;
        }

        private bool TryGetMeasurementEdges(ChartSignal signal, out DigitalEdge[] edges)
        {
            edges = null;
            if (signal == null)
            {
                return false;
            }

            int historyVersion = signal.HistoryVersion;
            MeasurementCacheEntry cacheEntry;
            if (_measurementCache.TryGetValue(signal, out cacheEntry)
                && cacheEntry != null
                && cacheEntry.HistoryVersion == historyVersion)
            {
                edges = cacheEntry.Edges;
                return edges != null && edges.Length >= 3;
            }

            edges = BuildDigitalEdges(signal.GetDigitalHistorySnapshot());
            _measurementCache[signal] = new MeasurementCacheEntry
            {
                HistoryVersion = historyVersion,
                Edges = edges
            };

            return edges != null && edges.Length >= 3;
        }

        private static int FindFirstEdgeIndexAtOrAfter(DigitalEdge[] edges, double measurementXValue)
        {
            int left = 0;
            int right = edges.Length;
            while (left < right)
            {
                int middle = left + ((right - left) / 2);
                if (edges[middle].X < measurementXValue)
                {
                    left = middle + 1;
                }
                else
                {
                    right = middle;
                }
            }

            return left;
        }

        private static int FindFirstMeasurementWindowCenterAtOrAfter(DigitalEdge[] edges, double measurementXValue)
        {
            int left = 0;
            int right = Math.Max(0, edges.Length - 2);
            while (left < right)
            {
                int middle = left + ((right - left) / 2);
                double centerX = (edges[middle].X + edges[middle + 2].X) / 2.0;
                if (centerX < measurementXValue)
                {
                    left = middle + 1;
                }
                else
                {
                    right = middle;
                }
            }

            return left;
        }

        private static void EvaluateMeasurementCandidate(
            ChartSignal signal,
            DigitalEdge[] edges,
            int startIndex,
            double measurementXValue,
            ref MeasurementResult bestMeasurement)
        {
            if (signal == null
                || edges == null
                || startIndex < 0
                || startIndex + 2 >= edges.Length)
            {
                return;
            }

            DigitalEdge firstEdge = edges[startIndex];
            DigitalEdge middleEdge = edges[startIndex + 1];
            DigitalEdge lastEdge = edges[startIndex + 2];

            if (firstEdge.Direction == middleEdge.Direction || firstEdge.Direction != lastEdge.Direction)
            {
                return;
            }

            double width = middleEdge.X - firstEdge.X;
            double measuredPeriod = lastEdge.X - firstEdge.X;
            if (width <= 0 || measuredPeriod <= 0 || width > measuredPeriod)
            {
                return;
            }

            bool isHighPulseWidth = firstEdge.Direction == DigitalEdgeDirection.Rising;
            double highDutyRatio = isHighPulseWidth
                ? width / measuredPeriod
                : (measuredPeriod - width) / measuredPeriod;
            highDutyRatio = Math.Max(0.0, Math.Min(1.0, highDutyRatio));

            double derivedPeriod = measuredPeriod;
            if (isHighPulseWidth)
            {
                if (highDutyRatio > 1e-9)
                {
                    derivedPeriod = width / highDutyRatio;
                }
            }
            else
            {
                double lowDutyRatio = 1.0 - highDutyRatio;
                if (lowDutyRatio > 1e-9)
                {
                    derivedPeriod = width / lowDutyRatio;
                }
            }

            if (derivedPeriod <= 0 || double.IsNaN(derivedPeriod) || double.IsInfinity(derivedPeriod))
            {
                return;
            }

            int matchRank = 2;
            double periodMidpoint = (firstEdge.X + lastEdge.X) / 2.0;
            double distanceToMeasurementX = Math.Abs(measurementXValue - periodMidpoint);
            if (measurementXValue >= firstEdge.X && measurementXValue <= middleEdge.X)
            {
                matchRank = 0;
                distanceToMeasurementX = 0;
            }
            else if (measurementXValue >= firstEdge.X && measurementXValue <= lastEdge.X)
            {
                matchRank = 1;
            }

            MeasurementResult candidate = new MeasurementResult
            {
                Signal = signal,
                Direction = firstEdge.Direction,
                Width = width,
                Period = derivedPeriod,
                FrequencyHz = 1.0 / derivedPeriod,
                DutyCyclePercent = highDutyRatio * 100.0,
                DisplayYValue = firstEdge.ValueAfter,
                IsHighPulseWidth = isHighPulseWidth,
                PeriodStartX = firstEdge.X,
                PeriodEndX = lastEdge.X,
                MatchRank = matchRank,
                DistanceToMeasurementX = distanceToMeasurementX
            };

            if (bestMeasurement == null
                || candidate.MatchRank < bestMeasurement.MatchRank
                || (candidate.MatchRank == bestMeasurement.MatchRank && candidate.DistanceToMeasurementX < bestMeasurement.DistanceToMeasurementX))
            {
                bestMeasurement = candidate;
            }
        }

        private static DigitalEdge[] BuildDigitalEdges(DigitalHistorySnapshot history)
        {
            if (history == null || history.DigitalWords == null || history.SampleCount < 2 || history.SampleInterval <= 0)
            {
                return new DigitalEdge[0];
            }

            List<DigitalEdge> edges = new List<DigitalEdge>();
            bool previousHigh = GetHistorySampleValue(history.DigitalWords, history.SampleCount, 0);
            for (int sampleIndex = 1; sampleIndex < history.SampleCount; sampleIndex++)
            {
                bool currentHigh = GetHistorySampleValue(history.DigitalWords, history.SampleCount, sampleIndex);
                if (previousHigh == currentHigh)
                {
                    continue;
                }

                DigitalEdge edge;
                edge.X = sampleIndex * history.SampleInterval;
                edge.ValueAfter = currentHigh ? 1.0 : 0.0;
                edge.Direction = currentHigh ? DigitalEdgeDirection.Rising : DigitalEdgeDirection.Falling;
                edges.Add(edge);
                previousHigh = currentHigh;
            }

            return edges.ToArray();
        }

        private static bool GetHistorySampleValue(uint[] digitalWords, int sampleCount, int sampleIndex)
        {
            if (digitalWords == null || sampleIndex < 0 || sampleIndex >= sampleCount)
            {
                return false;
            }

            int wordIndex = sampleIndex / 32;
            int bitOffset = sampleIndex % 32;
            return (digitalWords[wordIndex] & (1u << bitOffset)) != 0;
        }

        private string BuildMeasurementText(MeasurementResult measurement)
        {
            if (measurement == null)
            {
                return _measurementHoverXValue.ToString("0.000");
            }

            string edgeLabel = measurement.Direction == DigitalEdgeDirection.Rising ? "上升沿" : "下降沿";
            return string.Format(
                "{0}\n宽度: {1}\n周期: {2}\n占空比: {3:0.00}%",
                edgeLabel,
                FormatTimeValue(measurement.Width),
                FormatTimeValue(measurement.Period),
                measurement.DutyCyclePercent);
        }

        private string FormatTimeValue(double axisValue)
        {
            double seconds = axisValue * GetXAxisSecondsPerUnit();
            double absoluteSeconds = Math.Abs(seconds);
            if (absoluteSeconds >= 1.0)
            {
                return string.Format("{0:0.###} 秒", seconds);
            }

            if (absoluteSeconds >= 1e-3)
            {
                return string.Format("{0:0.###} 毫秒", seconds * 1e3);
            }

            if (absoluteSeconds >= 1e-6)
            {
                return string.Format("{0:0.###} 微秒", seconds * 1e6);
            }

            if (absoluteSeconds >= 1e-9)
            {
                return string.Format("{0:0.###} 纳秒", seconds * 1e9);
            }

            return string.Format("{0:0.###E+0} 秒", seconds);
        }

        private string FormatFrequencyValue(double frequencyHz)
        {
            double absoluteFrequency = Math.Abs(frequencyHz);
            if (absoluteFrequency >= 1e6)
            {
                return string.Format("{0:0.###} 兆赫", frequencyHz / 1e6);
            }

            if (absoluteFrequency >= 1e3)
            {
                return string.Format("{0:0.###} 千赫", frequencyHz / 1e3);
            }

            return string.Format("{0:0.###} 赫兹", frequencyHz);
        }

        private double GetXAxisSecondsPerUnit()
        {
            if (_chart == null)
            {
                return 1.0;
            }

            string unitsText = _chart.ViewXY.XAxes[0].Units.Text;
            if (string.Equals(unitsText, "us", StringComparison.OrdinalIgnoreCase)
                || string.Equals(unitsText, "微秒", StringComparison.OrdinalIgnoreCase))
            {
                return 1e-6;
            }

            if (string.Equals(unitsText, "ms", StringComparison.OrdinalIgnoreCase)
                || string.Equals(unitsText, "毫秒", StringComparison.OrdinalIgnoreCase))
            {
                return 1e-3;
            }

            if (string.Equals(unitsText, "ns", StringComparison.OrdinalIgnoreCase)
                || string.Equals(unitsText, "纳秒", StringComparison.OrdinalIgnoreCase))
            {
                return 1e-9;
            }

            return 1.0;
        }

        private void HideMeasurementVisuals()
        {
            if (_measurementStartLine != null)
            {
                _measurementStartLine.Visibility = Visibility.Collapsed;
            }

            if (_measurementEndLine != null)
            {
                _measurementEndLine.Visibility = Visibility.Collapsed;
            }

            if (_measurementSpanLine != null)
            {
                _measurementSpanLine.Visibility = Visibility.Collapsed;
            }
        }

        private void CollapseMeasurementVisual()
        {
            if (_measurementOverlay != null)
            {
                _measurementOverlay.Visibility = Visibility.Collapsed;
            }

            if (_measurementValueBorder != null)
            {
                _measurementValueBorder.Visibility = Visibility.Collapsed;
            }

            HideMeasurementVisuals();
        }

        private bool TryGetYAxisBounds(AxisY targetAxis, out double segmentTop, out double segmentBottom)
        {
            segmentTop = 0;
            segmentBottom = 0;
            if (_chart == null || targetAxis == null)
            {
                return false;
            }

            double plotLeft;
            double plotTop;
            double plotRight;
            double plotBottom;
            if (TryGetPlotAreaBounds(out plotLeft, out plotTop, out plotRight, out plotBottom) == false)
            {
                return false;
            }

            int visibleAxisCount = 0;
            foreach (AxisY axis in _chart.ViewXY.YAxes)
            {
                if (axis.Visible)
                {
                    visibleAxisCount++;
                }
            }

            if (visibleAxisCount == 0)
            {
                return false;
            }

            double plotHeight = plotBottom - plotTop;
            double segmentsGap = _chart.ViewXY.AxisLayout.SegmentsGap;
            double totalGapHeight = segmentsGap * (visibleAxisCount - 1);
            double segmentHeight = (plotHeight - totalGapHeight) / visibleAxisCount;
            if (segmentHeight <= 0)
            {
                return false;
            }

            double currentTop = plotTop;
            foreach (AxisY axis in _chart.ViewXY.YAxes)
            {
                if (axis.Visible == false)
                {
                    continue;
                }

                double currentBottom = currentTop + segmentHeight;
                if (ReferenceEquals(axis, targetAxis))
                {
                    segmentTop = currentTop;
                    segmentBottom = currentBottom;
                    return true;
                }

                currentTop = currentBottom + segmentsGap;
            }

            return false;
        }

        private void UpdateMeasurementMarkers(MeasurementResult measurement, double plotLeft, double plotRight)
        {
            if (_measurementStartLine == null
                || _measurementEndLine == null
                || _measurementSpanLine == null
                || measurement == null
                || measurement.Signal == null
                || measurement.Signal.AxisY == null
                || _chart == null)
            {
                HideMeasurementVisuals();
                return;
            }

            AxisX xAxis = _chart.ViewXY.XAxes[0];
            double visibleMin = xAxis.Minimum;
            double visibleMax = xAxis.Maximum;
            if (visibleMax <= visibleMin)
            {
                HideMeasurementVisuals();
                return;
            }

            double segmentTop;
            double segmentBottom;
            if (TryGetYAxisBounds(measurement.Signal.AxisY, out segmentTop, out segmentBottom) == false)
            {
                HideMeasurementVisuals();
                return;
            }

            double startCoord = plotLeft + (measurement.PeriodStartX - visibleMin) / (visibleMax - visibleMin) * (plotRight - plotLeft);
            double endCoord = plotLeft + (measurement.PeriodEndX - visibleMin) / (visibleMax - visibleMin) * (plotRight - plotLeft);
            if (double.IsNaN(startCoord) || double.IsNaN(endCoord) || double.IsInfinity(startCoord) || double.IsInfinity(endCoord))
            {
                HideMeasurementVisuals();
                return;
            }

            startCoord = Math.Max(plotLeft, Math.Min(plotRight, startCoord));
            endCoord = Math.Max(plotLeft, Math.Min(plotRight, endCoord));
            if (Math.Abs(endCoord - startCoord) < 1.0)
            {
                HideMeasurementVisuals();
                return;
            }

            double signalY = measurement.Signal.AxisY.ValueToCoord(0.5, true);
            if (double.IsNaN(signalY) || double.IsInfinity(signalY))
            {
                signalY = (segmentTop + segmentBottom) / 2.0;
            }

            signalY = Math.Max(segmentTop + 2.0, Math.Min(segmentBottom - 2.0, signalY));
            double lineTop = Math.Max(segmentTop + 2.0, signalY - 12.0);
            double lineBottom = Math.Min(segmentBottom - 2.0, signalY + 12.0);
            if (lineBottom <= lineTop)
            {
                lineTop = segmentTop + 2.0;
                lineBottom = segmentBottom - 2.0;
            }

            _measurementStartLine.X1 = startCoord;
            _measurementStartLine.X2 = startCoord;
            _measurementStartLine.Y1 = lineTop;
            _measurementStartLine.Y2 = lineBottom;
            _measurementStartLine.Visibility = Visibility.Visible;

            _measurementEndLine.X1 = endCoord;
            _measurementEndLine.X2 = endCoord;
            _measurementEndLine.Y1 = lineTop;
            _measurementEndLine.Y2 = lineBottom;
            _measurementEndLine.Visibility = Visibility.Visible;

            _measurementSpanLine.X1 = Math.Min(startCoord, endCoord);
            _measurementSpanLine.X2 = Math.Max(startCoord, endCoord);
            _measurementSpanLine.Y1 = signalY;
            _measurementSpanLine.Y2 = signalY;
            _measurementSpanLine.Visibility = Visibility.Visible;
        }

        private bool IsPointInsideVisibleDecodeRow(double controlY)
        {
            if (_chart == null)
            {
                return false;
            }

            double plotLeft;
            double plotTop;
            double plotRight;
            double plotBottom;
            if (TryGetPlotAreaBounds(out plotLeft, out plotTop, out plotRight, out plotBottom) == false)
            {
                return false;
            }

            AxisX xAxis = _chart.ViewXY.XAxes[0];
            double visibleMin = xAxis.Minimum;
            double visibleMax = xAxis.Maximum;
            if (visibleMax <= visibleMin)
            {
                return false;
            }

            List<DecodeRowLayout> decodeRows = BuildDecodeRows(plotTop, plotBottom);
            for (int i = 0; i < decodeRows.Count; i++)
            {
                DecodeRowLayout row = decodeRows[i];
                if (controlY < row.Top || controlY > row.Top + row.Height)
                {
                    continue;
                }

                if (BuildProtocolSegments(row.Signal, visibleMin, visibleMax).Count > 0)
                {
                    return true;
                }
            }

            return false;
        }

        private void UpdateMeasurementVisual()
        {
            _isMeasurementVisualDirty = false;
            if (_measurementOverlay == null || _measurementValueBorder == null || _measurementValueText == null)
            {
                return;
            }

            if (_isMeasurementHovering == false || _chart == null)
            {
                CollapseMeasurementVisual();
                return;
            }

            double plotLeft;
            double plotTop;
            double plotRight;
            double plotBottom;
            if (TryGetPlotAreaBounds(out plotLeft, out plotTop, out plotRight, out plotBottom) == false)
            {
                CollapseMeasurementVisual();
                return;
            }

            AxisX xAxis = _chart.ViewXY.XAxes[0];
            double currentMin = xAxis.Minimum;
            double currentMax = xAxis.Maximum;
            if (currentMax <= currentMin)
            {
                CollapseMeasurementVisual();
                return;
            }

            double measurementNormalizedX = (_measurementHoverXValue - currentMin) / (currentMax - currentMin);
            if (measurementNormalizedX < 0 || measurementNormalizedX > 1)
            {
                CollapseMeasurementVisual();
                return;
            }

            double measurementXCoord = plotLeft + measurementNormalizedX * (plotRight - plotLeft);

            _measurementOverlay.Width = _chart.ActualWidth;
            _measurementOverlay.Height = _chart.ActualHeight;
            _measurementOverlay.Visibility = Visibility.Visible;

            MeasurementResult measurement = null;
            if (TryGetMeasurement(_measurementHoverXValue, out measurement) == false || measurement == null)
            {
                CollapseMeasurementVisual();
                return;
            }

            _measurementValueText.Text = BuildMeasurementText(measurement);
            _measurementValueBorder.BorderBrush = new SolidColorBrush(Color.FromArgb(
                235,
                measurement.Signal.SeriesColor.R,
                measurement.Signal.SeriesColor.G,
                measurement.Signal.SeriesColor.B));
            UpdateMeasurementMarkers(measurement, plotLeft, plotRight);

            _measurementValueBorder.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));

            double labelWidth = _measurementValueBorder.DesiredSize.Width;
            double labelHeight = _measurementValueBorder.DesiredSize.Height;
            double labelLeft = Math.Min(plotRight - labelWidth, measurementXCoord + 12.0);
            if (labelLeft < plotLeft)
            {
                labelLeft = plotLeft;
            }

            if (labelLeft + labelWidth > plotRight)
            {
                labelLeft = Math.Max(plotLeft, measurementXCoord - labelWidth - 12.0);
            }

            double labelTop;
            if (measurement.Signal != null && measurement.Signal.AxisY != null)
            {
                double measurementY = measurement.Signal.AxisY.ValueToCoord(measurement.DisplayYValue, true);
                if (double.IsNaN(measurementY) || double.IsInfinity(measurementY))
                {
                    measurementY = (plotTop + plotBottom) / 2.0;
                }

                labelTop = Math.Max(plotTop, Math.Min(plotBottom - labelHeight, measurementY - labelHeight / 2.0));
            }
            else
            {
                labelTop = Math.Max(plotTop, plotBottom - labelHeight - 4.0);
            }

            Canvas.SetLeft(_measurementValueBorder, labelLeft);
            Canvas.SetTop(_measurementValueBorder, labelTop);
            _measurementValueBorder.Visibility = Visibility.Visible;
        }

        private sealed class DecodeRowLayout
        {
            public ChartSignal Signal { get; set; }
            public double Top { get; set; }
            public double Height { get; set; }
        }

        private sealed class DecodeCacheEntry
        {
            public int HistoryVersion { get; set; }
            public int DecodeSettingsVersion { get; set; }
            public List<ProtocolSegment> Segments { get; set; }
        }

        private sealed class MeasurementCacheEntry
        {
            public int HistoryVersion { get; set; }
            public DigitalEdge[] Edges { get; set; }
        }

        private const double DecodeVerticalOffset = -30.0;
        private const double DecodeSegmentEdgeTolerance = 1e-9;
        private const double DecodeViewportTolerance = 1e-9;

        private void UpdateDecodeOverlay()
        {
            _isDecodeOverlayDirty = false;
            if (_decodeOverlay == null)
            {
                return;
            }

            _decodeOverlay.Children.Clear();
            if (_chart == null)
            {
                _decodeOverlay.Visibility = Visibility.Collapsed;
                return;
            }

            double plotLeft;
            double plotTop;
            double plotRight;
            double plotBottom;
            if (TryGetPlotAreaBounds(out plotLeft, out plotTop, out plotRight, out plotBottom) == false)
            {
                _decodeOverlay.Visibility = Visibility.Collapsed;
                return;
            }

            AxisX xAxis = _chart.ViewXY.XAxes[0];
            double visibleMin = xAxis.Minimum;
            double visibleMax = xAxis.Maximum;
            if (visibleMax <= visibleMin)
            {
                _decodeOverlay.Visibility = Visibility.Collapsed;
                return;
            }

            List<DecodeRowLayout> decodeRows = BuildDecodeRows(plotTop, plotBottom);
            if (decodeRows.Count == 0)
            {
                _decodeOverlay.Visibility = Visibility.Collapsed;
                return;
            }

            _decodeOverlay.Width = _chart.ActualWidth;
            _decodeOverlay.Height = _chart.ActualHeight;
            double rowWidth = plotRight - plotLeft;
            bool hasVisibleRow = false;

            for (int i = 0; i < decodeRows.Count; i++)
            {
                DecodeRowLayout row = decodeRows[i];
                List<ProtocolSegment> segments = BuildProtocolSegments(row.Signal, visibleMin, visibleMax);
                if (segments.Count == 0)
                {
                    continue;
                }

                hasVisibleRow = true;
                DrawDecodeRowBackground(plotLeft, row.Top, rowWidth, row.Height);

                double innerTop = row.Top + 2.0;
                double innerHeight = Math.Max(6.0, row.Height - 4.0);
                for (int segmentIndex = 0; segmentIndex < segments.Count; segmentIndex++)
                {
                    DrawProtocolSegment(segments[segmentIndex], plotLeft, innerTop, rowWidth, innerHeight, visibleMin, visibleMax);
                }
            }

            _decodeOverlay.Visibility = hasVisibleRow ? Visibility.Visible : Visibility.Collapsed;
        }

        private List<DecodeRowLayout> BuildDecodeRows(double plotTop, double plotBottom)
        {
            List<DecodeRowLayout> rows = new List<DecodeRowLayout>();
            int visibleAxisCount = 0;

            for (int i = 0; i < _chartSignals.Count; i++)
            {
                if (_chartSignals[i].AxisY != null && _chartSignals[i].AxisY.Visible)
                {
                    visibleAxisCount++;
                }
            }

            if (visibleAxisCount == 0)
            {
                return rows;
            }

            double plotHeight = plotBottom - plotTop;
            double segmentsGap = _chart.ViewXY.AxisLayout.SegmentsGap;
            double totalGapHeight = segmentsGap * (visibleAxisCount - 1);
            double segmentHeight = (plotHeight - totalGapHeight) / visibleAxisCount;
            if (segmentHeight <= 0)
            {
                return rows;
            }

            double currentTop = plotTop;
            for (int i = 0; i < _chartSignals.Count; i++)
            {
                ChartSignal signal = _chartSignals[i];
                AxisY axisY = signal.AxisY;
                if (axisY == null || axisY.Visible == false)
                {
                    continue;
                }

                double rowHeight = Math.Max(28.0, Math.Min(36.0, segmentHeight * 0.34));
                rowHeight = Math.Min(rowHeight, Math.Max(4.0, segmentHeight - 2.0));
                double rowTop = currentTop + Math.Max(1.0, Math.Min(4.0, (segmentHeight - rowHeight) / 2.0));
                rows.Add(new DecodeRowLayout
                {
                    Signal = signal,
                    Top = rowTop + DecodeVerticalOffset,
                    Height = rowHeight
                });

                currentTop += segmentHeight + segmentsGap;
            }

            return rows;
        }

        private void DrawDecodeRowBackground(double plotLeft, double rowTop, double rowWidth, double rowHeight)
        {
            System.Windows.Controls.Border background = new System.Windows.Controls.Border
            {
                Width = rowWidth,
                Height = rowHeight,
                Background = new SolidColorBrush(Color.FromArgb(150, 14, 20, 16)),
                BorderBrush = new SolidColorBrush(Color.FromArgb(210, 54, 83, 64)),
                BorderThickness = new Thickness(1),
                CornerRadius = new CornerRadius(4)
            };

            Canvas.SetLeft(background, plotLeft);
            Canvas.SetTop(background, rowTop);
            _decodeOverlay.Children.Add(background);
        }

        private void DrawProtocolSegment(ProtocolSegment segment, double plotLeft, double rowTop, double plotWidth, double rowHeight, double visibleMin, double visibleMax)
        {
            double xRange = visibleMax - visibleMin;
            if (xRange <= 0)
            {
                return;
            }

            double normalizedStart = Math.Max(0.0, Math.Min(1.0, (segment.StartX - visibleMin) / xRange));
            double normalizedEnd = Math.Max(0.0, Math.Min(1.0, (segment.EndX - visibleMin) / xRange));
            if (normalizedEnd <= normalizedStart)
            {
                return;
            }

            double left = plotLeft + normalizedStart * plotWidth;
            double right = plotLeft + normalizedEnd * plotWidth;
            double width = right - left;
            if (width < 4.0)
            {
                return;
            }

            bool isCompleteSegmentVisible = IsCompleteSegmentVisible(segment);
            System.Windows.Shapes.Shape shape;
            if (isCompleteSegmentVisible == false || width < rowHeight * 1.3)
            {
                shape = new Rectangle
                {
                    Width = width,
                    Height = rowHeight,
                    Fill = new SolidColorBrush(segment.FillColor),
                    Stroke = new SolidColorBrush(segment.BorderColor),
                    StrokeThickness = 1,
                    RadiusX = 3,
                    RadiusY = 3
                };
            }
            else
            {
                double arrowSize = Math.Min(rowHeight * 0.32, width / 5.0);
                Polygon polygon = new Polygon
                {
                    Fill = new SolidColorBrush(segment.FillColor),
                    Stroke = new SolidColorBrush(segment.BorderColor),
                    StrokeThickness = 1
                };

                polygon.Points.Add(new Point(left + arrowSize, rowTop));
                polygon.Points.Add(new Point(right - arrowSize, rowTop));
                polygon.Points.Add(new Point(right, rowTop + rowHeight / 2.0));
                polygon.Points.Add(new Point(right - arrowSize, rowTop + rowHeight));
                polygon.Points.Add(new Point(left + arrowSize, rowTop + rowHeight));
                polygon.Points.Add(new Point(left, rowTop + rowHeight / 2.0));
                shape = polygon;
            }

            if (shape is Rectangle)
            {
                Canvas.SetLeft(shape, left);
                Canvas.SetTop(shape, rowTop);
            }

            _decodeOverlay.Children.Add(shape);

            if (width < 12.0)
            {
                return;
            }

            if (isCompleteSegmentVisible == false)
            {
                if (segment.IsMarker == false && segment.BitLabels != null && segment.BitLabels.Length > 0)
                {
                    DrawVisibleDataBitLabels(segment, rowTop, rowHeight, visibleMin, visibleMax, plotLeft, plotWidth);
                }

                TryDrawCenteredSegmentLabel(segment, left, rowTop, width, rowHeight);
                return;
            }

            if (segment.IsMarker == false && segment.BitLabels != null && segment.BitLabels.Length > 0)
            {
                DrawDataSegmentLabels(segment, left, rowTop, width, rowHeight);
                return;
            }

            TextBlock label = new TextBlock
            {
                Text = segment.Label,
                Foreground = new SolidColorBrush(segment.ForegroundColor),
                FontSize = segment.IsMarker ? 11 : 12,
                FontWeight = FontWeights.Bold
            };

            label.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));
            if (label.DesiredSize.Width > width - 6.0)
            {
                return;
            }

            Canvas.SetLeft(label, left + (width - label.DesiredSize.Width) / 2.0);
            Canvas.SetTop(label, rowTop + (rowHeight - label.DesiredSize.Height) / 2.0 - 1.0);
            _decodeOverlay.Children.Add(label);
        }

        private void TryDrawCenteredSegmentLabel(ProtocolSegment segment, double left, double rowTop, double width, double rowHeight)
        {
            if (segment == null || string.IsNullOrWhiteSpace(segment.Label) || width < 18.0)
            {
                return;
            }

            TextBlock label = new TextBlock
            {
                Text = segment.Label,
                Foreground = new SolidColorBrush(segment.ForegroundColor),
                FontSize = segment.IsMarker ? 11 : 12,
                FontWeight = FontWeights.Bold
            };

            label.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));
            if (label.DesiredSize.Width > width - 6.0)
            {
                return;
            }

            double topOffset = segment.IsMarker || segment.BitLabels == null || segment.BitLabels.Length == 0
                ? (rowHeight - label.DesiredSize.Height) / 2.0 - 1.0
                : 1.0;

            Canvas.SetLeft(label, left + (width - label.DesiredSize.Width) / 2.0);
            Canvas.SetTop(label, rowTop + topOffset);
            _decodeOverlay.Children.Add(label);
        }

        private static bool IsCompleteSegmentVisible(ProtocolSegment segment)
        {
            double originalStart = GetOriginalStartX(segment);
            double originalEnd = GetOriginalEndX(segment);
            double tolerance = Math.Max(1.0, Math.Max(Math.Abs(originalStart), Math.Abs(originalEnd))) * DecodeSegmentEdgeTolerance;
            return Math.Abs(segment.StartX - originalStart) <= tolerance
                && Math.Abs(segment.EndX - originalEnd) <= tolerance;
        }

        private static double GetOriginalStartX(ProtocolSegment segment)
        {
            if (segment == null)
            {
                return 0;
            }

            return segment.OriginalEndX > segment.OriginalStartX ? segment.OriginalStartX : segment.StartX;
        }

        private static double GetOriginalEndX(ProtocolSegment segment)
        {
            if (segment == null)
            {
                return 0;
            }

            return segment.OriginalEndX > segment.OriginalStartX ? segment.OriginalEndX : segment.EndX;
        }

        private void DrawVisibleDataBitLabels(
            ProtocolSegment segment,
            double rowTop,
            double rowHeight,
            double visibleMin,
            double visibleMax,
            double plotLeft,
            double plotWidth)
        {
            int bitCount = segment.BitLabels == null ? 0 : segment.BitLabels.Length;
            double originalStart = GetOriginalStartX(segment);
            double originalEnd = GetOriginalEndX(segment);
            if (bitCount <= 0 || originalEnd <= originalStart || visibleMax <= visibleMin)
            {
                return;
            }

            double bitBandTop = rowTop + rowHeight * 0.48;
            double bitBandHeight = Math.Max(9.0, rowHeight - (bitBandTop - rowTop) - 1.0);
            if (bitBandHeight < 8.0)
            {
                return;
            }

            double xRange = visibleMax - visibleMin;
            double originalBitWidth = (originalEnd - originalStart) / bitCount;
            for (int bitIndex = 0; bitIndex < bitCount; bitIndex++)
            {
                double bitStartX = originalStart + bitIndex * originalBitWidth;
                double bitEndX = bitStartX + originalBitWidth;
                double clippedBitStartX = Math.Max(bitStartX, visibleMin);
                double clippedBitEndX = Math.Min(bitEndX, visibleMax);
                if (clippedBitEndX <= clippedBitStartX)
                {
                    continue;
                }

                double bitLeft = plotLeft + (clippedBitStartX - visibleMin) / xRange * plotWidth;
                double bitRight = plotLeft + (clippedBitEndX - visibleMin) / xRange * plotWidth;
                double cellWidth = bitRight - bitLeft;
                if (cellWidth < 4.0)
                {
                    continue;
                }

                Rectangle bitCell = new Rectangle
                {
                    Width = Math.Max(2.0, cellWidth - 1.0),
                    Height = bitBandHeight,
                    Fill = new SolidColorBrush(Color.FromArgb(220, 109, 204, 224)),
                    Stroke = new SolidColorBrush(Color.FromArgb(230, 208, 245, 255)),
                    StrokeThickness = 0.8,
                    RadiusX = 1.5,
                    RadiusY = 1.5
                };

                Canvas.SetLeft(bitCell, bitLeft + 0.5);
                Canvas.SetTop(bitCell, bitBandTop);
                _decodeOverlay.Children.Add(bitCell);

                TextBlock bitLabel = new TextBlock
                {
                    Text = segment.BitLabels[bitIndex],
                    Foreground = new SolidColorBrush(Color.FromRgb(7, 35, 47)),
                    FontSize = 10,
                    FontWeight = FontWeights.Bold,
                    TextAlignment = TextAlignment.Center
                };

                bitLabel.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));
                if (bitLabel.DesiredSize.Width > cellWidth - 2.0 || bitLabel.DesiredSize.Height > bitBandHeight)
                {
                    continue;
                }

                Canvas.SetLeft(bitLabel, bitLeft + (cellWidth - bitLabel.DesiredSize.Width) / 2.0);
                Canvas.SetTop(bitLabel, bitBandTop + (bitBandHeight - bitLabel.DesiredSize.Height) / 2.0 - 1.0);
                _decodeOverlay.Children.Add(bitLabel);
            }
        }

        private void DrawDataSegmentLabels(ProtocolSegment segment, double left, double rowTop, double width, double rowHeight)
        {
            TextBlock topLabel = new TextBlock
            {
                Text = segment.Label,
                Foreground = new SolidColorBrush(segment.ForegroundColor),
                FontSize = 11,
                FontWeight = FontWeights.Bold,
                TextAlignment = TextAlignment.Center
            };

            topLabel.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));
            if (topLabel.DesiredSize.Width <= width - 8.0)
            {
                Canvas.SetLeft(topLabel, left + (width - topLabel.DesiredSize.Width) / 2.0);
                Canvas.SetTop(topLabel, rowTop + 1.0);
                _decodeOverlay.Children.Add(topLabel);
            }

            int bitCount = segment.BitLabels.Length;
            if (bitCount <= 0)
            {
                return;
            }

            double bitBandTop = rowTop + rowHeight * 0.48;
            double bitBandHeight = Math.Max(9.0, rowHeight - (bitBandTop - rowTop) - 1.0);
            double bitWidth = width / bitCount;
            if (bitWidth < 8.0 || bitBandHeight < 8.0)
            {
                return;
            }

            for (int bitIndex = 0; bitIndex < bitCount; bitIndex++)
            {
                double bitLeft = left + bitIndex * bitWidth;
                double cellWidth = Math.Max(2.0, bitWidth - 1.0);
                Rectangle bitCell = new Rectangle
                {
                    Width = cellWidth,
                    Height = bitBandHeight,
                    Fill = new SolidColorBrush(Color.FromArgb(220, 109, 204, 224)),
                    Stroke = new SolidColorBrush(Color.FromArgb(230, 208, 245, 255)),
                    StrokeThickness = 0.8,
                    RadiusX = 1.5,
                    RadiusY = 1.5
                };

                Canvas.SetLeft(bitCell, bitLeft + 0.5);
                Canvas.SetTop(bitCell, bitBandTop);
                _decodeOverlay.Children.Add(bitCell);

                TextBlock bitLabel = new TextBlock
                {
                    Text = segment.BitLabels[bitIndex],
                    Foreground = new SolidColorBrush(Color.FromRgb(7, 35, 47)),
                    FontSize = 10,
                    FontWeight = FontWeights.Bold,
                    TextAlignment = TextAlignment.Center
                };

                bitLabel.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));
                if (bitLabel.DesiredSize.Width > cellWidth - 2.0 || bitLabel.DesiredSize.Height > bitBandHeight)
                {
                    continue;
                }

                Canvas.SetLeft(bitLabel, bitLeft + (bitWidth - bitLabel.DesiredSize.Width) / 2.0);
                Canvas.SetTop(bitLabel, bitBandTop + (bitBandHeight - bitLabel.DesiredSize.Height) / 2.0 - 1.0);
                _decodeOverlay.Children.Add(bitLabel);
            }
        }

        private List<ProtocolSegment> BuildProtocolSegments(ChartSignal signal, double visibleMin, double visibleMax)
        {
            if (signal == null || visibleMax <= visibleMin)
            {
                return new List<ProtocolSegment>();
            }

            if (ShouldSkipDecode(signal))
            {
                return new List<ProtocolSegment>();
            }

            UartDecodeSettings decodeSettings = signal.DecodeSettings;
            if (decodeSettings == null)
            {
                return new List<ProtocolSegment>();
            }

            int historyVersion = signal.HistoryVersion;
            int decodeSettingsVersion = decodeSettings.Version;

            DecodeCacheEntry cacheEntry;
            if (_decodeCache.TryGetValue(signal, out cacheEntry) == false
                || cacheEntry.HistoryVersion != historyVersion
                || cacheEntry.DecodeSettingsVersion != decodeSettingsVersion)
            {
                DigitalHistorySnapshot history = signal.GetDigitalHistorySnapshot();
                List<ProtocolSegment> segments;
                switch (decodeSettings.Mode)
                {
                    case SignalDecodeMode.None:
                        segments = new List<ProtocolSegment>();
                        break;
                    case SignalDecodeMode.FixedWidth8Bit:
                        segments = DecodeLogic.BuildFixedWidthSegments(
                            history.DigitalWords,
                            history.SampleCount,
                            history.SampleInterval,
                            8,
                            decodeSettings.EmptyDataRunSegmentThreshold,
                            decodeSettings.SamplesPerBit);
                        break;
                    default:
                        segments = DecodeLogic.BuildProtocolSegments(
                            history.DigitalWords,
                            history.SampleCount,
                            history.SampleInterval,
                            decodeSettings.BaudRate,
                            decodeSettings.DataBits,
                            decodeSettings.StopBits,
                            decodeSettings.ParityMode,
                            decodeSettings.IdleBits,
                            decodeSettings.SamplesPerBit,
                            decodeSettings.LeadingIdleSamples);
                        break;
                }

                cacheEntry = new DecodeCacheEntry
                {
                    HistoryVersion = historyVersion,
                    DecodeSettingsVersion = decodeSettingsVersion,
                    Segments = segments
                };

                _decodeCache[signal] = cacheEntry;
            }

            return ClipProtocolSegments(cacheEntry.Segments, visibleMin, visibleMax);
        }

        private static bool ShouldSkipDecode(ChartSignal signal)
        {
            if (signal == null || string.IsNullOrWhiteSpace(signal.Name))
            {
                return false;
            }

            string signalName = signal.Name.Trim();
            return string.Equals(signalName, "CLK", StringComparison.OrdinalIgnoreCase)
                || string.Equals(signalName, "EN", StringComparison.OrdinalIgnoreCase);
        }

        private static List<ProtocolSegment> ClipProtocolSegments(List<ProtocolSegment> segments, double visibleMin, double visibleMax)
        {
            List<ProtocolSegment> visibleSegments = new List<ProtocolSegment>();
            if (segments == null || segments.Count == 0 || visibleMax <= visibleMin)
            {
                return visibleSegments;
            }

            double tolerance = GetDecodeViewportTolerance(visibleMin, visibleMax);
            int startIndex = FindFirstVisibleSegmentIndex(segments, visibleMin, tolerance);
            for (int i = startIndex; i < segments.Count; i++)
            {
                ProtocolSegment segment = segments[i];
                if (segment.StartX > visibleMax + tolerance)
                {
                    break;
                }

                if (segment.EndX < visibleMin - tolerance)
                {
                    continue;
                }

                double clippedStart = Math.Max(segment.StartX, visibleMin);
                double clippedEnd = Math.Min(segment.EndX, visibleMax);
                if (clippedEnd <= clippedStart)
                {
                    continue;
                }

                visibleSegments.Add(new ProtocolSegment
                {
                    StartX = clippedStart,
                    EndX = clippedEnd,
                    OriginalStartX = segment.OriginalEndX > segment.OriginalStartX ? segment.OriginalStartX : segment.StartX,
                    OriginalEndX = segment.OriginalEndX > segment.OriginalStartX ? segment.OriginalEndX : segment.EndX,
                    Label = segment.Label,
                    BitLabels = segment.BitLabels,
                    FillColor = segment.FillColor,
                    BorderColor = segment.BorderColor,
                    ForegroundColor = segment.ForegroundColor,
                    IsMarker = segment.IsMarker
                });
            }

            return visibleSegments;
        }

        private static int FindFirstVisibleSegmentIndex(List<ProtocolSegment> segments, double visibleMin, double tolerance)
        {
            int low = 0;
            int high = segments.Count - 1;
            int result = segments.Count;

            while (low <= high)
            {
                int mid = low + ((high - low) / 2);
                if (segments[mid].EndX >= visibleMin - tolerance)
                {
                    result = mid;
                    high = mid - 1;
                }
                else
                {
                    low = mid + 1;
                }
            }

            return result;
        }

        private static double GetDecodeViewportTolerance(double visibleMin, double visibleMax)
        {
            double scale = Math.Max(1.0, Math.Max(Math.Abs(visibleMin), Math.Abs(visibleMax)));
            double range = Math.Max(0.0, visibleMax - visibleMin);
            return Math.Max(range * DecodeViewportTolerance, scale * DecodeViewportTolerance);
        }

        private AxisY TryGetYAxisAt(double controlY)
        {
            if (_chart == null)
            {
                return null;
            }

            Thickness margins = _chart.ViewXY.Margins;
            double plotTop = margins.Top;
            double plotBottom = _chart.ActualHeight - margins.Bottom;
            if (controlY < plotTop || controlY > plotBottom)
            {
                return null;
            }

            int visibleAxisCount = 0;
            foreach (AxisY axis in _chart.ViewXY.YAxes)
            {
                if (axis.Visible)
                {
                    visibleAxisCount++;
                }
            }

            if (visibleAxisCount == 0)
            {
                return null;
            }

            double plotHeight = plotBottom - plotTop;
            double segmentsGap = _chart.ViewXY.AxisLayout.SegmentsGap;
            double totalGapHeight = segmentsGap * (visibleAxisCount - 1);
            double segmentHeight = (plotHeight - totalGapHeight) / visibleAxisCount;
            if (segmentHeight <= 0)
            {
                return null;
            }

            double currentTop = plotTop;
            foreach (AxisY axis in _chart.ViewXY.YAxes)
            {
                if (axis.Visible == false)
                {
                    continue;
                }

                double currentBottom = currentTop + segmentHeight;
                if (controlY >= currentTop && controlY <= currentBottom)
                {
                    return axis;
                }

                currentTop = currentBottom + segmentsGap;
            }

            return null;
        }

        private void MarkDecodeOverlayDirty()
        {
            _isDecodeOverlayDirty = true;
        }

        private void MarkMeasurementVisualDirty()
        {
            _isMeasurementVisualDirty = true;
        }

        private void InvalidateOverlayCaches()
        {
            _lastViewportMin = double.NaN;
            _lastViewportMax = double.NaN;
            _lastViewportWidth = double.NaN;
            _lastViewportHeight = double.NaN;
            MarkDecodeOverlayDirty();
            MarkMeasurementVisualDirty();
        }

        private static bool AreClose(double left, double right)
        {
            if (double.IsNaN(left) || double.IsNaN(right))
            {
                return double.IsNaN(left) && double.IsNaN(right);
            }

            double scale = Math.Max(1.0, Math.Max(Math.Abs(left), Math.Abs(right)));
            return Math.Abs(left - right) <= scale * 1e-9;
        }

        private bool RefreshOverlayDirtyStateFromViewport()
        {
            if (_chart == null)
            {
                return false;
            }

            AxisX xAxis = _chart.ViewXY.XAxes[0];
            double currentMin = xAxis.Minimum;
            double currentMax = xAxis.Maximum;
            double currentWidth = _chart.ActualWidth;
            double currentHeight = _chart.ActualHeight;

            bool viewportChanged =
                AreClose(_lastViewportMin, currentMin) == false
                || AreClose(_lastViewportMax, currentMax) == false
                || AreClose(_lastViewportWidth, currentWidth) == false
                || AreClose(_lastViewportHeight, currentHeight) == false;

            if (viewportChanged)
            {
                _lastViewportMin = currentMin;
                _lastViewportMax = currentMax;
                _lastViewportWidth = currentWidth;
                _lastViewportHeight = currentHeight;
                MarkDecodeOverlayDirty();
                MarkMeasurementVisualDirty();
            }

            return viewportChanged;
        }

        private void UpdateXAxisView(double lastX)
        {
            MarkDecodeOverlayDirty();
            MarkMeasurementVisualDirty();
        }

        private ChartSignal CreateChartSignal(ViewXY view, int seriesIndex)
        {
            ChartSignal signal = new ChartSignal(string.Format("通道 {0}", seriesIndex + 1));
            signal.DecodeSettings.BaudRate = 19200;
            signal.DecodeSettings.DataBits = 8;
            signal.DecodeSettings.StopBits = 1;
            signal.DecodeSettings.ParityMode = UartParityMode.None;
            signal.DecodeSettings.IdleBits = 4;
            signal.DecodeSettings.SamplesPerBit = 1;
            Color lineBaseColor = DefaultColors.SeriesForBlackBackgroundWpf[seriesIndex % DefaultColors.SeriesForBlackBackgroundWpf.Length];

            AxisY axisY = new AxisY(view);
            axisY.SetRange(YMin, YMax);

            axisY.Title.Text = signal.Name;
            axisY.Title.Angle = 90;
            axisY.Title.Color = ChartTools.CalcGradient(lineBaseColor, Colors.White, 50);
            axisY.Units.Visible = false;
            axisY.AllowScaling = false;
            axisY.AllowScrolling = false;
            axisY.MajorGrid.Visible = false;
            axisY.MinorGrid.Visible = false;
            axisY.MajorGrid.Pattern = LinePattern.Solid;
            axisY.AutoDivSeparationPercent = 0;
            axisY.Units.Text = "毫伏";
            axisY.Visible = true;
            axisY.LabelsVisible = false;
            axisY.MajorDivTickStyle.Visible = false;
            axisY.MajorDivTickStyle.Alignment = Alignment.Near;
            axisY.MinorDivTickStyle.Visible = false;
            axisY.Title.HorizontalAlign = YAxisTitleAlignmentHorizontal.Left;

            //if (seriesIndex == _seriesCount - 1)
            //{
            axisY.MiniScale.ShowX = true;
            axisY.MiniScale.ShowY = false;
            axisY.MiniScale.Color = Color.FromArgb(255, 255, 204, 0);
            axisY.MiniScale.HorizontalAlign = AlignmentHorizontal.Right;
            axisY.MiniScale.VerticalAlign = AlignmentVertical.Bottom;
            axisY.MiniScale.Offset = new PointIntXY(-30, -30);
            axisY.MiniScale.LabelX.Color = Colors.White;
            axisY.MiniScale.LabelY.Color = Colors.White;
            axisY.MiniScale.PreferredSize = new Arction.Wpf.Charting.SizeDoubleXY(50, 50);
            //}

            view.YAxes.Add(axisY);

            DigitalLineSeries series = new DigitalLineSeries(view, view.XAxes[0], axisY);
            view.DigitalLineSeries.Add(series);
            Color seriesColor = ChartTools.CalcGradient(lineBaseColor, Colors.White, 50);
            series.Color = seriesColor;
            series.Width = LineWidth;
            series.DigitalLow = 0;
            series.DigitalHigh = 1;
            series.LimitYToStackSegment = true;
            series.AllowUserInteraction = false;

            signal.AxisY = axisY;
            signal.Series = series;
            signal.SeriesColor = seriesColor;
            return signal;
        }



        private void UpdateSweepBands(double lastX)
        {
            if (_chart.ViewXY.XAxes[0].ScrollMode == XAxisScrollMode.Sweeping)
            {
                double pageLen = _chart.ViewXY.XAxes[0].Maximum - _chart.ViewXY.XAxes[0].Minimum;
                double sweepGapWidth = pageLen / 20.0;
                _chart.ViewXY.Bands[0].SetValues(lastX - pageLen, lastX - pageLen + sweepGapWidth);
                if (_chart.ViewXY.Bands[0].Visible == false)
                {
                    _chart.ViewXY.Bands[0].Visible = true;
                }

                _chart.ViewXY.Bands[1].SetValues(lastX - sweepGapWidth / 6, lastX);
                if (_chart.ViewXY.Bands[1].Visible == false)
                {
                    _chart.ViewXY.Bands[1].Visible = true;
                }
            }
            else
            {
                if (_chart.ViewXY.Bands[0].Visible == true)
                {
                    _chart.ViewXY.Bands[0].Visible = false;
                }

                if (_chart.ViewXY.Bands[1].Visible == true)
                {
                    _chart.ViewXY.Bands[1].Visible = false;
                }
            }
        }

        private void SetYAxisVisible(AxisY yAxis, bool visible)
        {
            yAxis.LabelsVisible = visible;
            yAxis.MajorDivTickStyle.Visible = visible;
            yAxis.MinorDivTickStyle.Visible = false;
            yAxis.Title.Visible = visible;
            yAxis.AxisThickness = visible ? 3 : 0;
        }

        private void ZoomX(double factor, double? anchorX = null)
        {
            _chart.BeginUpdate();
            AxisX xAxis = _chart.ViewXY.XAxes[0];
            double currentMin = xAxis.Minimum;
            double currentMax = xAxis.Maximum;
            double zoomAnchor = anchorX ?? ((currentMin + currentMax) / 2.0);

            if (zoomAnchor < currentMin)
            {
                zoomAnchor = currentMin;
            }
            else if (zoomAnchor > currentMax)
            {
                zoomAnchor = currentMax;
            }

            double newMin = zoomAnchor - (zoomAnchor - currentMin) * factor;
            double newMax = zoomAnchor + (currentMax - zoomAnchor) * factor;
            xAxis.SetRange(newMin, newMax);
            _chart.EndUpdate();
            MarkDecodeOverlayDirty();
            UpdateMeasurementVisual();
        }

        private void ZoomY(AxisY yAxis, double factor, double? anchorY = null)
        {
            if (yAxis == null)
            {
                return;
            }

            _chart.BeginUpdate();
            double currentMin = yAxis.Minimum;
            double currentMax = yAxis.Maximum;
            double zoomAnchor = anchorY ?? ((currentMin + currentMax) / 2.0);

            if (zoomAnchor < currentMin)
            {
                zoomAnchor = currentMin;
            }
            else if (zoomAnchor > currentMax)
            {
                zoomAnchor = currentMax;
            }

            double newMin = zoomAnchor - (zoomAnchor - currentMin) * factor;
            double newMax = zoomAnchor + (currentMax - zoomAnchor) * factor;
            yAxis.SetRange(newMin, newMax);
            _chart.EndUpdate();
            UpdateMeasurementVisual();
        }
    }
}

