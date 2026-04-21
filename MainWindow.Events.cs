using System;
using System.Windows;
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

            RefreshOverlayDirtyStateFromViewport();

            if (_isDecodeOverlayDirty)
            {
                UpdateDecodeOverlay();
            }

            if (_isMeasurementVisualDirty)
            {
                UpdateMeasurementVisual();
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

            bool isOverPlotArea = IsPointInsidePlotArea(position);
            bool isOverXAxisArea = position.Y >= _chart.ActualHeight - margins.Bottom;
            if (isOverPlotArea || isOverXAxisArea)
            {
                double? anchorX = TryGetXAxisValueAt(position.X);
                ZoomX(zoomFactor, anchorX);
                e.Handled = true;
            }
        }

        private void buttonPoints_Click(object sender, RoutedEventArgs e)
        {
            SetPointsVisible(_arePointsVisible == false);
        }

        private void buttonDecode_Click(object sender, RoutedEventArgs e)
        {
            SetDecodeVisible(_isDecodeVisible == false);
        }

        private void buttonGenerateBin_Click(object sender, RoutedEventArgs e)
        {
            ShowDataProducerDialog();
        }

        private void buttonSignal1_Click(object sender, RoutedEventArgs e)
        {
            ShowSignalImportDialogForSignal(0);
        }

        private void buttonSignal2_Click(object sender, RoutedEventArgs e)
        {
            ShowSignalImportDialogForSignal(1);
        }

        private void buttonSignal3_Click(object sender, RoutedEventArgs e)
        {
            ShowSignalImportDialogForSignal(2);
        }

        private void buttonSignal4_Click(object sender, RoutedEventArgs e)
        {
            ShowSignalImportDialogForSignal(3);
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
                _isMeasurementHovering = false;
                UpdateMeasurementVisual();
                return;
            }

            _isMeasurementHovering = true;
            UpdateMeasurementFromControlPosition(position.X, position.Y);
        }

    
    }
}
