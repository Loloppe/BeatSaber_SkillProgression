using System;
using System.Linq;

namespace SkillProgression.Core.Profiles
{
    /// <summary>Small numeric helpers matching the numpy behaviour used by the Python prototype.</summary>
    public static class Stats
    {
        /// <summary>np.average(x, weights=w); plain mean when the weights sum to zero.</summary>
        public static double WeightedAverage(double[] x, double[] w)
        {
            if (x.Length == 0) return 0;
            var sw = w.Sum();
            if (sw <= 0) return x.Average();
            double s = 0;
            for (var i = 0; i < x.Length; i++) s += x[i] * w[i];
            return s / sw;
        }

        /// <summary>Weighted median as in profiles._wmedian: first value whose cumulative weight reaches half.</summary>
        public static double WeightedMedian(double[] x, double[] w)
        {
            if (x.Length == 0) return 0;
            var order = Enumerable.Range(0, x.Length).OrderBy(i => x[i]).ToArray();
            var cum = new double[order.Length];
            double c = 0;
            for (var i = 0; i < order.Length; i++) { c += w[order[i]]; cum[i] = c; }
            var half = c / 2;
            for (var i = 0; i < cum.Length; i++)
                if (cum[i] >= half) return x[order[i]];
            return x[order[order.Length - 1]];
        }

        /// <summary>np.quantile(x, q) with linear interpolation.</summary>
        public static double Quantile(double[] x, double q)
        {
            if (x.Length == 0) return 0;
            var s = x.OrderBy(v => v).ToArray();
            var pos = (s.Length - 1) * q;
            var lo = (int)Math.Floor(pos);
            var hi = Math.Min(lo + 1, s.Length - 1);
            return s[lo] + (pos - lo) * (s[hi] - s[lo]);
        }

        /// <summary>Population standard deviation (np.std, ddof = 0).</summary>
        public static double StdDev(double[] x)
        {
            if (x.Length == 0) return 0;
            var m = x.Average();
            return Math.Sqrt(x.Sum(v => (v - m) * (v - m)) / x.Length);
        }

        /// <summary>Weight share of the flagged items.</summary>
        public static double Share(double[] w, bool[] flag)
        {
            var total = w.Sum();
            if (total <= 0) return 0;
            double s = 0;
            for (var i = 0; i < w.Length; i++) if (flag[i]) s += w[i];
            return s / total;
        }

        /// <summary>Weight share of flagged items among the items in a band (0 when the band is empty).</summary>
        public static double ShareWithin(double[] w, bool[] flag, bool[] band)
        {
            double total = 0, s = 0;
            for (var i = 0; i < w.Length; i++)
            {
                if (!band[i]) continue;
                total += w[i];
                if (flag[i]) s += w[i];
            }
            return total > 0 ? s / total : 0;
        }
    }
}
