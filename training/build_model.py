"""Assemble model.json (docs/ARCHITECTURE.md, D-026) in the exact shape of SkillProgression.Core.Model.ModelFile.

Inputs (all local, data/explore/*): leaning (06), waypoints + path snapshots (04), pools (05), cached clears (02),
population DB (D-015). Output: data/model/model.json(.gz). Aggregates only — no per-player records (D-007).
Run from training/:  .venv/Scripts/python build_model.py
"""
import gzip
import json
import os
import sys
from collections import Counter, defaultdict
from datetime import datetime, timezone

import numpy as np
import pandas as pd

sys.path.insert(0, os.path.join(os.path.dirname(__file__), "explore"))
import importlib  # noqa: E402

from common import POPULATION_DB, connect, load_leaderboards, load_players, load_scores, out_dir  # noqa: E402
from profiles import DESTINATIONS, THRESHOLDS, apply_modifier_ratings, destination, path_plays, speed_mod  # noqa: E402

OUT_DIR = os.path.abspath(os.path.join(os.path.dirname(__file__), "..", "data", "model"))
# Finer near the top: the acc curve bends steeply there and linear interpolation between 0.5% steps underrated
# 98-99% plays by up to ~5% (rank-18 anchor investigation, 2026-11-10).
ACC_GRID = sorted({0.0, 0.5, 0.6, 0.7, 0.75, 0.8, 0.85, 0.875, 0.9, 0.91, 0.92, 0.93, 0.94,
                   *[round(0.94 + i * 0.0025, 4) for i in range(1, 17)],
                   *[round(0.98 + i * 0.001, 4) for i in range(1, 21)]})
MIN_SCORES_FOR_CURVE = 5
RATING_KEYS = {"Stars": "Stars", "PassRating": "Pass", "AccRating": "Acc", "TechRating": "Tech", "PredictedAcc": "PredictedAcc"}


def thresholds():
    t = THRESHOLDS
    return {"AccResid": t["acc_resid"], "LowPass": t["low_pass"], "MidPass": t["mid_pass"], "MidGap": t["mid_gap"],
            "TechRatio": t["tech_ratio"], "TechMin": t["tech_min"], "TechArrive": t["tech_arrive"],
            "SpeedArrive": t["speed_arrive"], "ExtremeStars": t["extreme_stars"],
            "ExtremePathMinStars": t["extreme_path_min_stars"]}


def leaning():
    p = json.load(open(os.path.join(out_dir("06_leaning"), "leaning_model.json")))
    base = p["peer_baseline"]
    return {"Features": p["features"], "PeerLevelCenters": base["centers"], "PeerMedians": base["medians"],
            "Mean": p["mean"], "Scale": p["scale"], "Classes": p["classes"], "Coefficients": p["coef"],
            "Intercepts": p["intercept"]}


def paths(snaps, wps):
    out = []
    for path in DESTINATIONS:
        w = wps[wps.path == path].sort_values("wp")
        if w.empty:
            continue
        s = snaps[snaps.path == path]
        moves = w.moves.fillna(0)
        prior = float((w.push_share.fillna(0) * moves).sum() / moves.sum()) if moves.sum() > 0 else 0.5
        # Only push-driven paths are difficulty-gated (Speed 0.45, High Tech 0.49, Extreme 0.69). Polish-dominated paths
        # (Accuracy 0.23, Midspeed 0.34) are placed by path pp: gating Midspeed put a rank-151 Midspeed player
        # (user review 2026-11-10) at waypoint 3/6 because its top waypoints' pass comes from a few faster players.
        out.append({"Destination": path, "PushPrior": round(prior, 4), "DifficultyGated": bool(prior >= 0.4),
                    "DifficultySpread": round(float(s.D.std()), 4), "AccuracySpread": round(float(s.A.std()), 5),
                    "Waypoints": [{"Index": int(r.wp), "PathPpLow": round(float(r.path_pp_lo), 2),
                                   "PathPpHigh": round(float(r.path_pp_hi), 2), "Difficulty": round(float(r.D), 4),
                                   "AccuracyResidual": round(float(r.A), 5), "Accuracy": round(float(r.acc), 5),
                                   "EquivalentRank": int(r.milestone_rank)} for r in w.itertuples()]})
    return out


# Modifiers that lower pp at equal accuracy in the population DB (NO/NB ~-20%, SC ~-50%; NF scores have no pp).
# BE, PM, GN, DA were measured to leave pp unchanged (ratio 1.0000), so their scores are kept: dropping them
# clamped the curves below the best players' accuracy on the hardest maps (rank-1 anchor regression, 2026-11-10).
PP_REDUCING_MODIFIERS = {"NO", "NB", "NA", "SC", "NF", "OP", "OD"}


def pp_curves(scores):
    """Monotone acc -> pp lookup on ACC_GRID per (leaderboard, speed modifier), from population PBs without
    pp-reducing modifiers (so the curve is the map's own)."""
    mods = scores.Modifiers.fillna("")
    sm = mods.map(speed_mod)
    clean = mods.map(lambda m: not (set(m.split(",")) & PP_REDUCING_MODIFIERS))
    s = scores[clean].assign(SpeedMod=sm[clean])
    curves = defaultdict(dict)
    for (lb, mod), g in s.groupby(["LeaderboardId", "SpeedMod"]):
        if len(g) < MIN_SCORES_FOR_CURVE:
            continue
        g = g.sort_values("Accuracy")
        pp = np.maximum.accumulate(np.interp(ACC_GRID, g.Accuracy.to_numpy(), g.Pp.to_numpy()))
        curves[lb][mod] = {"Acc": ACC_GRID, "Pp": [round(float(x), 2) for x in pp]}
    return curves


# BeatLeader map type bits (docs/API_INTEGRATIONS.md) -> the model's single type, in the population DB's order.
TYPE_BITS = [(1, "acc"), (2, "tech"), (4, "midspeed"), (8, "speed")]
KNN_CURVES = 5


def has_ratings(ratings):
    """BeatLeader sometimes rates a map's stars and acc but not pass/tech (all 0): such a map fits no style, so it is
    left out of the model (user, 2026-11-13)."""
    r = ratings.get("")
    return r is not None and all(r[k] > 0 for k in ("Stars", "Pass", "Acc", "Tech"))


def pool_maps(maps):
    """D-041: ScoreSaber / AccSaber ranked maps that aren't BeatLeader-ranked, with BeatLeader's own ratings
    (fetch_pool_ratings.py). They have no population scores, so each speed version gets the mean acc -> pp curve of
    the KNN_CURVES BeatLeader curves with the closest (pass, acc, tech, predicted acc) ratings (D-026 lookup curves)."""
    path = os.path.join(out_dir("08_pool_ratings"), "pool_ratings.json")
    if not os.path.exists(path):
        print("no pool ratings (run fetch_pool_ratings.py) - BeatLeader-ranked maps only")
        return []
    feats, curves = [], []
    for m in maps:
        for mod, curve in m["PpCurves"].items():
            r = m["Ratings"].get(mod)
            if r:
                feats.append([r["Pass"], r["Acc"], r["Tech"], r["PredictedAcc"] * 100])
                curves.append(curve["Pp"])
    feats, curves = np.array(feats), np.array(curves)
    scale = feats.std(axis=0)

    def curve_for(r):
        x = np.array([r["Pass"], r["Acc"], r["Tech"], r["PredictedAcc"] * 100])
        nearest = np.argsort((((feats - x) / scale) ** 2).sum(axis=1))[:KNN_CURVES]
        return {"Acc": ACC_GRID, "Pp": [round(float(v), 2) for v in np.maximum.accumulate(curves[nearest].mean(axis=0))]}

    known = {(m["Hash"], m["Difficulty"]) for m in maps}
    extra = []
    for p in json.load(open(path, encoding="utf-8")):
        if (p["Hash"], p["Difficulty"]) in known or not has_ratings(p["Ratings"]):
            continue
        extra.append({
            "Hash": p["Hash"], "Characteristic": "Standard", "Difficulty": p["Difficulty"], "Name": p["Name"],
            "Mapper": p.get("Mapper", ""),
            "MapType": next((name for bit, name in TYPE_BITS if p["Type"] & bit), ""),
            "Duration": round(p["Duration"], 2), "PeakEbpm": 0,
            "RankedBeatLeader": False, "RankedScoreSaber": p["RankedScoreSaber"], "RankedAccSaber": p["RankedAccSaber"],
            "Ratings": p["Ratings"], "PpCurves": {mod: curve_for(r) for mod, r in p["Ratings"].items()}, "PeerPopularity": {},
        })
    return extra


def curated_maps(maps):
    """BeatSaver curated maps (fetch_curated.py, D-045): flags model maps that are curated and adds the curated maps
    BeatLeader rates but nobody ranks, with ratings only (no pp: Explore's Curated pool, never progression)."""
    path = os.path.join(out_dir("09_curated"), "curated.json")
    if not os.path.exists(path):
        print("no curated maps (run fetch_curated.py)")
        return []
    by_key = {(m["Hash"], m["Difficulty"]): m for m in maps}
    extra = []
    for c in json.load(open(path, encoding="utf-8")):
        known = by_key.get((c["Hash"], c["Difficulty"]))
        if known is not None:
            known["Curated"], known["BeatSaverScore"] = True, c["BeatSaverScore"]
            continue
        if not has_ratings(c["Ratings"]):
            continue
        entry = {
            "Hash": c["Hash"], "Characteristic": "Standard", "Difficulty": c["Difficulty"], "Name": c["Name"],
            "Mapper": c.get("Mapper", ""),
            "MapType": next((name for bit, name in TYPE_BITS if c["Type"] & bit), ""),
            "Duration": round(c["Duration"], 2), "PeakEbpm": 0,
            "RankedBeatLeader": False, "RankedScoreSaber": False, "RankedAccSaber": False,
            "Curated": True, "BeatSaverScore": c["BeatSaverScore"],
            "Ratings": c["Ratings"], "PpCurves": {}, "PeerPopularity": {},
        }
        by_key[(c["Hash"], c["Difficulty"])] = entry
        extra.append(entry)
    return extra


def popularity(snaps, wps, clears):
    """How many players at (path, waypoint) newly cleared each map during that quarter (D-025 ranking)."""
    snaps = snaps.copy()
    snaps["wp"] = -1
    for path, w in wps.groupby("path"):
        m = snaps.path == path
        snaps.loc[m, "wp"] = np.searchsorted(w.path_pp_lo.to_numpy(), snaps.loc[m, "path_pp"].to_numpy(), side="right") - 1
    pop = defaultdict(Counter)
    by_player = {pid: g.sort_values("Time") for pid, g in clears.groupby("PlayerId")}
    for (pid, path), g in snaps.sort_values("t").groupby(["PlayerId", "path"]):
        c = by_player.get(pid)
        if c is None:
            continue
        first = path_plays(c, path)[0].drop_duplicates("LeaderboardId")
        ts = list(g.t) + [pd.Timestamp.max]
        for (t0, wp), t1 in zip(zip(g.t, g.wp), ts[1:]):
            pop[f"{path}|{int(wp)}"].update(set(first[(first.Time >= t0) & (first.Time < t1)].LeaderboardId))
    by_map = defaultdict(dict)
    for key, counter in pop.items():
        for lb, n in counter.items():
            by_map[lb][key] = int(n)
    return by_map


def main():
    con = connect()
    lbs, players = load_leaderboards(con), load_players(con)
    mods = pd.read_sql_query("select * from ModifiersRating", con)
    raw_scores = load_scores(con).merge(lbs, left_on="LeaderboardId", right_on="Id", suffixes=("", "_lb"))
    raw_scores = raw_scores[raw_scores.ModeName == "Standard"]
    scores = apply_modifier_ratings(raw_scores, mods)

    snaps = pd.read_csv(os.path.join(out_dir("04_waypoints"), "path_snapshots.csv"), dtype={"PlayerId": str}, parse_dates=["t"])
    wps = pd.read_csv(os.path.join(out_dir("04_waypoints"), "waypoints.csv"))
    pools = pd.read_csv(os.path.join(out_dir("05_pools"), "pools.csv"))
    pools = pools[pools.Id.notna()].set_index("Id")
    cur = pd.read_csv(os.path.join(out_dir("03_thresholds"), "current_profiles.csv"), dtype={"PlayerId": str})
    cur["dest"] = cur.apply(destination, axis=1)
    arrived = set(cur[(cur.Rank <= 10000) & cur.dest.isin(DESTINATIONS)].PlayerId)
    clears = importlib.import_module("02_trajectories").clear_attempts(arrived, lbs, mods, scores)

    curves = pp_curves(scores)
    pop = popularity(snaps, wps, clears)
    mr = mods.set_index("Id")
    maps = []
    for r in lbs[lbs.ModeName == "Standard"].itertuples():
        ratings = {"": {v: round(float(getattr(r, k)), 5) for k, v in RATING_KEYS.items()}}
        if r.ModifiersRatingId in mr.index:
            m = mr.loc[r.ModifiersRatingId]
            for code in ("SS", "FS", "SF"):
                if pd.notna(m[f"{code}Stars"]):
                    ratings[code] = {v: round(float(m[f"{code}{k}"]), 5) for k, v in RATING_KEYS.items()}
        pool = pools.loc[r.Id] if r.Id in pools.index else None
        maps.append({
            "Hash": r.Hash.upper(), "Characteristic": "Standard", "Difficulty": r.DifficultyName, "Name": r.Name,
            "Mapper": r.Mapper or "",
            "MapType": r.MapType or "", "Duration": round(float(r.Duration or 0), 2), "PeakEbpm": round(float(r.PeakEbpm or 0), 1),
            "RankedBeatLeader": True,
            "RankedScoreSaber": bool(pool is not None and pool.SS), "RankedAccSaber": bool(pool is not None and pool.AS),
            "Ratings": ratings, "PpCurves": curves.get(r.Id, {}), "PeerPopularity": pop.get(r.Id, {}),
        })

    extra = pool_maps(maps)
    print(f"pool maps added: {len(extra)} (ScoreSaber {sum(m['RankedScoreSaber'] for m in extra)}, "
          f"AccSaber {sum(m['RankedAccSaber'] for m in extra)})")
    maps += extra
    curated = curated_maps(maps)
    print(f"curated: {sum(1 for m in maps if m.get('Curated'))} model maps flagged, {len(curated)} unranked maps added")
    maps += curated

    model = {"Version": 1, "GeneratedAt": datetime.now(timezone.utc).isoformat(),
             "SourceDb": os.path.basename(POPULATION_DB), "Thresholds": thresholds(), "Leaning": leaning(),
             "Paths": paths(snaps, wps), "Maps": maps}
    os.makedirs(OUT_DIR, exist_ok=True)
    text = json.dumps(model, separators=(",", ":"))
    open(os.path.join(OUT_DIR, "model.json"), "w", encoding="utf-8").write(text)
    with gzip.open(os.path.join(OUT_DIR, "model.json.gz"), "wt", encoding="utf-8") as f:
        f.write(text)
    with_curve = sum(1 for m in maps if "" in m["PpCurves"])
    print(f"maps {len(maps)} (nomod curve {with_curve}), paths {len(model['Paths'])}, "
          f"size {len(text) / 1e6:.1f} MB raw, {os.path.getsize(os.path.join(OUT_DIR, 'model.json.gz')) / 1e6:.1f} MB gz")


if __name__ == "__main__":
    main()
