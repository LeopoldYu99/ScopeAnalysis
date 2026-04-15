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
        private readonly LinkedList<SeriesPoint> _recentPoints = new LinkedList<SeriesPoint>();
        private readonly object _historySync = new object();
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
            ClearRecentPoints();
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

        public void AppendRecentPoints(SeriesPoint[] points, double keepSeconds)
        {
            if (points == null || points.Length == 0)
            {
                return;
            }

            lock (_historySync)
            {
                for (int i = 0; i < points.Length; i++)
                {
                    _recentPoints.AddLast(points[i]);
                }

                TrimRecentPointsUnsafe(keepSeconds);
            }
        }

        public SeriesPoint[] GetRecentPointsSnapshot(double keepSeconds)
        {
            lock (_historySync)
            {
                TrimRecentPointsUnsafe(keepSeconds);
                SeriesPoint[] snapshot = new SeriesPoint[_recentPoints.Count];
                _recentPoints.CopyTo(snapshot, 0);
                return snapshot;
            }
        }

        public SeriesPoint[] GetPointsSnapshot(double minX, double maxX, double paddingSeconds)
        {
            lock (_historySync)
            {
                if (_recentPoints.Count == 0)
                {
                    return null;
                    //return Array.Empty<SeriesPoint>();
                }

                double paddedMinX = minX - Math.Max(0, paddingSeconds);
                double paddedMaxX = maxX + Math.Max(0, paddingSeconds);

                LinkedListNode<SeriesPoint> startNode = _recentPoints.First;
                while (startNode != null
                    && startNode.Next != null
                    && startNode.Next.Value.X < paddedMinX)
                {
                    startNode = startNode.Next;
                }

                List<SeriesPoint> snapshot = new List<SeriesPoint>();
                for (LinkedListNode<SeriesPoint> node = startNode; node != null; node = node.Next)
                {
                    snapshot.Add(node.Value);
                    if (node.Value.X > paddedMaxX && node.Previous != null)
                    {
                        break;
                    }
                }

                return snapshot.ToArray();
            }
        }

        public void ClearRecentPoints()
        {
            lock (_historySync)
            {
                _recentPoints.Clear();
            }
        }

        private void TrimRecentPointsUnsafe(double keepSeconds)
        {
            if (_recentPoints.Last == null)
            {
                return;
            }

            double minX = _recentPoints.Last.Value.X - Math.Max(keepSeconds, 1.0);
            while (_recentPoints.First != null && _recentPoints.First.Value.X < minX)
            {
                if (_recentPoints.First.Next == null)
                {
                    break;
                }

                _recentPoints.RemoveFirst();
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
