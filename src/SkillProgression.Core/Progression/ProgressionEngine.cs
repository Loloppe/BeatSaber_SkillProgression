using System;
using System.Collections.Generic;
using System.Linq;
using SkillProgression.Core.Maps;
using SkillProgression.Core.Model;
using SkillProgression.Core.Plays;
using SkillProgression.Core.Profiles;

namespace SkillProgression.Core.Progression
{
    public sealed class PlayerReport
    {
        public int EvidenceCount { get; set; }
        public int RatedCount { get; set; }
        public PlayerProfile? Profile { get; set; }
        /// <summary>Overall-profile destination rule result (kept for diagnostics / anchors).</summary>
        public string ArrivedDestination { get; set; } = Destinations.Unspecialised;
        /// <summary>Style mix and established styles (shown to the player instead of a single label).</summary>
        public StyleMix StyleMix { get; set; } = new StyleMix();
        public IReadOnlyDictionary<string, double> Leaning { get; set; } = new Dictionary<string, double>();
        public Dictionary<string, PathProgress> Paths { get; set; } = new Dictionary<string, PathProgress>();
        /// <summary>Starter objectives for requested paths without enough evidence (fresh start / unplayed style).</summary>
        public Dictionary<string, Objective> Starters { get; set; } = new Dictionary<string, Objective>();
    }

    /// <summary>
    /// Single entry point used by both the CLI and the plugin (D-013: one implementation). Evidence in, report out.
    /// </summary>
    public sealed class ProgressionEngine
    {
        private readonly ModelFile _model;
        private readonly MapCatalog _catalog;

        public ProgressionEngine(ModelFile model)
        {
            _model = model ?? throw new ArgumentNullException(nameof(model));
            // Progression only sees ranked maps; curated-only maps are for Explore (D-045).
            _catalog = new MapCatalog(model.Maps.Where(MapPools.IsRanked), model.AccSaber, model.ScoreSaber);
            ExploreCatalog = new MapCatalog(model.Maps, model.AccSaber, model.ScoreSaber);
        }

        public MapCatalog Catalog => _catalog;

        /// <summary>Every model map, curated ones included: Explore only (its pool filter picks ranked or curated).</summary>
        public MapCatalog ExploreCatalog { get; }

        /// <param name="evidence">Cleared plays (BeatLeader PBs and/or local play log).</param>
        /// <param name="paths">Paths to analyse; null = every destination with a path model.</param>
        /// <param name="since">Fresh start (D-010): ignore evidence before this time.</param>
        /// <param name="startLevels">Fresh start (D-040): per-path level starter maps aim at (calibrated or cautious).</param>
        /// <param name="skipped">Skipped maps (D-029): recommended only when nothing else is left.</param>
        public PlayerReport Analyze(IEnumerable<PlayEvidence> evidence, IEnumerable<string>? paths = null, DateTime? since = null,
            IReadOnlyDictionary<string, double>? startLevels = null, ISet<MapKey>? skipped = null)
        {
            var list = evidence.Where(e => since == null || e.TimeUtc >= since.Value).ToList();
            var rated = list.Select(_catalog.Rate).Where(r => r != null).Select(r => r!).ToList();
            var best = ProfileBuilder.BestPerMap(rated);
            var report = new PlayerReport { EvidenceCount = list.Count, RatedCount = rated.Count };

            report.Profile = ProfileBuilder.Build(rated);
            if (report.Profile != null)
            {
                report.ArrivedDestination = DestinationRules.Classify(report.Profile, _model.Thresholds);
                report.StyleMix = StyleMixCalculator.Compute(rated, _model.Thresholds);
                report.Leaning = Leaning.Predict(report.Profile, _model.Leaning);
            }

            var playedMaps = best.Select(p => p.Evidence.Map).ToList();
            PeerTargets? oldPeers = null; // D-069: starter targets are capped at the player's level from the full history
            foreach (var path in paths ?? _model.Paths.Select(p => p.Destination))
            {
                var progress = report.Profile == null ? null : PathAnalyzer.Analyze(path, best, _model, _catalog, skipped);
                if (progress != null) report.Paths[path] = progress;
                else if (PathAnalyzer.Starter(path, _model, _catalog, playedMaps,
                             startLevels != null && startLevels.TryGetValue(path, out var level) ? level : (double?)null, skipped,
                             oldPeers ??= since == null ? PeerTargets.For(best, _model) : OldPeers(evidence)) is { } starter) report.Starters[path] = starter;
            }
            return report;
        }

        /// <summary>Peer levels from every PB, fresh start or not (D-069).</summary>
        public PeerTargets OldPeers(IEnumerable<PlayEvidence> evidence) =>
            PeerTargets.For(ProfileBuilder.BestPerMap(evidence.Select(_catalog.Rate).Where(r => r != null).Select(r => r!).ToList()), _model);
    }
}
