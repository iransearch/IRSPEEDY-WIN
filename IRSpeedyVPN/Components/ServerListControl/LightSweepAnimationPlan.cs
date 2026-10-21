using System;

namespace IRSpeedyVPN.Components.ServerListControl
{
    // Coordinates/key times are computed once, before WPF starts its clocks.
    internal sealed class LightSweepAnimationPlan
    {
        public double BandFrom { get; }
        public double BandTo { get; }
        public double CutFrom { get; }
        public double CutTo { get; }
        public double Travel { get; }
        public TimeSpan RevealTime { get; }

        public LightSweepAnimationPlan(double width, double bandWidth, double inset,
            TimeSpan duration, Func<double, double> ease)
        {
            BandFrom = -bandWidth;
            BandTo = width;
            CutFrom = -bandWidth / 2 - inset;
            CutTo = width + bandWidth / 2 - inset;
            Travel = width + bandWidth;
            // The discrete identity marker shares the same timeline as the
            // band/clip transforms, including non-symmetric monotonic easing.
            double low = 0, high = 1;
            for (int i = 0; i < 32; i++)
            {
                double middle = (low + high) / 2;
                if (ease(middle) < .5) low = middle;
                else high = middle;
            }
            RevealTime = TimeSpan.FromTicks((long)(duration.Ticks * (low + high) / 2));
        }
    }
}
