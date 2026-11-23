using SkillProgression.Core.Model;
using SkillProgression.Core.Progression;
using static SkillProgression.Core.Tests.TestMaps;

namespace SkillProgression.Core.Tests.Progression;

public class PeerTargetsTests
{
    private static PeerCurve Curve(double ease, params (double level, double value)[] points) => new()
    {
        Ease = ease, Levels = points.Select(p => p.level).ToList(), Values = points.Select(p => p.value).ToList(),
    };

    /// <summary>A Speed map with a style-wide curve and, when <paramref name="band"/> is set, a band-0 curve.</summary>
    private static MapEntry PeerMap(double ease, PeerCurve? band = null)
    {
        var m = Map(10, 9.0, 4, "speed", predictedAcc: 0.99);
        m.PeerTargets["Speed"] = Curve(ease, (-0.01, ease - 0.01), (0.01, ease + 0.01));
        if (band != null) m.PeerTargets["Speed|0"] = band;
        return m;
    }

    private static (ModelFile model, MapCatalog catalog) Model(IEnumerable<MapEntry> maps, int minPlayerMaps = 3)
    {
        var model = new ModelFile
        {
            Maps = maps.ToList(),
            PeerTargets = new PeerTargetsModel { MinPlayerMaps = minPlayerMaps },
            Paths = { new PathModel { Destination = "Speed", DifficultySpread = 1.0, Waypoints = { new WaypointModel { Index = 0, Difficulty = 9 } } } },
        };
        return (model, new MapCatalog(model.Maps));
    }

    [Fact]
    public void Curve_interpolates_moves_with_the_level_below_and_stops_at_its_top()
    {
        var c = Curve(0.95, (-0.01, 0.93), (0.0, 0.95), (0.01, 0.96));
        Assert.Equal(0.94, PeerTargets.At(c, -0.005), 9);
        Assert.Equal(0.955, PeerTargets.At(c, 0.005), 9);
        Assert.Equal(0.91, PeerTargets.At(c, -0.03), 9);
        // Above the best players' level the target stays at the curve's top (user, 2026-11-22).
        Assert.Equal(0.96, PeerTargets.At(c, 0.015), 9);
        Assert.Equal(0.96, PeerTargets.At(c, 0.1), 9);
        Assert.Equal(0.97, PeerTargets.At(Curve(0.96), 0.01), 9);
    }

    [Fact]
    public void Level_is_the_median_residual_without_the_target_map()
    {
        var maps = Enumerable.Range(0, 4).Select(_ => PeerMap(0.95)).ToList();
        var (model, catalog) = Model(maps);
        // Residuals +0.002, +0.004, +0.006 and -0.03 on the target map.
        var accs = new[] { 0.952, 0.954, 0.956, 0.92 };
        var best = maps.Select((m, i) => catalog.Rate(Play(m, accs[i]))!).ToList();

        var peers = PeerTargets.For(best, model);

        Assert.Equal(0.004, peers.Level("Speed", Key(maps[3]))!.Value, 9);
        Assert.Equal(0.954, peers.Target("Speed", maps[3])!.Value, 9);
    }

    [Fact]
    public void Level_comes_from_the_last_twelve_months_when_there_are_enough_maps_there()
    {
        var maps = Enumerable.Range(0, 7).Select(_ => PeerMap(0.95)).ToList();
        var (model, catalog) = Model(maps);
        var now = new DateTime(2026, 11, 1, 0, 0, 0, DateTimeKind.Utc);
        RatedPlay At(MapEntry m, double acc, DateTime time) =>
            catalog.Rate(new SkillProgression.Core.Plays.PlayEvidence(Key(m), acc, null, time, SkillProgression.Core.Plays.EvidenceSource.BeatLeader))!;
        // Four old unimproved PBs (-0.01) and three recent ones (+0.006).
        var best = maps.Take(4).Select(m => At(m, 0.94, now.AddMonths(-20)))
            .Concat(maps.Skip(4).Select(m => At(m, 0.956, now.AddDays(-30 * (maps.IndexOf(m) - 3))))).ToList();

        Assert.Equal(0.006, PeerTargets.For(best, model).Level("Speed")!.Value, 9);           // 3 recent maps >= minimum 3
        Assert.Equal(-0.01, PeerTargets.For(best, model, window: null).Level("Speed")!.Value, 9); // every PB (the fit's rule)

        var (strict, _) = Model(maps, minPlayerMaps: 5);                                        // too few recent maps
        Assert.Equal(-0.01, PeerTargets.For(best, strict).Level("Speed")!.Value, 9);           // falls back to every PB
    }

    [Fact]
    public void Typical_target_is_the_typical_player_capped_at_a_lower_player_level()
    {
        // D-069: style curve 0.94 at -0.01, 0.95 at 0 (typical), 0.96 at +0.01.
        var maps = Enumerable.Range(0, 4).Select(_ => PeerMap(0.95)).ToList();
        var (model, catalog) = Model(maps);

        Assert.Equal(0.95, PeerTargets.None.TypicalTarget("Speed", maps[3])!.Value, 9);       // no level: typical player
        var strong = maps.Take(3).Select(m => catalog.Rate(Play(m, 0.958))!).ToList();        // level +0.008
        Assert.Equal(0.95, PeerTargets.For(strong, model).TypicalTarget("Speed", maps[3])!.Value, 9);
        var weak = maps.Take(3).Select(m => catalog.Rate(Play(m, 0.945))!).ToList();          // level -0.005
        Assert.Equal(0.945, PeerTargets.For(weak, model).TypicalTarget("Speed", maps[3])!.Value, 9);
        Assert.Null(PeerTargets.None.TypicalTarget("Speed", Map(10, 9.0, 4, "speed")));      // no curve
    }

    [Fact]
    public void A_band_needs_enough_of_the_players_maps_else_the_style_level_is_used()
    {
        var band = Curve(0.90, (-0.01, 0.89), (0.01, 0.91));
        var target = PeerMap(0.95, band);
        var others = Enumerable.Range(0, 3).Select(_ => PeerMap(0.95)).ToList();
        var (model, catalog) = Model(others.Append(target));
        var best = others.Select(m => catalog.Rate(Play(m, 0.955))!).ToList();

        // No band-0 plays: style level +0.005 on the style curve.
        Assert.Equal(0.955, PeerTargets.For(best, model).Target("Speed", target)!.Value, 9);

        var bandMaps = Enumerable.Range(0, 3).Select(_ => PeerMap(0.95, Curve(0.90))).ToList();
        (model, catalog) = Model(others.Concat(bandMaps).Append(target));
        best = others.Select(m => catalog.Rate(Play(m, 0.955))!)
            .Concat(bandMaps.Select(m => catalog.Rate(Play(m, 0.898))!)).ToList();

        // Three band-0 plays at -0.002: the band's own curve and level.
        Assert.Equal(0.898, PeerTargets.For(best, model).Target("Speed", target)!.Value, 9);
    }

    [Fact]
    public void No_target_without_enough_plays_peer_data_or_on_older_models()
    {
        var maps = Enumerable.Range(0, 3).Select(_ => PeerMap(0.95)).ToList();
        var (model, catalog) = Model(maps);
        var best = maps.Take(2).Select(m => catalog.Rate(Play(m, 0.95))!).ToList();
        Assert.Null(PeerTargets.For(best, model).Target("Speed", maps[2]));

        var noCurve = Map(10, 9.0, 4, "speed");
        Assert.Null(PeerTargets.For(maps.Select(m => catalog.Rate(Play(m, 0.95))!), model).Target("Speed", noCurve));

        model.PeerTargets = null;
        Assert.Null(PeerTargets.For(maps.Select(m => catalog.Rate(Play(m, 0.95))!), model).Target("Speed", maps[0]));
    }

    [Fact]
    public void Plays_with_speed_or_easier_modifiers_do_not_count_neutral_ones_do()
    {
        var maps = Enumerable.Range(0, 4).Select(_ => PeerMap(0.95)).ToList();
        var (model, catalog) = Model(maps);
        Assert.Null(PeerTargets.For(maps.Take(3).Select(m => catalog.Rate(Play(m, 0.97, "NO"))!), model).Target("Speed", maps[3]));
        Assert.Null(PeerTargets.For(maps.Take(3).Select(m => catalog.Rate(Play(m, 0.97, "FS"))!), model).Target("Speed", maps[3]));

        var neutral = new[] { new[] { "OD" }, new[] { "PM", "GN" }, new[] { "DA", "BE" } };
        var best = maps.Take(3).Select((m, i) => catalog.Rate(Play(m, 0.955, neutral[i]))!).ToList();
        Assert.Equal(0.955, PeerTargets.For(best, model).Target("Speed", maps[3])!.Value, 9);
    }

    [Fact]
    public void Polish_leaves_out_maps_without_a_peer_target_once_the_style_has_peer_data()
    {
        var rest = Enumerable.Range(0, 6).Select(_ => PeerMap(0.95)).ToList();
        var modded = PeerMap(0.95);
        var (model, catalog) = Model(rest.Append(modded));
        model.Paths[0].Waypoints.Add(new WaypointModel { Index = 1, Difficulty = 9, PathPpLow = 1e6, AccuracyResidual = 0.05 });
        // The modded PB would get the residual rule (0.99 + 0.05): far "below target", it used to top the list.
        var best = rest.Select((m, i) => catalog.Rate(Play(m, i == 0 ? 0.94 : 0.952))!)
            .Append(catalog.Rate(Play(modded, 0.95, "NO"))!).ToList();

        var polish = PathAnalyzer.Analyze("Speed", best, model, catalog)!.Objectives.Single(o => o.Kind == ObjectiveKind.Polish);

        Assert.Equal(new[] { rest[0].Hash }, polish.Maps.Select(m => m.Map.Hash).ToArray());
    }

    [Fact]
    public void Explore_expects_what_players_at_the_players_level_get()
    {
        var weak = PeerMap(0.95);
        var played = Enumerable.Range(0, 5).Select(_ => PeerMap(0.95)).ToList();
        var (model, catalog) = Model(played.Append(weak));
        var best = played.Select(m => catalog.Rate(Play(m, 0.955))!).Append(catalog.Rate(Play(weak, 0.94))!).ToList();

        var item = Assert.Single(Explorer.Suggest("Speed", ExploreMode.Improve, 0, best, model, catalog, seed: 1).Items);

        Assert.Equal(weak.Hash, item.Map.Hash);
        Assert.Equal(0.955, item.ExpectedAccuracy, 9);
    }

    [Fact]
    public void Polish_aims_at_peers_and_lists_the_maps_furthest_below_them_first()
    {
        var a = PeerMap(0.95);
        var b = PeerMap(0.96);
        var rest = Enumerable.Range(0, 6).Select(_ => PeerMap(0.95)).ToList();
        var (model, catalog) = Model(rest.Append(a).Append(b));
        model.Paths[0].Waypoints.Add(new WaypointModel { Index = 1, Difficulty = 9, PathPpLow = 1e6, AccuracyResidual = 0.05 });
        // Level +0.002 (rest); a is 0.004 below its peer target, b 0.012 below.
        var best = rest.Select(m => catalog.Rate(Play(m, 0.952))!)
            .Append(catalog.Rate(Play(a, 0.948))!).Append(catalog.Rate(Play(b, 0.95))!).ToList();

        var polish = PathAnalyzer.Analyze("Speed", best, model, catalog)!.Objectives.Single(o => o.Kind == ObjectiveKind.Polish);

        Assert.Equal(new[] { b.Hash, a.Hash }, polish.Maps.Select(m => m.Map.Hash).ToArray());
        Assert.Equal(0.962, polish.Maps[0].TargetAccuracy, 9);
        Assert.Equal(0.952, polish.Maps[1].TargetAccuracy, 9);
    }
}
