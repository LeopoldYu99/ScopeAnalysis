using System.Windows.Media;

namespace InteractiveExamples
{
    internal sealed class ProtocolSegment
    {
        public double StartX { get; set; }
        public double EndX { get; set; }
        public string Label { get; set; }
        public string[] BitLabels { get; set; }
        public Color FillColor { get; set; }
        public Color BorderColor { get; set; }
        public Color ForegroundColor { get; set; }
        public bool IsMarker { get; set; }
    }
}
