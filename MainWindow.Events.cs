using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;

namespace ScopeAnalysis
{
    public partial class MainWindow
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

            bool isOverPlotArea = IsPointInsidePlotArea(position);
            bool isOverXAxisArea = position.Y >= _chart.ActualHeight - margins.Bottom;
            if (isOverPlotArea || isOverXAxisArea)
            {
                double? anchorX = TryGetXAxisValueAt(position.X);
                ZoomX(zoomFactor, anchorX);
                e.Handled = true;
            }
        }

        private void buttonGenerateBin_Click(object sender, RoutedEventArgs e)
        {
            ShowDataProducerDialog();
        }

        private void buttonCollection_Click(object sender, RoutedEventArgs e)
        {
            ShowCollectionDialog();
        }

        private void buttonImport_Click(object sender, RoutedEventArgs e)
        {
            ShowSignalImportDialogForSignal(0);
        }

        private void buttonSendUdpCommand_Click(object sender, RoutedEventArgs e)
        {
            SendSelectedUdpCommand();
        }

        private void checkBoxCursor_CheckedChanged(object sender, RoutedEventArgs e)
        {
            ApplyCursorControlState();
        }

        private void checkBoxCursorSnap_CheckedChanged(object sender, RoutedEventArgs e)
        {
            ApplyCursorControlState();
        }

        private void comboBoxImportPage_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (_isUpdatingImportPageSelection || _currentProtocolImportSession == null || comboBoxImportPage == null)
            {
                return;
            }

            ProtocolImportPageItem selectedPage = comboBoxImportPage.SelectedItem as ProtocolImportPageItem;
            if (selectedPage == null)
            {
                return;
            }

            LoadProtocolImportPage(0, selectedPage);
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
                _isCursorHovering = false;
                UpdateMeasurementVisual();
                UpdateCursorVisual();
                return;
            }

            UpdateCursorFromControlPosition(position.X, position.Y);
            UpdateMeasurementFromControlPosition(position.X, position.Y);
        }

        private void Chart_MouseLeave(object sender, MouseEventArgs e)
        {
            _isMeasurementHovering = false;
            _isCursorHovering = false;
            UpdateMeasurementVisual();
            UpdateCursorVisual();
        }

    
    }
}

