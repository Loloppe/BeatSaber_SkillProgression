"""AccSaber category tracks (D-061): True Acc / Standard Acc / Tech Acc as paths, built cross-sectionally.

Inputs: fetch_accsaber.py (13_accsaber: difficulties, curves, categories, scores_index) and the BeatLeader score cache
(12_peer_scores/raw). Every population player's PBs without a modifier AccSaber bans (BANNED_MODIFIERS) on a
category's maps give their category state today, exactly as Core's PathState computes it for a track: AP per score =
curve(acc) x (complexity - shift) x scale, path pp = AccSaber's weighted sum, D = AP-weighted median complexity of the
top 15, A = AP-weighted mean (acc - BL predicted acc), acc likewise; players need >= MIN_PLAYS plays. Players are
cut into waypoints at AccSaber rank milestones by the category AP AccSaber shows at that rank (rank_ap.json; the
BeatLeader scores reproduce a player's AP exactly but miss ~1/4 of AccSaber's players, so a rank within them would
overstate the rank), merged upward while a milestone has fewer than MIN_PLAYERS_PER_WAYPOINT players. No history, so the push/polish prior is the Accuracy style's and tracks are not
difficulty-gated. Popularity "Track|wp" = how many players at that waypoint have a PB on the map.
Aggregates only (D-007): waypoint values are quantiles / medians over >= MIN_PLAYERS_PER_WAYPOINT players.
"""
import json
import os
from collections import Counter, defaultdict

import numpy as np
import pandas as pd

from common import out_dir

# AccSaber's banned modifiers (user, 2026-11-22) + No Fail (assumption). Mirrors Core AccSaberTracks.BannedModifiers.
BANNED_MODIFIERS = {"NO", "NB", "SS", "SC", "SN", "NF"}
TRACKS = {"true_acc": "True Acc", "standard_acc": "Standard Acc", "tech_acc": "Tech Acc"}
DIFFICULTIES = {"EASY": "Easy", "NORMAL": "Normal", "HARD": "Hard", "EXPERT": "Expert", "EXPERT_PLUS": "ExpertPlus"}
SCORE_CURVE = "AccSaber Score Curve"
MIN_PLAYS = 5   # Core PathState.MinPlays
TOP_K = 15      # Core PathState.TopK
RANK_MILESTONES = [20000, 15000, 10000, 7500, 5000, 3000, 2000, 1000, 500, 250, 100, 50, 25, 10]
MIN_PLAYERS_PER_WAYPOINT = 40


def load():
    """(difficulties, model section, AP at rank milestones) from the fetch_accsaber.py cache, or Nones when it hasn't run."""
    d = out_dir("13_accsaber")
    if not os.path.exists(os.path.join(d, "rank_ap.json")):
        print("no AccSaber data (run fetch_accsaber.py) - no AccSaber tracks")
        return None, None, None
    diffs = json.load(open(os.path.join(d, "difficulties.json"), encoding="utf-8"))
    curves = json.load(open(os.path.join(d, "curves.json"), encoding="utf-8"))
    cats = [c for c in json.load(open(os.path.join(d, "categories.json"), encoding="utf-8")) if c["code"] in TRACKS]
    score, weight = cats[0]["scoreCurve"], cats[0]["weightCurve"]
    if any(c["scoreCurve"] != score or c["weightCurve"] != weight for c in cats):
        raise SystemExit("AccSaber categories use different curves: the model holds one (update AccSaberModel)")
    curve = next(c for c in curves if c["id"] == score["id"])
    pts = sorted((p["x"], p["y"]) for p in curve["points"])
    section = {"Curve": {"Acc": [round(x, 8) for x, _ in pts], "Pp": [round(y, 8) for _, y in pts]},
               "Scale": score["scale"], "Shift": score["shift"], "WeightK": weight["xParameterValue"],
               "WeightY1": weight["yParameterValue"], "WeightX1": weight["zParameterValue"]}
    return diffs, section, json.load(open(os.path.join(d, "rank_ap.json"), encoding="utf-8"))


def ap(section, complexity, acc):
    c = section["Curve"]
    return np.interp(acc, c["Acc"], c["Pp"]) * (complexity - section["Shift"]) * section["Scale"]


def weights(section, n):
    k, y1, x1 = section["WeightK"], section["WeightY1"], section["WeightX1"]
    a = (1 - y1) / (y1 - np.exp(-k * x1))
    s = 1 / (1 + a * np.exp(k * (np.arange(n) - x1)))
    return s / (1 / (1 + a * np.exp(-k * x1)))


def weighted_median(values, w):
    """Same rule as Core Stats.WeightedMedian: first value whose cumulative weight reaches half the total."""
    order = np.argsort(values)
    v, w = np.asarray(values)[order], np.asarray(w)[order]
    return float(v[np.searchsorted(np.cumsum(w), w.sum() / 2)])


def annotate(maps, diffs):
    """Sets AccSaberCategory / AccSaberComplexity on the model's AccSaber maps; returns {(HASH, diff): map index}."""
    by_key = {(m["Hash"], m["Difficulty"]): i for i, m in enumerate(maps)}
    found = {}
    for d in diffs:
        key = (d["songHash"].upper(), DIFFICULTIES.get(d["difficulty"], ""))
        i = by_key.get(key)
        if i is None or d.get("categoryCode") not in TRACKS or not maps[i]["RankedAccSaber"]:
            continue
        maps[i]["AccSaberCategory"], maps[i]["AccSaberComplexity"] = d["categoryCode"], float(d["complexity"])
        found[key] = i
    print(f"AccSaber tracks: {len(found)} of {len(diffs)} AccSaber difficulties in the model "
          f"({Counter(maps[i]['AccSaberCategory'] for i in found.values())})")
    return found


def counts(mods) -> bool:
    return not any(m.strip().upper() in BANNED_MODIFIERS for m in (mods or "").split(","))


def scores(maps, found):
    """PBs that earn AP on the AccSaber maps: DataFrame (player, map index, acc)."""
    d = out_dir("13_accsaber")
    raw = out_dir(os.path.join("12_peer_scores", "raw"))
    rows = []
    for e in json.load(open(os.path.join(d, "scores_index.json"), encoding="utf-8")):
        i = found.get((e["Hash"], e["Difficulty"]))
        path = os.path.join(raw, f"{e['LeaderboardId']}.json")
        if i is None or not os.path.exists(path):
            continue
        rows += [(p, i, a) for p, a, mods, _ in json.load(open(path, encoding="utf-8")) if counts(mods)]
    s = pd.DataFrame(rows, columns=["player", "map", "acc"])
    return s.sort_values("acc", ascending=False).drop_duplicates(["player", "map"])


def states(maps, s, section):
    """Per player and track: category AP, D, A, acc (Core PathState for a track)."""
    s = s.assign(cat=s["map"].map(lambda i: maps[i]["AccSaberCategory"]),
                 c=s["map"].map(lambda i: maps[i]["AccSaberComplexity"]),
                 pred=s["map"].map(lambda i: maps[i]["Ratings"][""]["PredictedAcc"]))
    s["ap"] = ap(section, s.c.to_numpy(), s.acc.to_numpy())
    s = s[s.ap > 0].sort_values(["player", "cat", "ap"], ascending=[True, True, False])
    out = []
    for (player, cat), g in s.groupby(["player", "cat"], sort=False):
        if len(g) < MIN_PLAYS:
            continue
        values = g.ap.to_numpy()
        top = g.head(TOP_K)
        w = top.ap.to_numpy()
        out.append({"player": player, "track": TRACKS[cat], "path_pp": float((values * weights(section, len(values))).sum()),
                    "D": weighted_median(top.c.to_numpy(), w),
                    "A": float(np.average(top.acc - top.pred, weights=w)), "accuracy": float(np.average(top.acc, weights=w))})
    return pd.DataFrame(out), s


def waypoints(st, prior, rank_ap):
    """Waypoints per track: players bucketed by the AP at AccSaber's rank milestones (04_waypoints.py's merge rule)."""
    paths, assignment = [], {}
    for code, track in TRACKS.items():
        milestones = [r for r in RANK_MILESTONES if str(r) in rank_ap[code]["Ap"]]
        thresholds = np.array([rank_ap[code]["Ap"][str(r)] for r in milestones])
        t = st[st.track == track].copy()
        # bucket b = players whose AP reaches milestone b's AP (rank <= milestone b) but not milestone b + 1's
        t["bucket"] = np.searchsorted(thresholds, t.path_pp.to_numpy(), side="right") - 1
        t = t[t.bucket >= 0]
        if len(t) < 2 * MIN_PLAYERS_PER_WAYPOINT:
            print(f"  {track}: only {len(t)} players - no waypoints")
            continue
        groups, pending = [], []
        for b in range(len(milestones)):
            pending.append(b)
            if t.bucket.isin(pending).sum() >= MIN_PLAYERS_PER_WAYPOINT:
                groups.append(list(pending))
                pending = []
        if pending and groups:
            groups[-1] += pending  # too few at the very top: merge into the last waypoint
        wps, low = [], -np.inf
        for i, g in enumerate(groups):
            b = t[t.bucket.isin(g)]
            low = max(low, float(b.path_pp.quantile(0.25)))  # placement by path pp needs increasing lower bounds
            wps.append({"Index": i, "PathPpLow": round(low, 2), "PathPpHigh": round(float(b.path_pp.quantile(0.75)), 2),
                        "Difficulty": round(float(b.D.median()), 4), "AccuracyResidual": round(float(b.A.median()), 5),
                        "Accuracy": round(float(b.accuracy.median()), 5), "EquivalentRank": milestones[g[0]]})
            for p in b.player:
                assignment[(track, p)] = i
            print(f"  {track:12} wp {i}  rank <= {milestones[g[0]]:6,}  players {len(b):5,}  "
                  f"AP {b.path_pp.quantile(0.25):8.0f}-{b.path_pp.quantile(0.75):8.0f}  D {b.D.median():5.2f}  "
                  f"acc {b.accuracy.median():.4f}")
        all_t = st[st.track == track]
        paths.append({"Destination": track, "PushPrior": round(prior, 4), "DifficultyGated": False,
                      "DifficultySpread": round(float(all_t.D.std()), 4), "AccuracySpread": round(float(all_t.A.std()), 5),
                      "Waypoints": wps})
    return paths, assignment


def popularity(maps, s, assignment):
    """MapEntry.PeerPopularity["Track|wp"] += players at that waypoint with a PB on the map."""
    counts = defaultdict(Counter)
    for player, m, cat in zip(s.player, s["map"], s.cat):
        wp = assignment.get((TRACKS[cat], player))
        if wp is not None:
            counts[m][f"{TRACKS[cat]}|{wp}"] += 1
    for m, c in counts.items():
        maps[m].setdefault("PeerPopularity", {}).update({k: int(n) for k, n in c.items()})


def build(maps, accuracy_prior):
    """Annotates the AccSaber maps in place; returns (track PathModels, AccSaberModel section) or ([], None)."""
    diffs, section, rank_ap = load()
    if diffs is None:
        return [], None
    found = annotate(maps, diffs)
    st, s = states(maps, scores(maps, found), section)
    print(f"AccSaber tracks: {st.player.nunique():,} players with >= {MIN_PLAYS} AP-earning plays in a category")
    paths, assignment = waypoints(st, accuracy_prior, rank_ap)
    popularity(maps, s, assignment)
    return paths, section
