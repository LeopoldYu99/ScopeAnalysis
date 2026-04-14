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

            if (position.X <= margins.Left)
            {
                ZoomY(zoomFactor);
                e.Handled = true;
                return;
            }

            if (position.Y >= _chart.ActualHeight - margins.Bottom)
            {
                double? anchorX = TryGetXAxisValueAt(position.X);
                ZoomX(zoomFactor, anchorX);
                e.Handled = true;
            }
        }




        private void buttonFollowScrollMode_Click(object sender, RoutedEventArgs e)
        {
            SetXAxisViewMode(XAxisViewMode.FollowScroll);
        }

        private void buttonPageMode_Click(object sender, RoutedEventArgs e)
        {
            SetXAxisViewMode(XAxisViewMode.Paging);
        }

        private void buttonFreeMode_Click(object sender, RoutedEventArgs e)
        {
            SetXAxisViewMode(XAxisViewMode.Free);
        }




    }
}
