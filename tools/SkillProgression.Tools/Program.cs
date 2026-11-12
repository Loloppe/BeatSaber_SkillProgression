using System.Globalization;
using SkillProgression.Core.Model;
using SkillProgression.Core.Profiles;
using SkillProgression.Core.Progression;
using SkillProgression.Data.BeatLeader;
using SkillProgression.Data.Model;

// SkillProgression.Tools — offline/dev commands (docs/ARCHITECTURE.md).
//   player <beatleaderId> [--model <path>] [--since yyyy-MM-dd] [--path "<destination>"]
//   check-anchors [--model <path>]       user anchor players (D-018..D-021) through the live API + Core
CultureInfo.DefaultThreadCurrentCulture = CultureInfo.InvariantCulture;
CultureInfo.CurrentCulture = CultureInfo.InvariantCulture;

if (args.Length == 0) return Usage();
var options = Options.Parse(args.Skip(1).ToArray());
var modelPath = options.Get("model") ?? DefaultModelPath();
if (modelPath == null || !File.Exists(modelPath))
{
    Console.Error.WriteLine("model.json(.gz) not found — run training/build_model.py or pass --model <path>.");
    return 2;
}

var model = ModelLoader.Load(modelPath);
var engine = new ProgressionEngine(model);
using var http = new HttpClient();
http.DefaultRequestHeaders.UserAgent.ParseAdd("SkillProgression-tools/0.1 (+https://github.com/)");
var beatLeader = new BeatLeaderClient(http);

switch (args[0])
{
    case "player" when options.Positional.Count == 1:
    {
        var since = options.Get("since") is { } s ? DateTime.SpecifyKind(DateTime.Parse(s), DateTimeKind.Utc) : (DateTime?)null;
        var scores = await beatLeader.GetRankedScoresAsync(options.Positional[0]);
        var paths = options.Get("path") is { } p ? new[] { p } : null;
        Report.Print(options.Positional[0], engine.Analyze(scores, paths, since), model);
        return 0;
    }
    case "check-anchors":
    {
        // Anchor players are local only (data/anchors.json, gitignored): no player IDs in the repository.
        var anchors = Anchors.Load();
        if (anchors.Count == 0)
        {
            Console.Error.WriteLine("No anchors: create data/anchors.json with {\"anchors\": {\"<blId>\": \"<Destination>\"}}.");
            return 2;
        }
        var ok = 0;
        foreach (var (id, expected) in anchors)
        {
            var report = engine.Analyze(await beatLeader.GetRankedScoresAsync(id), Array.Empty<string>());
            var pr = report.Profile;
            // Anchors must have their destination among the established styles (review round 3: players are mixes).
            var match = report.StyleMix.Established.Contains(expected);
            if (match) ok++;
            Console.WriteLine($"{(match ? "OK  " : "DIFF")} {id,-18} expected {expected,-10} established {string.Join("+", report.StyleMix.Established),-20} " +
                              $"pass {pr?.PassMedian:F2} tech {pr?.TechMedian:F2} stars {pr?.StarsMedian:F2} " +
                              $"accVsPred {pr?.Style.GetValueOrDefault(StyleFeatures.AccResidual):+0.000;-0.000} rated {report.RatedCount}/{report.EvidenceCount}");
        }
        Console.WriteLine($"{ok}/{anchors.Count} anchors match (live BL data vs population snapshot of 2026-10-28).");
        return ok == anchors.Count ? 0 : 1;
    }
    default:
        return Usage();
}

static int Usage()
{
    Console.Error.WriteLine("usage: player <beatleaderId> [--model path] [--since yyyy-MM-dd] [--path \"High Tech\"]");
    Console.Error.WriteLine("       check-anchors [--model path]");
    return 1;
}

static string? DefaultModelPath()
{
    for (var dir = new DirectoryInfo(AppContext.BaseDirectory); dir != null; dir = dir.Parent)
    {
        var gz = Path.Combine(dir.FullName, "data", "model", "model.json.gz");
        if (File.Exists(gz)) return gz;
    }
    return null;
}

internal static class Anchors
{
    public static Dictionary<string, string> Load()
    {
        for (var dir = new DirectoryInfo(AppContext.BaseDirectory); dir != null; dir = dir.Parent)
        {
            var path = Path.Combine(dir.FullName, "data", "anchors.json");
            if (!File.Exists(path)) continue;
            var json = Newtonsoft.Json.Linq.JObject.Parse(File.ReadAllText(path));
            return json["anchors"]?.ToObject<Dictionary<string, string>>() ?? new Dictionary<string, string>();
        }
        return new Dictionary<string, string>();
    }
}

internal sealed class Options
{
    public List<string> Positional { get; } = new();
    private readonly Dictionary<string, string> _named = new(StringComparer.OrdinalIgnoreCase);

    public static Options Parse(string[] args)
    {
        var o = new Options();
        for (var i = 0; i < args.Length; i++)
        {
            if (args[i].StartsWith("--") && i + 1 < args.Length) o._named[args[i][2..]] = args[++i];
            else o.Positional.Add(args[i]);
        }
        return o;
    }

    public string? Get(string name) => _named.TryGetValue(name, out var v) ? v : null;
}

internal static class Report
{
    public static void Print(string playerId, PlayerReport r, ModelFile model)
    {
        Console.WriteLine($"Player {playerId}: {r.EvidenceCount} ranked PBs, {r.RatedCount} on model maps");
        if (r.Profile is not { } p)
        {
            Console.WriteLine("  No rated plays — nothing to analyse.");
            return;
        }

        Console.WriteLine($"  Typical top play: pass {p.PassMedian:F2}, tech {p.TechMedian:F2}, stars {p.StarsMedian:F2} (effective), " +
                          $"acc {p.Accuracy:P2} ({p.Style[StyleFeatures.AccResidual]:+0.0%;-0.0%} vs predicted), pass ceiling {p.PassCeiling:F2}");
        Console.WriteLine("  Style mix: " + string.Join(" · ", r.StyleMix.Shares.Where(kv => kv.Value >= 0.005)
            .OrderByDescending(kv => kv.Value)
            .Select(kv => $"{kv.Key} {kv.Value:P0}{(r.StyleMix.Established.Contains(kv.Key) ? " ✓" : "")}")));
        if (r.Leaning.Count > 0)
            Console.WriteLine("  Leaning: " + string.Join(", ", r.Leaning.OrderByDescending(kv => kv.Value).Select(kv => $"{kv.Key} {kv.Value:P0}")));

        foreach (var (path, progress) in r.Paths.OrderByDescending(kv => r.Leaning.GetValueOrDefault(kv.Key)))
        {
            var s = progress.State;
            var axis = path switch { "Speed" or "Midspeed" => "pass", "High Tech" => "tech", _ => "stars" };
            Console.WriteLine();
            Console.WriteLine($"  == {path} — waypoint {progress.Current!.Index + 1}/{progress.WaypointCount} " +
                              $"(players here are typically rank ~{progress.Current.EquivalentRank:N0})");
            Console.WriteLine($"     You: {axis} {s.Difficulty:F2}, acc vs predicted {s.AccuracyResidual:+0.0%;-0.0%} ({s.PathPlayCount} path plays)");
            var next = progress.Next!;
            Console.WriteLine(progress.BeyondLastWaypoint
                ? $"     Past the last waypoint — next step: {axis} {next.Difficulty:F2}, acc vs predicted {next.AccuracyResidual:+0.0%;-0.0%}; progress {progress.ProgressToNext:P0}"
                : $"     Next waypoint {next.Index + 1}: {axis} {next.Difficulty:F2}, acc vs predicted {next.AccuracyResidual:+0.0%;-0.0%} " +
                  $"(~rank {next.EquivalentRank:N0}); progress {progress.ProgressToNext:P0}");
            foreach (var o in progress.Objectives)
            {
                var what = o.AtLevel
                    ? $"PUSH  — new maps at level: {axis} {o.CurrentValue:F2} (path pp {progress.State.PathPp:F0} -> {next.PathPpLow:F0})"
                    : o.Kind == ObjectiveKind.Push
                    ? $"PUSH  — clear harder maps: {axis} {o.CurrentValue:F2} -> {o.TargetValue:F2}"
                    : $"POLISH — raise accuracy: {o.CurrentValue:+0.0%;-0.0%} -> {o.TargetValue:+0.0%;-0.0%} vs predicted";
                Console.WriteLine($"     {what}  (gap {o.Gap:F2}, priority {o.Priority:F2})");
                foreach (var m in o.Maps.Take(5))
                    Console.WriteLine($"        - {Truncate($"{m.Name} [{Short(m.Map.Difficulty)}]", 44),-44} {axis} {m.AxisRating,5:F2}  target {m.TargetAccuracy:P1}" +
                                      (m.CurrentAccuracy is { } c ? $"  (now {c:P1})" : "") +
                                      (m.PeerPopularity > 0 ? $"  peers {m.PeerPopularity}" : ""));
            }
        }
    }

    private static string Truncate(string s, int n) => s.Length <= n ? s : s[..(n - 1)] + "…";

    private static string Short(string difficulty) => difficulty switch
    {
        "ExpertPlus" => "E+", "Expert" => "E", "Hard" => "H", "Normal" => "N", "Easy" => "Ez", _ => difficulty,
    };
}
