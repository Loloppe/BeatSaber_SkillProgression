using SkillProgression.Core.Profiles;

namespace SkillProgression.Core.Tests.Profiles;

/// <summary>Reference values computed with numpy / profiles.py on the same inputs (parity with the prototype, D-017).</summary>
public class StatsTests
{
    private static readonly double[] X = { 3.0, 1.0, 4.0, 1.5, 9.0 };
    private static readonly double[] W = { 1, 2, 3, 4, 5 };

    [Fact] public void Weighted_average_matches_numpy() => Assert.Equal(4.533333333333333, Stats.WeightedAverage(X, W), 12);
    [Fact] public void Quantile_matches_numpy_linear() => Assert.Equal(8.0, Stats.Quantile(X, 0.95), 9);
    [Fact] public void Std_is_population_std() => Assert.Equal(2.85657137141714, Stats.StdDev(X), 12);
    [Fact] public void Weighted_median_matches_prototype() => Assert.Equal(4.0, Stats.WeightedMedian(X, W));

    [Fact]
    public void Zero_weights_fall_back_to_plain_mean() =>
        Assert.Equal(X.Average(), Stats.WeightedAverage(X, new double[5]), 12);

    [Fact]
    public void Share_within_an_empty_band_is_zero() =>
        Assert.Equal(0, Stats.ShareWithin(W, new[] { true, true, true, true, true }, new bool[5]));

    [Fact]
    public void Share_within_band_ignores_items_outside_it()
    {
        var flag = new[] { true, false, true, false, false };
        var band = new[] { true, true, false, false, false };
        Assert.Equal(1.0 / 3.0, Stats.ShareWithin(W, flag, band), 12);
    }
}
