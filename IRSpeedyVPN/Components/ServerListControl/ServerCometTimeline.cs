using System;
using System.Linq;
namespace IRSpeedyVPN.Components.ServerListControl
{
    internal static class ServerCometTiming
    {
        public const int SORT_PASS_MS = 800;
        public const int FLASH_MS = 400;
        public const double BAND_WIDTH_DIP = 170;
        public const double SKEW_DEG = -18;
        public static double Ease(double t) => .5 - .5 * Math.Cos(Math.PI * Math.Max(0, Math.Min(1, t)));
        public static double LeadingEdge(double elapsedMs, double width) => Ease(elapsedMs / SORT_PASS_MS) * (width + .85 * BAND_WIDTH_DIP);
        public static double BandLeft(double elapsedMs, double width) => LeadingEdge(elapsedMs, width) - .85 * BAND_WIDTH_DIP;
        public static double CrossingTime(double center, double width) => Math.Acos(1 - 2 * Math.Max(0, Math.Min(1, center / (width + .85 * BAND_WIDTH_DIP)))) / Math.PI * SORT_PASS_MS;
    }
    internal sealed class ServerCometTimeline
    {
        private readonly int[] _order;
        private readonly double[] _crossings;
        private int _next;
        public readonly double Width;
        public ServerCometTimeline(double width, double[] physicalCenters)
        {
            Width = width;
            _order = Enumerable.Range(0, physicalCenters.Length).OrderBy(i => physicalCenters[i]).ToArray();
            _crossings = _order.Select(i => ServerCometTiming.CrossingTime(Math.Max(0, Math.Min(width, physicalCenters[i])), width)).ToArray();
        }
        public void Advance(double elapsedMs, Action<int, double> reveal)
        {
            while (_next < _order.Length && elapsedMs >= _crossings[_next])
            { int index = _next++; reveal(_order[index], _crossings[index]); }
        }
    }
}
