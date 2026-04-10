using Arction.Wpf.Charting;
using Arction.Wpf.Charting.Axes;
using Arction.Wpf.Charting.SeriesXY;
using System;
using System.Collections.Generic;

namespace InteractiveExamples
{
    internal enum SignalValueKind
    {
        StepDigital,
        Digital,
        Analog
    }

    internal sealed class ChartSignal
    {
        private readonly LinkedList<SeriesPoint[]> _pendingChunks = new LinkedList<SeriesPoint[]>();
        private readonly object _pendingSync = new object();
        private readonly double _analogMin;
        private readonly double _analogMax;

        public ChartSignal(string name, SignalValueKind kind, double analogMin, double analogMax)
        {
            Name = name;
            Kind = kind;
            _analogMin = analogMin;
            _analogMax = analogMax;
        }

        public string Name { get; private set; }
        public SignalValueKind Kind { get; private set; }
        public AxisY AxisY { get; set; }
        public PointLineSeries Series { get; set; }
        public Random Randomizer { get; private set; }
        public double ValueState { get; private set; }
        public bool HasPreviousValue { get; private set; }
        public float PreviousValue { get; private set; }

        public void ResetRuntimeState(Random randomizer, double initialValue)
        {
            Randomizer = randomizer;
            ValueState = initialValue;
            HasPreviousValue = false;
            PreviousValue = 0;
            ClearPendingChunks();
        }

        public SeriesPoint[] CreatePoints(double[] sampleTimes)
        {
            if (Kind == SignalValueKind.StepDigital)
            {
                List<SeriesPoint> points = new List<SeriesPoint>(sampleTimes.Length * 2);
                for (int i = 0; i < sampleTimes.Length; i++)
                {
                    float value = GenerateValue();
                    double time = sampleTimes[i];

                    if (HasPreviousValue == false)
                    {
                        points.Add(new SeriesPoint(time, value));
                        HasPreviousValue = true;
                    }
                    else
                    {
                        points.Add(new SeriesPoint(time, PreviousValue));
                        if (Math.Abs(value - PreviousValue) > float.Epsilon)
                        {
                            points.Add(new SeriesPoint(time, value));
                        }
                    }

                    PreviousValue = value;
                }

                return points.ToArray();
            }

            SeriesPoint[] pointsChunk = new SeriesPoint[sampleTimes.Length];
            for (int i = 0; i < sampleTimes.Length; i++)
            {
                pointsChunk[i] = new SeriesPoint(sampleTimes[i], GenerateValue());
            }

            return pointsChunk;
        }

        public void EnqueuePoints(SeriesPoint[] points)
        {
            lock (_pendingSync)
            {
                _pendingChunks.AddLast(points);
            }
        }

        public bool TryDequeuePoints(out SeriesPoint[] points)
        {
            points = null;
            lock (_pendingSync)
            {
                if (_pendingChunks.First == null)
                {
                    return false;
                }

                points = _pendingChunks.First.Value;
                _pendingChunks.RemoveFirst();
                return true;
            }
        }

        public void ClearPendingChunks()
        {
            lock (_pendingSync)
            {
                _pendingChunks.Clear();
            }
        }

        private float GenerateValue()
        {
            if (Kind != SignalValueKind.Analog)
            {
                return Randomizer.Next(0, 2);
            }

            double y = ValueState;
            y = y - 0.05 + Randomizer.NextDouble() / 10.0;
            y = Math.Max(_analogMin, Math.Min(_analogMax, y));
            ValueState = y;
            return (float)y;
        }
    }
}
