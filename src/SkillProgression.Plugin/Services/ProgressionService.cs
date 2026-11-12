using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using SkillProgression.Core.Achievements;
using SkillProgression.Core.Maps;
using SkillProgression.Core.Model;
using SkillProgression.Core.Plays;
using SkillProgression.Core.Profiles;
using SkillProgression.Core.Progression;
using SkillProgression.Data.BeatLeader;
using SkillProgression.Data.Model;
using SkillProgression.Data.Platforms;
using SkillProgression.Data.Plays;
using SkillProgression.Data.State;

namespace SkillProgression.Plugin.Services;

/// <summary>Everything the Training screen shows, computed off the main thread.</summary>
internal sealed class TrainingSnapshot
{
    public PlayerReport Report { get; init; } = new();
    public string FocusPath { get; init; } = "";
    public PathProgress? Focus { get; init; }
    /// <summary>True when the focus style has no progress yet and Next up shows starter maps.</summary>
    public bool Starter { get; init; }
    public IReadOnlyList<ShortTermStep> Steps { get; init; } = Array.Empty<ShortTermStep>();
    public IReadOnlyList<NextUpItem> NextUp { get; init; } = Array.Empty<NextUpItem>();
    /// <summary>Fresh-start calibration in progress on the focus style (D-040): Next up holds its single map.</summary>
    public CalibrationStatus? Calibration { get; init; }
    public DateTime? LastSyncUtc { get; init; }
    public bool Offline { get; init; }
    /// <summary>Achievements unlocked by this refresh (not retroactive): celebrated in the UI.</summary>
    public IReadOnlyList<Achievement> NewAchievements { get; init; } = Array.Empty<Achievement>();
}

/// <summary>Achievements screen data.</summary>
internal sealed class ProgressOverview
{
    public IReadOnlyList<Achievement> Unlocked { get; init; } = Array.Empty<Achievement>();
    public ProgressSnapshot? Last { get; init; }
    public IReadOnlyList<ProgressSnapshot> Snapshots { get; init; } = Array.Empty<ProgressSnapshot>();
    /// <summary>Rank history per platform ("BeatLeader", "ScoreSaber", "AccSaber").</summary>
    public IReadOnlyDictionary<string, IReadOnlyList<RankPoint>> RankHistories { get; init; } = new Dictionary<string, IReadOnlyList<RankPoint>>();
    /// <summary>Number of milestones per style (model).</summary>
    public IReadOnlyDictionary<string, int> MilestoneCounts { get; init; } = new Dictionary<string, int>();
}

/// <summary>
/// Orchestrates Data + Core for the plugin (docs/ARCHITECTURE.md). No Unity types: callers await it from the main
/// thread and get continuations back there (Unity's synchronization context); heavy work runs on the thread pool.
/// </summary>
internal sealed class ProgressionService
{
    private readonly string _dataDir;
    private readonly PlayLogStore _playLog;
    private readonly UserStateStore _stateStore;
    private readonly ScoreCache _scoreCache;
    /// <summary>Clears made from the game's Solo menu, scores only (D-049).</summary>
    private readonly SoloScoreStore _soloScores;
    /// <summary>Unranked PBs, for Explore's Curated pool only (D-045); synced once per session on first use.</summary>
    private readonly ScoreCache _unrankedCache;
    private Task<IReadOnlyList<PlayEvidence>>? _unrankedScores;
    private readonly BeatLeaderClient _beatLeader;
    private readonly PlatformStatsClient _platforms;
    private ProgressionEngine? _engine;
    private ModelFile? _model;
    private IReadOnlyList<PlayEvidence> _beatLeaderScores = Array.Empty<PlayEvidence>();
    private readonly ProgressStore _progress;
    private IReadOnlyList<PlatformStats> _lastPlatforms = Array.Empty<PlatformStats>();
    private readonly object _progressLock = new();

    public UserState State { get; private set; }

    public ProgressionService(string dataDir, PlayLogStore playLog, BeatLeaderClient beatLeader, PlatformStatsClient platforms)
    {
        _dataDir = dataDir;
        _playLog = playLog;
        _beatLeader = beatLeader;
        _platforms = platforms;
        _stateStore = new UserStateStore(Path.Combine(dataDir, "state.json"));
        _scoreCache = new ScoreCache(Path.Combine(dataDir, "beatleader-scores.json"));
        _soloScores = new SoloScoreStore(Path.Combine(dataDir, "solo-scores.json"));
        _unrankedCache = new ScoreCache(Path.Combine(dataDir, "beatleader-unranked-scores.json"), ScoreType.Unranked);
        _progress = new ProgressStore(Path.Combine(dataDir, "progress.json"));
        State = _stateStore.Load();
    }

    public string ModelPath => Path.Combine(_dataDir, "model.json.gz");

    public DateTime? LastSyncUtc => _scoreCache.LastSyncUtc;

    public void SaveState() => _stateStore.Save(State);

    /// <summary>Changes the start mode (D-029: the UI asks for confirmation before calling this).</summary>
    public void SetStartMode(StartMode mode, DateTime nowUtc)
    {
        State.StartMode = mode;
        State.FreshStartUtc = mode == StartMode.Fresh ? nowUtc : null;
        State.Calibrations.Clear();
        SaveState();
    }

    public void SetFocus(string path)
    {
        State.FocusPath = path;
        if (!State.Paths.Contains(path)) State.Paths.Add(path);
        SaveState();
    }

    /// <summary>AccSaber is an accuracy pool: choosing it also focuses the Accuracy style (user, 2026-11-11).</summary>
    public void SetMapPool(MapPool pool)
    {
        State.MapPool = pool;
        if (pool == MapPool.AccSaber) SetFocus(Destinations.Accuracy);
        else SaveState();
    }

    public void Skip(MapKey map)
    {
        State.Skip(map);
        // Skipping the calibration map: drop the pick so the next refresh chooses another one at the same level.
        foreach (var c in State.Calibrations.Values)
            if (c.Picks.Count > 0 && c.Picks[c.Picks.Count - 1].Map == map.ToString()) c.Picks.RemoveAt(c.Picks.Count - 1);
        SaveState();
    }

    /// <summary>Skips the focus style's calibration: starter maps use a cautious level from the old profile (D-040).</summary>
    public void SkipCalibration(string path)
    {
        State.Calibration(path).Skipped = true;
        SaveState();
    }

    /// <summary>Loads the model (once), syncs BeatLeader PBs when importing, then analyses. Network failures fall back to the cache.</summary>
    public async Task<TrainingSnapshot> RefreshAsync(string playerId, bool sync, IProgress<double>? progress = null,
        CancellationToken ct = default)
    {
        if (State.PlayerId != playerId)
        {
            State.PlayerId = playerId;
            SaveState();
        }

        await EnsureEngineAsync(ct);
        _engine!.Catalog.Pool = State.MapPool;

        var offline = false;
        if (State.StartMode == StartMode.ImportBeatLeader || State.StartMode == StartMode.Fresh)
        {
            try
            {
                _beatLeaderScores = sync
                    ? await _scoreCache.SyncAsync(_beatLeader, playerId, DateTime.UtcNow, ct, progress)
                    : _beatLeaderScores.Count > 0 ? _beatLeaderScores : _scoreCache.Cached();
            }
            catch (Exception e) when (!(e is OperationCanceledException))
            {
                Plugin.Log.Warn($"BeatLeader sync failed, using cached scores: {e.Message}");
                _beatLeaderScores = _scoreCache.Cached();
                offline = true;
            }
        }

        if (sync && !offline)
        {
            // History back-fill from each platform's API, once, so charts are not empty on day one.
            await BackfillAsync("BeatLeader", () => _beatLeader.GetRankHistoryAsync(playerId, cancellationToken: ct));
            await BackfillAsync("ScoreSaber", () => _platforms.GetScoreSaberHistoryAsync(playerId, ct));
            await BackfillAsync("AccSaber", () => _platforms.GetAccSaberHistoryAsync(playerId, ct));
        }

        return await Task.Run(() => Analyze(), ct);

        TrainingSnapshot Analyze()
        {
            var log = _playLog.ReadAll(out _);
            var evidence = Evidence(log);
            var report = _engine!.Analyze(evidence, Destinations.All, Since);
            var newAchievements = TrackProgress(report, evidence);

            var focus = State.MapPool == MapPool.AccSaber ? Destinations.Accuracy
                : string.IsNullOrEmpty(State.FocusPath) ? SuggestFocus(report) : State.FocusPath;
            var (calibration, startLevel) = Calibrate(focus, report, log, evidence);
            if (startLevel is double level && !report.Paths.ContainsKey(focus)
                && _engine.Analyze(evidence, new[] { focus }, Since, new Dictionary<string, double> { [focus] = level })
                    .Starters.TryGetValue(focus, out var levelled))
                report.Starters[focus] = levelled;
            report.Paths.TryGetValue(focus, out var path);
            var steps = StepSizer.StepsPerGap(log.Where(p => OnPath(p, focus)));
            // No progress on this style yet (fresh start / unplayed style): starter maps on the easier side.
            report.Starters.TryGetValue(focus, out var starter);
            return new TrainingSnapshot
            {
                Report = report,
                FocusPath = focus,
                Focus = calibration == null ? path : null,
                Starter = calibration == null && path == null && starter != null,
                Calibration = calibration,
                Steps = calibration == null ? path?.Objectives.Select(o => TrainingPlanner.Step(o, steps)).ToList() ?? new List<ShortTermStep>() : new List<ShortTermStep>(),
                NextUp = calibration != null ? CalibrationItems(calibration)
                    : path != null
                    ? TrainingPlanner.NextUp(path, log, State.SkippedKeys(), stepsPerGap: steps)
                    : TrainingPlanner.NextUpFromObjectives(starter != null ? new[] { starter } : Array.Empty<Objective>(), log, State.SkippedKeys(), stepsPerGap: steps),
                LastSyncUtc = _scoreCache.LastSyncUtc,
                Offline = offline,
                NewAchievements = newAchievements,
            };
        }
    }

    /// <summary>Snapshot of today + achievement detection (approved list 2026-11-10). Runs on the thread pool.</summary>
    private IReadOnlyList<Achievement> TrackProgress(PlayerReport report, List<PlayEvidence> evidence)
    {
        if (report.Profile == null) return Array.Empty<Achievement>();
        var now = DateTime.UtcNow;
        var t = _model!.Thresholds;
        var rated = evidence.Where(e => Since == null || e.TimeUtc >= Since.Value)
            .Select(_engine!.Catalog.Rate).Where(r => r != null).Select(r => r!).ToList();
        var best = ProfileBuilder.BestPerMap(rated);
        var local = rated.Where(r => r.Evidence.Source == EvidenceSource.LocalPlayLog).ToList();
        var platforms = _lastPlatforms.Where(p => p.Rank is int)
            .Select(p => new PlatformRank { Platform = p.Platform, Category = p.Category, Rank = p.Rank!.Value,
                                            Points = p.Points ?? 0, Unit = p.Unit }).ToList();

        lock (_progressLock)
        {
            var snapshot = AchievementEngine.Snapshot(report, best, local, t, platforms, now);
            var found = AchievementEngine.Detect(_progress.LastSnapshot, snapshot, _progress.UnlockedIds(), now);
            _progress.Add(found);
            _progress.PutSnapshot(snapshot);
            // Today's official rank for each platform (AccSaber: overall) extends the API history.
            foreach (var r in platforms.Where(r => string.IsNullOrEmpty(r.Category) || r.Category == "Overall"))
                _progress.MergeRankHistory(r.Platform, new[] { new RankPoint { DateUtc = now.Date, Rank = r.Rank, Pp = r.Points } });
            _progress.Save();
            return found.Where(a => !a.Retroactive).ToList();
        }
    }

    private async Task BackfillAsync(string platform, Func<Task<IReadOnlyList<RankPoint>>> fetch)
    {
        lock (_progressLock)
            if (_progress.RankHistory(platform).Count > 1) return;
        try
        {
            var history = await fetch();
            lock (_progressLock) { _progress.MergeRankHistory(platform, history); _progress.Save(); }
        }
        catch (Exception e) when (!(e is OperationCanceledException)) { Plugin.Log.Warn($"{platform} history failed: {e.Message}"); }
    }

    public ProgressOverview Overview()
    {
        lock (_progressLock)
        {
            var last = _progress.LastSnapshot;
            return new ProgressOverview
            {
                Unlocked = _progress.Achievements.OrderByDescending(a => a.UnlockedUtc).ToList(),
                Last = last,
                Snapshots = _progress.Snapshots.ToList(),
                RankHistories = new[] { "BeatLeader", "ScoreSaber", "AccSaber" }
                    .ToDictionary(k => k, k => (IReadOnlyList<RankPoint>)_progress.RankHistory(k).ToList()),
                MilestoneCounts = _model?.Paths.ToDictionary(p => p.Destination, p => p.Waypoints.Count) ?? new Dictionary<string, int>(),
            };
        }
    }

    private DateTime? Since => State.StartMode == StartMode.Fresh ? State.FreshStartUtc : null;

    private List<PlayEvidence> Evidence(IReadOnlyList<PlayRecord> log) => _beatLeaderScores
        .Concat(_soloScores.All())
        .Concat(log.Where(p => p.IsPass() && !p.Practice).Select(ToEvidence))
        .ToList();

    private async Task EnsureEngineAsync(CancellationToken ct)
    {
        if (_engine != null) return;
        _model = await Task.Run(() => ModelLoader.Load(ModelPath), ct);
        _engine = new ProgressionEngine(_model);
        if (_beatLeaderScores.Count == 0) _beatLeaderScores = _scoreCache.Cached();
    }

    /// <summary>
    /// Explore side feature: a fresh list for a style, mode and difficulty offset (uses cached scores). The pool is
    /// Explore's own choice: a ranked pool (shared with Training) or Curated (D-045, adds unranked BeatLeader PBs).
    /// </summary>
    public async Task<(ExploreResult result, int[] counts)> ExploreAsync(string path, ExploreMode mode, int offset, int seed,
        MapPool pool, CancellationToken ct = default) =>
        await ExploreCoreAsync(path, mode, offset, seed, pool, Explorer.DefaultCount, randomPick: true, ct);

    /// <summary>A clear from the Solo menu (D-049): kept as a score only, used as evidence until BeatLeader has it.</summary>
    public void AddSoloScore(PlayEvidence play) => _soloScores.Add(play);

    /// <summary>Saves the Explore settings (mode, style, pool, slider) the Solo tab is rebuilt from (D-049).</summary>
    public void SetToImprove(ExploreMode mode, string path, MapPool pool, int offset)
    {
        State.ToImprove = new ToImproveSettings { Mode = mode, Path = path, Pool = pool, Offset = offset };
        SaveState();
    }

    /// <summary>
    /// The Solo tab (D-049): Explore's list for the saved mode and settings, top candidates in order (no
    /// shuffle, so it only changes when scores do). Empty until settings were saved from Explore.
    /// </summary>
    public async Task<IReadOnlyList<ExploreItem>> ToImproveAsync(int count = Explorer.DefaultCount, CancellationToken ct = default)
    {
        if (State.ToImprove is not { } s || string.IsNullOrEmpty(s.Path)) return Array.Empty<ExploreItem>();
        var (result, _) = await ExploreCoreAsync(s.Path, s.Mode, s.Offset, 0, s.Pool, count, randomPick: false, ct);
        return result.Items;
    }

    /// <summary>Explore lists and "To improve" rebuilds share the Explore catalog's pool setting: one at a time.</summary>
    private readonly SemaphoreSlim _exploreLock = new(1, 1);

    private async Task<(ExploreResult result, int[] counts)> ExploreCoreAsync(string path, ExploreMode mode, int offset, int seed,
        MapPool pool, int count, bool randomPick, CancellationToken ct)
    {
        await _exploreLock.WaitAsync(ct);
        try { return await ExploreLockedAsync(path, mode, offset, seed, pool, count, randomPick, ct); }
        finally { _exploreLock.Release(); }
    }

    private async Task<(ExploreResult result, int[] counts)> ExploreLockedAsync(string path, ExploreMode mode, int offset, int seed,
        MapPool pool, int count, bool randomPick, CancellationToken ct)
    {
        await EnsureEngineAsync(ct);
        _engine!.Catalog.Pool = State.MapPool; // Training's pool, for Training's level below
        var catalog = _engine.ExploreCatalog;
        catalog.Pool = pool;
        // One shared sync per session (Explore reloads on every slider step); not cancelled with a superseded request.
        var unranked = pool == MapPool.Curated ? await (_unrankedScores ??= LoadUnrankedScoresAsync()) : Array.Empty<PlayEvidence>();
        return await Task.Run(() =>
        {
            var log = _playLog.ReadAll(out _);
            var all = Evidence(log);
            var ranked = all.Where(e => Since == null || e.TimeUtc >= Since.Value).ToList();
            // D-047: slider 0 = the difficulty Training works at on this style (same evidence, ranked maps only); after a
            // fresh start, starter maps aim at the calibrated level as in Training.
            var startLevel = FreshStartLevel(path, log, all);
            var report = _engine!.Analyze(all, new[] { path }, Since,
                startLevel is double s ? new Dictionary<string, double> { [path] = s } : null);
            var level = Explorer.TrainingLevel(report, path);
            var rated = ranked.Concat(unranked.Where(e => Since == null || e.TimeUtc >= Since.Value))
                .Select(catalog.Rate).Where(r => r != null).Select(r => r!).ToList();
            var best = ProfileBuilder.BestPerMap(rated);
            return (Explorer.Suggest(path, mode, offset, best, _model!, catalog, seed, count, level, randomPick),
                    Explorer.Counts(path, mode, best, _model!, catalog, level));
        }, ct);
    }

    /// <summary>
    /// Fresh start (D-040), read-only (Explore, D-047): the level a style's starter maps aim at — the calibration estimate
    /// once done, the level of the next calibration map while it runs, the cautious level when skipped — as
    /// <see cref="Calibrate"/> gives Training, without picking maps. Null outside a fresh start or without an old level.
    /// </summary>
    private double? FreshStartLevel(string path, IReadOnlyList<PlayRecord> log, List<PlayEvidence> evidence)
    {
        if (State.StartMode != StartMode.Fresh) return null;
        if (!_engine!.Analyze(evidence, new[] { path }).Paths.TryGetValue(path, out var old)) return null;
        var oldLevel = old.State.Difficulty;
        if (State.Calibrations.TryGetValue(path, out var c) && c.Skipped) return Calibrator.CautiousLevel(oldLevel);
        var status = Calibrator.Status(oldLevel, Calibrator.Bottom(path, _model!, _engine.Catalog) ?? 0, State.CalibrationPicks(path), log);
        return status.Done ? status.Estimate : status.Level;
    }

    /// <summary>The next Curated Explore list resyncs unranked PBs (the plugin was reopened, user 2026-11-11).</summary>
    public void ResyncUnrankedOnNextUse() => _unrankedScores = null;

    /// <summary>Unranked BeatLeader PBs (synced, falling back to the cache; none without BeatLeader import).</summary>
    private async Task<IReadOnlyList<PlayEvidence>> LoadUnrankedScoresAsync()
    {
        if (State.StartMode != StartMode.ImportBeatLeader && State.StartMode != StartMode.Fresh || string.IsNullOrEmpty(State.PlayerId))
            return Array.Empty<PlayEvidence>();
        try
        {
            return await _unrankedCache.SyncAsync(_beatLeader, State.PlayerId, DateTime.UtcNow);
        }
        catch (Exception e)
        {
            Plugin.Log.Warn($"BeatLeader unranked sync failed, using cached scores: {e.Message}");
            return _unrankedCache.Cached();
        }
    }

    public async Task<IReadOnlyList<PlatformStats>> GetPlatformStatsAsync(string playerId, CancellationToken ct = default)
    {
        _lastPlatforms = await _platforms.GetAllAsync(playerId, ct);
        return _lastPlatforms;
    }

    /// <summary>Main established style, else the strongest leaning (D-008: inferred, the player can switch).</summary>
    /// <summary>
    /// Fresh-start calibration of the focus style (D-040). Returns the status while it runs, or the level starter maps
    /// should aim at (calibrated estimate, or a cautious one when skipped / not needed). Nothing without an old level.
    /// </summary>
    private (CalibrationStatus? status, double? startLevel) Calibrate(string focus, PlayerReport report, IReadOnlyList<PlayRecord> log,
        List<PlayEvidence> evidence)
    {
        if (State.StartMode != StartMode.Fresh) return (null, null);
        // Old level = the style's difficulty from the full history (BeatLeader PBs before the fresh start included).
        if (!_engine!.Analyze(evidence, new[] { focus }).Paths.TryGetValue(focus, out var old)) return (null, null);
        var oldLevel = old.State.Difficulty;
        var picks = State.CalibrationPicks(focus);
        var skipped = State.Calibrations.TryGetValue(focus, out var c) && c.Skipped;
        // Only from the start: a style that already has progress since the fresh start doesn't need it.
        if (skipped || (picks.Count == 0 && report.Paths.ContainsKey(focus))) return (null, Calibrator.CautiousLevel(oldLevel));

        var bottom = Calibrator.Bottom(focus, _model!, _engine.Catalog) ?? 0;
        var status = Calibrator.Status(oldLevel, bottom, picks, log);
        if (status.Done) return (null, status.Estimate);
        if (status.Current == null)
        {
            var exclude = State.SkippedKeys();
            foreach (var p in picks) exclude.Add(p.Map);
            var pick = Calibrator.Pick(focus, status.Level, _model!, _engine.Catalog, exclude, DateTime.UtcNow);
            if (pick == null) return (null, Calibrator.CautiousLevel(oldLevel));
            State.AddCalibrationPick(focus, pick);
            SaveState();
            status = Calibrator.Status(oldLevel, bottom, State.CalibrationPicks(focus), log);
        }
        return (status, null);
    }

    /// <summary>A plugin play on a map of this style (effective ratings, speed modifiers included).</summary>
    private bool OnPath(PlayRecord p, string path) =>
        p.Hash.Length > 0 && p.Difficulty.Length > 0
        && _engine!.Catalog.Rate(ToEvidence(p)) is { } rated && PathRules.IsRelevant(path, rated.Ratings, _model!.Thresholds);

    private List<NextUpItem> CalibrationItems(CalibrationStatus status)
    {
        var pick = status.Current;
        if (pick == null || !_engine!.Catalog.TryGet(pick.Map, out var map)) return new List<NextUpItem>();
        return new List<NextUpItem>
        {
            new()
            {
                Kind = ObjectiveKind.Push,
                TargetAccuracy = pick.TargetAccuracy,
                Recommendation = new MapRecommendation
                {
                    Map = pick.Map, Name = map.Name, Mapper = map.Mapper, AxisRating = pick.Axis, Stars = map.Ratings[""].Stars,
                    TargetAccuracy = pick.TargetAccuracy,
                },
            },
        };
    }

    private static string SuggestFocus(PlayerReport report)
    {
        if (report.StyleMix.Established.Count > 0) return report.StyleMix.Main;
        return report.Leaning.Count > 0 ? report.Leaning.OrderByDescending(kv => kv.Value).First().Key : Destinations.Midspeed;
    }

    private static PlayEvidence ToEvidence(PlayRecord p) =>
        new(new MapKey(p.Hash, p.Characteristic, p.Difficulty), p.Accuracy, p.Modifiers, p.PlayedAtUtc, EvidenceSource.LocalPlayLog);
}
