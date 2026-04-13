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

        private void buttonStartStop_Click(object sender, RoutedEventArgs e)
        {
            Start();
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

        private void buttonZoomXPlus_Click(object sender, RoutedEventArgs e)
        {
            ZoomX(0.5);
        }

        private void buttonZoomXMinus_Click(object sender, RoutedEventArgs e)
        {
            ZoomX(2);
        }

        private void buttonZoomYPlus_Click(object sender, RoutedEventArgs e)
        {
            ZoomY(0.5);
        }

        private void buttonZoomYMinus_Click(object sender, RoutedEventArgs e)
        {
            ZoomY(2);
        }

        private void buttonPointCount_Checked(object sender, RoutedEventArgs e)
        {
            ulong pointCount = 1000000;
            ulong seriesCount = 1;
            ulong xAxisPointCount = 1000;
            ulong appendPointsPerRound = 1000;

            if (sender == button1M)
            {
                pointCount = 1000000;
                seriesCount = 4;
            }
            else if (sender == button10M)
            {
                pointCount = 10000000;
                seriesCount = 4;
            }
            else if (sender == button100M)
            {
                pointCount = 100000000;
                seriesCount = 8;
            }
            else if (sender == button1000M)
            {
                pointCount = 1000000000;
                seriesCount = 16;
            }
            else if (sender == button2000M)
            {
                pointCount = 2000000000;
                seriesCount = 16;
            }
            else if (sender == button3000M)
            {
                pointCount = 3000000000;
                seriesCount = 16;
            }
            else if (sender == button4000M)
            {
                pointCount = 4000000000;
                seriesCount = 16;
            }
            else if (sender == button5000M)
            {
                pointCount = 5000000000;
                seriesCount = 16;
            }
            else if (sender == button6000M)
            {
                pointCount = 6000000000;
                seriesCount = 32;
            }
            else if (sender == button7000M)
            {
                pointCount = 7000000000;
                seriesCount = 32;
            }
            else if (sender == button8000M)
            {
                pointCount = 8000000000;
                seriesCount = 32;
            }

            xAxisPointCount = pointCount / seriesCount;
            appendPointsPerRound = 10;

            if (textBoxSeriesCount != null)
            {
                textBoxSeriesCount.Text = seriesCount.ToString();
            }

            if (textBoxAppendCountPerRound != null)
            {
                textBoxAppendCountPerRound.Text = appendPointsPerRound.ToString();
            }

            if (textBoxXLen != null)
            {
                textBoxXLen.Text = xAxisPointCount.ToString();
            }
        }


    }
}
