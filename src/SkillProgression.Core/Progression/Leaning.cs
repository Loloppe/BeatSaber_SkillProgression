using System;
using System.Collections.Generic;
using System.Linq;
using SkillProgression.Core.Model;
using SkillProgression.Core.Profiles;

namespace SkillProgression.Core.Progression
{
    /// <summary>
    /// P(destination | style) for players who have not arrived yet (exploration 06). Style features are made
    /// peer-relative (minus the population median at the same level) before the logistic regression, because raw style
    /// is confounded by level. Below ~rank 20k results are low-confidence hints (PROGRESSION_DESIGN 06).
    /// </summary>
    public static class Leaning
    {
        public static double Level(PlayerProfile profile) => Math.Log(Math.Max(profile.TopPpSum, 1.0));

        /// <returns>Probabilities by destination (sums to 1), empty when the model has no leaning parameters.</returns>
        public static IReadOnlyDictionary<string, double> Predict(PlayerProfile profile, LeaningParameters? model)
        {
            var result = new Dictionary<string, double>();
            if (model == null || model.Classes.Count == 0) return result;

            var level = Level(profile);
            var x = new double[model.Features.Count];
            for (var i = 0; i < x.Length; i++)
            {
                var f = model.Features[i];
                var value = profile.Style.TryGetValue(f, out var v) ? v : 0;
                var peer = model.PeerMedians.TryGetValue(f, out var medians) ? Interpolate(level, model.PeerLevelCenters, medians) : 0;
                var scale = model.Scale[i] == 0 ? 1 : model.Scale[i];
                x[i] = (value - peer - model.Mean[i]) / scale;
            }

            var logits = new double[model.Classes.Count];
            for (var c = 0; c < logits.Length; c++)
            {
                double z = model.Intercepts[c];
                var coef = model.Coefficients[c];
                for (var i = 0; i < x.Length; i++) z += coef[i] * x[i];
                logits[c] = z;
            }

            var max = logits.Max();
            var exp = logits.Select(z => Math.Exp(z - max)).ToArray();
            var sum = exp.Sum();
            for (var c = 0; c < logits.Length; c++) result[model.Classes[c]] = exp[c] / sum;
            return result;
        }

        /// <summary>numpy.interp: linear, clamped at the ends.</summary>
        public static double Interpolate(double x, IReadOnlyList<double> xs, IReadOnlyList<double> ys)
        {
            var n = Math.Min(xs.Count, ys.Count);
            if (n == 0) return 0;
            if (x <= xs[0]) return ys[0];
            if (x >= xs[n - 1]) return ys[n - 1];
            for (var i = 1; i < n; i++)
            {
                if (x > xs[i]) continue;
                var span = xs[i] - xs[i - 1];
                return span <= 0 ? ys[i] : ys[i - 1] + (x - xs[i - 1]) / span * (ys[i] - ys[i - 1]);
            }
            return ys[n - 1];
        }
    }
}
