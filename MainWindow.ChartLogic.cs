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

namespace InteractiveExamples
{
    public partial class Example8BillionPoints
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

        private void UpdateCursorButton()
        {
            UpdateXAxisViewModeButton(buttonCursor, _isCursorEnabled);
        }

        private void UpdatePointsButton()
        {
            UpdateXAxisViewModeButton(buttonPoints, _arePointsVisible);
        }

        private void UpdateDecodeButton()
        {
            UpdateXAxisViewModeButton(buttonDecode, _isDecodeVisible);
        }

        private void SetPointsVisible(bool visible)
        {
            _arePointsVisible = visible;
            UpdatePointsButton();

            if (_chart == null)
            {
                return;
            }

            _chart.BeginUpdate();
            try
            {
                foreach (ChartSignal signal in _chartSignals)
                {
                    if (signal.Series != null)
                    {
                        signal.Series.PointsVisible = _arePointsVisible;
                    }
                }
            }
            finally
            {
                _chart.EndUpdate();
            }
        }

        private void SetDecodeVisible(bool visible)
        {
            _isDecodeVisible = visible;
            UpdateDecodeButton();
            UpdateDecodeOverlay();
        }

        private void SetCursorEnabled(bool enabled)
        {
            _isCursorEnabled = enabled;
            _isCursorDragging = false;

            if (_chart != null && _chart.IsMouseCaptured)
            {
                _chart.ReleaseMouseCapture();
            }

            if (_isCursorEnabled)
            {
                ResetCursorToVisibleRangeCenter();
            }

            UpdateCursorButton();
            UpdateCursorVisual();
        }

        private void ResetCursorToVisibleRangeCenter()
        {
            if (_chart == null)
            {
                return;
            }

            AxisX xAxis = _chart.ViewXY.XAxes[0];
            _cursorXValue = (xAxis.Minimum + xAxis.Maximum) / 2.0;
            _hoverMeasurementXValue = _cursorXValue;
            _cursorMeasurementSignal = FindPreferredMeasurementSignal();
        }

        private void SetCursorXFromControlX(double controlX)
        {
            double? xValue = TryGetXAxisValueAt(controlX);
            if (xValue.HasValue == false)
            {
                return;
            }

            _cursorXValue = xValue.Value;
            UpdateCursorVisual();
        }

        private void SetCursorFromControlPosition(double controlX, double controlY)
        {
            double? xValue = TryGetXAxisValueAt(controlX);
            if (xValue.HasValue == false)
            {
                return;
            }

            _hoverMeasurementXValue = xValue.Value;
            AxisY targetAxis = TryGetYAxisAt(controlY);
            if (targetAxis != null)
            {
                ChartSignal targetSignal = TryGetSignalByAxis(targetAxis);
                if (targetSignal != null && targetSignal.Kind != SignalValueKind.Analog)
                {
                    _cursorMeasurementSignal = targetSignal;
                }
            }

            if (_isCursorEnabled && _isCursorDragging)
            {
                _cursorXValue = xValue.Value;
            }

            UpdateCursorVisual();
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
            if (_cursorMeasurementSignal != null
                && _cursorMeasurementSignal.Kind != SignalValueKind.Analog
                && _cursorMeasurementSignal.AxisY != null
                && _cursorMeasurementSignal.AxisY.Visible)
            {
                return _cursorMeasurementSignal;
            }

            for (int i = 0; i < _chartSignals.Count; i++)
            {
                ChartSignal signal = _chartSignals[i];
                if (signal != null
                    && signal.Kind != SignalValueKind.Analog
                    && signal.AxisY != null
                    && signal.AxisY.Visible)
                {
                    return signal;
                }
            }

            return null;
        }

        private void EnsureCursorAxisValueLabels()
        {
            if (_cursorOverlay == null)
            {
                return;
            }

            while (_cursorAxisValueBorders.Count < _chartSignals.Count)
            {
                TextBlock textBlock = new TextBlock
                {
                    Foreground = Brushes.White,
                    FontSize = 12,
                    FontWeight = FontWeights.Bold
                };

                System.Windows.Controls.Border border = new System.Windows.Controls.Border
                {
                    BorderThickness = new Thickness(1),
                    CornerRadius = new CornerRadius(4),
                    Padding = new Thickness(6, 2, 6, 2),
                    Child = textBlock,
                    Visibility = Visibility.Collapsed
                };

                _cursorAxisValueTexts.Add(textBlock);
                _cursorAxisValueBorders.Add(border);
                _cursorOverlay.Children.Add(border);
            }
        }

        private void HideCursorAxisValueLabels()
        {
            foreach (System.Windows.Controls.Border border in _cursorAxisValueBorders)
            {
                border.Visibility = Visibility.Collapsed;
            }
        }

        private bool TryGetCursorYAxisValue(ChartSignal signal, out double yValue)
        {
            yValue = 0;
            if (signal == null || signal.Series == null || signal.AxisY == null || signal.Series.PointCount <= 0)
            {
                return false;
            }

            LineSeriesValueSolveResult solvedValue = signal.Series.SolveYValueAtXValue(_cursorXValue);
            if (solvedValue.NearestDataPointIndex < 0)
            {
                return false;
            }

            yValue = solvedValue.YMin;
            if (double.IsNaN(yValue) || double.IsInfinity(yValue))
            {
                return false;
            }

            if (double.IsNaN(solvedValue.YMax) == false && double.IsInfinity(solvedValue.YMax) == false)
            {
                if (Math.Abs(solvedValue.YMax - solvedValue.YMin) > double.Epsilon)
                {
                    yValue = (solvedValue.YMin + solvedValue.YMax) / 2.0;
                }
            }

            return true;
        }

        private enum DigitalEdgeDirection
        {
            Rising,
            Falling
        }

        private sealed class DigitalEdge
        {
            public double X { get; set; }
            public double ValueAfter { get; set; }
            public DigitalEdgeDirection Direction { get; set; }
        }

        private sealed class CursorMeasurement
        {
            public ChartSignal Signal { get; set; }
            public DigitalEdgeDirection Direction { get; set; }
            public double Width { get; set; }
            public double Period { get; set; }
            public double FrequencyHz { get; set; }
            public double DutyCyclePercent { get; set; }
            public double DisplayYValue { get; set; }
            public double PeriodStartX { get; set; }
            public double PeriodEndX { get; set; }
            public int MatchRank { get; set; }
            public double DistanceToCursor { get; set; }
        }

        private bool TryGetCursorMeasurement(double measurementXValue, out CursorMeasurement measurement)
        {
            measurement = null;
            if (_chart == null)
            {
                return false;
            }

            ChartSignal signal = FindPreferredMeasurementSignal();
            if (signal == null
                || signal.Kind == SignalValueKind.Analog
                || signal.AxisY == null
                || signal.AxisY.Visible == false)
            {
                return false;
            }

            _cursorMeasurementSignal = signal;
            if (TryBuildCursorMeasurement(signal, measurementXValue, out measurement) == false)
            {
                return false;
            }

            return measurement != null;
        }

        private static bool TryBuildCursorMeasurement(ChartSignal signal, double cursorX, out CursorMeasurement measurement)
        {
            measurement = null;
            if (signal == null)
            {
                return false;
            }

            SeriesPoint[] points = signal.GetAllRecentPointsSnapshot();
            List<DigitalEdge> edges = BuildDigitalEdges(points);
            if (edges.Count < 3)
            {
                return false;
            }

            CursorMeasurement bestMeasurement = null;
            for (int i = 0; i <= edges.Count - 3; i++)
            {
                DigitalEdge firstEdge = edges[i];
                DigitalEdge middleEdge = edges[i + 1];
                DigitalEdge lastEdge = edges[i + 2];

                if (firstEdge.Direction == middleEdge.Direction || firstEdge.Direction != lastEdge.Direction)
                {
                    continue;
                }

                double width = middleEdge.X - firstEdge.X;
                double period = lastEdge.X - firstEdge.X;
                if (width <= 0 || period <= 0 || width > period)
                {
                    continue;
                }

                int matchRank = 2;
                double distanceToCursor = Math.Abs(cursorX - (firstEdge.X + lastEdge.X) / 2.0);
                if (cursorX >= firstEdge.X && cursorX <= middleEdge.X)
                {
                    matchRank = 0;
                    distanceToCursor = 0;
                }
                else if (cursorX >= firstEdge.X && cursorX <= lastEdge.X)
                {
                    matchRank = 1;
                    distanceToCursor = Math.Abs(cursorX - (firstEdge.X + lastEdge.X) / 2.0);
                }

                CursorMeasurement candidate = new CursorMeasurement
                {
                    Signal = signal,
                    Direction = firstEdge.Direction,
                    Width = width,
                    Period = period,
                    FrequencyHz = 1.0 / period,
                    DutyCyclePercent = width / period * 100.0,
                    DisplayYValue = firstEdge.ValueAfter,
                    PeriodStartX = firstEdge.X,
                    PeriodEndX = lastEdge.X,
                    MatchRank = matchRank,
                    DistanceToCursor = distanceToCursor
                };

                if (bestMeasurement == null
                    || candidate.MatchRank < bestMeasurement.MatchRank
                    || (candidate.MatchRank == bestMeasurement.MatchRank && candidate.DistanceToCursor < bestMeasurement.DistanceToCursor))
                {
                    bestMeasurement = candidate;
                }
            }

            measurement = bestMeasurement;
            return measurement != null;
        }

        private static List<DigitalEdge> BuildDigitalEdges(SeriesPoint[] points)
        {
            List<DigitalEdge> edges = new List<DigitalEdge>();
            if (points == null || points.Length < 2)
            {
                return edges;
            }

            for (int i = 1; i < points.Length; i++)
            {
                double previousValue = points[i - 1].Y;
                double currentValue = points[i].Y;
                bool previousHigh = previousValue >= 0.5;
                bool currentHigh = currentValue >= 0.5;
                if (previousHigh == currentHigh)
                {
                    continue;
                }

                edges.Add(new DigitalEdge
                {
                    X = points[i].X,
                    ValueAfter = currentHigh ? 1.0 : 0.0,
                    Direction = currentHigh ? DigitalEdgeDirection.Rising : DigitalEdgeDirection.Falling
                });
            }

            return edges;
        }

        private string BuildCursorMeasurementText(CursorMeasurement measurement)
        {
            if (measurement == null)
            {
                return _cursorXValue.ToString("0.000");
            }

            string edgeLabel = measurement.Direction == DigitalEdgeDirection.Rising ? "上升沿" : "下降沿";
            return string.Format(
                "{0}\n宽度: {1}\n周期: {2}\n频率: {3}\n占空比: {4:0.00}%",
                edgeLabel,
                FormatTimeValue(measurement.Width),
                FormatTimeValue(measurement.Period),
                FormatFrequencyValue(measurement.FrequencyHz),
                measurement.DutyCyclePercent);
        }

        private string FormatTimeValue(double axisValue)
        {
            double seconds = axisValue * GetXAxisSecondsPerUnit();
            double absoluteSeconds = Math.Abs(seconds);
            if (absoluteSeconds >= 1.0)
            {
                return string.Format("{0:0.###} s", seconds);
            }

            if (absoluteSeconds >= 1e-3)
            {
                return string.Format("{0:0.###} ms", seconds * 1e3);
            }

            if (absoluteSeconds >= 1e-6)
            {
                return string.Format("{0:0.###} us", seconds * 1e6);
            }

            if (absoluteSeconds >= 1e-9)
            {
                return string.Format("{0:0.###} ns", seconds * 1e9);
            }

            return string.Format("{0:0.###E+0} s", seconds);
        }

        private string FormatFrequencyValue(double frequencyHz)
        {
            double absoluteFrequency = Math.Abs(frequencyHz);
            if (absoluteFrequency >= 1e6)
            {
                return string.Format("{0:0.###} MHz", frequencyHz / 1e6);
            }

            if (absoluteFrequency >= 1e3)
            {
                return string.Format("{0:0.###} kHz", frequencyHz / 1e3);
            }

            return string.Format("{0:0.###} Hz", frequencyHz);
        }

        private double GetXAxisSecondsPerUnit()
        {
            if (_chart == null)
            {
                return 1.0;
            }

            string unitsText = _chart.ViewXY.XAxes[0].Units.Text;
            if (string.Equals(unitsText, "us", StringComparison.OrdinalIgnoreCase))
            {
                return 1e-6;
            }

            if (string.Equals(unitsText, "ms", StringComparison.OrdinalIgnoreCase))
            {
                return 1e-3;
            }

            if (string.Equals(unitsText, "ns", StringComparison.OrdinalIgnoreCase))
            {
                return 1e-9;
            }

            return 1.0;
        }

        private void HideCursorMeasurementVisuals()
        {
            if (_cursorMeasurementStartLine != null)
            {
                _cursorMeasurementStartLine.Visibility = Visibility.Collapsed;
            }

            if (_cursorMeasurementEndLine != null)
            {
                _cursorMeasurementEndLine.Visibility = Visibility.Collapsed;
            }

            if (_cursorMeasurementSpanLine != null)
            {
                _cursorMeasurementSpanLine.Visibility = Visibility.Collapsed;
            }
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

        private void UpdateCursorMeasurementVisual(CursorMeasurement measurement, double plotLeft, double plotRight)
        {
            if (_cursorMeasurementStartLine == null
                || _cursorMeasurementEndLine == null
                || _cursorMeasurementSpanLine == null
                || measurement == null
                || measurement.Signal == null
                || measurement.Signal.AxisY == null
                || _chart == null)
            {
                HideCursorMeasurementVisuals();
                return;
            }

            AxisX xAxis = _chart.ViewXY.XAxes[0];
            double visibleMin = xAxis.Minimum;
            double visibleMax = xAxis.Maximum;
            if (visibleMax <= visibleMin)
            {
                HideCursorMeasurementVisuals();
                return;
            }

            double segmentTop;
            double segmentBottom;
            if (TryGetYAxisBounds(measurement.Signal.AxisY, out segmentTop, out segmentBottom) == false)
            {
                HideCursorMeasurementVisuals();
                return;
            }

            double startCoord = plotLeft + (measurement.PeriodStartX - visibleMin) / (visibleMax - visibleMin) * (plotRight - plotLeft);
            double endCoord = plotLeft + (measurement.PeriodEndX - visibleMin) / (visibleMax - visibleMin) * (plotRight - plotLeft);
            if (double.IsNaN(startCoord) || double.IsNaN(endCoord) || double.IsInfinity(startCoord) || double.IsInfinity(endCoord))
            {
                HideCursorMeasurementVisuals();
                return;
            }

            startCoord = Math.Max(plotLeft, Math.Min(plotRight, startCoord));
            endCoord = Math.Max(plotLeft, Math.Min(plotRight, endCoord));
            if (Math.Abs(endCoord - startCoord) < 1.0)
            {
                HideCursorMeasurementVisuals();
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

            _cursorMeasurementStartLine.X1 = startCoord;
            _cursorMeasurementStartLine.X2 = startCoord;
            _cursorMeasurementStartLine.Y1 = lineTop;
            _cursorMeasurementStartLine.Y2 = lineBottom;
            _cursorMeasurementStartLine.Visibility = Visibility.Visible;

            _cursorMeasurementEndLine.X1 = endCoord;
            _cursorMeasurementEndLine.X2 = endCoord;
            _cursorMeasurementEndLine.Y1 = lineTop;
            _cursorMeasurementEndLine.Y2 = lineBottom;
            _cursorMeasurementEndLine.Visibility = Visibility.Visible;

            _cursorMeasurementSpanLine.X1 = Math.Min(startCoord, endCoord);
            _cursorMeasurementSpanLine.X2 = Math.Max(startCoord, endCoord);
            _cursorMeasurementSpanLine.Y1 = signalY;
            _cursorMeasurementSpanLine.Y2 = signalY;
            _cursorMeasurementSpanLine.Visibility = Visibility.Visible;
        }

        private void UpdateCursorVisual()
        {
            if (_cursorOverlay == null || _cursorLine == null || _cursorValueBorder == null || _cursorValueText == null)
            {
                return;
            }

            EnsureCursorAxisValueLabels();

            bool showCursor = _isCursorEnabled;
            bool showMeasurement = _isCursorHovering;

            if ((showCursor == false && showMeasurement == false) || _chart == null)
            {
                _cursorOverlay.Visibility = Visibility.Collapsed;
                _cursorLine.Visibility = Visibility.Collapsed;
                _cursorValueBorder.Visibility = Visibility.Collapsed;
                HideCursorMeasurementVisuals();
                HideCursorAxisValueLabels();
                return;
            }

            double plotLeft;
            double plotTop;
            double plotRight;
            double plotBottom;
            if (TryGetPlotAreaBounds(out plotLeft, out plotTop, out plotRight, out plotBottom) == false)
            {
                _cursorOverlay.Visibility = Visibility.Collapsed;
                _cursorLine.Visibility = Visibility.Collapsed;
                _cursorValueBorder.Visibility = Visibility.Collapsed;
                HideCursorMeasurementVisuals();
                HideCursorAxisValueLabels();
                return;
            }

            AxisX xAxis = _chart.ViewXY.XAxes[0];
            double currentMin = xAxis.Minimum;
            double currentMax = xAxis.Maximum;
            if (currentMax <= currentMin)
            {
                _cursorOverlay.Visibility = Visibility.Collapsed;
                _cursorLine.Visibility = Visibility.Collapsed;
                _cursorValueBorder.Visibility = Visibility.Collapsed;
                HideCursorMeasurementVisuals();
                HideCursorAxisValueLabels();
                return;
            }

            double normalizedX = (_cursorXValue - currentMin) / (currentMax - currentMin);
            double measurementNormalizedX = (_hoverMeasurementXValue - currentMin) / (currentMax - currentMin);
            bool isCursorXVisible = normalizedX >= 0 && normalizedX <= 1;
            bool isMeasurementXVisible = measurementNormalizedX >= 0 && measurementNormalizedX <= 1;
            if ((showCursor && isCursorXVisible == false) && (showMeasurement && isMeasurementXVisible == false))
            {
                _cursorOverlay.Visibility = Visibility.Visible;
                _cursorLine.Visibility = Visibility.Collapsed;
                _cursorValueBorder.Visibility = Visibility.Collapsed;
                HideCursorMeasurementVisuals();
                HideCursorAxisValueLabels();
                return;
            }

            double xCoord = plotLeft + normalizedX * (plotRight - plotLeft);
            double measurementXCoord = plotLeft + measurementNormalizedX * (plotRight - plotLeft);

            _cursorOverlay.Width = _chart.ActualWidth;
            _cursorOverlay.Height = _chart.ActualHeight;
            _cursorOverlay.Visibility = Visibility.Visible;

            if (showCursor && isCursorXVisible)
            {
                _cursorLine.X1 = xCoord;
                _cursorLine.X2 = xCoord;
                _cursorLine.Y1 = plotTop;
                _cursorLine.Y2 = plotBottom;
                _cursorLine.Visibility = Visibility.Visible;
            }
            else
            {
                _cursorLine.Visibility = Visibility.Collapsed;
            }

            CursorMeasurement measurement = null;
            if (showMeasurement && isMeasurementXVisible)
            {
                TryGetCursorMeasurement(_hoverMeasurementXValue, out measurement);
            }

            if (measurement != null && showMeasurement && isMeasurementXVisible)
            {
                _cursorValueText.Text = BuildCursorMeasurementText(measurement);
                _cursorValueBorder.BorderBrush = new SolidColorBrush(Color.FromArgb(
                    235,
                    measurement.Signal.Series.LineStyle.Color.R,
                    measurement.Signal.Series.LineStyle.Color.G,
                    measurement.Signal.Series.LineStyle.Color.B));
                UpdateCursorMeasurementVisual(measurement, plotLeft, plotRight);

                _cursorValueBorder.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));

                double labelWidth = _cursorValueBorder.DesiredSize.Width;
                double labelHeight = _cursorValueBorder.DesiredSize.Height;
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

                Canvas.SetLeft(_cursorValueBorder, labelLeft);
                Canvas.SetTop(_cursorValueBorder, labelTop);
                _cursorValueBorder.Visibility = Visibility.Visible;
            }
            else if (showCursor && isCursorXVisible)
            {
                HideCursorMeasurementVisuals();
                _cursorValueText.Text = _cursorXValue.ToString("0.000");
                _cursorValueBorder.BorderBrush = new SolidColorBrush(Color.FromArgb(235, 255, 196, 64));
                _cursorValueBorder.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));

                double labelWidth = _cursorValueBorder.DesiredSize.Width;
                double labelHeight = _cursorValueBorder.DesiredSize.Height;
                double labelLeft = Math.Max(plotLeft, Math.Min(plotRight - labelWidth, xCoord - labelWidth / 2.0));
                double labelTop = Math.Max(0, plotBottom - labelHeight - 4.0);

                Canvas.SetLeft(_cursorValueBorder, labelLeft);
                Canvas.SetTop(_cursorValueBorder, labelTop);
                _cursorValueBorder.Visibility = Visibility.Visible;
            }
            else
            {
                _cursorValueBorder.Visibility = Visibility.Collapsed;
                HideCursorMeasurementVisuals();
            }

            if (showCursor == false || isCursorXVisible == false)
            {
                HideCursorAxisValueLabels();
                return;
            }

            for (int i = 0; i < _chartSignals.Count; i++)
            {
                ChartSignal signal = _chartSignals[i];
                System.Windows.Controls.Border axisValueBorder = _cursorAxisValueBorders[i];
                TextBlock axisValueText = _cursorAxisValueTexts[i];

                if (signal.AxisY == null || signal.AxisY.Visible == false || TryGetCursorYAxisValue(signal, out double yValue) == false)
                {
                    axisValueBorder.Visibility = Visibility.Collapsed;
                    continue;
                }

                double yCoord = signal.AxisY.ValueToCoord(yValue, true);
                if (double.IsNaN(yCoord) || double.IsInfinity(yCoord))
                {
                    axisValueBorder.Visibility = Visibility.Collapsed;
                    continue;
                }

                yCoord = Math.Max(plotTop, Math.Min(plotBottom, yCoord));

                axisValueText.Text = string.Format("{0}: {1:0.###}", signal.Name, yValue);
                Color signalColor = signal.Series.LineStyle.Color;
                axisValueText.Foreground = new SolidColorBrush(signalColor);
                axisValueBorder.Background = new SolidColorBrush(Color.FromArgb(205, 20, 22, 26));
                axisValueBorder.BorderBrush = new SolidColorBrush(Color.FromArgb(230, signalColor.R, signalColor.G, signalColor.B));
                axisValueBorder.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));

                double axisLabelWidth = axisValueBorder.DesiredSize.Width;
                double axisLabelHeight = axisValueBorder.DesiredSize.Height;
                double axisLabelLeft = Math.Max(plotLeft, plotRight - axisLabelWidth - 6);
                double axisLabelTop = Math.Max(plotTop, Math.Min(plotBottom - axisLabelHeight, yCoord - axisLabelHeight / 2.0));

                Canvas.SetLeft(axisValueBorder, axisLabelLeft);
                Canvas.SetTop(axisValueBorder, axisLabelTop);
                axisValueBorder.Visibility = Visibility.Visible;
            }
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

        private const double DecodeVerticalOffset = -30.0;

        private void UpdateDecodeOverlay()
        {
            if (_decodeOverlay == null)
            {
                return;
            }
            _decodeOverlay.Children.Clear();
            if (_isDecodeVisible == false || _chart == null)
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

                if (signal.Kind != SignalValueKind.Analog)
                {
                    double rowHeight = Math.Max(28.0, Math.Min(36.0, segmentHeight * 0.34));
                    rowHeight = Math.Min(rowHeight, Math.Max(4.0, segmentHeight - 2.0));
                    double rowTop = currentTop + Math.Max(1.0, Math.Min(4.0, (segmentHeight - rowHeight) / 2.0));
                    rows.Add(new DecodeRowLayout
                    {
                        Signal = signal,
                        Top = rowTop + DecodeVerticalOffset,
                        Height = rowHeight
                    });
                }

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
            double left = plotLeft + (segment.StartX - visibleMin) / (visibleMax - visibleMin) * plotWidth;
            double right = plotLeft + (segment.EndX - visibleMin) / (visibleMax - visibleMin) * plotWidth;
            double width = right - left;
            if (width < 4.0)
            {
                return;
            }

            System.Windows.Shapes.Shape shape;
            if (width < rowHeight * 1.3)
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
            if (signal == null || signal.Kind == SignalValueKind.Analog || visibleMax <= visibleMin)
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
                SeriesPoint[] history = signal.GetAllRecentPointsSnapshot();
                cacheEntry = new DecodeCacheEntry
                {
                    HistoryVersion = historyVersion,
                    DecodeSettingsVersion = decodeSettingsVersion,
                    Segments = DecodeLogic.BuildProtocolSegments(
                        history,
                        decodeSettings.BaudRate,
                        decodeSettings.DataBits,
                        decodeSettings.StopBits,
                        decodeSettings.ParityMode,
                        decodeSettings.IdleBits)
                };

                _decodeCache[signal] = cacheEntry;
            }

            return ClipProtocolSegments(cacheEntry.Segments, visibleMin, visibleMax);
        }

        private static List<ProtocolSegment> ClipProtocolSegments(List<ProtocolSegment> segments, double visibleMin, double visibleMax)
        {
            List<ProtocolSegment> visibleSegments = new List<ProtocolSegment>();
            if (segments == null || segments.Count == 0 || visibleMax <= visibleMin)
            {
                return visibleSegments;
            }

            for (int i = 0; i < segments.Count; i++)
            {
                ProtocolSegment segment = segments[i];
                if (segment.EndX < visibleMin || segment.StartX > visibleMax)
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

        private static void UpdateXAxisViewModeButton(Button button, bool isActive)
        {
            if (button == null)
            {
                return;
            }

            button.Background = isActive ? new SolidColorBrush(Color.FromRgb(255, 204, 0)) : new SolidColorBrush(Color.FromRgb(70, 70, 70));
            button.BorderBrush = isActive ? new SolidColorBrush(Color.FromRgb(255, 230, 140)) : new SolidColorBrush(Color.FromRgb(120, 120, 120));
            button.Foreground = isActive ? Brushes.Black : Brushes.White;
        }

        private void UpdateXAxisView(double lastX)
        {
            UpdateCursorVisual();
        }

        private ChartSignal CreateChartSignal(ViewXY view, int seriesIndex)
        {
            SignalValueKind kind = SignalValueKind.Analog;
            //if (seriesIndex == 0)
            //{
                kind = SignalValueKind.StepDigital;
            //}

            //if (seriesIndex == 1)
            //{
            //    kind = SignalValueKind.Digital;
            //}


            ChartSignal signal = new ChartSignal(string.Format("Signal {0}", seriesIndex + 1), kind, YMin, YMax);
            signal.DecodeSettings.BaudRate = 19200;
            signal.DecodeSettings.DataBits = 8;
            signal.DecodeSettings.StopBits = 1;
            signal.DecodeSettings.ParityMode = UartParityMode.None;
            signal.DecodeSettings.IdleBits = 4;
            Color lineBaseColor = DefaultColors.SeriesForBlackBackgroundWpf[seriesIndex % DefaultColors.SeriesForBlackBackgroundWpf.Length];

            AxisY axisY = new AxisY(view);
            //if (kind == SignalValueKind.Analog)
            //{
            //    axisY.SetRange(YMin, YMax);
            //}
            //else
            //{
                axisY.SetRange(YMin, YMax);
            //}

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
            axisY.Units.Text = "mV";
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

            PointLineSeries series = new PointLineSeries(view, view.XAxes[0], axisY);
            view.PointLineSeries.Add(series);
            Color seriesColor = ChartTools.CalcGradient(lineBaseColor, Colors.White, 50);
            series.LineStyle.Color = seriesColor;
            series.LineStyle.Width = LineWidth;
            series.PointStyle.Color1 = seriesColor;
            series.PointStyle.Color2 = seriesColor;
            series.PointStyle.Color3 = seriesColor;
            series.PointStyle.BorderColor = seriesColor;
            series.PointStyle.Width = 4;
            series.PointStyle.Height = 4;
            series.PointStyle.BorderWidth = 0.5;
            series.LimitYToStackSegment = true;
            series.AllowUserInteraction = false;
            series.PointsVisible = _arePointsVisible;

            signal.AxisY = axisY;
            signal.Series = series;
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
            UpdateCursorVisual();
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
            UpdateCursorVisual();
        }
    }
}
