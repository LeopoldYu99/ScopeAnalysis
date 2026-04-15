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

        private void FitYAxisToAll()
        {
            if (_chart == null)
            {
                return;
            }

            _chart.BeginUpdate();
            try
            {
                foreach (AxisY yAxis in _chart.ViewXY.YAxes)
                {
                    if (yAxis.Visible == false)
                    {
                        continue;
                    }

                    bool scaleChanged = false;
                    yAxis.Fit(0.0, out scaleChanged, true, false);
                }
            }
            finally
            {
                _chart.EndUpdate();
            }

            UpdateCursorVisual();
        }

        private void FitXAxisToAll()
        {
            if (_chart == null)
            {
                return;
            }

            // Prevent follow/page mode from immediately overriding the fitted range.
            SetXAxisViewMode(XAxisViewMode.Free);

            _chart.BeginUpdate();
            try
            {
                bool scaleChanged = false;
                _chart.ViewXY.XAxes[0].Fit(out scaleChanged, true);
            }
            finally
            {
                _chart.EndUpdate();
            }

            UpdateCursorVisual();
        }

        private void FitXYToAll()
        {
            if (_chart == null)
            {
                return;
            }

            // Prevent follow/page mode from immediately overriding the fitted X range.
            SetXAxisViewMode(XAxisViewMode.Free);

            _chart.BeginUpdate();
            try
            {
                _chart.ViewXY.ZoomToFit();
            }
            finally
            {
                _chart.EndUpdate();
            }

            UpdateCursorVisual();
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

        private void UpdateCursorVisual()
        {
            if (_cursorOverlay == null || _cursorLine == null || _cursorValueBorder == null || _cursorValueText == null)
            {
                return;
            }

            EnsureCursorAxisValueLabels();

            if (_isCursorEnabled == false || _chart == null)
            {
                _cursorOverlay.Visibility = Visibility.Collapsed;
                _cursorLine.Visibility = Visibility.Collapsed;
                _cursorValueBorder.Visibility = Visibility.Collapsed;
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
                HideCursorAxisValueLabels();
                return;
            }

            double normalizedX = (_cursorXValue - currentMin) / (currentMax - currentMin);
            if (normalizedX < 0 || normalizedX > 1)
            {
                _cursorOverlay.Visibility = Visibility.Visible;
                _cursorLine.Visibility = Visibility.Collapsed;
                _cursorValueBorder.Visibility = Visibility.Collapsed;
                HideCursorAxisValueLabels();
                return;
            }

            double xCoord = plotLeft + normalizedX * (plotRight - plotLeft);

            _cursorOverlay.Width = _chart.ActualWidth;
            _cursorOverlay.Height = _chart.ActualHeight;
            _cursorOverlay.Visibility = Visibility.Visible;

            _cursorLine.X1 = xCoord;
            _cursorLine.X2 = xCoord;
            _cursorLine.Y1 = plotTop;
            _cursorLine.Y2 = plotBottom;
            _cursorLine.Visibility = Visibility.Visible;

            _cursorValueText.Text = _cursorXValue.ToString("0.000");
            _cursorValueBorder.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));

            double labelWidth = _cursorValueBorder.DesiredSize.Width;
            double labelHeight = _cursorValueBorder.DesiredSize.Height;
            double labelLeft = Math.Max(plotLeft, Math.Min(plotRight - labelWidth, xCoord - labelWidth / 2.0));
            double labelTop = Math.Max(0, plotBottom - labelHeight - 4);

            Canvas.SetLeft(_cursorValueBorder, labelLeft);
            Canvas.SetTop(_cursorValueBorder, labelTop);
            _cursorValueBorder.Visibility = Visibility.Visible;

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
                    double rowHeight = Math.Max(18.0, Math.Min(24.0, segmentHeight * 0.24));
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

        private List<ProtocolSegment> BuildProtocolSegments(ChartSignal signal, double visibleMin, double visibleMax)
        {
            List<ProtocolSegment> segments = new List<ProtocolSegment>();
            if (signal == null || signal.Kind == SignalValueKind.Analog)
            {
                return segments;
            }

            SeriesPoint[] history = signal.GetRecentPointsSnapshot(Math.Max((visibleMax - visibleMin) * 6.0, 2.0));
            if (history == null || history.Length < 2)
            {
                return segments;
            }

            const double epsilon = 1e-9;
            double segmentStart = history[0].X;
            bool currentHigh = history[0].Y >= 0.5f;
            double lastX = history[0].X;

            for (int i = 1; i < history.Length; i++)
            {
                double x = history[i].X;
                bool isHigh = history[i].Y >= 0.5f;

                if (Math.Abs(x - lastX) < epsilon)
                {
                    if (isHigh != currentHigh && x > segmentStart)
                    {
                        AddProtocolSegment(segments, segmentStart, x, currentHigh, visibleMin, visibleMax);
                        segmentStart = x;
                    }

                    currentHigh = isHigh;
                    lastX = x;
                    continue;
                }

                if (isHigh != currentHigh)
                {
                    AddProtocolSegment(segments, segmentStart, x, currentHigh, visibleMin, visibleMax);
                    segmentStart = x;
                    currentHigh = isHigh;
                }

                lastX = x;
            }

            AddProtocolSegment(segments, segmentStart, Math.Max(lastX, visibleMax), currentHigh, visibleMin, visibleMax);
            return segments;
        }

        private void AddProtocolSegment(List<ProtocolSegment> segments, double startX, double endX, bool isHigh, double visibleMin, double visibleMax)
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
            bool isMarker = durationMs <= CurrentSampleIntervalSeconds * 1000.0 * 3.5;

            ProtocolSegment segment = new ProtocolSegment();
            segment.StartX = clippedStart;
            segment.EndX = clippedEnd;
            segment.IsMarker = isMarker;
            segment.Label = isMarker ? (isHigh ? "T" : "S") : FormatProtocolDuration(durationMs);

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

        private void SetXAxisViewMode(XAxisViewMode mode)
        {
            _xAxisViewMode = mode;
            UpdateXAxisViewModeButtons();

            if (_chart == null || _hasConsumedData == false || mode != XAxisViewMode.FollowScroll)
            {
                return;
            }

            _chart.BeginUpdate();
            UpdateXAxisView(_lastConsumedX);
            _chart.EndUpdate();
        }

        private void UpdateXAxisViewModeButtons()
        {
            UpdateXAxisViewModeButton(buttonFollowScrollMode, _xAxisViewMode == XAxisViewMode.FollowScroll);
            UpdateXAxisViewModeButton(buttonPageMode, _xAxisViewMode == XAxisViewMode.Paging);
            UpdateXAxisViewModeButton(buttonFreeMode, _xAxisViewMode == XAxisViewMode.Free);
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
            AxisX xAxis = _chart.ViewXY.XAxes[0];

            if (_xAxisViewMode == XAxisViewMode.FollowScroll)
            {
                xAxis.ScrollPosition = lastX;
                UpdateCursorVisual();
                return;
            }

            if (_xAxisViewMode != XAxisViewMode.Paging || lastX < xAxis.Maximum)
            {
                UpdateCursorVisual();
                return;
            }

            double pageWidth = xAxis.Maximum - xAxis.Minimum;
            if (pageWidth <= 0)
            {
                pageWidth = _xLen;
            }

            xAxis.SetRange(lastX, lastX + pageWidth);
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
            Color lineBaseColor = DefaultColors.SeriesForBlackBackgroundWpf[seriesIndex % DefaultColors.SeriesForBlackBackgroundWpf.Length];

            AxisY axisY = new AxisY(view);
            //if (kind == SignalValueKind.Analog)
            //{
            //    axisY.SetRange(YMin, YMax);
            //}
            //else
            //{
                axisY.SetRange(0, 1);
            //}

            axisY.Title.Text = signal.Name;
            axisY.Title.Angle = 90;
            axisY.Title.Color = ChartTools.CalcGradient(lineBaseColor, Colors.White, 50);
            axisY.Units.Visible = false;
            axisY.AllowScaling = false;
            axisY.MajorGrid.Visible = false;
            axisY.MinorGrid.Visible = false;
            axisY.MajorGrid.Pattern = LinePattern.Solid;
            axisY.AutoDivSeparationPercent = 0;
            axisY.Units.Text = "mV";
            axisY.Visible = true;
            axisY.MajorDivTickStyle.Alignment = Alignment.Near;
            axisY.MinorDivTickStyle.Visible = false;
            axisY.Title.HorizontalAlign = YAxisTitleAlignmentHorizontal.Left;

            //if (seriesIndex == _seriesCount - 1)
            //{
            axisY.MiniScale.ShowX = true;
            axisY.MiniScale.ShowY = true;
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
