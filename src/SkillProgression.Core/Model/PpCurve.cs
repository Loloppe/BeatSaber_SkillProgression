using System.Collections.Generic;

namespace SkillProgression.Core.Model
{
    /// <summary>
    /// Piecewise-linear acc -> pp lookup (D-026). Points are sorted by accuracy; values outside the range are clamped
    /// to the end points, like numpy.interp in the Python prototype.
    /// </summary>
    public sealed class PpCurve
    {
        public List<double> Acc { get; set; } = new List<double>();
        public List<double> Pp { get; set; } = new List<double>();

        public double Evaluate(double accuracy)
        {
            var n = Acc.Count;
            if (n == 0 || Pp.Count != n) return 0;
            if (accuracy <= Acc[0]) return Pp[0];
            if (accuracy >= Acc[n - 1]) return Pp[n - 1];

            int lo = 0, hi = n - 1;
            while (hi - lo > 1)
            {
                var mid = (lo + hi) / 2;
                if (Acc[mid] <= accuracy) lo = mid; else hi = mid;
            }

            var span = Acc[hi] - Acc[lo];
            if (span <= 0) return Pp[hi];
            var t = (accuracy - Acc[lo]) / span;
            return Pp[lo] + t * (Pp[hi] - Pp[lo]);
        }
    }
}
