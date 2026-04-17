using Arction.Wpf.Charting;
using Arction.Wpf.Charting.Axes;
using System;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;

namespace InteractiveExamples
{
    public partial class Example8BillionPoints
    {
        private void CompositionTarget_Rendering(object sender, EventArgs e)
        {
            if (_chart == null)
            {
                return;
            }

            ConsumePendingSignalData();
            RefreshOverlayDirtyStateFromViewport();

            if (_isDecodeOverlayDirty)
            {
                UpdateDecodeOverlay();
            }

            if (_isCursorVisualDirty)
            {
                UpdateCursorVisual();
            }
        }

        private void Chart_PreviewMouseWheel(object sender, MouseWheelEventArgs e)
        {
            if (_chart == null)
            {
                return;
            }

            Point position = e.GetPosition(_chart);
            Thickness margins = _chart.ViewXY.Margins;
            double zoomFactor = e.Delta > 0 ? 0.8 : 1.25;

            //if (position.X <= margins.Left)
            //{
            //    AxisY targetYAxis = TryGetYAxisAt(position.Y);
            //    if (targetYAxis != null)
            //    {
            //        double anchorY;
            //        if (targetYAxis.CoordToValue((float)position.Y, out anchorY, true) == false)
            //        {
            //            anchorY = (targetYAxis.Minimum + targetYAxis.Maximum) / 2.0;
            //        }

            //        ZoomY(targetYAxis, zoomFactor, anchorY);
            //        e.Handled = true;
            //        return;
            //    }
            //}

            bool isOverPlotArea = IsPointInsidePlotArea(position);
            bool isOverXAxisArea = position.Y >= _chart.ActualHeight - margins.Bottom;
            if (isOverPlotArea || isOverXAxisArea)
            {
                double? anchorX = TryGetXAxisValueAt(position.X);
                ZoomX(zoomFactor, anchorX);
                e.Handled = true;
            }
        }


        private void buttonCursor_Click(object sender, RoutedEventArgs e)
        {
            SetCursorEnabled(_isCursorEnabled == false);
        }

        private void buttonPoints_Click(object sender, RoutedEventArgs e)
        {
            SetPointsVisible(_arePointsVisible == false);
        }

        private void buttonDecode_Click(object sender, RoutedEventArgs e)
        {
            SetDecodeVisible(_isDecodeVisible == false);
        }

        private void buttonSignal1_Click(object sender, RoutedEventArgs e)
        {
            ShowSignalGeneratorForSignal(0);
        }

        private void buttonSignal2_Click(object sender, RoutedEventArgs e)
        {
            ShowSignalGeneratorForSignal(1);
        }

        private void buttonSignal3_Click(object sender, RoutedEventArgs e)
        {
            ShowSignalGeneratorForSignal(2);
        }

        private void buttonSignal4_Click(object sender, RoutedEventArgs e)
        {
            ShowSignalGeneratorForSignal(3);
        }

        private void Chart_PreviewMouseLeftButtonDown(object sender, MouseButtonEventArgs e)
        {
            if (_chart == null)
            {
                return;
            }

            Point position = e.GetPosition(_chart);
            if (IsPointInsidePlotArea(position) == false)
            {
                return;
            }

            _isCursorHovering = true;
            if (_isCursorEnabled)
            {
                _isCursorDragging = true;
                _chart.CaptureMouse();
                e.Handled = true;
            }

            SetCursorFromControlPosition(position.X, position.Y);
        }

        private void Chart_PreviewMouseMove(object sender, MouseEventArgs e)
        {
            if (_chart == null)
            {
                return;
            }

            Point position = e.GetPosition(_chart);
            if (IsPointInsidePlotArea(position) == false)
            {
                _isCursorHovering = false;
                if (_isCursorDragging == false)
                {
                    UpdateCursorVisual();
                }

                return;
            }

            _isCursorHovering = true;
            SetCursorFromControlPosition(position.X, position.Y);
            if (_isCursorDragging)
            {
                e.Handled = true;
            }
        }

        private void Chart_PreviewMouseLeftButtonUp(object sender, MouseButtonEventArgs e)
        {
            if (_chart == null || _isCursorDragging == false)
            {
                return;
            }

            _isCursorDragging = false;
            if (_chart.IsMouseCaptured)
            {
                _chart.ReleaseMouseCapture();
            }

            e.Handled = true;
        }

        private void Chart_LostMouseCapture(object sender, MouseEventArgs e)
        {
            _isCursorDragging = false;
        }

        private void Chart_MouseLeave(object sender, MouseEventArgs e)
        {
            _isCursorHovering = false;
            _isCursorDragging = false;
            _cursorMeasurementSignal = null;
            UpdateCursorVisual();
        }




    }
}
